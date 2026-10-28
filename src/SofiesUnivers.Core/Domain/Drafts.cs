using SofiesUnivers.Core.Model;

namespace SofiesUnivers.Core.Domain;

/// <summary>Input from the session form. <see cref="Id"/> is null for a new session.</summary>
public sealed record SessionDraft
{
    public Guid? Id { get; init; }
    public required DateOnly Date { get; init; }
    public required IReadOnlyList<Guid> ElementIds { get; init; }
    public required int DurationMinutes { get; init; }
    public required decimal Price { get; init; }
    public string? ClientNickname { get; init; }

    /// <summary>
    /// The complete list of payments for the session as shown in the form. Existing payments keep
    /// their id; a payment missing from the list was explicitly removed by Sofie.
    /// </summary>
    public IReadOnlyList<PaymentDraft> Payments { get; init; } = [];
}

public sealed record PaymentDraft
{
    public Guid? Id { get; init; }
    public required DateOnly Date { get; init; }
    public required decimal Amount { get; init; }
    public required Guid MethodId { get; init; }
}

public sealed record ExpenseDraft
{
    public Guid? Id { get; init; }
    public required DateOnly Date { get; init; }
    public required decimal Amount { get; init; }
    public required Guid CategoryId { get; init; }
    public string? Description { get; init; }
    public AttachmentChange Attachment { get; init; } = AttachmentChange.Keep;
}

/// <summary>What should happen to an expense's receipt when the expense is saved.</summary>
public abstract record AttachmentChange
{
    public static readonly AttachmentChange Keep = new KeepAttachment();
    public static readonly AttachmentChange Remove = new RemoveAttachment();

    public static AttachmentChange Replace(string fileName, byte[] data) => new ReplaceAttachment(fileName, data);

    public sealed record KeepAttachment : AttachmentChange;

    public sealed record RemoveAttachment : AttachmentChange;

    public sealed record ReplaceAttachment(string FileName, byte[] Data) : AttachmentChange;
}

public sealed record TripDraft
{
    public Guid? Id { get; init; }
    public required DateOnly Date { get; init; }
    public required string Purpose { get; init; }
    public required string FromLabel { get; init; }
    public required string ToLabel { get; init; }
    public decimal? DistanceKm { get; init; }
    public required bool IncludeInTaxReport { get; init; }
    public required bool IsClinicTrip { get; init; }
}

public sealed record ValidationError(string Field, string Message);

/// <summary>Outcome of a save. On failure nothing was stored.</summary>
public sealed record SaveResult<T>
{
    public T? Value { get; init; }
    public IReadOnlyList<ValidationError> Errors { get; init; } = [];

    /// <summary>Friendly notes about side effects, e.g. an automatically registered trip.</summary>
    public IReadOnlyList<string> Notes { get; init; } = [];

    public bool Ok => Errors.Count == 0;

    public static SaveResult<T> Failed(IReadOnlyList<ValidationError> errors) => new() { Errors = errors };

    public static SaveResult<T> Failed(string field, string message) => new() { Errors = [new ValidationError(field, message)] };

    public static SaveResult<T> Success(T value, IReadOnlyList<string>? notes = null) => new() { Value = value, Notes = notes ?? [] };
}

/// <summary>Everything removed by a delete, so it can be undone ("Fortryd").</summary>
public sealed record DeletedRecords
{
    public IReadOnlyList<Session> Sessions { get; init; } = [];
    public IReadOnlyList<Payment> Payments { get; init; } = [];
    public IReadOnlyList<Expense> Expenses { get; init; } = [];
    public IReadOnlyList<Trip> Trips { get; init; } = [];
    public AttachmentBlob? Attachment { get; init; }
    public IReadOnlyList<string> Notes { get; init; } = [];
}

/// <summary>Consequences of deleting a session, shown before Sofie confirms.</summary>
public sealed record SessionDeletionPreview(Session Session, IReadOnlyList<Payment> Payments, Trip? AutomaticTripRemoved);
