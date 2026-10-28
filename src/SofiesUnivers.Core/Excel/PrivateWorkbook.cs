using System.Globalization;
using System.Text.Json;
using SofiesUnivers.Core.Backup;
using SofiesUnivers.Core.Model;
using SofiesUnivers.Core.Reports;
using SofiesUnivers.Core.Storage;
using SofiesUnivers.Core.Text;

namespace SofiesUnivers.Core.Excel;

/// <summary>
/// The complete, private workbook ("Sofies-Regnskab.xlsx"). The first sheet is a human-readable
/// overview; the other sheets hold structured rows with stable ids, which is enough to
/// reconstruct all app data (except receipt files) — see <see cref="WorkbookImporter"/>.
/// </summary>
public static class PrivateWorkbook
{
    public const string FormatName = "Sofies Univers – komplet regnskab";
    public const int FormatVersion = 1;

    public static class SheetNames
    {
        public const string Overview = "Overblik";
        public const string Sessions = "Sessioner";
        public const string SessionElements = "Sessionselementer";
        public const string Payments = "Betalinger";
        public const string Expenses = "Udgifter";
        public const string Trips = "Kørsel";
        public const string Lists = "Lister";
        public const string Settings = "Indstillinger";
        public const string Info = "Info";
    }

    public static class Col
    {
        public const string Id = "ID";
        public const string Date = "Dato";
        public const string Nickname = "Klient (kaldenavn)";
        public const string Elements = "Sessionselementer";
        public const string DurationMinutes = "Varighed (min.)";
        public const string Duration = "Varighed";
        public const string Price = "Pris (kr.)";
        public const string Paid = "Betalt (kr.)";
        public const string Outstanding = "Udestående (kr.)";
        public const string Created = "Oprettet (UTC)";
        public const string Modified = "Ændret (UTC)";
        public const string SessionId = "Session-ID";
        public const string SessionDate = "Sessionsdato";
        public const string Number = "Nr.";
        public const string ElementId = "Element-ID";
        public const string Element = "Element";
        public const string PaymentDate = "Betalingsdato";
        public const string Amount = "Beløb (kr.)";
        public const string MethodId = "Metode-ID";
        public const string Method = "Metode";
        public const string CategoryId = "Kategori-ID";
        public const string Category = "Kategori";
        public const string Description = "Beskrivelse";
        public const string ReceiptFile = "Kvittering (fil i backup)";
        public const string ReceiptId = "Kvittering-ID";
        public const string ReceiptName = "Kvittering (filnavn)";
        public const string ReceiptType = "Filtype";
        public const string ReceiptSize = "Størrelse (bytes)";
        public const string ReceiptHash = "SHA-256";
        public const string Purpose = "Formål";
        public const string From = "Fra";
        public const string To = "Til";
        public const string Km = "Km";
        public const string InTaxReport = "Medtag i skatteoversigt";
        public const string ClinicTrip = "Klinik-tur";
        public const string Origin = "Oprindelse";
        public const string List = "Liste";
        public const string Name = "Navn";
        public const string Hidden = "Skjult";
        public const string Order = "Rækkefølge";
        public const string Setting = "Indstilling";
        public const string Value = "Værdi";
        public const string Field = "Felt";
    }

    public const string ListElement = "Sessionselement";
    public const string ListCategory = "Udgiftskategori";
    public const string ListMethod = "Betalingsmetode";
    public const string TechnicalSettingsKey = "Teknisk (bruges ved gendannelse)";
    public const string InfoFormat = "Format";
    public const string InfoFormatVersion = "Formatversion";
    public const string InfoDataVersion = "Dataversion";
    public const string InfoExported = "Eksporteret (UTC)";
    public const string InfoFingerprint = "Kontrolsum (SHA-256)";

