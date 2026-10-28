using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace SofiesUnivers.Core.Excel;

/// <summary>A sheet read back as a grid of raw cell values (text, or numbers in invariant format).</summary>
public sealed class XlsxSheetData(string name, IReadOnlyList<IReadOnlyList<string?>> rows)
{
    public string Name { get; } = name;

    /// <summary>Rows by index (row number - 1); missing rows and cells are empty/null.</summary>
    public IReadOnlyList<IReadOnlyList<string?>> Rows { get; } = rows;

    public string? Cell(int rowIndex, int columnIndex) =>
        rowIndex < Rows.Count && columnIndex < Rows[rowIndex].Count ? Rows[rowIndex][columnIndex] : null;
}

/// <summary>
/// Reads cell values from .xlsx files. Supports inline strings (as written by <see cref="XlsxWorkbook"/>)
/// and shared strings (as written by Excel after re-saving). Formulas are read as their cached value.
/// </summary>
public static class XlsxReader
{
    private const long MaxUncompressedBytes = 100L * 1024 * 1024;
    private static readonly XNamespace Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PkgRel = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static IReadOnlyDictionary<string, XlsxSheetData> Read(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        if (zip.Entries.Sum(e => e.Length) > MaxUncompressedBytes)
        {
            throw new InvalidDataException("Regnearket er for stort.");
        }

        var workbook = Load(zip, "xl/workbook.xml") ?? throw new InvalidDataException("Filen er ikke et Excel-regneark.");
        var rels = Load(zip, "xl/_rels/workbook.xml.rels") ?? throw new InvalidDataException("Regnearket mangler relationer.");
        var targets = rels.Root!.Elements(PkgRel + "Relationship")
            .ToDictionary(r => (string)r.Attribute("Id")!, r => (string)r.Attribute("Target")!);

        var shared = new List<string>();
        var sharedPath = rels.Root.Elements(PkgRel + "Relationship")
            .FirstOrDefault(r => ((string?)r.Attribute("Type"))?.EndsWith("/sharedStrings", StringComparison.Ordinal) == true);
        if (sharedPath is not null && Load(zip, ResolvePath((string)sharedPath.Attribute("Target")!)) is { } sst)
        {
            shared.AddRange(sst.Root!.Elements(Main + "si").Select(TextOf));
        }

        var result = new Dictionary<string, XlsxSheetData>(StringComparer.OrdinalIgnoreCase);
        foreach (var sheet in workbook.Root!.Element(Main + "sheets")?.Elements(Main + "sheet") ?? [])
        {
            var name = (string)sheet.Attribute("name")!;
            var relId = (string?)sheet.Attribute(Rel + "id");
            if (relId is null || !targets.TryGetValue(relId, out var target))
            {
                continue;
            }

            var doc = Load(zip, ResolvePath(target));
            if (doc is not null)
            {
                result[name] = new XlsxSheetData(name, ReadRows(doc, shared));
            }
        }

        return result;
    }

    private static string ResolvePath(string target) =>
        target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;

    private static XDocument? Load(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        if (entry is null)
        {
            return null;
        }

        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }

    private static string TextOf(XElement si)
    {
        // Concatenate all text runs, ignoring phonetic hints.
        var sb = new StringBuilder();
        foreach (var t in si.Descendants(Main + "t"))
        {
            if (t.Parent?.Name != Main + "rPh")
            {
                sb.Append(t.Value);
            }
        }

        return sb.ToString();
    }

    private static List<IReadOnlyList<string?>> ReadRows(XDocument doc, List<string> shared)
    {
        var rows = new List<IReadOnlyList<string?>>();
        var data = doc.Root!.Element(Main + "sheetData");
        if (data is null)
        {
            return rows;
        }

        var nextRow = 1;
        foreach (var row in data.Elements(Main + "row"))
        {
            var rowNumber = int.TryParse((string?)row.Attribute("r"), NumberStyles.None, CultureInfo.InvariantCulture, out var r) ? r : nextRow;
            while (rows.Count < rowNumber - 1)
            {
                rows.Add([]);
            }

            var cells = new List<string?>();
            var nextCol = 0;
            foreach (var c in row.Elements(Main + "c"))
            {
                var col = ColumnIndex((string?)c.Attribute("r")) ?? nextCol;
                while (cells.Count < col)
                {
                    cells.Add(null);
                }

                cells.Add(CellValue(c, shared));
                nextCol = col + 1;
            }

            rows.Add(cells);
            nextRow = rowNumber + 1;
        }

        return rows;
    }

    private static string? CellValue(XElement c, List<string> shared)
    {
        var type = (string?)c.Attribute("t");
        var v = c.Element(Main + "v")?.Value;
        switch (type)
        {
            case "inlineStr":
                var inline = c.Element(Main + "is");
                return inline is null ? null : TextOf(inline);
            case "s":
                return int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var i) && i < shared.Count ? shared[i] : null;
            case "b":
                return v == "1" ? "TRUE" : "FALSE";
            default:
                return string.IsNullOrEmpty(v) ? null : v;
        }
    }

    private static int? ColumnIndex(string? cellRef)
    {
        if (string.IsNullOrEmpty(cellRef))
        {
            return null;
        }

        var n = 0;
        foreach (var ch in cellRef)
        {
            if (ch is >= 'A' and <= 'Z')
            {
                n = (n * 26) + (ch - 'A' + 1);
            }
            else
            {
                break;
            }
        }

        return n == 0 ? null : n - 1;
    }
}
