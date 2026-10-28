namespace SofiesUnivers.App.Services;

/// <summary>Remembers recent choices on this device (non-critical, stored in localStorage).</summary>
public sealed class Prefs(BrowserApp browser)
{
    public async Task<IReadOnlyList<Guid>> LastElementsAsync()
    {
        var raw = await Safe(() => browser.GetPrefAsync("lastElements"));
        return (raw ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Guid.TryParse(s, out var g) ? g : Guid.Empty)
            .Where(g => g != Guid.Empty)
            .ToList();
    }

    public async Task<int?> LastDurationAsync() =>
        int.TryParse(await Safe(() => browser.GetPrefAsync("lastDuration")), out var m) && m > 0 ? m : null;

    public async Task<Guid?> LastPaymentMethodAsync() =>
        Guid.TryParse(await Safe(() => browser.GetPrefAsync("lastMethod")), out var g) ? g : null;

    public async Task RememberSessionAsync(IReadOnlyList<Guid> elements, int duration, Guid? method)
    {
        await Safe(async () => { await browser.SetPrefAsync("lastElements", string.Join(',', elements)); return ""; });
        await Safe(async () => { await browser.SetPrefAsync("lastDuration", duration.ToString(System.Globalization.CultureInfo.InvariantCulture)); return ""; });
        if (method is { } m)
        {
            await Safe(async () => { await browser.SetPrefAsync("lastMethod", m.ToString()); return ""; });
        }
    }

    public async Task RememberMethodAsync(Guid method) =>
        await Safe(async () => { await browser.SetPrefAsync("lastMethod", method.ToString()); return ""; });

    private static async Task<string?> Safe(Func<Task<string?>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception)
        {
            return null;
        }
    }
}
