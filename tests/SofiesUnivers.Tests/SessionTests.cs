using SofiesUnivers.Core.Domain;
using SofiesUnivers.Core.Model;
using SofiesUnivers.Tests.Infrastructure;
using static SofiesUnivers.Tests.Infrastructure.Dates;

namespace SofiesUnivers.Tests;

public class SessionTests
{
    [Fact]
    public async Task Creates_a_session_and_it_survives_a_restart()
    {
        var w = await TestWorld.CreateAsync();
        var session = await w.AddSessionAsync(D(2026, 10, 28), 800m, "Kunde 042");

        await w.RestartAsync();

        var loaded = Assert.Single(w.Data.Sessions);
        Assert.Equal(session.Id, loaded.Id);
        Assert.Equal(800m, loaded.Price);
        Assert.Equal(120, loaded.DurationMinutes);
        Assert.Equal("Kunde 042", loaded.ClientNickname);
        Assert.Equal([w.Massage], loaded.ElementIds);
    }

    [Fact]
    public async Task Edits_a_session()
    {
        var w = await TestWorld.CreateAsync();
        var s = await w.AddSessionAsync(D(2026, 10, 28));
        w.Clock.Advance(TimeSpan.FromHours(1));

        var result = await w.Ledger.SaveSessionAsync(new SessionDraft
        {
            Id = s.Id,
            Date = s.Date,
            ElementIds = [w.Massage, w.Element("Sexologisk samtale")],
            DurationMinutes = 150,
            Price = 950m,
            ClientNickname = "  Undertøjsfabrikanten ",
        });

        var edited = result.AssertOk();
        Assert.Equal(950m, edited.Price);
        Assert.Equal(150, edited.DurationMinutes);
        Assert.Equal("Undertøjsfabrikanten", edited.ClientNickname);
        Assert.Equal(s.CreatedAt, edited.CreatedAt);
        Assert.True(edited.ModifiedAt > s.ModifiedAt);
        Assert.Single(w.Data.Sessions);
    }

