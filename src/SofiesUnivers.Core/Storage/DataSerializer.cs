using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using SofiesUnivers.Core.Model;

namespace SofiesUnivers.Core.Storage;

/// <summary>Converts between the domain model and the JSON stored in IndexedDB and in backups.</summary>
public static class DataSerializer
{
    private static readonly JsonSerializerOptions IndentedOptions = new(AppJsonContext.Default.Options) { WriteIndented = true };
    private static readonly AppJsonContext IndentedContext = new(IndentedOptions);

    /// <summary>Operations JSON for <see cref="IDataStore.CommitAsync"/>.</summary>
    public static string SerializeOperations(ChangeSet changes)
    {
        var ops = new JsonArray();
        foreach (var change in changes.Changes)
        {
            var op = new JsonObject { ["store"] = change.Store };
            if (change.IsDelete)
            {
                op["op"] = "delete";
                op["key"] = change.Key;
            }
            else
            {
                op["op"] = "put";
                op["value"] = ToNode(change.Value!);
            }

            ops.Add((JsonNode)op);
        }

        foreach (var id in changes.AttachmentDeletes)
        {
            ops.Add((JsonNode)new JsonObject { ["store"] = StoreNames.Attachments, ["op"] = "delete", ["key"] = id.ToString() });
        }

        return ops.ToJsonString();
    }

    public static JsonNode ToNode(object value) =>
        JsonSerializer.SerializeToNode(value, value.GetType(), AppJsonContext.Default)
        ?? throw new InvalidOperationException("Could not serialize " + value.GetType().Name);

    /// <summary>All record stores as JSON for <see cref="IDataStore.ReplaceAllAsync"/>.</summary>
    public static string SerializeAllStores(AppData data, BackupStatus backupStatus)
    {
        var root = new JsonObject
        {
            [StoreNames.Sessions] = Array(data.Sessions),
            [StoreNames.Payments] = Array(data.Payments),
            [StoreNames.ElementTypes] = Array(data.ElementTypes),
            [StoreNames.ExpenseCategories] = Array(data.ExpenseCategories),
            [StoreNames.PaymentMethods] = Array(data.PaymentMethods),
            [StoreNames.Expenses] = Array(data.Expenses),
            [StoreNames.Trips] = Array(data.Trips),
            [StoreNames.Settings] = new JsonArray(
                ToNode(data.Settings),
                ToNode(backupStatus),
                ToNode(new StorageMeta { DataVersion = data.DataVersion })),
        };
        return root.ToJsonString();
    }

    private static JsonArray Array<T>(IEnumerable<T> items) where T : notnull =>
        new(items.Select(i => (JsonNode?)ToNode(i)).ToArray());

    /// <summary>Result of reading the stores. <see cref="Data"/> is null for a brand new, empty database.</summary>
    public sealed record LoadedStores(AppData? Data, BackupStatus BackupStatus, int StoredVersion, bool WasMigrated, bool HasAnyRecords);

    public static LoadedStores ParseAllStores(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new FormatException("Tomt datasæt.");
        var settings = root[StoreNames.Settings]?.AsArray() ?? [];

        JsonObject? FindSetting(string id) =>
            settings.OfType<JsonObject>().FirstOrDefault(o => o["id"]?.GetValue<string>() == id);

        var backupNode = FindSetting(BackupStatus.RecordId);
        var backup = backupNode is null
            ? new BackupStatus()
            : backupNode.Deserialize(AppJsonContext.Default.BackupStatus) ?? new BackupStatus();

        var appNode = FindSetting(AppSettings.RecordId);
        if (appNode is null)
        {
            var hasRecords = StoreNames.RecordStores
                .Where(s => s != StoreNames.Settings)
                .Any(s => root[s] is JsonArray { Count: > 0 });
            return new LoadedStores(null, backup, AppData.CurrentVersion, false, hasRecords);
        }

        var version = FindSetting(StorageMeta.RecordId)?["dataVersion"]?.GetValue<int>() ?? 1;
        var shaped = new JsonObject
        {
            ["dataVersion"] = version,
            ["settings"] = appNode.DeepClone(),
            ["elementTypes"] = StoreArray(root, StoreNames.ElementTypes),
            ["expenseCategories"] = StoreArray(root, StoreNames.ExpenseCategories),
            ["paymentMethods"] = StoreArray(root, StoreNames.PaymentMethods),
            ["sessions"] = StoreArray(root, StoreNames.Sessions),
            ["payments"] = StoreArray(root, StoreNames.Payments),
            ["expenses"] = StoreArray(root, StoreNames.Expenses),
            ["trips"] = StoreArray(root, StoreNames.Trips),
        };

        var migrated = DataMigrator.Migrate(shaped);
        var data = shaped.Deserialize(AppJsonContext.Default.AppData)
                   ?? throw new FormatException("Data kunne ikke læses.");
        return new LoadedStores(data, backup, version, migrated, true);
    }

