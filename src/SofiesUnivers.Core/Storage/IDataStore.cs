using SofiesUnivers.Core.Model;

namespace SofiesUnivers.Core.Storage;

/// <summary>
/// Durable storage boundary. The browser implementation uses IndexedDB; tests use an in-memory
/// implementation that goes through the same JSON format.
/// Every method that writes must be atomic: either everything is stored or nothing is.
/// </summary>
public interface IDataStore
{
    /// <summary>Returns all record stores as JSON: <c>{"sessions":[...],"settings":[...],...}</c>.</summary>
    Task<string> LoadAllJsonAsync();

    /// <summary>Applies a list of put/delete operations (JSON, see <see cref="DataSerializer.SerializeOperations"/>) and an optional attachment in one transaction.</summary>
    Task CommitAsync(string operationsJson, AttachmentBlob? attachment);

    Task<AttachmentBlob?> GetAttachmentAsync(Guid id);

    /// <summary>
    /// Writes an attachment that is not yet referenced by any record. Used while staging a restore.
    /// Unreferenced attachments are harmless and removed by <see cref="DeleteAttachmentsAsync"/> during cleanup.
    /// </summary>
    Task PutAttachmentAsync(AttachmentBlob attachment);

    Task<IReadOnlyList<Guid>> ListAttachmentIdsAsync();

    Task DeleteAttachmentsAsync(IReadOnlyCollection<Guid> ids);

    /// <summary>
    /// Atomically replaces every record store with the given content (JSON, same shape as
    /// <see cref="LoadAllJsonAsync"/>) and deletes all attachments not listed in <paramref name="keepAttachmentIds"/>.
    /// </summary>
    Task ReplaceAllAsync(string allStoresJson, IReadOnlyCollection<Guid> keepAttachmentIds);
}

/// <summary>Thrown when the browser storage refuses an operation (quota, private mode, aborted transaction…).</summary>
public sealed class StorageException(string message, Exception? inner = null) : Exception(message, inner);

public static class StoreNames
{
    public const string Sessions = "sessions";
    public const string Payments = "payments";
    public const string ElementTypes = "elementTypes";
    public const string ExpenseCategories = "expenseCategories";
    public const string PaymentMethods = "paymentMethods";
    public const string Expenses = "expenses";
    public const string Trips = "trips";
    public const string Settings = "settings";
    public const string Attachments = "attachments";

    public static readonly string[] RecordStores =
        [Sessions, Payments, ElementTypes, ExpenseCategories, PaymentMethods, Expenses, Trips, Settings];

    public static string ForLookup(LookupKind kind) => kind switch
    {
        LookupKind.SessionElement => ElementTypes,
        LookupKind.ExpenseCategory => ExpenseCategories,
        LookupKind.PaymentMethod => PaymentMethods,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };
}
