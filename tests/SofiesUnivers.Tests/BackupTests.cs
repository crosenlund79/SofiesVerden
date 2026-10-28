using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using SofiesUnivers.Core.Backup;
using SofiesUnivers.Core.Domain;
using SofiesUnivers.Core.Excel;
using SofiesUnivers.Core.Model;
using SofiesUnivers.Core.Storage;
using SofiesUnivers.Tests.Infrastructure;
using static SofiesUnivers.Tests.Infrastructure.Dates;

namespace SofiesUnivers.Tests;

public class BackupTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 9, 0, 0, TimeSpan.Zero);

    /// <summary>A realistic year of data with receipts.</summary>
    private static async Task<TestWorld> RealisticAsync()
    {
        var w = await ReportTests.SampleAsync();
        var rnd = new Random(42);
        var tantra = (await w.Ledger.AddLookupAsync(LookupKind.SessionElement, "Tantra")).AssertOk();
        for (var i = 0; i < 120; i++)
        {
            var date = D(2026, 1, 1).AddDays(rnd.Next(0, 300));
            var price = 400m + rnd.Next(0, 12) * 50m;
            var elements = i % 3 == 0 ? new[] { w.Massage, tantra.Id } : new[] { w.Massage };
            var payments = i % 5 == 0 ? Array.Empty<PaymentDraft>() : [w.Pay(price - (i % 7 == 0 ? 100m : 0m), date, i % 2 == 0 ? w.MobilePay : w.Cash)];
            (await w.Ledger.SaveSessionAsync(new SessionDraft
            {
                Date = date, ElementIds = elements, DurationMinutes = 120 + (i % 3) * 30, Price = price,
                ClientNickname = i % 4 == 0 ? $"Kunde {i:000}" : null, Payments = payments,
            })).AssertOk();
        }

        for (var i = 0; i < 25; i++)
        {
            byte[]? receipt = i % 3 == 0 ? TestFiles.Pdf(3000 + i) : i % 3 == 1 ? TestFiles.Jpeg(5000 + i, (byte)i) : null;
            (await w.Ledger.SaveExpenseAsync(new ExpenseDraft
            {
                Date = D(2026, 1, 1).AddDays(i * 11), Amount = 10m + i * 3.5m, CategoryId = w.Data.ExpenseCategories[i % 6].Id,
                Description = i % 2 == 0 ? $"Udgift nr. {i} – æøå" : null,
                Attachment = receipt is null ? AttachmentChange.Keep : AttachmentChange.Replace($"kvittering-{i}", receipt),
            })).AssertOk();
        }

        return w;
    }

    private static async Task<BackupFile> BackupAsync(TestWorld w) =>
        await BackupWriter.BuildAsync(w.Data, w.Ledger.GetAttachmentAsync, "1.0.0", Now, D(2026, 10, 28));

    [Fact]
    public async Task Backup_has_the_documented_structure()
    {
        var w = await RealisticAsync();
        var backup = await BackupAsync(w);

        Assert.Equal("Sofie-Backup-2026-10-28.zip", backup.FileName);
        using var zip = new ZipArchive(new MemoryStream(backup.Bytes));
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("Sofies-Regnskab.xlsx", names);
        Assert.Contains("Backup-info.json", names);
        Assert.Contains(BackupPaths.DataFile, names);
        Assert.Equal(w.Data.Expenses.Count(e => e.Attachment is not null), names.Count(n => n.StartsWith("Kvitteringer/")));
        Assert.Contains(names, n => n.StartsWith("Kvitteringer/2026-") && n.EndsWith(".pdf"));
    }

    [Fact]
    public async Task Complete_round_trip_onto_an_empty_database_preserves_everything()
    {
        var source = await RealisticAsync();
        var backup = await BackupAsync(source);

        var read = BackupReader.Read(backup.Bytes);
        Assert.True(read.Ok, string.Join("\n", read.Errors));

        var target = await TestWorld.CreateAsync(routeKm: null);
        await target.Ledger.ReplaceAllAsync(read.Data!, read.Attachments, read.Info!.CreatedAt);
        await target.RestartAsync();

        Assert.Equal(DataSerializer.Fingerprint(source.Data), DataSerializer.Fingerprint(target.Data));
        foreach (var e in source.Data.Expenses.Where(e => e.Attachment is not null))
        {
            var original = await source.Ledger.GetAttachmentAsync(e.Attachment!.Id);
            var restored = await target.Ledger.GetAttachmentAsync(e.Attachment.Id);
            Assert.Equal(original!.Data, restored!.Data);
            Assert.Equal(original.ContentType, restored.ContentType);
        }

        Assert.Equal(source.Data.Expenses.Count(e => e.Attachment is not null), target.Store.Attachments.Count);
    }

    [Fact]
    public async Task Restore_over_existing_data_replaces_and_never_merges()
    {
        var source = await RealisticAsync();
        var backup = await BackupAsync(source);

        var target = await TestWorld.CreateAsync();
        await target.AddSessionAsync(D(2026, 5, 5), 123m, "Skal forsvinde");
        (await target.Ledger.SaveExpenseAsync(new ExpenseDraft
        {
            Date = D(2026, 5, 5), Amount = 1m, CategoryId = target.Category("Andet"), Attachment = AttachmentChange.Replace("x.jpg", TestFiles.Jpeg(100, 9)),
        })).AssertOk();

        var read = BackupReader.Read(backup.Bytes);
        await target.Ledger.ReplaceAllAsync(read.Data!, read.Attachments, read.Info!.CreatedAt);
        await target.RestartAsync();

        Assert.DoesNotContain(target.Data.Sessions, s => s.ClientNickname == "Skal forsvinde");
        Assert.Equal(source.Data.Sessions.Count, target.Data.Sessions.Count);
        Assert.Equal(DataSerializer.Fingerprint(source.Data), DataSerializer.Fingerprint(target.Data));
        Assert.Equal(source.Data.Expenses.Count(e => e.Attachment is not null), target.Store.Attachments.Count); // the old receipt is gone
    }

    [Fact]
    public async Task Repeated_restore_does_not_introduce_duplicates()
    {
        var source = await RealisticAsync();
        var read = BackupReader.Read((await BackupAsync(source)).Bytes);
        var target = await TestWorld.CreateAsync();

        for (var i = 0; i < 3; i++)
        {
            await target.Ledger.ReplaceAllAsync(read.Data!, read.Attachments, read.Info!.CreatedAt);
        }

        await target.RestartAsync();
        Assert.Equal(source.Data.Sessions.Count, target.Data.Sessions.Count);
        Assert.Equal(source.Data.Payments.Count, target.Data.Payments.Count);
        Assert.Equal(source.Data.Trips.Count, target.Data.Trips.Count);
    }

    [Fact]
    public async Task Failed_restore_leaves_current_data_intact()
    {
        var source = await RealisticAsync();
        var read = BackupReader.Read((await BackupAsync(source)).Bytes);

        var target = await TestWorld.CreateAsync();
        var mine = await target.AddSessionAsync(D(2026, 5, 5), 123m, "Min egen");
        var before = DataSerializer.Fingerprint(target.Data);

        target.Store.FailNextReplace = true;
        await Assert.ThrowsAsync<StorageException>(() => target.Ledger.ReplaceAllAsync(read.Data!, read.Attachments, read.Info!.CreatedAt));
        Assert.Equal(before, DataSerializer.Fingerprint(target.Data));

        target.Store.FailAttachmentPutAfter = 2;
        await Assert.ThrowsAsync<StorageException>(() => target.Ledger.ReplaceAllAsync(read.Data!, read.Attachments, read.Info!.CreatedAt));

        await target.RestartAsync(); // also cleans up staged receipts nobody refers to
        Assert.Equal(before, DataSerializer.Fingerprint(target.Data));
        Assert.Equal(mine.Id, target.Data.Sessions.Single().Id);
        Assert.Empty(target.Store.Attachments);
    }

    [Fact]
    public async Task Invalid_data_is_refused_before_anything_is_written()
    {
        var target = await TestWorld.CreateAsync();
        await target.AddSessionAsync(D(2026, 5, 5));
        var before = DataSerializer.Fingerprint(target.Data);
        var broken = target.Data with
        {
            Payments = [new Payment { Id = Guid.NewGuid(), SessionId = Guid.NewGuid(), Date = D(2026, 1, 1), Amount = 10m, MethodId = target.MobilePay, CreatedAt = Now, ModifiedAt = Now }],
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => target.Ledger.ReplaceAllAsync(broken, [], Now));
        Assert.Equal(before, DataSerializer.Fingerprint(target.Data));
    }

    [Fact]
    public async Task Missing_receipt_file_fails_validation()
    {
        var w = await RealisticAsync();
        var bytes = await BackupAsync(w);
        var withoutReceipt = RewriteZip(bytes.Bytes, (name, content) => name.StartsWith("Kvitteringer/") && name.EndsWith(".pdf") ? null : content);

        var read = BackupReader.Read(withoutReceipt);

        Assert.False(read.Ok);
        Assert.Contains(read.Errors, e => e.Contains("mangler"));
    }

    [Fact]
    public async Task Corrupted_receipt_or_data_fails_validation()
    {
        var w = await RealisticAsync();
        var bytes = (await BackupAsync(w)).Bytes;

        var badReceipt = RewriteZip(bytes, (name, content) => name.StartsWith("Kvitteringer/") ? content.Reverse().ToArray() : content);
        Assert.Contains(BackupReader.Read(badReceipt).Errors, e => e.Contains("beskadiget"));

        var badData = RewriteZip(bytes, (name, content) => name == BackupPaths.DataFile ? Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(content).Replace("Massage", "Masage")) : content);
        Assert.Contains(BackupReader.Read(badData).Errors, e => e.Contains("kontrolsum"));
    }

    [Fact]
    public async Task Invalid_relationships_fail_validation_even_with_matching_checksums()
    {
        var w = await RealisticAsync();
        var data = w.Data with { Payments = w.Data.Payments.Select((p, i) => i == 0 ? p with { SessionId = Guid.NewGuid() } : p).ToList() };
        var backup = await BackupWriter.BuildAsync(data, w.Ledger.GetAttachmentAsync, "1.0.0", Now, D(2026, 10, 28));

        var read = BackupReader.Read(backup.Bytes);

        Assert.False(read.Ok);
        Assert.Contains(read.Errors, e => e.Contains("henviser til en session"));
    }

    [Fact]
    public async Task Future_backup_versions_are_refused_safely()
    {
        var w = await TestWorld.CreateAsync();
        var bytes = (await BackupAsync(w)).Bytes;

        var future = RewriteZip(bytes, (name, content) =>
        {
            if (name != BackupPaths.InfoFile)
            {
                return content;
            }

            var json = JsonNode.Parse(content)!;
            json["formatVersion"] = 99;
            return Encoding.UTF8.GetBytes(json.ToJsonString());
        });

        var read = BackupReader.Read(future);
        Assert.False(read.Ok);
        Assert.Contains(read.Errors, e => e.Contains("nyere version"));
    }

    [Fact]
    public void Non_backup_files_are_refused()
    {
        Assert.False(BackupReader.Read(Encoding.UTF8.GetBytes("hello")).Ok);
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, true))
        {
            zip.CreateEntry("random.txt");
        }

        Assert.Contains("Backup-info.json", BackupReader.Read(ms.ToArray()).Errors[0]);
    }

    [Fact]
    public async Task Workbook_inside_backup_is_complete_and_restorable_too()
    {
        var w = await RealisticAsync();
        var backup = await BackupAsync(w);
        using var zip = new ZipArchive(new MemoryStream(backup.Bytes));
        using var ms = new MemoryStream();
        zip.GetEntry(BackupPaths.WorkbookFile)!.Open().CopyTo(ms);

        var imported = WorkbookImporter.Import(ms.ToArray());

        Assert.True(imported.Ok, string.Join("\n", imported.Errors));
        Assert.Equal(w.Data.Sessions.Count, imported.Data!.Sessions.Count);
    }

    [Fact]
    public async Task Excel_only_restore_replaces_data_without_receipts()
    {
        var source = await RealisticAsync();
        var imported = WorkbookImporter.Import(PrivateWorkbook.Build(source.Data, Now));
        var target = await TestWorld.CreateAsync();
        await target.AddSessionAsync(D(2026, 5, 5), 123m, "Skal forsvinde");

        await target.Ledger.ReplaceAllAsync(imported.Data!, [], imported.ExportedAt!.Value);
        await target.RestartAsync();

        Assert.Equal(source.Data.Sessions.Count, target.Data.Sessions.Count);
        Assert.All(target.Data.Expenses, e => Assert.Null(e.Attachment));
        Assert.Empty(target.Store.Attachments);
    }

    [Fact]
    public async Task A_receipt_lost_from_storage_is_left_out_with_a_warning_and_the_backup_stays_valid()
    {
        var w = await RealisticAsync();
        var lost = w.Data.Expenses.First(e => e.Attachment is not null).Attachment!.Id;
        await w.Store.DeleteAttachmentsAsync([lost]);

        var backup = await BackupAsync(w);
        Assert.Single(backup.Warnings);
        Assert.True(BackupReader.Read(backup.Bytes).Ok);
    }

    private static byte[] RewriteZip(byte[] source, Func<string, byte[], byte[]?> transform)
    {
        using var input = new ZipArchive(new MemoryStream(source));
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
        {
            foreach (var entry in input.Entries)
            {
                using var ms = new MemoryStream();
                entry.Open().CopyTo(ms);
                var content = transform(entry.FullName, ms.ToArray());
                if (content is null)
                {
                    continue;
                }

                using var stream = zip.CreateEntry(entry.FullName).Open();
                stream.Write(content);
            }
        }

        return output.ToArray();
    }
}
