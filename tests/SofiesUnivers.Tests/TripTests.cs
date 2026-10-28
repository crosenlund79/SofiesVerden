using SofiesUnivers.Core.Domain;
using SofiesUnivers.Core.Model;
using SofiesUnivers.Tests.Infrastructure;
using static SofiesUnivers.Tests.Infrastructure.Dates;

namespace SofiesUnivers.Tests;

public class TripTests
{
    private static TripDraft Manual(TestWorld w, DateOnly date, string purpose = "Rengøring", bool clinic = true, decimal? km = 42m, bool include = true) => new()
    {
        Date = date,
        Purpose = purpose,
        FromLabel = "Hjem (Soluret, Odense SØ)",
        ToLabel = clinic ? "Klinikken (Hundtofte 14, 5771 Stenstrup)" : "Bauhaus Odense",
        DistanceKm = km,
        IncludeInTaxReport = include,
        IsClinicTrip = clinic,
    };

    [Fact]
    public async Task First_session_on_a_date_registers_one_clinic_round_trip()
    {
        var w = await TestWorld.CreateAsync(routeKm: 64.4m);
        var result = await w.Ledger.SaveSessionAsync(w.Draft(D(2026, 10, 28), 800m));

        var trip = Assert.Single(w.Data.Trips);
        Assert.Equal(D(2026, 10, 28), trip.Date);
        Assert.Equal(64.4m, trip.DistanceKm);
        Assert.True(trip.IsClinicTrip);
        Assert.Equal(TripOrigin.Automatic, trip.Origin);
        Assert.Contains("Hundtofte 14", trip.ToLabel);
        Assert.Contains(result.Notes, n => n.Contains("Kørsel til klinikken"));
    }

    [Fact]
    public async Task Second_session_on_same_date_does_not_duplicate_the_trip()
    {
        var w = await TestWorld.CreateAsync();
        await w.AddSessionAsync(D(2026, 10, 28));
        await w.AddSessionAsync(D(2026, 10, 28));
        Assert.Single(w.Data.Trips);
    }

    [Fact]
    public async Task Existing_manual_clinic_trip_prevents_a_duplicate()
    {
        var w = await TestWorld.CreateAsync();
        (await w.Ledger.SaveTripAsync(Manual(w, D(2026, 10, 28)))).AssertOk();

        await w.AddSessionAsync(D(2026, 10, 28));

        var trip = Assert.Single(w.Data.Trips);
        Assert.Equal(TripOrigin.Manual, trip.Origin);
    }

    [Fact]
    public async Task Manual_trip_elsewhere_does_not_count_as_the_clinic_trip()
    {
        var w = await TestWorld.CreateAsync();
        (await w.Ledger.SaveTripAsync(Manual(w, D(2026, 10, 28), "Indkøb", clinic: false, km: 12m))).AssertOk();

        await w.AddSessionAsync(D(2026, 10, 28));

        Assert.Equal(2, w.Data.Trips.Count);
    }

    [Fact]
    public async Task Backdated_session_creates_trip_on_the_session_date()
    {
        var w = await TestWorld.CreateAsync();
        await w.AddSessionAsync(D(2026, 3, 14));
        Assert.Equal(D(2026, 3, 14), Assert.Single(w.Data.Trips).Date);
    }

    [Fact]
    public async Task Missing_route_distance_is_left_empty_never_invented()
    {
        var w = await TestWorld.CreateAsync(routeKm: null);
        var result = await w.Ledger.SaveSessionAsync(w.Draft(D(2026, 10, 28), 800m));
        Assert.Null(Assert.Single(w.Data.Trips).DistanceKm);
        Assert.Contains(result.Notes, n => n.Contains("km mangler"));
    }

    [Fact]
    public async Task Trip_can_be_excluded_from_the_tax_report_without_deleting_it()
    {
        var w = await TestWorld.CreateAsync();
        await w.AddSessionAsync(D(2026, 10, 28));
        var trip = w.Data.Trips.Single();

        (await w.Ledger.SaveTripAsync(new TripDraft
        {
            Id = trip.Id, Date = trip.Date, Purpose = trip.Purpose, FromLabel = trip.FromLabel, ToLabel = trip.ToLabel,
            DistanceKm = trip.DistanceKm, IsClinicTrip = true, IncludeInTaxReport = false,
        })).AssertOk();

        var after = Assert.Single(w.Data.Trips);
        Assert.False(after.IncludeInTaxReport);
        Assert.Equal(TripOrigin.Manual, after.Origin);
        Assert.Equal(0m, SofiesUnivers.Core.Reports.ReportCalculator.ForYear(w.Data, 2026).DrivingKmInTaxReport);
    }

    [Fact]
    public async Task Changing_the_standard_distance_does_not_rewrite_history()
    {
        var w = await TestWorld.CreateAsync(routeKm: 40m);
        await w.AddSessionAsync(D(2026, 10, 1));

        var s = w.Data.Settings;
        (await w.Ledger.UpdateSettingsAsync(s with { ClinicRoute = s.ClinicRoute with { RoundTripKm = 50m } })).AssertOk();
        await w.AddSessionAsync(D(2026, 10, 2));

        Assert.Equal(40m, w.Data.Trips.Single(t => t.Date == D(2026, 10, 1)).DistanceKm);
        Assert.Equal(50m, w.Data.Trips.Single(t => t.Date == D(2026, 10, 2)).DistanceKm);
    }

