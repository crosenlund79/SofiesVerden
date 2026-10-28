using SofiesUnivers.Core;
using SofiesUnivers.Core.Domain;
using SofiesUnivers.Core.Storage;

namespace SofiesUnivers.App.Services;

/// <summary>Holds the loaded <see cref="Ledger"/> for the lifetime of the app.</summary>
public sealed class AppState(IndexedDbStore store)
{
    private Ledger? _ledger;

    public Ledger Ledger => _ledger ?? throw new InvalidOperationException("Data er ikke indlæst endnu.");

    public bool IsReady => _ledger is not null;

    public string? LoadError { get; private set; }

    public string? LoadErrorDetail { get; private set; }

    public static string AppVersion =>
        typeof(AppState).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";

    /// <summary>Raised whenever data changes, so open pages can refresh.</summary>
    public event Action? Changed;

    public async Task InitializeAsync()
    {
        if (_ledger is not null)
        {
            return;
        }

        try
        {
            if (!await store.IsAvailableAsync())
            {
                LoadError = "Denne browser kan ikke gemme data. Brug Safari (ikke et privat vindue), og føj appen til hjemmeskærmen.";
                return;
            }

            _ledger = await Ledger.LoadAsync(store, SystemClock.Instance);
            _ledger.Changed += () => Changed?.Invoke();
            LoadError = null;
        }
        catch (UnsupportedDataVersionException ex)
        {
            LoadError = ex.Message;
        }
        catch (StorageException ex)
        {
            LoadError = "Dine data kunne ikke indlæses. Prøv at lukke og åbne appen igen.";
            LoadErrorDetail = ex.InnerException?.Message ?? ex.Message;
        }
    }
}