    private static JsonNode StoreArray(JsonObject root, string store) => root[store]?.DeepClone() ?? new JsonArray();

    /// <summary>Human-readable JSON of the complete dataset, used inside backups.</summary>
    public static string SerializeAppData(AppData data) => JsonSerializer.Serialize(data, IndentedContext.AppData);

    public static AppData DeserializeAppData(string json)
    {
        var root = JsonNode.Parse(json)?.AsObject() ?? throw new FormatException("Tomt datasæt.");
        DataMigrator.Migrate(root);
        return root.Deserialize(AppJsonContext.Default.AppData) ?? throw new FormatException("Data kunne ikke læses.");
    }

    /// <summary>
    /// A stable SHA-256 fingerprint of the dataset content. Independent of list order and of
    /// trailing zeros in decimals, so a workbook re-read from Excel produces the same value.
    /// </summary>
    public static string Fingerprint(AppData data)
    {
        var canonical = Canonicalize(data);
        var json = JsonSerializer.Serialize(canonical, AppJsonContext.Default.AppData);
        return Hashing.Sha256Hex(Encoding.UTF8.GetBytes(json));
    }

    public static AppData Canonicalize(AppData d) => d with
    {
        Settings = d.Settings with
        {
            ClinicRoute = d.Settings.ClinicRoute with { RoundTripKm = N(d.Settings.ClinicRoute.RoundTripKm) },
            Locations = d.Settings.Locations.OrderBy(l => l.Id).ToList(),
        },
        ElementTypes = d.ElementTypes.OrderBy(x => x.Id).ToList(),
        ExpenseCategories = d.ExpenseCategories.OrderBy(x => x.Id).ToList(),
        PaymentMethods = d.PaymentMethods.OrderBy(x => x.Id).ToList(),
        Sessions = d.Sessions.OrderBy(x => x.Id).Select(s => s with { Price = N(s.Price), ElementIds = s.ElementIds.ToList(), CreatedAt = s.CreatedAt.ToUniversalTime(), ModifiedAt = s.ModifiedAt.ToUniversalTime() }).ToList(),
        Payments = d.Payments.OrderBy(x => x.Id).Select(p => p with { Amount = N(p.Amount), CreatedAt = p.CreatedAt.ToUniversalTime(), ModifiedAt = p.ModifiedAt.ToUniversalTime() }).ToList(),
        Expenses = d.Expenses.OrderBy(x => x.Id).Select(e => e with { Amount = N(e.Amount), CreatedAt = e.CreatedAt.ToUniversalTime(), ModifiedAt = e.ModifiedAt.ToUniversalTime() }).ToList(),
        Trips = d.Trips.OrderBy(x => x.Id).Select(t => t with { DistanceKm = N(t.DistanceKm), CreatedAt = t.CreatedAt.ToUniversalTime(), ModifiedAt = t.ModifiedAt.ToUniversalTime() }).ToList(),
    };

    /// <summary>Removes trailing zeros: 800.50m becomes 800.5m.</summary>
    public static decimal N(decimal value) => value / 1.000000000000000000000000000000000m;

    public static decimal? N(decimal? value) => value is { } v ? N(v) : null;
}
