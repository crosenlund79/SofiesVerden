using System.Globalization;
using System.Text.Json;
using SofiesUnivers.Core.Model;
using SofiesUnivers.Core.Storage;
using static SofiesUnivers.Core.Excel.PrivateWorkbook;

namespace SofiesUnivers.Core.Excel;

public sealed record WorkbookImportResult
{
    public AppData? Data { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public DateTimeOffset? ExportedAt { get; init; }
    public int MissingReceipts { get; init; }

    public bool Ok => Errors.Count == 0 && Data is not null;
}

/// <summary>
/// Reads the app's own private workbook back into a complete dataset (for "restore from Excel").
/// The workbook is only accepted when the reconstructed data matches the checksum written at export,
/// so a spreadsheet that was edited afterwards is never silently treated as a safe backup.
/// Receipt files are not part of a workbook, so expenses come back without attachments.
/// </summary>
public static class WorkbookImporter
{
    public static WorkbookImportResult Import(byte[] bytes)
    {
        IReadOnlyDictionary<string, XlsxSheetData> sheets;
        try
        {
            sheets = XlsxReader.Read(bytes);
        }
        catch (Exception)
        {
            return Fail("Filen kunne ikke læses som et Excel-regneark.");
        }

        if (!sheets.TryGetValue(SheetNames.Info, out var infoSheet))
        {
            return Fail("Regnearket er ikke en eksport fra Sofies Univers (arket \"Info\" mangler).");
        }

        var info = KeyValues(infoSheet);
        if (info.GetValueOrDefault(InfoFormat) != FormatName)
        {
            return Fail("Regnearket er ikke en komplet eksport fra Sofies Univers.");
        }

        if (!TryInt(info.GetValueOrDefault(InfoFormatVersion), out var formatVersion) || formatVersion > FormatVersion)
        {
            return Fail("Regnearket er lavet med en nyere version af appen. Opdatér appen og prøv igen.");
        }

        if (!TryInt(info.GetValueOrDefault(InfoDataVersion), out var dataVersion) || dataVersion != AppData.CurrentVersion)
        {
            return Fail("Regnearket er fra en anden version af appen. Brug en ZIP-backup til gendannelse.");
        }

        var errors = new List<string>();
        var reader = new RowReader(errors);
        try
        {
            var data = Read(sheets, reader, dataVersion);
            if (errors.Count > 0)
            {
                return new WorkbookImportResult { Errors = errors.Take(20).ToList() };
            }

            var expected = info.GetValueOrDefault(InfoFingerprint);
            if (string.IsNullOrEmpty(expected) || DataSerializer.Fingerprint(data) != expected)
            {
                return Fail("Regnearket er blevet ændret efter eksporten og kan derfor ikke bruges til gendannelse. Brug en ZIP-backup, eller eksportér et nyt regneark fra appen.");
            }

            var missing = data.Expenses.Count(e => e.Attachment is not null);
            var stripped = data with { Expenses = data.Expenses.Select(e => e with { Attachment = null }).ToList() };
            var warnings = missing > 0
                ? new List<string> { $"{missing} kvittering{(missing == 1 ? "" : "er")} er ikke med i et regneark og vil mangle efter gendannelsen. Brug en ZIP-backup for at få kvitteringerne med." }
                : [];
            DateTimeOffset? exported = DateTimeOffset.TryParse(info.GetValueOrDefault(InfoExported), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var e) ? e : null;
            return new WorkbookImportResult { Data = stripped, Warnings = warnings, ExportedAt = exported, MissingReceipts = missing };
        }
        catch (Exception ex) when (ex is FormatException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return Fail("Regnearket kunne ikke læses: " + ex.Message);
        }
    }

    private static WorkbookImportResult Fail(string message) => new() { Errors = [message] };

