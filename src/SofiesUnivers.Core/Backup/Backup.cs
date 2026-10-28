using System.IO.Compression;
using System.Text;
using System.Text.Json;
using SofiesUnivers.Core.Domain;
using SofiesUnivers.Core.Excel;
using SofiesUnivers.Core.Model;
using SofiesUnivers.Core.Storage;
using SofiesUnivers.Core.Text;

namespace SofiesUnivers.Core.Backup;

/// <summary>Contents of Backup-info.json: version and integrity metadata.</summary>
public sealed record BackupInfo
{
    public const string FormatName = "sofies-univers-backup";
    public const int CurrentFormatVersion = 1;

    public string Format { get; init; } = FormatName;
    public int FormatVersion { get; init; } = CurrentFormatVersion;
    public required int DataVersion { get; init; }
    public required string AppVersion { get; init; }
    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>The authoritative file for restore.</summary>
    public required string DataFile { get; init; }

    public required string DataSha256 { get; init; }
    public required string WorkbookFile { get; init; }
    public required string WorkbookSha256 { get; init; }
    public required IReadOnlyList<BackupFileEntry> Attachments { get; init; }
    public required BackupCounts Counts { get; init; }
    public string Note { get; init; } = "Gendannelse bruger datafilen og kvitteringerne. Regnearket er til at læse i Excel.";
}

public sealed record BackupFileEntry
{
    public required Guid AttachmentId { get; init; }
    public required string Path { get; init; }
    public required long SizeBytes { get; init; }
    public required string Sha256 { get; init; }
}

public sealed record BackupCounts
{
    public int Sessions { get; init; }
    public int Payments { get; init; }
    public int Expenses { get; init; }
    public int Receipts { get; init; }
    public int Trips { get; init; }
    public int SessionElements { get; init; }
    public int ExpenseCategories { get; init; }
    public int PaymentMethods { get; init; }

    public static BackupCounts From(AppData d) => new()
    {
        Sessions = d.Sessions.Count,
        Payments = d.Payments.Count,
        Expenses = d.Expenses.Count,
        Receipts = d.Expenses.Count(e => e.Attachment is not null),
        Trips = d.Trips.Count,
        SessionElements = d.ElementTypes.Count,
        ExpenseCategories = d.ExpenseCategories.Count,
        PaymentMethods = d.PaymentMethods.Count,
    };
}

public sealed record BackupFile(byte[] Bytes, string FileName, BackupInfo Info, IReadOnlyList<string> Warnings);

/// <summary>Builds the portable ZIP backup.</summary>
public static class BackupWriter
{
    public static async Task<BackupFile> BuildAsync(AppData data, Func<Guid, Task<AttachmentBlob?>> loadAttachment, string appVersion, DateTimeOffset now, DateOnly today)
    {
        var warnings = new List<string>();

        // Collect receipts first. A receipt that cannot be read is left out of the backup and its
        // reference removed, so the backup is still complete and restorable.
        var blobs = new Dictionary<Guid, AttachmentBlob>();
        var expenses = new List<Expense>();
        foreach (var e in data.Expenses)
        {
            if (e.Attachment is { } a)
            {
                AttachmentBlob? blob = null;
                try
                {
                    blob = await loadAttachment(a.Id);
                }
                catch (Exception)
                {
                    blob = null;
                }

                if (blob is null || blob.Data.Length == 0)
                {
                    warnings.Add($"Kvitteringen til udgiften d. {Dk.Date(e.Date)} kunne ikke findes og er ikke med i backuppen.");
                    expenses.Add(e with { Attachment = null });
                    continue;
                }

                blobs[a.Id] = blob;
            }

            expenses.Add(e);
        }

        if (warnings.Count > 0)
        {
            data = data with { Expenses = expenses };
        }

        var paths = BackupPaths.AttachmentPaths(data);
        var dataJson = Encoding.UTF8.GetBytes(DataSerializer.SerializeAppData(data));
        var workbook = PrivateWorkbook.Build(data, now);

        using var ms = new MemoryStream();
        var entries = new List<BackupFileEntry>();
        BackupInfo info;
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, BackupPaths.WorkbookFile, workbook, CompressionLevel.Optimal);
            Add(zip, BackupPaths.DataFile, dataJson, CompressionLevel.Optimal);
            foreach (var e in data.Expenses.Where(e => e.Attachment is not null).OrderBy(e => e.Date))
            {
                var blob = blobs[e.Attachment!.Id];
                var path = paths[e.Attachment.Id];
                Add(zip, path, blob.Data, CompressionLevel.NoCompression); // images and PDFs are already compressed
                entries.Add(new BackupFileEntry
                {
                    AttachmentId = e.Attachment.Id,
                    Path = path,
                    SizeBytes = blob.Data.Length,
                    Sha256 = Hashing.Sha256Hex(blob.Data),
                });
            }