    public static byte[] Build(AppData data, DateTimeOffset now)
    {
        var wb = new XlsxWorkbook
        {
            Title = "Sofies Univers – komplet regnskab",
            Subject = "Komplet eksport af sessioner, betalinger, udgifter og kørsel",
            Created = now,
        };

        var paid = data.Payments.GroupBy(p => p.SessionId).ToDictionary(g => g.Key, g => g.Sum(p => p.Amount));
        var attachmentPaths = BackupPaths.AttachmentPaths(data);
        string Element(Guid id) => data.LookupName(LookupKind.SessionElement, id);

        WriteOverview(wb.AddSheet(SheetNames.Overview), data, now);

        var sessions = wb.AddSheet(SheetNames.Sessions).Columns(38, 12, 22, 34, 14, 12, 13, 13, 15, 30, 30);
        sessions.Table(Col.Id, Col.Date, Col.Nickname, Col.Elements, Col.DurationMinutes, Col.Duration, Col.Price, Col.Paid, Col.Outstanding, Col.Created, Col.Modified);
        foreach (var s in data.Sessions.OrderBy(s => s.Date).ThenBy(s => s.CreatedAt))
        {
            var p = paid.GetValueOrDefault(s.Id);
            sessions.Row(
                Cell.T(Id(s.Id)), Cell.D(s.Date), Cell.T(s.ClientNickname),
                Cell.T(string.Join(" + ", s.ElementIds.Select(Element))),
                Cell.N(s.DurationMinutes, CellStyle.Integer), Cell.T(Dk.Duration(s.DurationMinutes)),
                Cell.N(s.Price), Cell.N(p), Cell.N(Balances.Outstanding(s.Price, p)),
                Cell.T(Stamp(s.CreatedAt)), Cell.T(Stamp(s.ModifiedAt)));
        }

        var elements = wb.AddSheet(SheetNames.SessionElements).Columns(38, 12, 6, 38, 26);
        elements.Table(Col.SessionId, Col.Date, Col.Number, Col.ElementId, Col.Element);
        foreach (var s in data.Sessions.OrderBy(s => s.Date).ThenBy(s => s.CreatedAt))
        {
            for (var i = 0; i < s.ElementIds.Count; i++)
            {
                elements.Row(Cell.T(Id(s.Id)), Cell.D(s.Date), Cell.N(i + 1, CellStyle.Integer), Cell.T(Id(s.ElementIds[i])), Cell.T(Element(s.ElementIds[i])));
            }
        }

        var sessionById = data.Sessions.ToDictionary(s => s.Id);
        var payments = wb.AddSheet(SheetNames.Payments).Columns(38, 38, 13, 13, 13, 38, 16, 22, 30, 30);
        payments.Table(Col.Id, Col.SessionId, Col.SessionDate, Col.PaymentDate, Col.Amount, Col.MethodId, Col.Method, Col.Nickname, Col.Created, Col.Modified);
        foreach (var p in data.Payments.OrderBy(p => p.Date).ThenBy(p => p.CreatedAt))
        {
            var s = sessionById.GetValueOrDefault(p.SessionId);
            payments.Row(
                Cell.T(Id(p.Id)), Cell.T(Id(p.SessionId)), s is null ? Cell.Empty : Cell.D(s.Date), Cell.D(p.Date), Cell.N(p.Amount),
                Cell.T(Id(p.MethodId)), Cell.T(data.LookupName(LookupKind.PaymentMethod, p.MethodId)), Cell.T(s?.ClientNickname),
                Cell.T(Stamp(p.CreatedAt)), Cell.T(Stamp(p.ModifiedAt)));
        }

        var expenses = wb.AddSheet(SheetNames.Expenses).Columns(38, 12, 13, 38, 16, 36, 44, 38, 26, 16, 14, 66, 30, 30);
        expenses.Table(Col.Id, Col.Date, Col.Amount, Col.CategoryId, Col.Category, Col.Description, Col.ReceiptFile, Col.ReceiptId, Col.ReceiptName, Col.ReceiptType, Col.ReceiptSize, Col.ReceiptHash, Col.Created, Col.Modified);
        foreach (var e in data.Expenses.OrderBy(e => e.Date).ThenBy(e => e.CreatedAt))
        {
            var a = e.Attachment;
            expenses.Row(
                Cell.T(Id(e.Id)), Cell.D(e.Date), Cell.N(e.Amount), Cell.T(Id(e.CategoryId)), Cell.T(data.LookupName(LookupKind.ExpenseCategory, e.CategoryId)),
                Cell.T(e.Description, CellStyle.Wrap),
                Cell.T(a is null ? null : attachmentPaths[a.Id]), Cell.T(a is null ? null : Id(a.Id)), Cell.T(a?.FileName), Cell.T(a?.ContentType),
                a is null ? Cell.Empty : Cell.N(a.SizeBytes, CellStyle.Integer), Cell.T(a?.Sha256),
                Cell.T(Stamp(e.CreatedAt)), Cell.T(Stamp(e.ModifiedAt)));
        }

        var trips = wb.AddSheet(SheetNames.Trips).Columns(38, 12, 22, 40, 40, 9, 12, 10, 12, 30, 30);
        trips.Table(Col.Id, Col.Date, Col.Purpose, Col.From, Col.To, Col.Km, Col.InTaxReport, Col.ClinicTrip, Col.Origin, Col.Created, Col.Modified);
        foreach (var t in data.Trips.OrderBy(t => t.Date).ThenBy(t => t.CreatedAt))
        {
            trips.Row(
                Cell.T(Id(t.Id)), Cell.D(t.Date), Cell.T(t.Purpose), Cell.T(t.FromLabel), Cell.T(t.ToLabel), Cell.N(t.DistanceKm, CellStyle.Decimal),
                Cell.YesNo(t.IncludeInTaxReport), Cell.YesNo(t.IsClinicTrip), Cell.T(t.Origin == TripOrigin.Automatic ? "Automatisk" : "Manuel"),
                Cell.T(Stamp(t.CreatedAt)), Cell.T(Stamp(t.ModifiedAt)));
        }

        var lists = wb.AddSheet(SheetNames.Lists).Columns(18, 38, 26, 8, 12, 30);
        lists.Table(Col.List, Col.Id, Col.Name, Col.Hidden, Col.Order, Col.Created);
        foreach (var (listName, items) in new[] { (ListElement, data.ElementTypes), (ListCategory, data.ExpenseCategories), (ListMethod, data.PaymentMethods) })
        {
            foreach (var item in items.OrderBy(i => i.SortOrder))
            {
                lists.Row(Cell.T(listName), Cell.T(Id(item.Id)), Cell.T(item.Name), Cell.YesNo(item.IsArchived), Cell.N(item.SortOrder, CellStyle.Integer), Cell.T(Stamp(item.CreatedAt)));
            }
        }

        var settings = wb.AddSheet(SheetNames.Settings).Columns(34, 80);
        settings.Table(Col.Setting, Col.Value);
        foreach (var location in data.Settings.Locations)
        {
            settings.Row(Cell.T($"Sted: {location.Name}"), Cell.T(location.Address));
        }

        var route = data.Settings.ClinicRoute;
        settings.Row(Cell.T("Standardrute"), Cell.T($"{data.Settings.FindLocation(route.FromLocationId)?.Name} ⇄ {data.Settings.FindLocation(route.ToLocationId)?.Name}"));
        settings.Row(Cell.T("Km tur/retur"), route.RoundTripKm is null ? Cell.T("Ikke angivet") : Cell.N(route.RoundTripKm.Value, CellStyle.Decimal));
        settings.Row(Cell.T("Registrér klinikkørsel automatisk"), Cell.YesNo(route.AutoRegisterTrips));
        settings.Row(Cell.T("Klinikkørsel i skatteoversigt som standard"), Cell.YesNo(route.IncludeInTaxReportByDefault));
        settings.Row(Cell.T("Momsfritaget"), Cell.YesNo(data.Settings.VatExempt));
        settings.Row(Cell.T(TechnicalSettingsKey), Cell.T(JsonSerializer.Serialize(data.Settings, AppJsonContext.Default.AppSettings)));

        var info = wb.AddSheet(SheetNames.Info).Columns(26, 80);
        info.Table(Col.Field, Col.Value);
        info.Row(Cell.T(InfoFormat), Cell.T(FormatName));
        info.Row(Cell.T(InfoFormatVersion), Cell.N(FormatVersion, CellStyle.Integer));
        info.Row(Cell.T(InfoDataVersion), Cell.N(data.DataVersion, CellStyle.Integer));
        info.Row(Cell.T(InfoExported), Cell.T(Stamp(now)));
        info.Row(Cell.T(InfoFingerprint), Cell.T(DataSerializer.Fingerprint(data)));
        info.BlankRow();
        info.Row(Cell.T("Kontrolsummen bruges til at sikre, at regnearket ikke er ændret, hvis det bruges til gendannelse.", CellStyle.Note));

        return wb.ToBytes();
    }

