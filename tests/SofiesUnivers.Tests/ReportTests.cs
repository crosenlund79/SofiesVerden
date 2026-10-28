using SofiesUnivers.Core.Domain;
using SofiesUnivers.Core.Reports;
using SofiesUnivers.Tests.Infrastructure;
using static SofiesUnivers.Tests.Infrastructure.Dates;

namespace SofiesUnivers.Tests;

public class ReportTests
{
    internal static async Task<TestWorld> SampleAsync()
    {
        var w = await TestWorld.CreateAsync(routeKm: 64m);
        await w.AddSessionAsync(D(2026, 1, 15), 800m, "Undertøjsfabrikanten", w.Pay(800m, D(2026, 1, 15)));
        await w.AddSessionAsync(D(2026, 1, 15), 600m, "Kunde 042", w.Pay(300m, D(2026, 1, 15), w.Cash));
        await w.AddSessionAsync(D(2026, 3, 2), 1000m, null, w.Pay(1000m, D(2026, 3, 2), w.Method("Bankoverførsel")));
        await w.AddSessionAsync(D(2025, 12, 30), 900m, "Gammel kunde", w.Pay(450m, D(2025, 12, 30)), w.Pay(450m, D(2026, 1, 5)));
        (await w.Ledger.SaveExpenseAsync(new ExpenseDraft { Date = D(2026, 1, 3), Amount = 250m, CategoryId = w.Category("Materialer"), Description = "Olie" })).AssertOk();
        (await w.Ledger.SaveExpenseAsync(new ExpenseDraft { Date = D(2026, 2, 1), Amount = 3000m, CategoryId = w.Category("Lokaleleje") })).AssertOk();
        (await w.Ledger.SaveExpenseAsync(new ExpenseDraft { Date = D(2025, 11, 1), Amount = 99m, CategoryId = w.Category("Andet") })).AssertOk();
        (await w.Ledger.SaveTripAsync(new TripDraft
        {
            Date = D(2026, 2, 10), Purpose = "Indkøb", FromLabel = "Hjem", ToLabel = "Bauhaus", DistanceKm = 12.5m, IncludeInTaxReport = false, IsClinicTrip = false,
        })).AssertOk();
        return w;
    }

    [Fact]
    public async Task Annual_totals_group_each_figure_by_its_own_date()
    {
        var w = await SampleAsync();
        var y = ReportCalculator.ForYear(w.Data, 2026);

        Assert.Equal(3, y.SessionCount);
        Assert.Equal(2400m, y.SessionValue);                 // by session date
        Assert.Equal(800m + 300m + 1000m + 450m, y.PaymentsReceived); // by payment date, incl. 2025 session paid in 2026
        Assert.Equal(300m, y.OutstandingOnYearSessions);
        Assert.Equal(3250m, y.ExpensesTotal);                 // by expense date
        Assert.Equal(2, y.ExpenseCount);
        Assert.Equal("Lokaleleje", y.ExpensesByCategory[0].Name);
        Assert.Equal(3, y.TripCount);                          // 2 automatic clinic trips + 1 manual
        Assert.Equal(64m * 2 + 12.5m, y.DrivingKm);
        Assert.Equal(64m * 2, y.DrivingKmInTaxReport);
        Assert.Equal(1400m, y.Months[0].SessionValue);
        Assert.Equal(1550m, y.Months[0].PaymentsReceived);
        Assert.Equal(3000m, y.Months[1].Expenses);

        var y2025 = ReportCalculator.ForYear(w.Data, 2025);
        Assert.Equal(900m, y2025.SessionValue);
        Assert.Equal(450m, y2025.PaymentsReceived);
        Assert.Equal(99m, y2025.ExpensesTotal);
    }

    [Fact]
    public async Task Outstanding_overview_and_years()
    {
        var w = await SampleAsync();
        var outstanding = Assert.Single(ReportCalculator.OutstandingSessions(w.Data));
        Assert.Equal(300m, outstanding.Outstanding);
        Assert.Equal([2026, 2025], ReportCalculator.Years(w.Data, 2026));
    }

    [Fact]
    public async Task References_are_numbered_by_date_within_the_year()
    {
        var w = await SampleAsync();
        var refs = ReportCalculator.SessionReferences(w.Data);
        var ordered = w.Data.Sessions.Where(s => s.Date.Year == 2026).OrderBy(s => s.Date).ThenBy(s => s.CreatedAt).Select(s => refs[s.Id]);
        Assert.Equal(["S-2026-001", "S-2026-002", "S-2026-003"], ordered);
        Assert.Equal("S-2025-001", refs[w.Data.Sessions.Single(s => s.Date.Year == 2025).Id]);
    }

    [Fact]
    public async Task History_contains_everything_and_can_be_filtered_and_searched()
    {
        var w = await SampleAsync();
        var all = History.Build(w.Data);
        Assert.Equal(w.Data.Sessions.Count + w.Data.Payments.Count + w.Data.Expenses.Count + w.Data.Trips.Count, all.Count);
        Assert.True(all.Zip(all.Skip(1)).All(p => p.First.Date >= p.Second.Date));

        var expenses = History.Filter(all, new HistoryFilter { Kinds = new HashSet<HistoryKind> { HistoryKind.Expense } });
        Assert.Equal(3, expenses.Count);

        var feb = History.Filter(all, new HistoryFilter { From = D(2026, 2, 1), To = D(2026, 2, 28) });
        Assert.All(feb, e => Assert.Equal(2, e.Date.Month));

        var search = History.Filter(all, new HistoryFilter { Search = "undertøj" });
        Assert.Equal(2, search.Count); // the session and its payment
        Assert.Single(History.Filter(all, new HistoryFilter { Search = "olie" }));
    }
}
