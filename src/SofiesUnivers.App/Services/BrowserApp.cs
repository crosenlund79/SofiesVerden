using Microsoft.JSInterop;

namespace SofiesUnivers.App.Services;

/// <summary>Theme, preferences, install state and update notifications (see wwwroot/js/app.js).</summary>
public sealed class BrowserApp(IJSRuntime js) : IAsyncDisposable
{
    private IJSObjectReference? _module;
    private DotNetObjectReference<BrowserApp>? _self;

    public event Action? UpdateAvailable;

    public bool IsUpdateAvailable { get; private set; }

    private async ValueTask<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>("import", "./js/app.js");

    public async Task<string?> GetThemeAsync() => await (await ModuleAsync()).InvokeAsync<string?>("getTheme");

    public async Task SetThemeAsync(string theme) => await (await ModuleAsync()).InvokeVoidAsync("setTheme", theme);

    public async Task<string?> GetPrefAsync(string key) => await (await ModuleAsync()).InvokeAsync<string?>("getPref", key);

    public async Task SetPrefAsync(string key, string? value) => await (await ModuleAsync()).InvokeVoidAsync("setPref", key, value);

    public async Task<bool> IsStandaloneAsync() => await (await ModuleAsync()).InvokeAsync<bool>("isStandalone");

    public async Task<bool> IsIosAsync() => await (await ModuleAsync()).InvokeAsync<bool>("isIos");

    public async Task<bool> ConfirmAsync(string message) => await (await ModuleAsync()).InvokeAsync<bool>("confirmLeave", message);

    public async Task ScrollToTopAsync() => await (await ModuleAsync()).InvokeVoidAsync("scrollToTop");

    public async Task ScrollIntoViewAsync(string selector) => await (await ModuleAsync()).InvokeVoidAsync("scrollIntoView", selector);

    public async Task<string> UserAgentAsync() => await (await ModuleAsync()).InvokeAsync<string>("userAgent");

    public async Task WatchForUpdatesAsync()
    {
        _self ??= DotNetObjectReference.Create(this);
        await (await ModuleAsync()).InvokeVoidAsync("watchForUpdates", _self);
    }

    public async Task ApplyUpdateAsync() => await (await ModuleAsync()).InvokeVoidAsync("applyUpdate");

    [JSInvokable]
    public void OnUpdateAvailable()
    {
        IsUpdateAvailable = true;
        UpdateAvailable?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        _self?.Dispose();
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
