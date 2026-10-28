using System.Text.Json.Nodes;
using SofiesUnivers.Core.Model;

namespace SofiesUnivers.Core.Storage;

/// <summary>
/// Upgrades stored or backed-up data to <see cref="AppData.CurrentVersion"/>.
/// Migrations work on the JSON shape of <see cref="AppData"/>, so the same step upgrades both
/// IndexedDB content and old backup files.
///
/// To change the data format:
///  1. Bump <see cref="AppData.CurrentVersion"/>.
///  2. Add a step to <see cref="Steps"/> keyed by the old version that edits the JSON in place.
///  3. Add a test that migrates a sample of the old format.
/// </summary>
public static class DataMigrator
{
    internal static readonly Dictionary<int, Action<JsonObject>> Steps = new()
    {
        // [1] = root => { /* upgrade version 1 JSON to version 2 here */ },
    };

    /// <summary>Migrates in place. Returns true when a migration was applied.</summary>
    public static bool Migrate(JsonObject root) => Migrate(root, AppData.CurrentVersion, Steps);

    internal static bool Migrate(JsonObject root, int targetVersion, IReadOnlyDictionary<int, Action<JsonObject>> steps)
    {
        var version = root["dataVersion"]?.GetValue<int>() ?? 1;
        if (version > targetVersion)
        {
            throw new UnsupportedDataVersionException(version, targetVersion);
        }

        if (version < 1)
        {
            throw new FormatException($"Ugyldig dataversion {version}.");
        }

        var migrated = false;
        while (version < targetVersion)
        {
            if (!steps.TryGetValue(version, out var step))
            {
                throw new InvalidOperationException($"Mangler migrering fra version {version}.");
            }

            step(root);
            version++;
            root["dataVersion"] = version;
            migrated = true;
        }

        return migrated;
    }
}

public sealed class UnsupportedDataVersionException(int found, int supported)
    : Exception($"Data er fra en nyere version af appen (version {found}). Denne version understøtter op til version {supported}. Opdatér appen og prøv igen.")
{
    public int Found { get; } = found;
    public int Supported { get; } = supported;
}
