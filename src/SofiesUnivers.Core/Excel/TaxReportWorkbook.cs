using SofiesUnivers.Core.Model;
using SofiesUnivers.Core.Reports;
using SofiesUnivers.Core.Text;

namespace SofiesUnivers.Core.Excel;

/// <summary>
/// The authority-oriented annual report ("Skatteoversigt"). Contains the financial information an
/// accountant needs, and deliberately <b>no</b> client nicknames, internal ids or other identifying
/// information — not in cells, sheet names or file properties.
/// </summary>
public static class TaxReportWorkbook
{
    public static string FileName(int year) => $"Regnskabsoversigt-{year}.xlsx";

    public static byte[] Build(AppData data, int year, DateTimeOffset now)
    {
        var summary = ReportCalculator.ForYear(data, year);
        var sessionRefs = ReportCalculator.SessionReferences(data);
        var expenseRefs = ReportCalculator.ExpenseReferences(data);
        var paid = data.Payments.GroupBy(p => p.SessionId).ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
        var sessionById = data.Sessions.ToDictionary(s => s.Id);

        var wb = new XlsxWorkbook
        {
            Title = $"Regnskabsoversigt {year}",
            Subject = "Materiale til skatteopgørelse",
            Created = now,
        };

        var o = wb.AddSheet("Oversigt").Columns(44, 20, 20, 18);
        o.Row(Cell.T($"Regnskabsoversigt {year}", CellStyle.Title));
        o.Row(Cell.T("Materiale til skatteopgørelse – udarbejdet med appen Sofies Univers", CellStyle.Subtitle));
        o.Row(Cell.T($"Dannet {Dk.Timestamp(now)}. Oversigten er ikke godkendt af myndighederne og bør gennemgås af en revisor.", CellStyle.Note));
        o.Row(Cell.T(data.Settings.VatExempt
            ? "Virksomheden er registreret som momsfritaget i appen. Der er ikke beregnet moms af indtægter eller fratrukket købsmoms."
            : "Der er ikke beregnet moms i denne oversigt.", CellStyle.Note));
        o.BlankRow();
        o.Row(Cell.T("Nøgletal", CellStyle.Subtitle));
        o.Row(Cell.H("Post"), Cell.H("Beløb / antal"));
        o.Row(Cell.T("Antal sessioner (efter sessionsdato)"), Cell.N(summary.SessionCount, CellStyle.Integer));
        o.Row(Cell.T("Sessionsværdi (sum af priser, efter sessionsdato)"), Cell.N(summary.SessionValue));
        o.Row(Cell.T("Modtagne betalinger (efter betalingsdato)"), Cell.N(summary.PaymentsReceived));
        o.Row(Cell.T($"Udestående på årets sessioner pr. {Dk.Date(DateOnly.FromDateTime(now.LocalDateTime))}"), Cell.N(summary.OutstandingOnYearSessions));
        o.Row(Cell.T("Udgifter i alt (efter udgiftsdato)"), Cell.N(summary.ExpensesTotal));
        o.Row(Cell.T("Antal udgifter uden kvittering"), Cell.N(summary.ExpensesWithoutReceipt, CellStyle.Integer));
        o.Row(Cell.T("Kørsel medtaget i skatteoversigten (km)"), Cell.N(summary.DrivingKmInTaxReport, CellStyle.Decimal));
        o.Row(Cell.T("Antal ture medtaget i skatteoversigten"), Cell.N(summary.TripsInTaxReport, CellStyle.Integer));
        var missingKm = data.Trips.Count(t => t.Date.Year == year && t.IncludeInTaxReport && t.DistanceKm is null);
        if (missingKm > 0)
        {
            o.Row(Cell.T($"Bemærk: {missingKm} medtagne ture mangler antal km.", CellStyle.Note));
        }

        o.Row(Cell.T("Kørslen er brugerens egen markering af ture. Det er ikke en vurdering af fradragsret, og der er ikke beregnet kørselsfradrag.", CellStyle.Note));
        o.BlankRow();

        o.Row(Cell.T("Pr. måned", CellStyle.Subtitle));
        o.Row(Cell.H("Måned"), Cell.H("Sessionsværdi (kr.)"), Cell.H("Modtagne betalinger (kr.)"), Cell.H("Udgifter (kr.)"));
        foreach (var m in summary.Months)
        {
            o.Row(Cell.T(Dk.Capitalize(Dk.MonthName(m.Month))), Cell.N(m.SessionValue), Cell.N(m.PaymentsReceived), Cell.N(m.Expenses));
        }

        o.Row(Cell.T("I alt", CellStyle.Bold), Cell.N(summary.SessionValue, CellStyle.MoneyBold), Cell.N(summary.PaymentsReceived, CellStyle.MoneyBold), Cell.N(summary.ExpensesTotal, CellStyle.MoneyBold));
        o.BlankRow();

        o.Row(Cell.T("Udgifter pr. kategori", CellStyle.Subtitle));
        o.Row(Cell.H("Kategori"), Cell.H("Beløb (kr.)"), Cell.H("Antal"));
        foreach (var c in summary.ExpensesByCategory)
        {
            o.Row(Cell.T(c.Name), Cell.N(c.Amount), Cell.N(c.Count, CellStyle.Integer));
        }

        o.BlankRow();
        o.Row(Cell.T("Betalinger pr. betalingsmetode", CellStyle.Subtitle));
        o.Row(Cell.H("Metode"), Cell.H("Beløb (kr.)"), Cell.H("Antal"));
        foreach (var m in summary.PaymentsByMethod)
        {
            o.Row(Cell.T(m.Name), Cell.N(m.Amount), Cell.N(m.Count, CellStyle.Integer));
        }

        var income = wb.AddSheet("Sessioner").Columns(14, 12, 34, 12, 14, 16, 16);
        income.Table("Reference", "Dato", "Ydelse", "Varighed (timer)", "Pris (kr.)", "Betalt i alt (kr.)", "Udestående (kr.)");
        foreach (var s in data.Sessions.Where(s => s.Date.Year == year).OrderBy(s => s.Date).ThenBy(s => s.CreatedAt))
        {
            var p = paid.GetValueOrDefault(s.Id);
            income.Row(
                Cell.T(sessionRefs[s.Id]), Cell.D(s.Date),
                Cell.T(string.Join(" + ", s.ElementIds.Select(id => data.LookupName(LookupKind.SessionElement, id)))),
                Cell.N(Math.Round(s.DurationMinutes / 60m, 2), CellStyle.Decimal), Cell.N(s.Price), Cell.N(p), Cell.N(Balances.Outstanding(s.Price, p)));
        }

        var payments = wb.AddSheet("Betalinger").Columns(14, 14, 16, 16, 14);
        payments.Table("Betalingsdato", "Beløb (kr.)", "Metode", "Session (reference)", "Sessionsdato");
        foreach (var p in data.Payments.Where(p => p.Date.Year == year).OrderBy(p => p.Date).ThenBy(p => p.CreatedAt))
        {
            var s = sessionById.GetValueOrDefault(p.SessionId);
            payments.Row(
                Cell.D(p.Date), Cell.N(p.Amount), Cell.T(data.LookupName(LookupKind.PaymentMethod, p.MethodId)),
                Cell.T(s is null ? null : sessionRefs[s.Id]), s is null ? Cell.Empty : Cell.D(s.Date));
        }

        var expenses = wb.AddSheet("Udgifter").Columns(14, 12, 18, 44, 14, 12);
        expenses.Table("Reference", "Dato", "Kategori", "Beskrivelse", "Beløb (kr.)", "Kvittering");
        foreach (var e in data.Expenses.Where(e => e.Date.Year == year).OrderBy(e => e.Date).ThenBy(e => e.CreatedAt))
        {
            expenses.Row(
                Cell.T(expenseRefs[e.Id]), Cell.D(e.Date), Cell.T(data.LookupName(LookupKind.ExpenseCategory, e.CategoryId)),
                Cell.T(e.Description, CellStyle.Wrap), Cell.N(e.Amount), Cell.YesNo(e.Attachment is not null));
        }

        var trips = wb.AddSheet("Kørsel").Columns(12, 22, 40, 40, 10);
        trips.Table("Dato", "Formål", "Fra", "Til", "Km");
        foreach (var t in data.Trips.Where(t => t.Date.Year == year && t.IncludeInTaxReport).OrderBy(t => t.Date).ThenBy(t => t.CreatedAt))
        {
            trips.Row(Cell.D(t.Date), Cell.T(t.Purpose), Cell.T(t.FromLabel), Cell.T(t.ToLabel), Cell.N(t.DistanceKm, CellStyle.Decimal));
        }

        return wb.ToBytes();
    }
}
