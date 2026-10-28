using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SofiesUnivers.App;
using SofiesUnivers.App.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// In Blazor WebAssembly every service lives for the lifetime of the app.
builder.Services.AddSingleton<IndexedDbStore>();
builder.Services.AddSingleton<AppState>();
builder.Services.AddSingleton<BrowserApp>();
builder.Services.AddSingleton<BrowserFiles>();
builder.Services.AddSingleton<ThemeService>();
builder.Services.AddSingleton<ToastService>();
builder.Services.AddSingleton<Prefs>();

await builder.Build().RunAsync();
