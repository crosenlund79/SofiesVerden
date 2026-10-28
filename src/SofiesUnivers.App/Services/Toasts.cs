namespace SofiesUnivers.App.Services;

public enum ToastKind
{
    Success,
    Info,
    Error,
}

public sealed record Toast(Guid Id, string Message, ToastKind Kind, IReadOnlyList<string> Details, string? ActionLabel, Func<Task>? Action);

/// <summary>Short, non-blocking messages ("Gemt", "Slettet – Fortryd").</summary>
public sealed class ToastService
{
    private readonly List<Toast> _toasts = [];

    public IReadOnlyList<Toast> Current => _toasts;

    public event Action? Changed;

    public void Success(string message, IReadOnlyList<string>? details = null) => Show(message, ToastKind.Success, details);

    public void Info(string message, IReadOnlyList<string>? details = null) => Show(message, ToastKind.Info, details);

    public void Error(string message, IReadOnlyList<string>? details = null) => Show(message, ToastKind.Error, details, timeout: TimeSpan.FromSeconds(10));

    public void WithUndo(string message, Func<Task> undo, IReadOnlyList<string>? details = null) =>
        Show(message, ToastKind.Info, details, "Fortryd", undo, TimeSpan.FromSeconds(10));

    public void Show(string message, ToastKind kind, IReadOnlyList<string>? details = null, string? actionLabel = null, Func<Task>? action = null, TimeSpan? timeout = null)
    {
        var toast = new Toast(Guid.NewGuid(), message, kind, details ?? [], actionLabel, action);
        _toasts.Clear(); // one message at a time keeps the screen calm
        _toasts.Add(toast);
        Changed?.Invoke();
        var delay = timeout ?? TimeSpan.FromSeconds(details is { Count: > 0 } ? 7 : 4);
        _ = DismissLater(toast.Id, delay);
    }

    public void Dismiss(Guid id)
    {
        if (_toasts.RemoveAll(t => t.Id == id) > 0)
        {
            Changed?.Invoke();
        }
    }

    private async Task DismissLater(Guid id, TimeSpan delay)
    {
        await Task.Delay(delay);
        Dismiss(id);
    }
}
