using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Xml;

namespace SofiesUnivers.Core.Excel;

public enum CellStyle
{
    Default = 0,
    Header = 1,
    Date = 2,
    Money = 3,
    Integer = 4,
    Decimal = 5,
    Title = 6,
    Note = 7,
    MoneyBold = 8,
    Bold = 9,
    Wrap = 10,
    Subtitle = 11,
}

public enum CellKind
{
    Empty,
    Text,
    Number,
}

/// <summary>A single cell. Numbers stay numeric (dates are Excel serial numbers with a date format).</summary>
public readonly record struct Cell(CellKind Kind, string? Text, decimal Number, CellStyle Style)
{
    public static readonly Cell Empty = new(CellKind.Empty, null, 0, CellStyle.Default);

    public static Cell T(string? text, CellStyle style = CellStyle.Default) =>
        string.IsNullOrEmpty(text) ? Empty with { Style = style } : new(CellKind.Text, text, 0, style);

    public static Cell N(decimal value, CellStyle style = CellStyle.Money) => new(CellKind.Number, null, value, style);

    public static Cell N(decimal? value, CellStyle style = CellStyle.Money) => value is { } v ? N(v, style) : Empty;

    public static Cell D(DateOnly date) => new(CellKind.Number, null, XlsxDates.ToSerial(date), CellStyle.Date);

    public static Cell H(string text) => new(CellKind.Text, text, 0, CellStyle.Header);

    public static Cell YesNo(bool value) => T(value ? "Ja" : "Nej");
}

public static class XlsxDates
{
    private static readonly DateOnly Epoch = new(1899, 12, 30);

    public static decimal ToSerial(DateOnly date) => date.DayNumber - Epoch.DayNumber;

    public static DateOnly FromSerial(double serial) => Epoch.AddDays((int)Math.Floor(serial));
}

public sealed class XlsxSheet
{
    private readonly List<Cell[]> _rows = [];

    internal XlsxSheet(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public IReadOnlyList<Cell[]> Rows => _rows;

    public double[] ColumnWidths { get; private set; } = [];

    /// <summary>Number of rows frozen at the top (header).</summary>
    public int FrozenRows { get; private set; }

    /// <summary>1-based row number of the header row that gets an auto filter, or 0.</summary>
    public int AutoFilterRow { get; private set; }

    public int AutoFilterColumns { get; private set; }

    public XlsxSheet Row(params Cell[] cells)
    {
        _rows.Add(cells);
        return this;
    }

    public XlsxSheet BlankRow() => Row();

    public XlsxSheet Columns(params double[] widths)
    {
        ColumnWidths = widths;
        return this;
    }

    /// <summary>Adds a header row with auto filter and freezes everything above and including it.</summary>
    public XlsxSheet Table(params string[] headers)
    {
        Row(headers.Select(Cell.H).ToArray());
        AutoFilterRow = _rows.Count;
        AutoFilterColumns = headers.Length;
        FrozenRows = _rows.Count;
        return this;
    }
}

/// <summary>
/// Minimal, dependency-free writer for Office Open XML spreadsheets (.xlsx).
/// Produces inline strings, numeric cells, a small fixed style sheet, column widths, frozen
/// header rows and auto filters. Validated against the official schema in the test suite.
/// </summary>
public sealed class XlsxWorkbook
{
    private const string MainNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PkgRelNs = "http://schemas.openxmlformats.org/package/2006/relationships";

    private readonly List<XlsxSheet> _sheets = [];

    public string Title { get; init; } = string.Empty;
    public string Subject { get; init; } = string.Empty;
    public string Author { get; init; } = "Sofies Univers";
    public DateTimeOffset Created { get; init; } = DateTimeOffset.UtcNow;

    public IReadOnlyList<XlsxSheet> Sheets => _sheets;

