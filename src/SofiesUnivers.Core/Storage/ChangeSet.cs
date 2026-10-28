using SofiesUnivers.Core.Model;

namespace SofiesUnivers.Core.Storage;

/// <summary>A group of record changes that must be stored together in one transaction.</summary>
public sealed class ChangeSet
{
    private readonly List<RecordChange> _changes = [];

    public IReadOnlyList<RecordChange> Changes => _changes;

    /// <summary>At most one new attachment per change set (an expense has at most one receipt).</summary>
    public AttachmentBlob? AttachmentPut { get; private set; }

    public List<Guid> AttachmentDeletes { get; } = [];

    /// <summary>False for bookkeeping-only changes (backup status, defaults) that should not trigger a backup reminder.</summary>
    public bool CountsAsDataChange { get; init; } = true;

    public bool IsEmpty => _changes.Count == 0 && AttachmentPut is null && AttachmentDeletes.Count == 0;

    public ChangeSet Put(Session s) => Add(new RecordChange(StoreNames.Sessions, s.Id.ToString(), s));
    public ChangeSet Put(Payment p) => Add(new RecordChange(StoreNames.Payments, p.Id.ToString(), p));
    public ChangeSet Put(Expense e) => Add(new RecordChange(StoreNames.Expenses, e.Id.ToString(), e));
    public ChangeSet Put(Trip t) => Add(new RecordChange(StoreNames.Trips, t.Id.ToString(), t));
    public ChangeSet Put(LookupKind kind, LookupItem item) => Add(new RecordChange(StoreNames.ForLookup(kind), item.Id.ToString(), item));
    public ChangeSet Put(AppSettings s) => Add(new RecordChange(StoreNames.Settings, s.Id, s));
    public ChangeSet Put(BackupStatus s) => Add(new RecordChange(StoreNames.Settings, s.Id, s));
    public ChangeSet Put(StorageMeta m) => Add(new RecordChange(StoreNames.Settings, m.Id, m));

    public ChangeSet DeleteSession(Guid id) => Add(new RecordChange(StoreNames.Sessions, id.ToString(), null));
    public ChangeSet DeletePayment(Guid id) => Add(new RecordChange(StoreNames.Payments, id.ToString(), null));
    public ChangeSet DeleteExpense(Guid id) => Add(new RecordChange(StoreNames.Expenses, id.ToString(), null));
    public ChangeSet DeleteTrip(Guid id) => Add(new RecordChange(StoreNames.Trips, id.ToString(), null));

    public ChangeSet PutAttachment(AttachmentBlob blob)
    {
        if (AttachmentPut is not null)
        {
            throw new InvalidOperationException("Only one attachment can be written per change.");
        }

        AttachmentPut = blob;
        return this;
    }

    public ChangeSet DeleteAttachment(Guid id)
    {
        AttachmentDeletes.Add(id);
        return this;
    }

    private ChangeSet Add(RecordChange change)
    {
        // The last change for a key wins, matching IndexedDB semantics within one transaction.
        _changes.RemoveAll(c => c.Store == change.Store && c.Key == change.Key);
        _changes.Add(change);
        return this;
    }
}

/// <summary>A put (Value is not null) or a delete (Value is null) of one record.</summary>
public sealed record RecordChange(string Store, string Key, object? Value)
{
    public bool IsDelete => Value is null;
}
