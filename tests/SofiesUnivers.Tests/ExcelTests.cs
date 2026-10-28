using System.IO.Compression;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using S = DocumentFormat.OpenXml.Spreadsheet;
using DocumentFormat.OpenXml.Validation;
using SofiesUnivers.Core.Domain;
using SofiesUnivers.Core.Excel;
using SofiesUnivers.Tests.Infrastructure;
using static SofiesUnivers.Tests.Infrastructure.Dates;

namespace SofiesUnivers.Tests;

public class ExcelTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    private static void AssertSchemaValid(byte[] xlsx)
    {
        using var doc = SpreadsheetDocument.Open(new MemoryStream(xlsx), false);
        var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(doc).ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors.Select(e => $"{e.Part?.Uri} {e.Path?.XPath}: {e.Description}")));
    }

    private static Dictionary<string, S.Worksheet> SheetsOf(SpreadsheetDocument doc) =>
        doc.WorkbookPart!.Workbook.Sheets!.Elements<S.Sheet>().ToDictionary(
            s => s.Name!.Value!,
            s => ((WorksheetPart)doc.WorkbookPart.GetPartById(s.Id!.Value!)).Worksheet);

    [Fact]
    public async Task Private_workbook_is_valid_Office_Open_XML()
    {
        var w = await ReportTests.SampleAsync();
        AssertSchemaValid(PrivateWorkbook.Build(w.Data, Now));
    }

    [Fact]
    public async Task Tax_report_is_valid_Office_Open_XML()
    {
        var w = await ReportTests.SampleAsync();
        AssertSchemaValid(TaxReportWorkbook.Build(w.Data, 2026, Now));
    }

    [Fact]
    public async Task Empty_dataset_produces_valid_workbooks()
    {
        var w = await TestWorld.CreateAsync();
        AssertSchemaValid(PrivateWorkbook.Build(w.Data, Now));
        AssertSchemaValid(TaxReportWorkbook.Build(w.Data, 2026, Now));
    }

    [Fact]
    public async Task Private_workbook_has_expected_sheets_numeric_money_and_real_dates()
    {
        var w = await ReportTests.SampleAsync();
        using var doc = SpreadsheetDocument.Open(new MemoryStream(PrivateWorkbook.Build(w.Data, Now)), false);
        var sheets = SheetsOf(doc);

        Assert.Equal(["Overblik", "Sessioner", "Sessionselementer", "Betalinger", "Udgifter", "Kørsel", "Lister", "Indstillinger", "Info"], sheets.Keys);

        var sessions = sheets["Sessioner"];
        Assert.NotNull(sessions.GetFirstChild<S.AutoFilter>());
        var pane = sessions.Descendants<S.Pane>().Single();
        Assert.Equal(S.PaneStateValues.Frozen, pane.State!.Value);

        var row2 = sessions.Descendants<S.Row>().Single(r => r.RowIndex! == 2u);
        var cells = row2.Elements<S.Cell>().ToDictionary(c => new string(c.CellReference!.Value!.TakeWhile(char.IsLetter).ToArray()));
        Assert.Null(cells["B"].DataType);                   // date is a number…
        Assert.Equal("2", cells["B"].StyleIndex!.Value.ToString()); // …with the date style
        Assert.Equal(XlsxDates.ToSerial(D(2025, 12, 30)).ToString(), cells["B"].CellValue!.Text);
        Assert.Null(cells["G"].DataType);                   // price is numeric
        Assert.Equal("900", cells["G"].CellValue!.Text);
    }

    [Fact]
    public async Task Private_workbook_includes_nicknames()
    {
        var w = await ReportTests.SampleAsync();
        var text = AllText(PrivateWorkbook.Build(w.Data, Now));
        Assert.Contains("Undertøjsfabrikanten", text);
        Assert.Contains("Kunde 042", text);
    }

    [Fact]
    public async Task Tax_report_excludes_nicknames_and_ids_everywhere()
    {
        var w = await ReportTests.SampleAsync();
        var bytes = TaxReportWorkbook.Build(w.Data, 2026, Now);
        var text = AllText(bytes);

        foreach (var nickname in w.Data.Sessions.Select(s => s.ClientNickname).OfType<string>())
        {
            Assert.DoesNotContain(nickname, text, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var id in w.Data.Sessions.Select(s => s.Id).Concat(w.Data.Payments.Select(p => p.Id)))
        {
            Assert.DoesNotContain(id.ToString(), text, StringComparison.OrdinalIgnoreCase);
        }

        // Hidden columns, hidden sheets, comments and custom properties: none exist.
        using var doc = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        Assert.DoesNotContain(doc.WorkbookPart!.Workbook.Sheets!.Elements<S.Sheet>(), s => s.State is not null);
        Assert.DoesNotContain(doc.WorkbookPart.WorksheetParts.SelectMany(p => p.Worksheet.Descendants<S.Column>()), c => c.Hidden?.Value == true);
        Assert.Empty(doc.WorkbookPart.WorksheetParts.SelectMany(p => new[] { p.WorksheetCommentsPart }.OfType<WorksheetCommentsPart>()));
        Assert.Null(doc.CustomFilePropertiesPart);
    }

    [Fact]
    public async Task Tax_report_contains_only_trips_marked_for_the_report_and_the_selected_year()
    {
        var w = await ReportTests.SampleAsync();
        var sheets = XlsxReader.Read(TaxReportWorkbook.Build(w.Data, 2026, Now));

        var trips = sheets["Kørsel"].Rows.Skip(1).Where(r => r.Count > 0).ToList();
        Assert.Equal(2, trips.Count);
        Assert.DoesNotContain(trips, r => r.Contains("Bauhaus"));

        var expenses = sheets["Udgifter"].Rows.Skip(1).Where(r => r.Count > 0).ToList();
        Assert.Equal(2, expenses.Count); // the 2025 expense is not included

        var payments = sheets["Betalinger"].Rows.Skip(1).Where(r => r.Count > 0).ToList();
        Assert.Equal(4, payments.Count);
        Assert.Contains(payments, r => r.Contains("S-2025-001")); // a 2025 session paid in 2026
    }

    [Fact]
    public void Writer_escapes_text_and_removes_invalid_characters()
    {
        var wb = new XlsxWorkbook { Created = Now };
        wb.AddSheet("Test").Row(Cell.T("<b>&\"'</b>\u0001 æøå 🌺 =SUM(A1)"), Cell.T("  spaces  "));
        var bytes = wb.ToBytes();
        AssertSchemaValid(bytes);
        var rows = XlsxReader.Read(bytes)["Test"].Rows;
        Assert.Equal("<b>&\"'</b> æøå 🌺 =SUM(A1)", rows[0][0]);
        Assert.Equal("  spaces  ", rows[0][1]);
    }

    [Fact]
    public void Writer_rejects_invalid_sheet_names()
    {
        var wb = new XlsxWorkbook();
        Assert.Throws<ArgumentException>(() => wb.AddSheet("a/b"));
        Assert.Throws<ArgumentException>(() => wb.AddSheet(new string('x', 32)));
        wb.AddSheet("Ok");
        Assert.Throws<ArgumentException>(() => wb.AddSheet("ok"));
    }

    [Fact]
    public void Column_names()
    {
        Assert.Equal("A", XlsxWorkbook.ColumnName(0));
        Assert.Equal("Z", XlsxWorkbook.ColumnName(25));
        Assert.Equal("AA", XlsxWorkbook.ColumnName(26));
        Assert.Equal("AZ", XlsxWorkbook.ColumnName(51));
    }

    [Fact]
    public void Reader_understands_shared_strings_like_a_workbook_saved_by_Excel()
    {
        using var ms = new MemoryStream();
        using (var doc = SpreadsheetDocument.Create(ms, SpreadsheetDocumentType.Workbook))
        {
            var wbPart = doc.AddWorkbookPart();
            wbPart.Workbook = new S.Workbook();
            var sst = wbPart.AddNewPart<SharedStringTablePart>();
            sst.SharedStringTable = new S.SharedStringTable(new S.SharedStringItem(new S.Text("Hej")), new S.SharedStringItem(new S.Run(new S.Text("Dav ")), new S.Run(new S.Text("Sofie"))));
            var wsPart = wbPart.AddNewPart<WorksheetPart>();
            wsPart.Worksheet = new S.Worksheet(new S.SheetData(
                new S.Row(new S.Cell { CellReference = "A1", DataType = S.CellValues.SharedString, CellValue = new S.CellValue("0") },
                        new S.Cell { CellReference = "C1", CellValue = new S.CellValue("12.5") }),
                new S.Row(new S.Cell { CellReference = "B2", DataType = S.CellValues.SharedString, CellValue = new S.CellValue("1") }) { RowIndex = 2 }));
            wbPart.Workbook.AppendChild(new S.Sheets(new S.Sheet { Name = "Ark1", SheetId = 1, Id = wbPart.GetIdOfPart(wsPart) }));
        }

        var sheet = XlsxReader.Read(ms.ToArray())["Ark1"];
        Assert.Equal("Hej", sheet.Cell(0, 0));
        Assert.Null(sheet.Cell(0, 1));
        Assert.Equal("12.5", sheet.Cell(0, 2));
        Assert.Equal("Dav Sofie", sheet.Cell(1, 1));
    }

    internal static string AllText(byte[] xlsx)
    {
        using var zip = new ZipArchive(new MemoryStream(xlsx));
        var sb = new StringBuilder();
        foreach (var entry in zip.Entries)
        {
            using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
            sb.AppendLine(entry.FullName).AppendLine(reader.ReadToEnd());
        }

        return sb.ToString();
    }
}