            info = new BackupInfo
            {
                DataVersion = data.DataVersion,
                AppVersion = appVersion,
                CreatedAt = now,
                DataFile = BackupPaths.DataFile,
                DataSha256 = Hashing.Sha256Hex(dataJson),
                WorkbookFile = BackupPaths.WorkbookFile,
                WorkbookSha256 = Hashing.Sha256Hex(workbook),
                Attachments = entries,
                Counts = BackupCounts.From(data),
            };
            var infoJson = JsonSerializer.SerializeToUtf8Bytes(info, new AppJsonContext(new JsonSerializerOptions(AppJsonContext.Default.Options) { WriteIndented = true }).BackupInfo);
            Add(zip, BackupPaths.InfoFile, infoJson, CompressionLevel.Optimal);
            Add(zip, BackupPaths.ReadMeFile, Encoding.UTF8.GetBytes(ReadMe(info)), CompressionLevel.Optimal);
        }

        return new BackupFile(ms.ToArray(), BackupPaths.BackupFileName(today), info, warnings);
    }

    private static void Add(ZipArchive zip, string path, byte[] content, CompressionLevel level)
    {
        var entry = zip.CreateEntry(path, level);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private static string ReadMe(BackupInfo info) => $"""
        Sofies Univers – backup
        Lavet: {Dk.Timestamp(info.CreatedAt)}

        Indhold:
        - {BackupPaths.WorkbookFile}: alle registreringer som regneark. Kan åbnes i Excel.
        - {BackupPaths.ReceiptFolder}: kvitteringer ({info.Attachments.Count} filer).
        - {BackupPaths.DataFile}: data, som appen bruger ved gendannelse.
        - {BackupPaths.InfoFile}: version og kontrolsummer.

        Gendannelse: Åbn appen, gå til Indstillinger → Backup og gendannelse → Gendan, og vælg denne ZIP-fil.
        Ret ikke i filerne – så kan backuppen ikke bruges til gendannelse.

        Filen indeholder personlige oplysninger (klienternes kaldenavne). Opbevar den sikkert.
        """;
}

public sealed record BackupReadResult
{
    public BackupInfo? Info { get; init; }
    public AppData? Data { get; init; }
    public IReadOnlyList<AttachmentBlob> Attachments { get; init; } = [];
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public bool Ok => Errors.Count == 0 && Data is not null && Info is not null;

    public static BackupReadResult Fail(params string[] errors) => new() { Errors = errors };
}

/// <summary>Reads and fully validates a ZIP backup without touching the current data.</summary>
public static class BackupReader
{
    public const long MaxTotalBytes = 600L * 1024 * 1024;
    public const int MaxEntries = 20_000;

