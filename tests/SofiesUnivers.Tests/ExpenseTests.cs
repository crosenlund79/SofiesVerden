using SofiesUnivers.Core.Domain;
using SofiesUnivers.Core.Model;
using SofiesUnivers.Tests.Infrastructure;
using static SofiesUnivers.Tests.Infrastructure.Dates;

namespace SofiesUnivers.Tests;

public class ExpenseTests
{
    private static ExpenseDraft Draft(TestWorld w, decimal amount = 249.95m, AttachmentChange? attachment = null) => new()
    {
        Date = D(2026, 10, 12),
        Amount = amount,
        CategoryId = w.Category("Materialer"),
        Description = "Massageolie",
        Attachment = attachment ?? AttachmentChange.Keep,
    };

    [Fact]
    public async Task Creates_edits_and_deletes_an_expense()
    {
        var w = await TestWorld.CreateAsync();
        var e = (await w.Ledger.SaveExpenseAsync(Draft(w))).AssertOk();
        Assert.Null(e.Attachment);

        var edited = (await w.Ledger.SaveExpenseAsync(Draft(w, 300m) with { Id = e.Id, CategoryId = w.Category("Udstyr") })).AssertOk();
        Assert.Equal(300m, edited.Amount);
        Assert.Equal(w.Category("Udstyr"), edited.CategoryId);

        await w.RestartAsync();
        Assert.Equal(300m, w.Data.Expenses.Single().Amount);

        await w.Ledger.DeleteExpenseAsync(e.Id);
        Assert.Empty(w.Data.Expenses);
    }

    [Fact]
    public async Task Category_can_be_created_from_the_form()
    {
        var w = await TestWorld.CreateAsync();
        var c = (await w.Ledger.AddLookupAsync(LookupKind.ExpenseCategory, "Kurser")).AssertOk();
        (await w.Ledger.SaveExpenseAsync(Draft(w) with { CategoryId = c.Id })).AssertOk();
        Assert.Equal("Kurser", w.Data.LookupName(LookupKind.ExpenseCategory, w.Data.Expenses.Single().CategoryId));
    }

    [Fact]
    public async Task Attach_replace_and_remove_a_receipt()
    {
        var w = await TestWorld.CreateAsync();
        var jpeg = TestFiles.Jpeg();
        var e = (await w.Ledger.SaveExpenseAsync(Draft(w, attachment: AttachmentChange.Replace("IMG_0042.HEIC.jpg", jpeg)))).AssertOk();

        Assert.NotNull(e.Attachment);
        Assert.Equal("image/jpeg", e.Attachment!.ContentType);
        Assert.Equal(jpeg.Length, e.Attachment.SizeBytes);
        Assert.Equal(jpeg, (await w.Ledger.GetAttachmentAsync(e.Attachment.Id))!.Data);

        var pdf = TestFiles.Pdf();
        var replaced = (await w.Ledger.SaveExpenseAsync(Draft(w) with { Id = e.Id, Attachment = AttachmentChange.Replace("faktura.pdf", pdf) })).AssertOk();
        Assert.Equal("application/pdf", replaced.Attachment!.ContentType);
        Assert.Equal("faktura.pdf", replaced.Attachment.FileName);
        Assert.Single(w.Store.Attachments); // the old receipt is gone
        Assert.Null(await w.Ledger.GetAttachmentAsync(e.Attachment.Id));

        var removed = (await w.Ledger.SaveExpenseAsync(Draft(w) with { Id = e.Id, Attachment = AttachmentChange.Remove })).AssertOk();
        Assert.Null(removed.Attachment);
        Assert.Empty(w.Store.Attachments);
    }

    [Fact]
    public async Task Rejects_unsupported_and_oversized_files_safely()
    {
        var w = await TestWorld.CreateAsync();
        var text = "hello world"u8.ToArray();
        (await w.Ledger.SaveExpenseAsync(Draft(w, attachment: AttachmentChange.Replace("note.txt", text)))).AssertError("attachment");

        var huge = TestFiles.Jpeg((int)AttachmentRules.MaxStoredBytes + 1);
        (await w.Ledger.SaveExpenseAsync(Draft(w, attachment: AttachmentChange.Replace("big.jpg", huge)))).AssertError("attachment");

        (await w.Ledger.SaveExpenseAsync(Draft(w, attachment: AttachmentChange.Replace("empty.jpg", [])))).AssertError("attachment");

        Assert.Empty(w.Data.Expenses);
        Assert.Empty(w.Store.Attachments);
    }

    [Fact]
    public async Task Detects_file_types_from_content()
    {
        Assert.Equal("image/heic", AttachmentRules.Detect(TestFiles.Heic())!.ContentType);
        Assert.Equal("application/pdf", AttachmentRules.Detect(TestFiles.Pdf())!.ContentType);
        Assert.Null(AttachmentRules.Detect("GIF"u8));
        Assert.Equal("kvittering.jpg", AttachmentRules.SafeFileName("../../", AttachmentRules.Detect(TestFiles.Jpeg())!));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Deleting_an_expense_with_a_receipt_can_be_undone_including_the_file()
    {
        var w = await TestWorld.CreateAsync();
        var jpeg = TestFiles.Jpeg();
        var e = (await w.Ledger.SaveExpenseAsync(Draft(w, attachment: AttachmentChange.Replace("kvit.jpg", jpeg)))).AssertOk();

        var deleted = await w.Ledger.DeleteExpenseAsync(e.Id);
        Assert.Empty(w.Store.Attachments);

        await w.Ledger.UndoDeleteAsync(deleted!);
        Assert.Equal(jpeg, (await w.Ledger.GetAttachmentAsync(e.Attachment!.Id))!.Data);
        Assert.Single(w.Data.Expenses);
    }

    [Fact]
    public async Task Expense_validation()
    {
        var w = await TestWorld.CreateAsync();
        (await w.Ledger.SaveExpenseAsync(Draft(w, 0m))).AssertError("amount");
        (await w.Ledger.SaveExpenseAsync(Draft(w) with { CategoryId = Guid.NewGuid() })).AssertError("category");
        (await w.Ledger.SaveExpenseAsync(Draft(w) with { Description = new string('x', 501) })).AssertError("description");
    }
}
