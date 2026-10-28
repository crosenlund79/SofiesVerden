using SofiesUnivers.Core.Model;
using SofiesUnivers.Core.Text;

namespace SofiesUnivers.Core.Reports;

public sealed record MonthFigures(int Month, int SessionCount, decimal SessionValue, decimal PaymentsReceived, decimal Expenses);

public sealed record NamedTotal(Guid Id, string Name, decimal Amount, int Count);

/// <summary>
/// Annual figures. Each figure is grouped by its own date:
/// session value by session date, payments by payment date, expenses by expense date and driving by trip date.
/// </summary>
public sealed record YearSummary
{
    public required int Year { get; init; }
    public required int SessionCount { get; init; }
    public required int TotalMinutes { get; init; }

    /// <summary>Sum of prices of sessions dated in the year.</summary>
    public required decimal SessionValue { get; init; }

    /// <summary>Sum of payments dated in the year (cash received), whatever the session date.</summary>
    public required decimal PaymentsReceived { get; init; }

    /// <summary>Outstanding today on sessions dated in the year.</summary>
    public required decimal OutstandingOnYearSessions { get; init; }

    public required decimal ExpensesTotal { get; init; }
    public required int ExpenseCount { get; init; }
    public required int ExpensesWithoutReceipt { get; init; }
    public required IReadOnlyList<NamedTotal> ExpensesByCategory { get; init; }
    public required IReadOnlyList<NamedTotal> PaymentsByMethod { get; init; }
    public required IReadOnlyList<NamedTotal> SessionsByElement { get; init; }
    public required int TripCount { get; init; }
    public required decimal DrivingKm { get; init; }
    public required int TripsInTaxReport { get; init; }
    public required decimal DrivingKmInTaxReport { get; init; }
    public required int TripsMissingKm { get; init; }
    public required IReadOnlyList<MonthFigures> Months { get; init; }
}

public sealed record OutstandingSession(Session Session, decimal Paid, decimal Outstanding);

public static class ReportCalculator
{
    public static YearSummary ForYear(AppData data, int year)
    {
        var sessions = data.Sessions.Where(s => s.Date.Year == year).ToList();
        var payments = data.Payments.Where(p => p.Date.Year == year).ToList();
        var expenses = data.Expenses.Where(e => e.Date.Year == year).ToList();
        var trips = data.Trips.Where(t => t.Date.Year == year).ToList();
        var taxTrips = trips.Where(t => t.IncludeInTaxReport).ToList();

        var months = Enumerable.Range(1, 12).Select(m => new MonthFigures(
            m,
            sessions.Count(s => s.Date.Month == m),
            sessions.Where(s => s.Date.Month == m).Sum(s => s.Price),
            payments.Where(p => p.Date.Month == m).Sum(p => p.Amount),
            expenses.Where(e => e.Date.Month == m).Sum(e => e.Amount))).ToList();

        return new YearSummary
        {
            Year = year,
            SessionCount = sessions.Count,
            TotalMinutes = sessions.Sum(s => s.DurationMinutes),
            SessionValue = sessions.Sum(s => s.Price),
            PaymentsReceived = payments.Sum(p => p.Amount),
            OutstandingOnYearSessions = sessions.Sum(data.OutstandingFor),
            ExpensesTotal = expenses.Sum(e => e.Amount),
            ExpenseCount = expenses.Count,
            ExpensesWithoutReceipt = expenses.Count(e => e.Attachment is null),
            ExpensesByCategory = expenses
                .GroupBy(e => e.CategoryId)
                .Select(g => new NamedTotal(g.Key, data.LookupName(LookupKind.ExpenseCategory, g.Key), g.Sum(e => e.Amount), g.Count()))
                .OrderByDescending(t => t.Amount).ThenBy(t => t.Name, DanishComparer.Instance)
                .ToList(),
            PaymentsByMethod = payments
                .GroupBy(p => p.MethodId)
                .Select(g => new NamedTotal(g.Key, data.LookupName(LookupKind.PaymentMethod, g.Key), g.Sum(p => p.Amount), g.Count()))
                .OrderByDescending(t => t.Amount).ThenBy(t => t.Name, DanishComparer.Instance)
                .ToList(),
            SessionsByElement = sessions
                .SelectMany(s => s.ElementIds)
                .GroupBy(id => id)
                .Select(g => new NamedTotal(g.Key, data.LookupName(LookupKind.SessionElement, g.Key), 0m, g.Count()))
                .OrderByDescending(t => t.Count).ThenBy(t => t.Name, DanishComparer.Instance)
                .ToList(),
            TripCount = trips.Count,
            DrivingKm = trips.Sum(t => t.DistanceKm ?? 0m),
            TripsInTaxReport = taxTrips.Count,
            DrivingKmInTaxReport = taxTrips.Sum(t => t.DistanceKm ?? 0m),
            TripsMissingKm = trips.Count(t => t.DistanceKm is null),
            Months = months,
        };
    }

    /// <summary>All years that contain data, plus the current year, newest first.</summary>
    public static IReadOnlyList<int> Years(AppData data, int currentYear) =>
        data.Sessions.Select(s => s.Date.Year)
            .Concat(data.Payments.Select(p => p.Date.Year))
            .Concat(data.Expenses.Select(e => e.Date.Year))
            .Concat(data.Trips.Select(t => t.Date.Year))
            .Append(currentYear)
            .Distinct()
            .OrderByDescending(y => y)
            .ToList();

    public static IReadOnlyList<OutstandingSession> OutstandingSessions(AppData data)
    {
        var paid = data.Payments.GroupBy(p => p.SessionId).ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
        return data.Sessions
            .Select(s => new OutstandingSession(s, paid.GetValueOrDefault(s.Id), Balances.Outstanding(s.Price, paid.GetValueOrDefault(s.Id))))
            .Where(o => o.Outstanding > 0)
            .OrderBy(o => o.Session.Date)
            .ThenBy(o => o.Session.CreatedAt)
            .ToList();
    }

    /// <summary>
    /// Neutral, stable references for reports: S-2026-001 for sessions and U-2026-001 for expenses,
    /// numbered by date within each year. They contain no client information.
    /// </summary>
    public static IReadOnlyDictionary<Guid, string> SessionReferences(AppData data) =>
        Number(data.Sessions.Select(s => (s.Id, s.Date, s.CreatedAt)), "S");

    public static IReadOnlyDictionary<Guid, string> ExpenseReferences(AppData data) =>
        Number(data.Expenses.Select(e => (e.Id, e.Date, e.CreatedAt)), "U");

    private static Dictionary<Guid, string> Number(IEnumerable<(Guid Id, DateOnly Date, DateTimeOffset CreatedAt)> items, string prefix)
    {
        var result = new Dictionary<Guid, string>();
        foreach (var year in items.GroupBy(i => i.Date.Year))
        {
            var n = 0;
            foreach (var item in year.OrderBy(i => i.Date).ThenBy(i => i.CreatedAt).ThenBy(i => i.Id))
            {
                result[item.Id] = $"{prefix}-{year.Key}-{++n:000}";
            }
        }

        return result;
    }
}
