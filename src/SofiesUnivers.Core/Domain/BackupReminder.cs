using SofiesUnivers.Core.Model;

namespace SofiesUnivers.Core.Domain;

/// <summary>Decides when the gentle backup reminder card is shown on the home screen.</summary>
public static class BackupReminder
{
    public static readonly TimeSpan Interval = TimeSpan.FromDays(7);

    /// <summary>Before the very first confirmed backup, remind a little sooner.</summary>
    public static readonly TimeSpan FirstReminderAfter = TimeSpan.FromDays(1);

    public static readonly TimeSpan Snooze = TimeSpan.FromDays(3);

    public static bool ShouldShow(BackupStatus status, bool hasBusinessRecords, DateTimeOffset now)
    {
        if (!hasBusinessRecords || status.LastDataChangeAt is not { } lastChange)
        {
            return false;
        }

        if (status.ReminderSnoozedUntil is { } snoozed && snoozed > now)
        {
            return false;
        }

        if (status.LastConfirmedAt is { } confirmed)
        {
            return lastChange > confirmed && now - confirmed >= Interval;
        }

        var start = status.TrackingStartedAt ?? lastChange;
        return now - start >= FirstReminderAfter;
    }

    /// <summary>True when there are changes that are not covered by the latest confirmed backup.</summary>
    public static bool HasUnbackedChanges(BackupStatus status) =>
        status.LastDataChangeAt is { } change && (status.LastConfirmedAt is not { } confirmed || change > confirmed);
}
