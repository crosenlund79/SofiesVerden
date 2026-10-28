using System.Text.Json.Serialization;

namespace SofiesUnivers.Core.Model;

public enum LocationKind
{
    Home,
    Clinic,
    Other,
}

/// <summary>A named place. Configuration data, never hardcoded business logic.</summary>
public sealed record Location
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public required string Address { get; init; }
    public required LocationKind Kind { get; init; }

    [JsonIgnore]
    public string Label => string.IsNullOrWhiteSpace(Address) ? Name : $"{Name} ({Address})";
}

/// <summary>The standard route used for automatic clinic trips.</summary>
public sealed record ClinicRoute
{
    public required Guid FromLocationId { get; init; }
    public required Guid ToLocationId { get; init; }

    /// <summary>Verified round-trip distance in km. Null until Sofie or the project owner enters it.</summary>
    public decimal? RoundTripKm { get; init; }

    /// <summary>Register a clinic round trip automatically when the first session on a date is saved.</summary>
    public bool AutoRegisterTrips { get; init; } = true;

    /// <summary>Default value of "Medtag i skatteoversigt" for automatically registered clinic trips.</summary>
    public bool IncludeInTaxReportByDefault { get; init; } = true;
}

/// <summary>Business configuration. Stored as a single record and included in backups.</summary>
public sealed record AppSettings
{
    public const string RecordId = "app";

    public string Id { get; init; } = RecordId;
    public required IReadOnlyList<Location> Locations { get; init; }
    public required ClinicRoute ClinicRoute { get; init; }

    /// <summary>Business-level VAT exemption. V1 never calculates VAT; this is informational.</summary>
    public bool VatExempt { get; init; } = true;

    public Location? FindLocation(Guid id) => Locations.FirstOrDefault(l => l.Id == id);
}

/// <summary>
/// Bookkeeping about backups. Not part of the backup itself, and changing it does not
/// count as a data change.
/// </summary>
public sealed record BackupStatus
{
    public const string RecordId = "backup";

    public string Id { get; init; } = RecordId;

    /// <summary>When the first data change was recorded on this device.</summary>
    public DateTimeOffset? TrackingStartedAt { get; init; }

    public DateTimeOffset? LastDataChangeAt { get; init; }

    /// <summary>A backup file was generated in the app.</summary>
    public DateTimeOffset? LastGeneratedAt { get; init; }

    /// <summary>The share sheet or a download was started. Does not prove the file was saved.</summary>
    public DateTimeOffset? LastSharedAt { get; init; }

    /// <summary>Sofie explicitly confirmed that the backup file was saved somewhere safe.</summary>
    public DateTimeOffset? LastConfirmedAt { get; init; }

    public DateTimeOffset? ReminderSnoozedUntil { get; init; }
}

/// <summary>Version bookkeeping stored next to the settings.</summary>
public sealed record StorageMeta
{
    public const string RecordId = "meta";

    public string Id { get; init; } = RecordId;
    public required int DataVersion { get; init; }
}
