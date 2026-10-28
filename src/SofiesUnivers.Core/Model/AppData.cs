using System.Text.Json.Serialization;

namespace SofiesUnivers.Core.Model;

/// <summary>
/// The complete persistent dataset (excluding attachment bytes and backup bookkeeping).
/// This is exactly what a backup must be able to reconstruct.
/// </summary>
public sealed record AppData
{
    /// <summary>Version of the data format. Bump together with a migration in <see cref="Storage.DataMigrator"/>.</summary>
    public const int CurrentVersion = 1;

    public int DataVersion { get; init; } = CurrentVersion;
    public required AppSettings Settings { get; init; }
    public required IReadOnlyList<LookupItem> ElementTypes { get; init; }
    public required IReadOnlyList<LookupItem> ExpenseCategories { get; init; }
    public required IReadOnlyList<LookupItem> PaymentMethods { get; init; }
    public required IReadOnlyList<Session> Sessions { get; init; }
    public required IReadOnlyList<Payment> Payments { get; init; }
    public required IReadOnlyList<Expense> Expenses { get; init; }
    public required IReadOnlyList<Trip> Trips { get; init; }

    [JsonIgnore]
    public bool HasBusinessRecords => Sessions.Count > 0 || Payments.Count > 0 || Expenses.Count > 0 || Trips.Count > 0;

    public IReadOnlyList<LookupItem> Lookups(LookupKind kind) => kind switch
    {
        LookupKind.SessionElement => ElementTypes,
        LookupKind.ExpenseCategory => ExpenseCategories,
        LookupKind.PaymentMethod => PaymentMethods,
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public string LookupName(LookupKind kind, Guid id) =>
        Lookups(kind).FirstOrDefault(l => l.Id == id)?.Name ?? "(ukendt)";

    public decimal PaidFor(Guid sessionId) => Payments.Where(p => p.SessionId == sessionId).Sum(p => p.Amount);

    public decimal OutstandingFor(Session s) => Balances.Outstanding(s.Price, PaidFor(s.Id));
}

public static class Balances
{
    /// <summary>Outstanding = price minus payments, never negative.</summary>
    public static decimal Outstanding(decimal price, decimal paid) => Math.Max(0m, price - paid);
}
