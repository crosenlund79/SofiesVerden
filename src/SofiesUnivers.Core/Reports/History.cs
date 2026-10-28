using SofiesUnivers.Core.Model;
using SofiesUnivers.Core.Text;

namespace SofiesUnivers.Core.Reports;

public enum HistoryKind
{
    Session,
    Payment,
    Expense,
    Trip,
}

public sealed record HistoryEntry
{
    public required HistoryKind Kind { get; init; }
    public required Guid RecordId { get; init; }

    /// <summary>The session to open for sessions and payments.</summary>
    public Guid? SessionId { get; init; }

    public required DateOnly Date { get; init; }
    public required string Title { get; init; }
    public string? Detail { get; init; }
    public decimal? Amount { get; init; }
    public decimal? Km { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    internal string SearchText { get; init; } = string.Empty;
}

public sealed record HistoryFilter
{
    public IReadOnlySet<HistoryKind>? Kinds { get; init; }
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public string? Search { get; init; }
}

public static class History
{
    public static IReadOnlyList<HistoryEntry> Build(AppData data)
    {
        var entries = new List<HistoryEntry>();
        var paid = data.Payments.GroupBy(p => p.SessionId).ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
        var sessions = data.Sessions.ToDictionary(s => s.Id);

        foreach (var s in data.Sessions)
        {
            var elements = string.Join(" + ", s.ElementIds.Select(id => data.LookupName(LookupKind.SessionElement, id)));
            var outstanding = Balances.Outstanding(s.Price, paid.GetValueOrDefault(s.Id));
            var detail = Dk.Duration(s.DurationMinutes)
                         + (s.ClientNickname is null ? "" : " · " + s.ClientNickname)
                         + (outstanding > 0 ? $" · mangler {Dk.Money(outstanding)}" : "");
            entries.Add(new HistoryEntry
            {
                Kind = HistoryKind.Session,
                RecordId = s.Id,
                SessionId = s.Id,
                Date = s.Date,
                Title = elements,
                Detail = detail,
                Amount = s.Price,
                CreatedAt = s.CreatedAt,
                SearchText = $"session {elements} {s.ClientNickname} {Dk.Amount(s.Price)} {s.Price}",
            });
        }

        foreach (var p in data.Payments)
        {
            var method = data.LookupName(LookupKind.PaymentMethod, p.MethodId);
            var session = sessions.GetValueOrDefault(p.SessionId);
            var detail = session is null
                ? null
                : $"For session d. {Dk.Date(session.Date)}" + (session.ClientNickname is null ? "" : " · " + session.ClientNickname);
            entries.Add(new HistoryEntry
            {
                Kind = HistoryKind.Payment,
                RecordId = p.Id,
                SessionId = p.SessionId,
                Date = p.Date,
                Title = $"Betaling · {method}",
                Detail = detail,
                Amount = p.Amount,
                CreatedAt = p.CreatedAt,
                SearchText = $"betaling {method} {session?.ClientNickname} {Dk.Amount(p.Amount)} {p.Amount}",
            });
        }

        foreach (var e in data.Expenses)
        {
            var category = data.LookupName(LookupKind.ExpenseCategory, e.CategoryId);
            entries.Add(new HistoryEntry
            {
                Kind = HistoryKind.Expense,
                RecordId = e.Id,
                Date = e.Date,
                Title = category,
                Detail = (e.Description ?? "") + (e.Attachment is null ? "" : (e.Description is null ? "" : " · ") + "📎 kvittering"),
                Amount = e.Amount,
                CreatedAt = e.CreatedAt,
                SearchText = $"udgift {category} {e.Description} {Dk.Amount(e.Amount)} {e.Amount}",
            });
        }

        foreach (var t in data.Trips)
        {
            entries.Add(new HistoryEntry
            {
                Kind = HistoryKind.Trip,
                RecordId = t.Id,
                Date = t.Date,
                Title = $"Kørsel · {t.Purpose}",
                Detail = $"{t.FromLabel} → {t.ToLabel}" + (t.IncludeInTaxReport ? "" : " · ikke i skatteoversigt"),
                Km = t.DistanceKm,
                CreatedAt = t.CreatedAt,
                SearchText = $"kørsel {t.Purpose} {t.FromLabel} {t.ToLabel}",
            });
        }

        return entries
            .OrderByDescending(e => e.Date)
            .ThenByDescending(e => e.CreatedAt)
            .ToList();
    }

    public static IReadOnlyList<HistoryEntry> Filter(IEnumerable<HistoryEntry> entries, HistoryFilter filter)
    {
        var search = Names.Clean(filter.Search)?.ToUpperInvariant();
        return entries.Where(e =>
                (filter.Kinds is null || filter.Kinds.Count == 0 || filter.Kinds.Contains(e.Kind)) &&
                (filter.From is null || e.Date >= filter.From) &&
                (filter.To is null || e.Date <= filter.To) &&
                (search is null || search.Split(' ').All(word => e.SearchText.ToUpperInvariant().Contains(word))))
            .ToList();
    }
}