    [Fact]
    public async Task Backdated_session_keeps_its_own_date()
    {
        var w = await TestWorld.CreateAsync();
        var s = await w.AddSessionAsync(D(2025, 12, 30));
        Assert.Equal(D(2025, 12, 30), s.Date);
        Assert.Equal(2026, w.Clock.Today.Year);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Accepts_one_to_three_elements(int count)
    {
        var w = await TestWorld.CreateAsync();
        var third = (await w.Ledger.AddLookupAsync(LookupKind.SessionElement, "Tantra")).AssertOk();
        var all = new[] { w.Massage, w.Element("Sexologisk samtale"), third.Id };

        var result = await w.Ledger.SaveSessionAsync(w.Draft(D(2026, 10, 1), 500m) with { ElementIds = all.Take(count).ToList() });

        Assert.Equal(count, result.AssertOk().ElementIds.Count);
    }

    [Fact]
    public async Task Rejects_zero_or_more_than_three_elements()
    {
        var w = await TestWorld.CreateAsync();
        var a = (await w.Ledger.AddLookupAsync(LookupKind.SessionElement, "A")).AssertOk();
        var b = (await w.Ledger.AddLookupAsync(LookupKind.SessionElement, "B")).AssertOk();

        (await w.Ledger.SaveSessionAsync(w.Draft(D(2026, 10, 1), 500m) with { ElementIds = [] })).AssertError("elements");
        (await w.Ledger.SaveSessionAsync(w.Draft(D(2026, 10, 1), 500m) with
        {
            ElementIds = [w.Massage, w.Element("Sexologisk samtale"), a.Id, b.Id],
        })).AssertError("elements");
        Assert.Empty(w.Data.Sessions);
    }

    [Fact]
    public async Task Rejects_invalid_duration_and_price()
    {
        var w = await TestWorld.CreateAsync();
        (await w.Ledger.SaveSessionAsync(w.Draft(D(2026, 10, 1), 500m) with { DurationMinutes = 0 })).AssertError("duration");
        (await w.Ledger.SaveSessionAsync(w.Draft(D(2026, 10, 1), -1m))).AssertError("price");
        (await w.Ledger.SaveSessionAsync(w.Draft(D(2026, 10, 1), 10.555m))).AssertError("price");
    }

    [Fact]
    public async Task Creates_new_element_inline_and_avoids_duplicates()
    {
        var w = await TestWorld.CreateAsync();
        var before = w.Data.ElementTypes.Count;

        var created = (await w.Ledger.AddLookupAsync(LookupKind.SessionElement, "  Tantra   massage ")).AssertOk();
        var again = (await w.Ledger.AddLookupAsync(LookupKind.SessionElement, "TANTRA MASSAGE")).AssertOk();
        var existing = (await w.Ledger.AddLookupAsync(LookupKind.SessionElement, "massage")).AssertOk();

        Assert.Equal("Tantra massage", created.Name);
        Assert.Equal(created.Id, again.Id);
        Assert.Equal(w.Massage, existing.Id);
        Assert.Equal(before + 1, w.Data.ElementTypes.Count);
    }

    [Fact]
    public async Task Renaming_an_element_is_shown_on_old_sessions_and_rejects_duplicates()
    {
        var w = await TestWorld.CreateAsync();
        var s = await w.AddSessionAsync(D(2026, 1, 5));
        var massage = w.Massage;

        (await w.Ledger.RenameLookupAsync(LookupKind.SessionElement, massage, "Massage (90/10)")).AssertOk();
        (await w.Ledger.RenameLookupAsync(LookupKind.SessionElement, massage, "sexologisk SAMTALE")).AssertError("name");

        Assert.Equal("Massage (90/10)", w.Data.LookupName(LookupKind.SessionElement, s.ElementIds[0]));
    }

    [Fact]
    public async Task Archived_element_disappears_from_picker_but_old_sessions_stay_valid()
    {
        var w = await TestWorld.CreateAsync();
        var s = await w.AddSessionAsync(D(2026, 1, 5));

        await w.Ledger.SetLookupArchivedAsync(LookupKind.SessionElement, w.Massage, true);

        Assert.DoesNotContain(w.Ledger.ActiveLookups(LookupKind.SessionElement), l => l.Id == w.Massage);
        (await w.Ledger.SaveSessionAsync(w.Draft(s.Date, 900m) with { Id = s.Id })).AssertOk();
        Assert.Empty(DataValidator.Validate(w.Data, null));

        // Adding the same name again brings it back instead of creating a duplicate.
        var back = (await w.Ledger.AddLookupAsync(LookupKind.SessionElement, "massage")).AssertOk();
        Assert.Equal(w.Massage, back.Id);
        Assert.False(back.IsArchived);
    }

    [Fact]
    public async Task Deleting_a_session_removes_its_payments_and_can_be_undone()
    {
        var w = await TestWorld.CreateAsync();
        var s = await w.AddSessionAsync(D(2026, 10, 28), 800m, null, w.Pay(300m), w.Pay(200m, method: w.Cash));

        var preview = w.Ledger.PreviewSessionDeletion(s.Id)!;
        Assert.Equal(2, preview.Payments.Count);

        var deleted = await w.Ledger.DeleteSessionAsync(s.Id);
        Assert.Empty(w.Data.Sessions);
        Assert.Empty(w.Data.Payments);

        await w.Ledger.UndoDeleteAsync(deleted!);
        await w.RestartAsync();
        Assert.Single(w.Data.Sessions);
        Assert.Equal(500m, w.Data.PaidFor(s.Id));
        Assert.Single(w.Data.Trips);
    }

    [Fact]
    public async Task Failed_write_changes_nothing()
    {
        var w = await TestWorld.CreateAsync();
        w.Store.FailNextCommit = true;

        await Assert.ThrowsAsync<SofiesUnivers.Core.Storage.StorageException>(() => w.AddSessionAsync(D(2026, 10, 28)));

        Assert.Empty(w.Data.Sessions);
        Assert.Empty(w.Data.Trips);
        await w.RestartAsync();
        Assert.Empty(w.Data.Sessions);
    }

    [Fact]
    public async Task Saving_an_unchanged_session_writes_nothing()
    {
        var w = await TestWorld.CreateAsync();
        var s = await w.AddSessionAsync(D(2026, 10, 28));
        var commits = w.Store.CommitCount;

        (await w.Ledger.SaveSessionAsync(w.Draft(s.Date, s.Price) with { Id = s.Id })).AssertOk();

        Assert.Equal(commits, w.Store.CommitCount);
    }
}
