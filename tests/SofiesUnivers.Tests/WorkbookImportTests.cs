using System.IO.Compression;
using System.Text;
using SofiesUnivers.Core.Domain;
using SofiesUnivers.Core.Excel;
using SofiesUnivers.Core.Storage;
using SofiesUnivers.Tests.Infrastructure;
using static SofiesUnivers.Tests.Infrastructure.Dates;

namespace SofiesUnivers.Tests;

public class WorkbookImportTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Own_workbook_reconstructs_the_complete_dataset()
    {
        var w = await ReportTests.SampleAsync();
        (await w.Ledger.SaveExpenseAsync(new ExpenseDraft
        {
            Date = D(2026, 4, 1), Amount = 12.34m, CategoryId = w.Category("Udstyr"), Description = "Linje 1\nLinje 2  ",
            Attachment = AttachmentChange.Replace("kvit.jpg", TestFiles.Jpeg()),
        })).AssertOk();

        var result = WorkbookImporter.Import(PrivateWorkbook.Build(w.Data, Now));

        Assert.True(result.Ok, string.Join("\n", result.Errors));
        var expected = w.Data with { Expenses = w.Data.Expenses.Select(e => e with { Attachment = null }).ToList() };
        Assert.Equal(DataSerializer.Fingerprint(expected), DataSerializer.Fingerprint(result.Data!));
        Assert.Equal(1, result.MissingReceipts);
        Assert.Single(result.Warnings);
        Assert.Equal(Now, result.ExportedAt);
    }

    [Fact]
    public async Task Modified_workbook_is_rejected()
    {
        var w = await ReportTests.SampleAsync();
        var bytes = PrivateWorkbook.Build(w.Data, Now);
        var tampered = ReplaceInSheet(bytes, "Undertøjsfabrikanten", "Anden kunde");

        var result = WorkbookImporter.Import(tampered);

        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.Contains("ændret"));
    }

    [Fact]
    public async Task Tax_report_is_not_accepted_as_a_backup()
    {
        var w = await ReportTests.SampleAsync();
        var result = WorkbookImporter.Import(TaxReportWorkbook.Build(w.Data, 2026, Now));
        Assert.False(result.Ok);
    }

    [Fact]
    public void Garbage_is_rejected_gracefully()
    {
        Assert.False(WorkbookImporter.Import(Encoding.UTF8.GetBytes("not a zip")).Ok);
        Assert.False(WorkbookImporter.Import([]).Ok);
    }

    [Fact]
    public async Task Workbook_from_a_newer_app_version_is_rejected()
    {
        var w = await TestWorld.CreateAsync();
        var bytes = PrivateWorkbook.Build(w.Data, Now);
        var newer = ReplaceInSheet(bytes, $"<v>{PrivateWorkbook.FormatVersion}</v>", "<v>99</v>", "sheet9.xml");
        var result = WorkbookImporter.Import(newer);
        Assert.False(result.Ok);
        Assert.Contains(result.Errors, e => e.Contains("nyere version"));
    }

    internal static byte[] ReplaceInSheet(byte[] xlsx, string find, string replace, string? onlyEntry = null)
    {
        using var input = new ZipArchive(new MemoryStream(xlsx));
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var entry in input.Entries)
            {
                using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
                var text = reader.ReadToEnd();
                if (entry.FullName.StartsWith("xl/worksheets/") && (onlyEntry is null || entry.FullName.EndsWith(onlyEntry)))
                {
                    text = text.Replace(find, replace);
                }

                using var writer = new StreamWriter(zip.CreateEntry(entry.FullName).Open(), new UTF8Encoding(false));
                writer.Write(text);
            }
        }

        return output.ToArray();
    }
}