    private static AppData Read(IReadOnlyDictionary<string, XlsxSheetData> sheets, RowReader r, int dataVersion)
    {
        var settingsRows = KeyValues(Sheet(sheets, SheetNames.Settings));
        var settingsJson = settingsRows.GetValueOrDefault(TechnicalSettingsKey) ?? throw new FormatException("indstillingerne mangler.");
        var settings = JsonSerializer.Deserialize(settingsJson, AppJsonContext.Default.AppSettings) ?? throw new FormatException("indstillingerne mangler.");

        var lists = Table(Sheet(sheets, SheetNames.Lists)).Select(row => (
            List: row.Get(Col.List),
            Item: new LookupItem
            {
                Id = r.Guid(row, Col.Id),
                Name = row.Get(Col.Name) ?? string.Empty,
                IsArchived = r.Bool(row, Col.Hidden),
                SortOrder = r.Int(row, Col.Order),
                CreatedAt = r.Stamp(row, Col.Created),
            })).ToList();

        var elementLinks = Table(Sheet(sheets, SheetNames.SessionElements))
            .Select(row => (Session: r.Guid(row, Col.SessionId), Nr: r.Int(row, Col.Number), Element: r.Guid(row, Col.ElementId)))
            .GroupBy(x => x.Session)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<Guid>)g.OrderBy(x => x.Nr).Select(x => x.Element).ToList());

        var sessions = Table(Sheet(sheets, SheetNames.Sessions)).Select(row =>
        {
            var id = r.Guid(row, Col.Id);
            return new Session
            {
                Id = id,
                Date = r.Date(row, Col.Date),
                ElementIds = elementLinks.GetValueOrDefault(id) ?? [],
                DurationMinutes = r.Int(row, Col.DurationMinutes),
                Price = r.Decimal(row, Col.Price),
                ClientNickname = row.Get(Col.Nickname),
                CreatedAt = r.Stamp(row, Col.Created),
                ModifiedAt = r.Stamp(row, Col.Modified),
            };
        }).ToList();

        var payments = Table(Sheet(sheets, SheetNames.Payments)).Select(row => new Payment
        {
            Id = r.Guid(row, Col.Id),
            SessionId = r.Guid(row, Col.SessionId),
            Date = r.Date(row, Col.PaymentDate),
            Amount = r.Decimal(row, Col.Amount),
            MethodId = r.Guid(row, Col.MethodId),
            CreatedAt = r.Stamp(row, Col.Created),
            ModifiedAt = r.Stamp(row, Col.Modified),
        }).ToList();

        var expenses = Table(Sheet(sheets, SheetNames.Expenses)).Select(row => new Expense
        {
            Id = r.Guid(row, Col.Id),
            Date = r.Date(row, Col.Date),
            Amount = r.Decimal(row, Col.Amount),
            CategoryId = r.Guid(row, Col.CategoryId),
            Description = row.Get(Col.Description),
            Attachment = row.Get(Col.ReceiptId) is null
                ? null
                : new AttachmentInfo
                {
                    Id = r.Guid(row, Col.ReceiptId),
                    FileName = row.Get(Col.ReceiptName) ?? string.Empty,
                    ContentType = row.Get(Col.ReceiptType) ?? string.Empty,
                    SizeBytes = r.Int(row, Col.ReceiptSize),
                    Sha256 = row.Get(Col.ReceiptHash) ?? string.Empty,
                },
            CreatedAt = r.Stamp(row, Col.Created),
            ModifiedAt = r.Stamp(row, Col.Modified),
        }).ToList();

        var trips = Table(Sheet(sheets, SheetNames.Trips)).Select(row => new Trip
        {
            Id = r.Guid(row, Col.Id),
            Date = r.Date(row, Col.Date),
            Purpose = row.Get(Col.Purpose) ?? string.Empty,
            FromLabel = row.Get(Col.From) ?? string.Empty,
            ToLabel = row.Get(Col.To) ?? string.Empty,
            DistanceKm = row.Get(Col.Km) is null ? null : r.Decimal(row, Col.Km),
            IncludeInTaxReport = r.Bool(row, Col.InTaxReport),
            IsClinicTrip = r.Bool(row, Col.ClinicTrip),
            Origin = row.Get(Col.Origin) == "Automatisk" ? TripOrigin.Automatic : TripOrigin.Manual,
            CreatedAt = r.Stamp(row, Col.Created),
            ModifiedAt = r.Stamp(row, Col.Modified),
        }).ToList();