    public XlsxSheet AddSheet(string name)
    {
        if (name.Length is 0 or > 31 || name.IndexOfAny(['[', ']', ':', '*', '?', '/', '\\']) >= 0)
        {
            throw new ArgumentException($"Invalid sheet name '{name}'.", nameof(name));
        }

        if (_sheets.Any(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            throw new ArgumentException($"Duplicate sheet name '{name}'.", nameof(name));
        }

        var sheet = new XlsxSheet(name);
        _sheets.Add(sheet);
        return sheet;
    }

    public byte[] ToBytes()
    {
        if (_sheets.Count == 0)
        {
            throw new InvalidOperationException("A workbook needs at least one sheet.");
        }

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "[Content_Types].xml", WriteContentTypes);
            Write(zip, "_rels/.rels", WriteRootRels);
            Write(zip, "docProps/core.xml", WriteCore);
            Write(zip, "docProps/app.xml", WriteApp);
            Write(zip, "xl/workbook.xml", WriteWorkbook);
            Write(zip, "xl/_rels/workbook.xml.rels", WriteWorkbookRels);
            Write(zip, "xl/styles.xml", WriteStyles);
            for (var i = 0; i < _sheets.Count; i++)
            {
                var sheet = _sheets[i];
                var index = i;
                Write(zip, $"xl/worksheets/sheet{i + 1}.xml", w => WriteSheet(w, sheet, index == 0));
            }
        }

        return ms.ToArray();
    }