    public static BackupReadResult Read(byte[] zipBytes)
    {
        ZipArchive zip;
        try
        {
            zip = new ZipArchive(new MemoryStream(zipBytes, writable: false), ZipArchiveMode.Read);
        }
        catch (Exception)
        {
            return BackupReadResult.Fail("Filen er ikke en gyldig backup (ZIP-filen kan ikke åbnes).");
        }

        using (zip)
        {
            if (zip.Entries.Count > MaxEntries || zip.Entries.Sum(e => e.Length) > MaxTotalBytes)
            {
                return BackupReadResult.Fail("Backup-filen er for stor eller indeholder for mange filer.");
            }

            var infoBytes = ReadEntry(zip, BackupPaths.InfoFile);
            if (infoBytes is null)
            {
                return BackupReadResult.Fail($"Filen er ikke en backup fra Sofies Univers ({BackupPaths.InfoFile} mangler).");
            }

            BackupInfo? info;
            try
            {
                info = JsonSerializer.Deserialize(infoBytes, AppJsonContext.Default.BackupInfo);
            }
            catch (JsonException)
            {
                info = null;
            }

            if (info is null || info.Format != BackupInfo.FormatName)
            {
                return BackupReadResult.Fail("Filen er ikke en backup fra Sofies Univers.");
            }

            if (info.FormatVersion > BackupInfo.CurrentFormatVersion || info.DataVersion > AppData.CurrentVersion)
            {
                return BackupReadResult.Fail("Backuppen er lavet med en nyere version af appen. Opdatér appen og prøv igen.");
            }

            var dataBytes = ReadEntry(zip, info.DataFile);
            if (dataBytes is null)
            {
                return BackupReadResult.Fail("Backuppen er ufuldstændig (datafilen mangler).");
            }

            if (Hashing.Sha256Hex(dataBytes) != info.DataSha256)
            {
                return BackupReadResult.Fail("Backuppens data er beskadiget eller ændret (kontrolsummen passer ikke).");
            }

            AppData data;
            try
            {
                data = DataSerializer.DeserializeAppData(Encoding.UTF8.GetString(dataBytes));
            }
            catch (UnsupportedDataVersionException ex)
            {
                return BackupReadResult.Fail(ex.Message);
            }
            catch (Exception)
            {
                return BackupReadResult.Fail("Backuppens data kunne ikke læses.");
            }

            var errors = new List<string>();
            var attachments = new List<AttachmentBlob>();
            var expenseByAttachment = data.Expenses.Where(e => e.Attachment is not null).ToDictionary(e => e.Attachment!.Id, e => e.Attachment!);
            foreach (var entry in info.Attachments)
            {
                if (!expenseByAttachment.TryGetValue(entry.AttachmentId, out var meta))
                {
                    continue; // a file nobody refers to is ignored
                }

                var bytes = ReadEntry(zip, entry.Path);
                if (bytes is null)
                {
                    errors.Add($"Kvitteringen \"{entry.Path}\" mangler i backuppen.");
                }
                else if (bytes.Length != entry.SizeBytes || Hashing.Sha256Hex(bytes) != entry.Sha256 || entry.Sha256 != meta.Sha256)
                {
                    errors.Add($"Kvitteringen \"{entry.Path}\" er beskadiget (kontrolsummen passer ikke).");
                }
                else
                {
                    attachments.Add(new AttachmentBlob(entry.AttachmentId, meta.FileName, meta.ContentType, bytes));
                }
            }

            errors.AddRange(DataValidator.Validate(data, attachments.Select(a => a.Id).ToHashSet()));
            if (errors.Count > 0)
            {
                return BackupReadResult.Fail(errors.Distinct().ToArray());
            }

            var warnings = new List<string>();
            var workbook = ReadEntry(zip, info.WorkbookFile);
            if (workbook is null || Hashing.Sha256Hex(workbook) != info.WorkbookSha256)
            {
                warnings.Add("Regnearket i backuppen mangler eller er ændret. Det påvirker ikke gendannelsen.");
            }

            return new BackupReadResult { Info = info, Data = data, Attachments = attachments, Warnings = warnings };
        }
    }

    private static byte[]? ReadEntry(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        if (entry is null || entry.Length > MaxTotalBytes)
        {
            return null;
        }

        using var stream = entry.Open();
        using var ms = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