    [Fact]
    public async Task Missing_distances_can_be_filled_in_explicitly_later()
    {
        var w = await TestWorld.CreateAsync(routeKm: null);
        await w.AddSessionAsync(D(2026, 10, 1));
        (await w.Ledger.SaveTripAsync(Manual(w, D(2026, 10, 5), km: 30m))).AssertOk();

        var s = w.Data.Settings;
        (await w.Ledger.UpdateSettingsAsync(s with { ClinicRoute = s.ClinicRoute with { RoundTripKm = 64m } })).AssertOk();
        var filled = await w.Ledger.FillMissingClinicTripDistancesAsync();

        Assert.Equal(1, filled);
        Assert.Equal(64m, w.Data.Trips.Single(t => t.Date == D(2026, 10, 1)).DistanceKm);
        Assert.Equal(30m, w.Data.Trips.Single(t => t.Date == D(2026, 10, 5)).DistanceKm);
    }

    [Fact]
    public async Task Deleting_the_last_session_removes_only_the_untouched_automatic_trip()
    {
        var w = await TestWorld.CreateAsync();
        var a = await w.AddSessionAsync(D(2026, 10, 1));
        var b = await w.AddSessionAsync(D(2026, 10, 1));
        (await w.Ledger.SaveTripAsync(Manual(w, D(2026, 10, 1), "Indkøb", clinic: false))).AssertOk();

        await w.Ledger.DeleteSessionAsync(a.Id);
        Assert.Equal(2, w.Data.Trips.Count); // another session remains

        var deleted = await w.Ledger.DeleteSessionAsync(b.Id);
        var remaining = Assert.Single(w.Data.Trips);
        Assert.Equal("Indkøb", remaining.Purpose);
        Assert.Contains(deleted!.Notes, n => n.Contains("automatiske kørsel"));
    }

    [Fact]
    public async Task Session_deletion_never_removes_a_manual_or_edited_trip()
    {
        var w = await TestWorld.CreateAsync();
        var s = await w.AddSessionAsync(D(2026, 10, 1));
        var trip = w.Data.Trips.Single();
        (await w.Ledger.SaveTripAsync(new TripDraft
        {
            Id = trip.Id, Date = trip.Date, Purpose = "Klinik / sessioner + indkøb", FromLabel = trip.FromLabel, ToLabel = trip.ToLabel,
            DistanceKm = 50m, IsClinicTrip = true, IncludeInTaxReport = true,
        })).AssertOk();

        await w.Ledger.DeleteSessionAsync(s.Id);

        Assert.Single(w.Data.Trips);
    }

    [Fact]
    public async Task Moving_a_session_to_another_date_moves_the_automatic_trip()
    {
        var w = await TestWorld.CreateAsync();
        var s = await w.AddSessionAsync(D(2026, 10, 1));

        var result = await w.Ledger.SaveSessionAsync(w.Draft(D(2026, 10, 2), 800m) with { Id = s.Id });

        result.AssertOk();
        Assert.Equal(D(2026, 10, 2), Assert.Single(w.Data.Trips).Date);
    }

    [Fact]
    public async Task A_trip_Sofie_deleted_is_not_recreated_for_another_session_that_day()
    {
        var w = await TestWorld.CreateAsync();
        await w.AddSessionAsync(D(2026, 10, 1));
        await w.Ledger.DeleteTripAsync(w.Data.Trips.Single().Id);

        await w.AddSessionAsync(D(2026, 10, 1));

        Assert.Empty(w.Data.Trips);
    }

    [Fact]
    public async Task Only_one_clinic_trip_per_date()
    {
        var w = await TestWorld.CreateAsync();
        await w.AddSessionAsync(D(2026, 10, 1));
        (await w.Ledger.SaveTripAsync(Manual(w, D(2026, 10, 1)))).AssertError("date");
        (await w.Ledger.SaveTripAsync(Manual(w, D(2026, 10, 1), "Indkøb", clinic: false))).AssertOk();
    }

    [Fact]
    public async Task Automatic_trips_can_be_switched_off()
    {
        var w = await TestWorld.CreateAsync();
        var s = w.Data.Settings;
        (await w.Ledger.UpdateSettingsAsync(s with { ClinicRoute = s.ClinicRoute with { AutoRegisterTrips = false } })).AssertOk();
        await w.AddSessionAsync(D(2026, 10, 1));
        Assert.Empty(w.Data.Trips);
    }

    [Fact]
    public async Task Undo_of_a_session_deletion_does_not_create_a_second_clinic_trip()
    {
        var w = await TestWorld.CreateAsync();
        var s = await w.AddSessionAsync(D(2026, 10, 1));
        var deleted = await w.Ledger.DeleteSessionAsync(s.Id);
        (await w.Ledger.SaveTripAsync(Manual(w, D(2026, 10, 1)))).AssertOk();

        await w.Ledger.UndoDeleteAsync(deleted!);

        Assert.Single(w.Data.Trips);
        Assert.Single(w.Data.Sessions);
    }

    [Fact]
    public async Task Trip_validation()
    {
        var w = await TestWorld.CreateAsync();
        (await w.Ledger.SaveTripAsync(Manual(w, D(2026, 10, 1)) with { Purpose = " " })).AssertError("purpose");
        (await w.Ledger.SaveTripAsync(Manual(w, D(2026, 10, 1)) with { DistanceKm = 0m })).AssertError("distance");
        (await w.Ledger.SaveTripAsync(Manual(w, D(2026, 10, 1)) with { ToLabel = "" })).AssertError("to");
    }
}
