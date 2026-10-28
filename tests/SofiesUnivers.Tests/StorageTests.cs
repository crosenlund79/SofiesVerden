using System.Text.Json.Nodes;
using SofiesUnivers.Core.Domain;
using SofiesUnivers.Core.Model;
using SofiesUnivers.Core.Storage;
using SofiesUnivers.Tests.Infrastructure;

namespace SofiesUnivers.Tests;

public class StorageTests
{
    [Fact]
    public async Task First_start_seeds_defaults_once()
    {
        var w = await TestWorld.CreateAsync(routeKm: null);
        Assert.Equal(["Massage", "Sexologisk samtale"], w.Data.ElementTypes.Select(e => e.Name));
        Assert.Equal(["MobilePay", "Kontant", "Bankoverførsel"], w.Data.PaymentMethods.Select(e => e.Name));
        Assert.Equal(6, w.Data.ExpenseCategories.Count);
        Assert.Null(w.Data.Settings.ClinicRoute.RoundTripKm);
        Assert.Contains(w.Data.Settings.Locations, l => l.Address.Contains("Hundtofte 14, 5771 Stenstrup"));

        var ids = w.Data.ElementTypes.Select(e => e.Id).ToList();
        await w.RestartAsync();
        Assert.Equal(ids, w.Data.ElementTypes.Select(e => e.Id));
        Assert.False(w.Data.HasBusinessRecords);
    }

    [Fact]
    public async Task Incomplete_storage_is_never_overwritten()
    {
        var w = await TestWorld.CreateAsync();
        await w.AddSessionAsync(new DateOnly(2026, 10, 1));
        w.Store.Tamper(StoreNames.Settings, s => s.Remove(AppSettings.RecordId));

        await Assert.ThrowsAsync<StorageException>(() => w.RestartAsync());
        Assert.Equal(1, w.Store.Count(StoreNames.Sessions));
    }

    [Fact]
    public async Task Data_from_a_newer_version_is_not_touched()
    {
        var w = await TestWorld.CreateAsync();
        w.Store.Tamper(StoreNames.Settings, s => s[StorageMeta.RecordId] = new JsonObject { ["id"] = "meta", ["dataVersion"] = 99 });
        await Assert.ThrowsAsync<UnsupportedDataVersionException>(() => w.RestartAsync());
    }

    [Fact]
    public void Migrations_run_in_order_and_refuse_unknown_versions()
    {
        var steps = new Dictionary<int, Action<JsonObject>>
        {
            [1] = root => root["sessions"]!.AsArray().Add((JsonNode)new JsonObject { ["migrated"] = 2 }),
            [2] = root => root["note"] = "v3",
        };
        var root = new JsonObject { ["dataVersion"] = 1, ["sessions"] = new JsonArray() };

        Assert.True(DataMigrator.Migrate(root, 3, steps));
        Assert.Equal(3, root["dataVersion"]!.GetValue<int>());
        Assert.Equal("v3", root["note"]!.GetValue<string>());
        Assert.Single(root["sessions"]!.AsArray());

        Assert.False(DataMigrator.Migrate(root, 3, steps));
        Assert.Throws<UnsupportedDataVersionException>(() => DataMigrator.Migrate(new JsonObject { ["dataVersion"] = 4 }, 3, steps));
        Assert.Throws<InvalidOperationException>(() => DataMigrator.Migrate(new JsonObject { ["dataVersion"] = 1 }, 3, new Dictionary<int, Action<JsonObject>>()));
    }

    [Fact]
    public async Task Orphan_receipts_are_cleaned_up_on_start()
    {
        var w = await TestWorld.CreateAsync();
        await w.Store.PutAttachmentAsync(new AttachmentBlob(Guid.NewGuid(), "x.jpg", "image/jpeg", TestFiles.Jpeg()));
        await w.RestartAsync();
        Assert.Empty(w.Store.Attachments);
    }

    [Fact]
    public async Task Stored_json_uses_stable_readable_names()
    {
        var w = await TestWorld.CreateAsync();
        await w.AddPaidSessionAsync(new DateOnly(2026, 10, 1), 800m, "Kunde 1");
        var json = JsonNode.Parse(await w.Store.LoadAllJsonAsync())!;
        var session = json["sessions"]![0]!;
        Assert.Equal("2026-10-01", session["date"]!.GetValue<string>());
        Assert.Equal(800m, session["price"]!.GetValue<decimal>());
        Assert.Equal("Automatic", json["trips"]![0]!["origin"]!.GetValue<string>());
    }

    [Fact]
    public async Task Fingerprint_ignores_order_and_trailing_zeros()
    {
        var w = await ReportTests.SampleAsync();
        var shuffled = w.Data with
        {
            Sessions = w.Data.Sessions.Reverse().Select(s => s with { Price = s.Price * 1.00m }).ToList(),
            Payments = w.Data.Payments.Reverse().ToList(),
        };
        Assert.Equal(DataSerializer.Fingerprint(w.Data), DataSerializer.Fingerprint(shuffled));
        Assert.NotEqual(DataSerializer.Fingerprint(w.Data), DataSerializer.Fingerprint(shuffled with { Trips = [] }));
    }

    [Fact]
    public async Task Validator_accepts_real_data_and_reports_problems()
    {
        var w = await ReportTests.SampleAsync();
        Assert.Empty(DataValidator.Validate(w.Data, null));

        var session = w.Data.Sessions[0];
        var broken = w.Data with
        {
            Sessions = w.Data.Sessions.Append(session).ToList(), // duplicate id
            ElementTypes = w.Data.ElementTypes.Append(w.Data.ElementTypes[0] with { Id = Guid.NewGuid(), Name = "MASSAGE" }).ToList(),
            Trips = w.Data.Trips.Append(w.Data.Trips.First(t => t.IsClinicTrip) with { Id = Guid.NewGuid() }).ToList(),
        };
        var errors = DataValidator.Validate(broken, null);
        Assert.Contains(errors, e => e.Contains("flere gange"));
        Assert.Contains(errors, e => e.Contains("\"MASSAGE\"") || e.Contains("\"Massage\""));
        Assert.Contains(errors, e => e.Contains("mere end én tur"));
    }
}
