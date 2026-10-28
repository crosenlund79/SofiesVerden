namespace SofiesUnivers.Core.Model;

/// <summary>A session (the central business record). Payments are stored separately.</summary>
public sealed record Session
{
    public required Guid Id { get; init; }
    public required DateOnly Date { get; init; }

    /// <summary>One to three session element ids, in the order Sofie selected them.</summary>
    public required IReadOnlyList<Guid> ElementIds { get; init; }

    /// <summary>Total duration of the session in minutes.</summary>
    public required int DurationMinutes { get; init; }

    /// <summary>Total price in DKK. Never derived from duration or elements.</summary>
    public required decimal Price { get; init; }

    /// <summary>Optional free-text nickname. Treated as personal data.</summary>
    public string? ClientNickname { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset ModifiedAt { get; init; }
}

/// <summary>A payment received for a session. A session can have zero or more payments.</summary>
public sealed record Payment
{
    public required Guid Id { get; init; }
    public required Guid SessionId { get; init; }
    public required DateOnly Date { get; init; }
    public required decimal Amount { get; init; }
    public required Guid MethodId { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset ModifiedAt { get; init; }
}

public enum LookupKind
{
    SessionElement,
    ExpenseCategory,
    PaymentMethod,
}

/// <summary>
/// A reusable named value: a session element type, an expense category or a payment method.
/// Records reference lookups by id, so a rename is shown consistently everywhere.
/// Lookups are archived (hidden from pickers) rather than deleted, to keep history intact.
/// </summary>
public sealed record LookupItem
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public bool IsArchived { get; init; }
    public int SortOrder { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }
}

public sealed record Expense
{
    public required Guid Id { get; init; }
    public required DateOnly Date { get; init; }
    public required decimal Amount { get; init; }
    public required Guid CategoryId { get; init; }
    public string? Description { get; init; }

    /// <summary>Metadata for the optional single receipt. The bytes live in the attachment store.</summary>
    public AttachmentInfo? Attachment { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset ModifiedAt { get; init; }
}

public sealed record AttachmentInfo
{
    public required Guid Id { get; init; }
    public required string FileName { get; init; }
    public required string ContentType { get; init; }
    public required long SizeBytes { get; init; }

    /// <summary>Lower-case hex SHA-256 of the stored bytes.</summary>
    public required string Sha256 { get; init; }
}

public enum TripOrigin
{
    /// <summary>Created by the app because a session was registered on the date. Not yet edited by Sofie.</summary>
    Automatic,

    /// <summary>Registered or edited by Sofie.</summary>
    Manual,
}

/// <summary>
/// A driving record. Route labels and distance are stored on the record itself, so later
/// changes to the configured route never rewrite history.
/// </summary>
public sealed record Trip
{
    public required Guid Id { get; init; }
    public required DateOnly Date { get; init; }
    public required string Purpose { get; init; }
    public required string FromLabel { get; init; }
    public required string ToLabel { get; init; }

    /// <summary>Total distance driven for this record (round trip where relevant). Null when not yet known.</summary>
    public decimal? DistanceKm { get; init; }

    /// <summary>"Medtag i skatteoversigt". Sofie's own classification, not a legal assessment.</summary>
    public required bool IncludeInTaxReport { get; init; }

    public required TripOrigin Origin { get; init; }

    /// <summary>True when this is the (single) round trip to the clinic on this date.</summary>
    public required bool IsClinicTrip { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
    public required DateTimeOffset ModifiedAt { get; init; }
}

/// <summary>Raw attachment content as stored in the attachment store.</summary>
public sealed record AttachmentBlob(Guid Id, string FileName, string ContentType, byte[] Data);