    private static void WriteOverview(XlsxSheet sheet, AppData data, DateTimeOffset now)
    {
        sheet.Columns(30, 16, 18, 20, 18, 16, 14, 18);
        sheet.Row(Cell.T("Sofies Univers – komplet regnskab", CellStyle.Title));
        sheet.Row(Cell.T($"Eksporteret {Dk.Timestamp(now)}", CellStyle.Note));
        sheet.Row(Cell.T("Privat: indeholder klienternes kaldenavne. Del ikke filen med andre end din revisor.", CellStyle.Note));
        sheet.BlankRow();
        sheet.Row(Cell.T("Årsoversigt", CellStyle.Subtitle));
        sheet.Row(Cell.H("År"), Cell.H("Sessioner"), Cell.H("Sessionsværdi (kr.)"), Cell.H("Modtagne betalinger (kr.)"), Cell.H("Udestående (kr.)"), Cell.H("Udgifter (kr.)"), Cell.H("Km i alt"), Cell.H("Km i skatteoversigt"));
        foreach (var year in ReportCalculator.Years(data, now.Year).OrderBy(y => y))
        {
            var y = ReportCalculator.ForYear(data, year);
            sheet.Row(Cell.T(year.ToString(CultureInfo.InvariantCulture), CellStyle.Bold), Cell.N(y.SessionCount, CellStyle.Integer), Cell.N(y.SessionValue), Cell.N(y.PaymentsReceived),
                Cell.N(y.OutstandingOnYearSessions), Cell.N(y.ExpensesTotal), Cell.N(y.DrivingKm, CellStyle.Decimal), Cell.N(y.DrivingKmInTaxReport, CellStyle.Decimal));
        }

        sheet.BlankRow();
        sheet.Row(Cell.T("Forklaring", CellStyle.Subtitle));
        sheet.Row(Cell.T("Sessionsværdi er summen af priser for sessioner med dato i året. Modtagne betalinger er grupperet efter betalingsdato.", CellStyle.Note));
        sheet.Row(Cell.T("Udestående er beløb, der endnu ikke er betalt for årets sessioner (pr. eksportdatoen).", CellStyle.Note));
        sheet.Row(Cell.T("Modtagne betalinger er ikke nødvendigvis det samme som skattepligtig omsætning. Spørg din revisor.", CellStyle.Note));
        sheet.BlankRow();
        sheet.Row(Cell.T("Arkene", CellStyle.Subtitle));
        sheet.Row(Cell.T("Sessioner, Sessionselementer, Betalinger, Udgifter og Kørsel indeholder alle registreringer med faste ID'er."));
        sheet.Row(Cell.T("Lister og Indstillinger indeholder sessionselementer, kategorier, betalingsmetoder og ruten."));
        sheet.Row(Cell.T("Ret ikke i arkene, hvis filen skal kunne bruges til gendannelse – et ændret ark afvises af appen.", CellStyle.Note));
    }

    public static string Id(Guid id) => id.ToString("D");

    public static string Stamp(DateTimeOffset t) => t.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}
