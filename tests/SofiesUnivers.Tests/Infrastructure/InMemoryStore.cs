using System.Text.Json.Nodes;
using SofiesUnivers.Core.Model;
using SofiesUnivers.Core.Storage;

namespace SofiesUnivers.Tests.Infrastructure;

/// <summary>
/// An <see cref="IDataStore"/> that behaves like the IndexedDB implementation: JSON records keyed by
/// "id", one store per record type, and all-or-nothing writes. Failures can be injected.
/// </summary>
public sealed class InMemoryStore : IDataStore
{
    private Dictionary<string, Dictionary<string, JsonObject>> _stores = NewStores();
    private Dictionary<Guid, AttachmentBlob> _attachments = [];

    public bool FailNextCommit { get; set; }
    public bool FailNextReplace { get; set; }
    public int? FailAttachmentPutAfter { get; set; }
    public int CommitCount { get; private set; }

    private int _attachmentPuts;

    private static Dictionary<string, Dictionary<string, JsonObject>> NewStores() =>
        StoreNames.RecordStores.ToDictionary(s => s, _ => new Dictionary<string, JsonObject>());

    public IReadOnlyDictionary<Guid, AttachmentBlob> Attachments => _attachments;

    public int Count(string store) => _stores[store].Count;

    public Task<string> LoadAllJsonAsync()
    {
        var root = new JsonObject();
        foreach (var (name, records) in _stores)
        {
            root[name] = new JsonArray(records.Values.Select(r => (JsonNode)r.DeepClone()).ToArray());
        }

        return Task.FromResult(root.ToJsonString());
    }

    public Task CommitAsync(string operationsJson, AttachmentBlob? attachment)
    {
        if (FailNextCommit)
        {
            FailNextCommit = false;
            throw new StorageException("Simuleret fejl (QuotaExceededError).");
        }

        // Apply to copies and swap at the end: all or nothing.
        var stores = Clone(_stores);
        var attachments = new Dictionary<Guid, AttachmentBlob>(_attachments);
        foreach (var op in JsonNode.Parse(operationsJson)!.AsArray())
        {
            var store = op!["store"]!.GetValue<string>();
            var kind = op["op"]!.GetValue<string>();
            if (store == StoreNames.Attachments)
            {
                if (kind != "delete")
                {
                    throw new InvalidOperationException("Attachment puts go through the attachment argument.");
                }

                attachments.Remove(Guid.Parse(op["key"]!.GetValue<string>()));
                continue;
            }

            if (!stores.TryGetValue(store, out var records))
            {
                throw new StorageException($"Ukendt lager {store}");
            }

            if (kind == "put")
            {
                var value = op["value"]!.AsObject();
                var key = value["id"]?.GetValue<string>() ?? throw new StorageException("DataError: key path 'id' missing");
                records[key] = (JsonObject)value.DeepClone();
            }
            else
            {
                records.Remove(op["key"]!.GetValue<string>());
            }
        }

        if (attachment is not null)
        {
            attachments[attachment.Id] = attachment with { Data = attachment.Data.ToArray() };
        }

        _stores = stores;
        _attachments = attachments;
        CommitCount++;
        return Task.CompletedTask;
    }

    public Task<AttachmentBlob?> GetAttachmentAsync(Guid id) =>
        Task.FromResult(_attachments.TryGetValue(id, out var a) ? a with { Data = a.Data.ToArray() } : null);

    public Task PutAttachmentAsync(AttachmentBlob attachment)
    {
        _attachmentPuts++;
        if (FailAttachmentPutAfter is { } limit && _attachmentPuts > limit)
        {
            throw new StorageException("Simuleret fejl under kvitteringer.");
        }

        _attachments[attachment.Id] = attachment with { Data = attachment.Data.ToArray() };
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Guid>> ListAttachmentIdsAsync() => Task.FromResult<IReadOnlyList<Guid>>(_attachments.Keys.ToList());

    public Task DeleteAttachmentsAsync(IReadOnlyCollection<Guid> ids)
    {
        foreach (var id in ids)
        {
            _attachments.Remove(id);
        }

        return Task.CompletedTask;
    }

    public Task ReplaceAllAsync(string allStoresJson, IReadOnlyCollection<Guid> keepAttachmentIds)
    {
        var root = JsonNode.Parse(allStoresJson)!.AsObject();
        var stores = NewStores();
        foreach (var name in StoreNames.RecordStores)
        {
            var array = root[name] as JsonArray ?? throw new StorageException($"Gendannelsesdata mangler \"{name}\".");
            foreach (var record in array)
            {
                var obj = record!.AsObject();
                var key = obj["id"]?.GetValue<string>() ?? throw new StorageException("DataError: key path 'id' missing");
                stores[name][key] = (JsonObject)obj.DeepClone();
            }
        }

        if (FailNextReplace)
        {
            FailNextReplace = false;
            throw new StorageException("Simuleret afbrudt transaktion.");
        }

        _stores = stores;
        _attachments = _attachments.Where(a => keepAttachmentIds.Contains(a.Key)).ToDictionary();
        return Task.CompletedTask;
    }

    /// <summary>Directly edits stored JSON (to simulate older formats or corruption).</summary>
    public void Tamper(string store, Action<Dictionary<string, JsonObject>> edit) => edit(_stores[store]);

    private static Dictionary<string, Dictionary<string, JsonObject>> Clone(Dictionary<string, Dictionary<string, JsonObject>> source) =>
        source.ToDictionary(s => s.Key, s => s.Value.ToDictionary(r => r.Key, r => r.Value));
}

public sealed class TestClock : SofiesUnivers.Core.IClock
{
    private DateTimeOffset _now = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Moves forward by one millisecond on every read, so records get distinct, realistic timestamps.</summary>
    public DateTimeOffset UtcNow
    {
        get => _now = _now.AddMilliseconds(1);
        set => _now = value;
    }

    public DateOnly Today => DateOnly.FromDateTime(_now.UtcDateTime);

    public void Advance(TimeSpan by) => _now += by;
}
