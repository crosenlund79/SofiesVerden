using System.Text;
using SofiesUnivers.Core.Model;
using SofiesUnivers.Core.Reports;

namespace SofiesUnivers.Core.Backup;

public static class BackupPaths
{
    public const string InfoFile = "Backup-info.json";
    public const string WorkbookFile = "Sofies-Regnskab.xlsx";
    public const string DataFile = "Data/sofies-univers-data.json";
    public const string ReceiptFolder = "Kvitteringer/";
    public const string ReadMeFile = "LÆS MIG.txt";

    public static string BackupFileName(DateOnly date) => $"Sofie-Backup-{date:yyyy-MM-dd}.zip";

    public static string WorkbookExportFileName(DateOnly date) => $"Sofies-Regnskab-{date:yyyy-MM-dd}.xlsx";

    /// <summary>
    /// Readable, unique file names for receipts inside the backup, e.g.
    /// "Kvitteringer/2026-10-28_U-2026-004_Materialer.jpg".
    /// </summary>
    public static Dictionary<Guid, string> AttachmentPaths(AppData data)
    {
        var refs = ReportCalculator.ExpenseReferences(data);
        var result = new Dictionary<Guid, string>();
        foreach (var e in data.Expenses.Where(e => e.Attachment is not null))
        {
            var a = e.Attachment!;
            var category = Ascii(data.LookupName(LookupKind.ExpenseCategory, e.CategoryId));
            var ext = Path.GetExtension(a.FileName);
            if (string.IsNullOrEmpty(ext) || ext.Length > 6)
            {
                ext = a.ContentType == "application/pdf" ? ".pdf" : ".jpg";
            }

            var path = $"{ReceiptFolder}{e.Date:yyyy-MM-dd}_{refs[e.Id]}_{category}{ext.ToLowerInvariant()}";
            if (result.ContainsValue(path))
            {
                path = $"{ReceiptFolder}{e.Date:yyyy-MM-dd}_{refs[e.Id]}_{a.Id:N}{ext.ToLowerInvariant()}";
            }

            result[a.Id] = path;
        }

        return result;
    }

    /// <summary>File-name friendly ASCII: "Markedsføring" becomes "Markedsfoering".</summary>
    public static string Ascii(string text)
    {
        var sb = new StringBuilder();
        foreach (var c in text)
        {
            var mapped = c switch
            {
                'æ' => "ae",
                'Æ' => "Ae",
                'ø' => "oe",
                'Ø' => "Oe",
                'å' => "aa",
                'Å' => "Aa",
                _ when char.IsAsciiLetterOrDigit(c) => c.ToString(),
                _ => "-",
            };
            sb.Append(mapped);
        }

        var result = sb.ToString().Trim('-');
        while (result.Contains("--", StringComparison.Ordinal))
        {
            result = result.Replace("--", "-", StringComparison.Ordinal);
        }

        return result.Length == 0 ? "Udgift" : result.Length > 30 ? result[..30] : result;
    }
}