    private static void Write(ZipArchive zip, string path, Action<XmlWriter> body)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        using var writer = XmlWriter.Create(stream, new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            CheckCharacters = true,
        });
        writer.WriteStartDocument(true);
        body(writer);
        writer.WriteEndDocument();
    }

    private void WriteContentTypes(XmlWriter w)
    {
        const string ns = "http://schemas.openxmlformats.org/package/2006/content-types";
        w.WriteStartElement("Types", ns);
        Default(w, ns, "rels", "application/vnd.openxmlformats-package.relationships+xml");
        Default(w, ns, "xml", "application/xml");
        Override(w, ns, "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
        Override(w, ns, "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
        for (var i = 1; i <= _sheets.Count; i++)
        {
            Override(w, ns, $"/xl/worksheets/sheet{i}.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
        }

        Override(w, ns, "/docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml");
        Override(w, ns, "/docProps/app.xml", "application/vnd.openxmlformats-officedocument.extended-properties+xml");
        w.WriteEndElement();

        static void Default(XmlWriter w, string ns, string ext, string type)
        {
            w.WriteStartElement("Default", ns);
            w.WriteAttributeString("Extension", ext);
            w.WriteAttributeString("ContentType", type);
            w.WriteEndElement();
        }

        static void Override(XmlWriter w, string ns, string part, string type)
        {
            w.WriteStartElement("Override", ns);
            w.WriteAttributeString("PartName", part);
            w.WriteAttributeString("ContentType", type);
            w.WriteEndElement();
        }
    }

    private static void WriteRootRels(XmlWriter w)
    {
        w.WriteStartElement("Relationships", PkgRelNs);
        Rel(w, "rId1", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument", "xl/workbook.xml");
        Rel(w, "rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml");
        Rel(w, "rId3", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties", "docProps/app.xml");
        w.WriteEndElement();
    }

    private static void Rel(XmlWriter w, string id, string type, string target)
    {
        w.WriteStartElement("Relationship", PkgRelNs);
        w.WriteAttributeString("Id", id);
        w.WriteAttributeString("Type", type);
        w.WriteAttributeString("Target", target);
        w.WriteEndElement();
    }

    private void WriteCore(XmlWriter w)
    {
        const string cp = "http://schemas.openxmlformats.org/package/2006/metadata/core-properties";
        const string dc = "http://purl.org/dc/elements/1.1/";
        const string dcterms = "http://purl.org/dc/terms/";
        const string xsi = "http://www.w3.org/2001/XMLSchema-instance";
        w.WriteStartElement("cp", "coreProperties", cp);
        w.WriteAttributeString("xmlns", "dc", null, dc);
        w.WriteAttributeString("xmlns", "dcterms", null, dcterms);
        w.WriteAttributeString("xmlns", "xsi", null, xsi);
        w.WriteElementString("dc", "title", dc, Clean(Title));
        w.WriteElementString("dc", "subject", dc, Clean(Subject));
        w.WriteElementString("dc", "creator", dc, Clean(Author));
        w.WriteElementString("cp", "lastModifiedBy", cp, Clean(Author));
        var stamp = Created.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        foreach (var name in new[] { "created", "modified" })
        {
            w.WriteStartElement("dcterms", name, dcterms);
            w.WriteAttributeString("xsi", "type", xsi, "dcterms:W3CDTF");
            w.WriteString(stamp);
            w.WriteEndElement();
        }

        w.WriteEndElement();
    }

    private static void WriteApp(XmlWriter w)
    {
        w.WriteStartElement("Properties", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties");
        w.WriteElementString("Application", "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties", "Sofies Univers");
        w.WriteEndElement();
    }

    private void WriteWorkbook(XmlWriter w)
    {
        w.WriteStartElement("workbook", MainNs);
        w.WriteAttributeString("xmlns", "r", null, RelNs);
        w.WriteStartElement("bookViews", MainNs);
        w.WriteStartElement("workbookView", MainNs);
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteStartElement("sheets", MainNs);
        for (var i = 0; i < _sheets.Count; i++)
        {
            w.WriteStartElement("sheet", MainNs);
            w.WriteAttributeString("name", _sheets[i].Name);
            w.WriteAttributeString("sheetId", (i + 1).ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("r", "id", RelNs, $"rId{i + 1}");
            w.WriteEndElement();
        }

        w.WriteEndElement();

        var filtered = _sheets.Select((s, i) => (s, i)).Where(x => x.s.AutoFilterRow > 0).ToList();
        if (filtered.Count > 0)
        {
            w.WriteStartElement("definedNames", MainNs);
            foreach (var (sheet, index) in filtered)
            {
                w.WriteStartElement("definedName", MainNs);
                w.WriteAttributeString("name", "_xlnm._FilterDatabase");
                w.WriteAttributeString("localSheetId", index.ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("hidden", "1");
                var range = FilterRange(sheet);
                w.WriteString($"'{sheet.Name.Replace("'", "''")}'!{Absolute(range.From)}:{Absolute(range.To)}");
                w.WriteEndElement();
            }

            w.WriteEndElement();
        }

        w.WriteEndElement();
    }

    private void WriteWorkbookRels(XmlWriter w)
    {
        w.WriteStartElement("Relationships", PkgRelNs);
        for (var i = 1; i <= _sheets.Count; i++)
        {
            Rel(w, $"rId{i}", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet", $"worksheets/sheet{i}.xml");
        }

        Rel(w, $"rId{_sheets.Count + 1}", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles", "styles.xml");
        w.WriteEndElement();
    }

    private static void WriteStyles(XmlWriter w)
    {
        w.WriteStartElement("styleSheet", MainNs);

        w.WriteStartElement("numFmts", MainNs);
        w.WriteAttributeString("count", "2");
        NumFmt(w, 164, "dd\\-mm\\-yyyy");
        NumFmt(w, 165, "#,##0.0#");
        w.WriteEndElement();

        // Fonts: 0 normal, 1 bold, 2 title, 3 italic grey, 4 subtitle
        w.WriteStartElement("fonts", MainNs);
        w.WriteAttributeString("count", "5");
        Font(w, bold: false, italic: false, size: 11, color: null);
        Font(w, bold: true, italic: false, size: 11, color: null);
        Font(w, bold: true, italic: false, size: 16, color: "FF6B2D5C");
        Font(w, bold: false, italic: true, size: 10, color: "FF595959");
        Font(w, bold: true, italic: false, size: 12, color: "FF6B2D5C");
        w.WriteEndElement();

        w.WriteStartElement("fills", MainNs);
        w.WriteAttributeString("count", "3");
        PatternFill(w, "none", null);
        PatternFill(w, "gray125", null);
        PatternFill(w, "solid", "FFF6E3D4");
        w.WriteEndElement();

        w.WriteStartElement("borders", MainNs);
        w.WriteAttributeString("count", "2");
        Border(w, false);
        Border(w, true);
        w.WriteEndElement();

        w.WriteStartElement("cellStyleXfs", MainNs);
        w.WriteAttributeString("count", "1");
        Xf(w, 0, 0, 0, 0, includeXfId: false, wrap: false);
        w.WriteEndElement();

        // Must match the CellStyle enum order.
        w.WriteStartElement("cellXfs", MainNs);
        w.WriteAttributeString("count", "12");
        Xf(w, numFmt: 0, font: 0, fill: 0, border: 0);       // Default
        Xf(w, numFmt: 0, font: 1, fill: 2, border: 1);       // Header
        Xf(w, numFmt: 164, font: 0, fill: 0, border: 0);     // Date
        Xf(w, numFmt: 4, font: 0, fill: 0, border: 0);       // Money  #,##0.00
        Xf(w, numFmt: 1, font: 0, fill: 0, border: 0);       // Integer 0
        Xf(w, numFmt: 165, font: 0, fill: 0, border: 0);     // Decimal
        Xf(w, numFmt: 0, font: 2, fill: 0, border: 0);       // Title
        Xf(w, numFmt: 0, font: 3, fill: 0, border: 0);       // Note
        Xf(w, numFmt: 4, font: 1, fill: 0, border: 0);       // MoneyBold
        Xf(w, numFmt: 0, font: 1, fill: 0, border: 0);       // Bold
        Xf(w, numFmt: 0, font: 0, fill: 0, border: 0, wrap: true); // Wrap
        Xf(w, numFmt: 0, font: 4, fill: 0, border: 0);       // Subtitle
        w.WriteEndElement();

        w.WriteStartElement("cellStyles", MainNs);
        w.WriteAttributeString("count", "1");
        w.WriteStartElement("cellStyle", MainNs);
        w.WriteAttributeString("name", "Normal");
        w.WriteAttributeString("xfId", "0");
        w.WriteAttributeString("builtinId", "0");
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteEndElement();

        static void NumFmt(XmlWriter w, int id, string code)
        {
            w.WriteStartElement("numFmt", MainNs);
            w.WriteAttributeString("numFmtId", id.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("formatCode", code);
            w.WriteEndElement();
        }

        static void Font(XmlWriter w, bool bold, bool italic, int size, string? color)
        {
            w.WriteStartElement("font", MainNs);
            if (bold)
            {
                w.WriteStartElement("b", MainNs);
                w.WriteEndElement();
            }

            if (italic)
            {
                w.WriteStartElement("i", MainNs);
                w.WriteEndElement();
            }

            w.WriteStartElement("sz", MainNs);
            w.WriteAttributeString("val", size.ToString(CultureInfo.InvariantCulture));
            w.WriteEndElement();
            if (color is not null)
            {
                w.WriteStartElement("color", MainNs);
                w.WriteAttributeString("rgb", color);
                w.WriteEndElement();
            }

            w.WriteStartElement("name", MainNs);
            w.WriteAttributeString("val", "Calibri");
            w.WriteEndElement();
            w.WriteStartElement("family", MainNs);
            w.WriteAttributeString("val", "2");
            w.WriteEndElement();
            w.WriteEndElement();
        }

        static void PatternFill(XmlWriter w, string pattern, string? color)
        {
            w.WriteStartElement("fill", MainNs);
            w.WriteStartElement("patternFill", MainNs);
            w.WriteAttributeString("patternType", pattern);
            if (color is not null)
            {
                w.WriteStartElement("fgColor", MainNs);
                w.WriteAttributeString("rgb", color);
                w.WriteEndElement();
                w.WriteStartElement("bgColor", MainNs);
                w.WriteAttributeString("indexed", "64");
                w.WriteEndElement();
            }

            w.WriteEndElement();
            w.WriteEndElement();
        }

        static void Border(XmlWriter w, bool bottom)
        {
            w.WriteStartElement("border", MainNs);
            foreach (var side in new[] { "left", "right", "top", "bottom", "diagonal" })
            {
                w.WriteStartElement(side, MainNs);
                if (bottom && side == "bottom")
                {
                    w.WriteAttributeString("style", "thin");
                    w.WriteStartElement("color", MainNs);
                    w.WriteAttributeString("rgb", "FFB08D7A");
                    w.WriteEndElement();
                }

                w.WriteEndElement();
            }

            w.WriteEndElement();
        }

        static void Xf(XmlWriter w, int numFmt, int font, int fill, int border, bool includeXfId = true, bool wrap = false)
        {
            w.WriteStartElement("xf", MainNs);
            w.WriteAttributeString("numFmtId", numFmt.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("fontId", font.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("fillId", fill.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("borderId", border.ToString(CultureInfo.InvariantCulture));
            if (includeXfId)
            {
                w.WriteAttributeString("xfId", "0");
                if (numFmt != 0)
                {
                    w.WriteAttributeString("applyNumberFormat", "1");
                }

                if (font != 0)
                {
                    w.WriteAttributeString("applyFont", "1");
                }

                if (fill != 0)
                {
                    w.WriteAttributeString("applyFill", "1");
                }

                if (border != 0)
                {
                    w.WriteAttributeString("applyBorder", "1");
                }

                if (wrap)
                {
                    w.WriteAttributeString("applyAlignment", "1");
                }
            }

            if (wrap)
            {
                w.WriteStartElement("alignment", MainNs);
                w.WriteAttributeString("vertical", "top");
                w.WriteAttributeString("wrapText", "1");
                w.WriteEndElement();
            }

            w.WriteEndElement();
        }
    }

    private static void WriteSheet(XmlWriter w, XlsxSheet sheet, bool selected)
    {
        w.WriteStartElement("worksheet", MainNs);
        w.WriteAttributeString("xmlns", "r", null, RelNs);

        w.WriteStartElement("sheetViews", MainNs);
        w.WriteStartElement("sheetView", MainNs);
        if (selected)
        {
            w.WriteAttributeString("tabSelected", "1");
        }

        w.WriteAttributeString("workbookViewId", "0");
        if (sheet.FrozenRows > 0)
        {
            var topLeft = $"A{sheet.FrozenRows + 1}";
            w.WriteStartElement("pane", MainNs);
            w.WriteAttributeString("ySplit", sheet.FrozenRows.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("topLeftCell", topLeft);
            w.WriteAttributeString("activePane", "bottomLeft");
            w.WriteAttributeString("state", "frozen");
            w.WriteEndElement();
            w.WriteStartElement("selection", MainNs);
            w.WriteAttributeString("pane", "bottomLeft");
            w.WriteAttributeString("activeCell", topLeft);
            w.WriteAttributeString("sqref", topLeft);
            w.WriteEndElement();
        }

        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteStartElement("sheetFormatPr", MainNs);
        w.WriteAttributeString("defaultRowHeight", "15");
        w.WriteEndElement();

        if (sheet.ColumnWidths.Length > 0)
        {
            w.WriteStartElement("cols", MainNs);
            for (var i = 0; i < sheet.ColumnWidths.Length; i++)
            {
                w.WriteStartElement("col", MainNs);
                var n = (i + 1).ToString(CultureInfo.InvariantCulture);
                w.WriteAttributeString("min", n);
                w.WriteAttributeString("max", n);
                w.WriteAttributeString("width", sheet.ColumnWidths[i].ToString("0.##", CultureInfo.InvariantCulture));
                w.WriteAttributeString("customWidth", "1");
                w.WriteEndElement();
            }

            w.WriteEndElement();
        }

        w.WriteStartElement("sheetData", MainNs);
        for (var r = 0; r < sheet.Rows.Count; r++)
        {
            var cells = sheet.Rows[r];
            var rowNumber = (r + 1).ToString(CultureInfo.InvariantCulture);
            w.WriteStartElement("row", MainNs);
            w.WriteAttributeString("r", rowNumber);
            for (var c = 0; c < cells.Length; c++)
            {
                var cell = cells[c];
                if (cell.Kind == CellKind.Empty && cell.Style == CellStyle.Default)
                {
                    continue;
                }

                w.WriteStartElement("c", MainNs);
                w.WriteAttributeString("r", ColumnName(c) + rowNumber);
                if (cell.Style != CellStyle.Default)
                {
                    w.WriteAttributeString("s", ((int)cell.Style).ToString(CultureInfo.InvariantCulture));
                }

                switch (cell.Kind)
                {
                    case CellKind.Text:
                        w.WriteAttributeString("t", "inlineStr");
                        w.WriteStartElement("is", MainNs);
                        w.WriteStartElement("t", MainNs);
                        var text = Clean(cell.Text);
                        if (text.Length > 0 && (char.IsWhiteSpace(text[0]) || char.IsWhiteSpace(text[^1]) || text.Contains('\n')))
                        {
                            w.WriteAttributeString("xml", "space", null, "preserve");
                        }

                        w.WriteString(text);
                        w.WriteEndElement();
                        w.WriteEndElement();
                        break;

                    case CellKind.Number:
                        w.WriteElementString("v", MainNs, cell.Number.ToString(CultureInfo.InvariantCulture));
                        break;
                }

                w.WriteEndElement();
            }

            w.WriteEndElement();
        }

        w.WriteEndElement();

        if (sheet.AutoFilterRow > 0)
        {
            var range = FilterRange(sheet);
            w.WriteStartElement("autoFilter", MainNs);
            w.WriteAttributeString("ref", $"{range.From}:{range.To}");
            w.WriteEndElement();
        }

        w.WriteStartElement("pageMargins", MainNs);
        foreach (var (name, value) in new[] { ("left", "0.7"), ("right", "0.7"), ("top", "0.75"), ("bottom", "0.75"), ("header", "0.3"), ("footer", "0.3") })
        {
            w.WriteAttributeString(name, value);
        }

        w.WriteEndElement();
        w.WriteStartElement("pageSetup", MainNs);
        w.WriteAttributeString("paperSize", "9");
        w.WriteAttributeString("orientation", "landscape");
        w.WriteEndElement();

        w.WriteEndElement();
    }

    private static (string From, string To) FilterRange(XlsxSheet sheet)
    {
        var lastRow = Math.Max(sheet.AutoFilterRow, sheet.Rows.Count);
        return ($"A{sheet.AutoFilterRow}", $"{ColumnName(sheet.AutoFilterColumns - 1)}{lastRow}");
    }

    private static string Absolute(string cellRef)
    {
        var letters = new string(cellRef.TakeWhile(char.IsLetter).ToArray());
        return $"${letters}${cellRef[letters.Length..]}";
    }

    public static string ColumnName(int zeroBasedIndex)
    {
        var n = zeroBasedIndex + 1;
        var name = string.Empty;
        while (n > 0)
        {
            var rem = (n - 1) % 26;
            name = (char)('A' + rem) + name;
            n = (n - 1) / 26;
        }

        return name;
    }

    /// <summary>Removes characters that are not allowed in XML and limits to Excel's cell size.</summary>
    internal static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                sb.Append(ch).Append(text[++i]);
            }
            else if (char.IsSurrogate(ch))
            {
                // drop a lone surrogate
            }
            else if (ch == '\t' || ch == '\n' || ch == '\r' || (ch >= 0x20 && ch != 0xFFFE && ch != 0xFFFF))
            {
                sb.Append(ch);
            }
        }

        var result = sb.ToString();
        if (result.Length > 32767)
        {
            result = result[..32767];
        }

        // Do not cut a surrogate pair in half.
        if (result.Length > 0 && char.IsHighSurrogate(result[^1]))
        {
            result = result[..^1];
        }

        return result;
    }
}
