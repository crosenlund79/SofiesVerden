using System.Text.Json;
using Microsoft.JSInterop;
using SofiesUnivers.Core.Model;
using SofiesUnivers.Core.Storage;

namespace SofiesUnivers.App.Services;

/// <summary><see cref="IDataStore"/> backed by the browser's IndexedDB (see wwwroot/js/storage.js).</summary>
public sealed class IndexedDbStore(IJSRuntime js) : IDataStore, IAsyncDisposable
{
    private IJSObjectReference? _module;

    private async ValueTask<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/storage.js");

    private async Task<T> Call<T>(string fn, params object?[] args)
    {
        try
        {
            var module = await ModuleAsync();
            return await module.InvokeAsync<T>(fn, args);
        }
        catch (JSException ex)
        {
            throw new StorageException(ex.Message, ex);
        }
    }

    private async Task Call(string fn, params object?[] args)
    {
        try
        {
            var module = await ModuleAsync();
            await module.InvokeVoidAsync(fn, args);
        }
        catch (JSException ex)
        {
            throw new StorageException(ex.Message, ex);
        }
    }

    public Task<bool> IsAvailableAsync() => Call<bool>("isAvailable");

    public Task<string> LoadAllJsonAsync() => Call<string>("loadAll");

    public Task CommitAsync(string operationsJson, AttachmentBlob? attachment) =>
        attachment is null
            ? Call("commit", operationsJson, null, null)
            : Call("commit", operationsJson, MetaJson(attachment), attachment.Data);

    public async Task<AttachmentBlob?> GetAttachmentAsync(Guid id)
    {
        var metaJson = await Call<string?>("getAttachment", id.ToString());
        if (metaJson is null)
        {
            return null;
        }

        using var meta = JsonDocument.Parse(metaJson);
        var bytes = await Call<byte[]?>("getAttachmentBytes", id.ToString());
        if (bytes is null)
        {
            return null;
        }

        return new AttachmentBlob(
            id,
            meta.RootElement.GetProperty("fileName").GetString() ?? "kvittering",
            meta.RootElement.GetProperty("contentType").GetString() ?? "application/octet-stream",
            bytes);
    }

    public Task PutAttachmentAsync(AttachmentBlob attachment) => Call("putAttachment", MetaJson(attachment), attachment.Data);

    public async Task<IReadOnlyList<Guid>> ListAttachmentIdsAsync()
    {
        var ids = await Call<string[]>("listAttachmentIds");
        return ids.Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty).Where(g => g != Guid.Empty).ToList();
    }

    public Task DeleteAttachmentsAsync(IReadOnlyCollection<Guid> ids) =>
        Call("deleteAttachments", ids.Select(i => i.ToString()).ToArray());

    public Task ReplaceAllAsync(string allStoresJson, IReadOnlyCollection<Guid> keepAttachmentIds) =>
        Call("replaceAll", allStoresJson, keepAttachmentIds.Select(i => i.ToString()).ToArray());

    public async Task<StorageInfo> GetStorageInfoAsync()
    {
        try
        {
            var json = await Call<string>("storageInfo");
            return JsonSerializer.Deserialize(json, AppInteropJson.Default.StorageInfo) ?? new StorageInfo();
        }
        catch (Exception)
        {
            return new StorageInfo();
        }
    }

    public async Task<bool> RequestPersistenceAsync()
    {
        try
        {
            return await Call<bool>("requestPersistence");
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string MetaJson(AttachmentBlob a) =>
        JsonSerializer.Serialize(new AttachmentMeta(a.Id.ToString(), a.FileName, a.ContentType), AppInteropJson.Default.AttachmentMeta);

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
            }
        }
    }
}

public sealed record AttachmentMeta(string Id, string FileName, string ContentType);

public sealed record StorageInfo
{
    public bool? Persisted { get; init; }
    public long? Usage { get; init; }
    public long? Quota { get; init; }
    public bool PersistSupported { get; init; }
}

[System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNamingPolicy = System.Text.Json.Serialization.JsonKnownNamingPolicy.CamelCase)]
[System.Text.Json.Serialization.JsonSerializable(typeof(AttachmentMeta))]
[System.Text.Json.Serialization.JsonSerializable(typeof(StorageInfo))]
[System.Text.Json.Serialization.JsonSerializable(typeof(FileInfoDto))]
public sealed partial class AppInteropJson : System.Text.Json.Serialization.JsonSerializerContext;

public sealed record FileInfoDto(string Name, string Type, long Size);
