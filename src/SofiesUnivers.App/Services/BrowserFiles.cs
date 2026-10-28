using Microsoft.JSInterop;

namespace SofiesUnivers.App.Services;

public enum ShareOutcome
{
    Shared,
    Downloaded,
    Cancelled,
    Failed,
}

/// <summary>Share sheet, downloads, previews and image compression (see wwwroot/js/files.js).</summary>
public sealed class BrowserFiles(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;

    private async ValueTask<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/files.js");

    /// <summary>Opens the share sheet where supported (iPhone), otherwise downloads. Call directly from a click handler.</summary>
    public async Task<(ShareOutcome Outcome, string? Error)> ShareOrDownloadAsync(byte[] bytes, string fileName, string mime, string title)
    {
        try
        {
            var module = await ModuleAsync();
            var result = await module.InvokeAsync<string>("shareOrDownload", bytes, fileName, mime, title);
            return result switch
            {
                "shared" => (ShareOutcome.Shared, null),
                "downloaded" => (ShareOutcome.Downloaded, null),
                "cancelled" => (ShareOutcome.Cancelled, null),
                _ => (ShareOutcome.Failed, result.StartsWith("failed:", StringComparison.Ordinal) ? result[7..] : result),
            };
        }
        catch (JSException ex)
        {
            return (ShareOutcome.Failed, ex.Message);
        }
    }

    public async Task<bool> CanShareFilesAsync()
    {
        try
        {
            return await (await ModuleAsync()).InvokeAsync<bool>("canShareFiles");
        }
        catch (JSException)
        {
            return false;
        }
    }

    public async Task DownloadAsync(byte[] bytes, string fileName, string mime) =>
        await (await ModuleAsync()).InvokeAsync<string>("downloadBytes", bytes, fileName, mime);

    public async Task<string> CreateObjectUrlAsync(byte[] bytes, string mime) =>
        await (await ModuleAsync()).InvokeAsync<string>("createObjectUrl", bytes, mime);

    public async Task RevokeObjectUrlAsync(string url)
    {
        try
        {
            await (await ModuleAsync()).InvokeVoidAsync("revokeObjectUrl", url);
        }
        catch (JSDisconnectedException)
        {
        }
    }

    public async Task<bool> OpenAsync(byte[] bytes, string mime) =>
        await (await ModuleAsync()).InvokeAsync<bool>("openBytes", bytes, mime);

    /// <summary>Returns a smaller JPEG version of a photo, or null when compression is not possible or not worth it.</summary>
    public async Task<byte[]?> CompressImageAsync(byte[] bytes, string mime, int maxSide = 2400, double quality = 0.85)
    {
        try
        {
            return await (await ModuleAsync()).InvokeAsync<byte[]?>("compressImage", bytes, mime, maxSide, quality);
        }
        catch (JSException)
        {
            return null;
        }
    }

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
