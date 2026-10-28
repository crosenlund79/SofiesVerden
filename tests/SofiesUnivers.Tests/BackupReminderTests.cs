using SofiesUnivers.Core.Domain;
using SofiesUnivers.Core.Model;

namespace SofiesUnivers.Tests;

public class BackupReminderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Not_shown_without_data() =>
        Assert.False(BackupReminder.ShouldShow(new BackupStatus { LastDataChangeAt = Now.AddDays(-30) }, hasBusinessRecords: false, Now));

    [Fact]
    public void Shown_a_day_after_first_data_when_never_backed_up()
    {
        Assert.False(BackupReminder.ShouldShow(new BackupStatus { TrackingStartedAt = Now.AddHours(-2), LastDataChangeAt = Now.AddHours(-1) }, true, Now));
        Assert.True(BackupReminder.ShouldShow(new BackupStatus { TrackingStartedAt = Now.AddDays(-2), LastDataChangeAt = Now.AddHours(-1) }, true, Now));
    }

    [Fact]
    public void Shown_weekly_only_when_something_changed_since_the_confirmed_backup()
    {
        var confirmed = Now.AddDays(-8);
        Assert.True(BackupReminder.ShouldShow(new BackupStatus { LastConfirmedAt = confirmed, LastDataChangeAt = Now.AddDays(-1) }, true, Now));
        Assert.False(BackupReminder.ShouldShow(new BackupStatus { LastConfirmedAt = confirmed, LastDataChangeAt = confirmed.AddMinutes(-1) }, true, Now));
        Assert.False(BackupReminder.ShouldShow(new BackupStatus { LastConfirmedAt = Now.AddDays(-3), LastDataChangeAt = Now.AddDays(-1) }, true, Now));
    }

    [Fact]
    public void Generated_or_shared_backups_do_not_count_as_confirmed()
    {
        var status = new BackupStatus { TrackingStartedAt = Now.AddDays(-10), LastDataChangeAt = Now.AddDays(-1), LastGeneratedAt = Now.AddHours(-1), LastSharedAt = Now.AddHours(-1) };
        Assert.True(BackupReminder.ShouldShow(status, true, Now));
        Assert.True(BackupReminder.HasUnbackedChanges(status));
    }

    [Fact]
    public void Snooze_hides_the_card()
    {
        var status = new BackupStatus { TrackingStartedAt = Now.AddDays(-10), LastDataChangeAt = Now.AddDays(-1), ReminderSnoozedUntil = Now.AddDays(1) };
        Assert.False(BackupReminder.ShouldShow(status, true, Now));
    }

    [Fact]
    public async Task Data_changes_are_tracked_but_backup_bookkeeping_is_not()
    {
        var w = await Infrastructure.TestWorld.CreateAsync();
        var before = w.Ledger.BackupStatus.LastDataChangeAt;
        w.Clock.Advance(TimeSpan.FromMinutes(5));
        await w.AddSessionAsync(new DateOnly(2026, 10, 28));
        var changed = w.Ledger.BackupStatus.LastDataChangeAt;
        Assert.NotEqual(before, changed);

        w.Clock.Advance(TimeSpan.FromMinutes(5));
        await w.Ledger.UpdateBackupStatusAsync(s => s with { LastConfirmedAt = w.Clock.UtcNow });
        Assert.Equal(changed, w.Ledger.BackupStatus.LastDataChangeAt);
        Assert.False(BackupReminder.HasUnbackedChanges(w.Ledger.BackupStatus));
    }
}