        return new AppData
        {
            DataVersion = dataVersion,
            Settings = settings,
            ElementTypes = lists.Where(l => l.List == ListElement).Select(l => l.Item).ToList(),
            ExpenseCategories = lists.Where(l => l.List == ListCategory).Select(l => l.Item).ToList(),
            PaymentMethods = lists.Where(l => l.List == ListMethod).Select(l => l.Item).ToList(),
            Sessions = sessions,
            Payments = payments,
            Expenses = expenses,
            Trips = trips,
        };
    }

    private static XlsxSheetData Sheet(IReadOnlyDictionary<string, XlsxSheetData> sheets, string name) =>
        sheets.TryGetValue(name, out var s) ? s : throw new FormatException($"arket \"{name}\" mangler.");

    /// <summary>Two-column sheets (header row, then key/value rows).</summary>
    private static Dictionary<string, string?> KeyValues(XlsxSheetData sheet)
    {
        var result = new Dictionary<string, string?>();
        for (var i = 1; i < sheet.Rows.Count; i++)
        {
            if (sheet.Cell(i, 0) is { Length: > 0 } key)
            {
                result.TryAdd(key, sheet.Cell(i, 1));
            }
        }

        return result;
    }

    private sealed class TableRow(int rowNumber, Dictionary<string, int> columns, IReadOnlyList<string?> cells)
    {
        public int RowNumber { get; } = rowNumber;

        public string? Get(string column) =>
            columns.TryGetValue(column, out var i) && i < cells.Count && !string.IsNullOrEmpty(cells[i]) ? cells[i] : null;

        public bool Has(string column) => columns.ContainsKey(column);
    }

    private static IEnumerable<TableRow> Table(XlsxSheetData sheet)
    {
        if (sheet.Rows.Count == 0)
        {
            yield break;
        }

        var columns = new Dictionary<string, int>();
        var header = sheet.Rows[0];
        for (var i = 0; i < header.Count; i++)
        {
            if (header[i] is { Length: > 0 } name)
            {
                columns.TryAdd(name, i);
            }
        }

        for (var r = 1; r < sheet.Rows.Count; r++)
        {
            var cells = sheet.Rows[r];
            if (cells.All(string.IsNullOrEmpty))
            {
                continue;
            }

            yield return new TableRow(r + 1, columns, cells);
        }
    }

    private sealed class RowReader(List<string> errors)
    {
        private string Where(TableRow row, string column) => $"række {row.RowNumber}, kolonne \"{column}\"";

        private void Error(TableRow row, string column, string problem)
        {
            if (!row.Has(column))
            {
                errors.Add($"Kolonnen \"{column}\" mangler.");
            }
            else
            {
                errors.Add($"Ugyldig værdi i {Where(row, column)}: {problem}.");
            }
        }

        public Guid Guid(TableRow row, string column)
        {
            if (System.Guid.TryParse(row.Get(column), out var g))
            {
                return g;
            }

            Error(row, column, "ID mangler eller er ugyldigt");
            return System.Guid.Empty;
        }

        public DateOnly Date(TableRow row, string column)
        {
            var v = row.Get(column);
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial) && serial is > 0 and < 100000)
            {
                return XlsxDates.FromSerial(serial);
            }

            if (DateOnly.TryParseExact(v, ["yyyy-MM-dd", "dd-MM-yyyy"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            {
                return d;
            }

            Error(row, column, "dato mangler eller er ugyldig");
            return DateOnly.MinValue;
        }

        public decimal Decimal(TableRow row, string column)
        {
            if (decimal.TryParse(row.Get(column), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                // Excel stores doubles; amounts are always whole øre.
                return Math.Round(d, 2);
            }

            Error(row, column, "tal mangler eller er ugyldigt");
            return 0m;
        }

        public int Int(TableRow row, string column)
        {
            if (decimal.TryParse(row.Get(column), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d == Math.Floor(d) && d is >= int.MinValue and <= int.MaxValue)
            {
                return (int)d;
            }

            Error(row, column, "heltal mangler eller er ugyldigt");
            return 0;
        }

        public bool Bool(TableRow row, string column)
        {
            switch (row.Get(column))
            {
                case "Ja":
                case "TRUE":
                    return true;
                case "Nej":
                case "FALSE":
                    return false;
                default:
                    Error(row, column, "skal være Ja eller Nej");
                    return false;
            }
        }

        public DateTimeOffset Stamp(TableRow row, string column)
        {
            if (DateTimeOffset.TryParseExact(row.Get(column), "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t))
            {
                return t;
            }

            Error(row, column, "tidspunkt mangler eller er ugyldigt");
            return DateTimeOffset.MinValue;
        }
    }

    private static bool TryInt(string? value, out int result)
    {
        result = 0;
        if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && d == Math.Floor(d) && d is >= 0 and <= 10000)
        {
            result = (int)d;
            return true;
        }

        return false;
    }
}
