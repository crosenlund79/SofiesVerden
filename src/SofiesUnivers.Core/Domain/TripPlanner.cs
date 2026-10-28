using SofiesUnivers.Core.Model;

namespace SofiesUnivers.Core.Domain;

/// <summary>
/// Helpers for automatic clinic trips. The rules (applied by <see cref="Ledger"/>):
/// <list type="bullet">
/// <item>When the first session on a date is saved, one clinic round trip is created, unless a clinic trip (manual or automatic) already exists on that date.</item>
/// <item>When a date no longer has sessions, an <em>automatic</em> trip that Sofie never edited is removed. Manual or edited trips are never removed.</item>
/// </list>
/// </summary>
public static class TripPlanner
{
    /// <summary>The automatic trip that would be removed if the given date lost all its sessions.</summary>
    public static Trip? AutomaticTripOn(DateOnly date, IEnumerable<Trip> trips) =>
        trips.FirstOrDefault(t => t.Date == date && t.IsClinicTrip && t.Origin == TripOrigin.Automatic);

    public static Trip CreateAutomaticTrip(DateOnly date, AppSettings settings, DateTimeOffset now)
    {
        var route = settings.ClinicRoute;
        return new Trip
        {
            Id = Guid.NewGuid(),
            Date = date,
            Purpose = DefaultData.ClinicTripPurpose,
            FromLabel = settings.FindLocation(route.FromLocationId)?.Label ?? "Hjem",
            ToLabel = settings.FindLocation(route.ToLocationId)?.Label ?? "Klinikken",
            DistanceKm = route.RoundTripKm,
            IncludeInTaxReport = route.IncludeInTaxReportByDefault,
            Origin = TripOrigin.Automatic,
            IsClinicTrip = true,
            CreatedAt = now,
            ModifiedAt = now,
        };
    }
}
