using StaffManagement.Models;

namespace StaffManagement.Services;

public sealed class ReminderSettings
{
    /// <summary>Start reminding this many days before a post ends.</summary>
    public int LeadDays { get; set; } = 100;
    /// <summary>Remind again at every multiple of this many days (100, 95, 90 … 5, 0, then every 5 days overdue).</summary>
    public int IntervalDays { get; set; } = 5;
    /// <summary>A post also ends when the holder retires (the day before the retirement month).</summary>
    public bool IncludeRetirement { get; set; } = true;
}

public enum ReminderReason { TermEnd, Retirement }

/// <summary>A post of an active row that ends within the reminder window.</summary>
public sealed record ReminderItem(StaffRecord Record, DateTime EndDate, ReminderReason Reason, int DaysLeft, int Milestone, bool Estimated)
{
    /// <summary>Changes when the end date changes (e.g. after re-appointment), so old state no longer applies.</summary>
    public string Key => $"{Record.Id}|{EndDate:yyyyMMdd}";

    public string ReasonText => Reason == ReminderReason.Retirement ? "Nghỉ hưu" : Estimated ? "Hết nhiệm kỳ (ước tính)" : "Hết nhiệm kỳ";

    public string CountdownText => DaysLeft switch
    {
        > 0 => $"Còn {DaysLeft} ngày",
        0 => "Hết hạn hôm nay",
        _ => $"Quá hạn {-DaysLeft} ngày",
    };

    /// <summary>0 = overdue / today, 1 = ≤ 30 days, 2 = ≤ 60 days, 3 = later.</summary>
    public int Urgency => DaysLeft switch { <= 0 => 0, <= 30 => 1, <= 60 => 2, _ => 3 };
}

/// <summary>What has already been notified / dismissed for one reminder.</summary>
public sealed class ReminderState
{
    public string Key { get; set; } = "";
    public int? LastMilestone { get; set; }
    public bool Dismissed { get; set; }
}

/// <summary>
/// Count-down reminders for posts that are about to end. A reminder becomes "due" (worth a notification) each time
/// the count-down crosses a new mark: with the defaults, at 100, 95, 90 … 5 and 0 days left, then every 5 days while
/// overdue. Missed days are caught up: opening the app at 97 days left notifies for the 100-day mark once.
/// </summary>
public static class ReminderService
{
    public static List<ReminderItem> Compute(IEnumerable<StaffRecord> rows, ReminderSettings settings, DateTime today)
    {
        int interval = Math.Max(1, settings.IntervalDays);
        var items = new List<ReminderItem>();
        foreach (var r in rows.Where(r => !r.IsFormer))
        {
            (DateTime End, ReminderReason Reason, bool Estimated)? end = null;
            if (r.TermEndDate is { } t) end = (t, ReminderReason.TermEnd, r.IsTermEndEstimated);
            if (settings.IncludeRetirement && r.RetirementDate is { } ret && (end is null || ret.AddDays(-1) < end.Value.End))
                end = (ret.AddDays(-1), ReminderReason.Retirement, false);
            if (end is not { } e) continue;

            int daysLeft = (e.End.Date - today.Date).Days;
            if (daysLeft > settings.LeadDays) continue;
            items.Add(new ReminderItem(r, e.End, e.Reason, daysLeft, Milestone(daysLeft, interval), e.Estimated));
        }
        return items.OrderBy(i => i.DaysLeft).ThenBy(i => i.Record.FullName, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>The count-down mark reached: the smallest multiple of <paramref name="interval"/> that is ≥ days left.</summary>
    public static int Milestone(int daysLeft, int interval)
    {
        int q = daysLeft / interval;
        if (daysLeft % interval > 0) q++;  // integer division truncates toward zero; ceiling for positive remainders
        return q * interval;
    }

    /// <summary>Reminders that reached a mark not notified yet (and are not dismissed).</summary>
    public static List<ReminderItem> Due(IEnumerable<ReminderItem> items, IReadOnlyDictionary<string, ReminderState> states) =>
        items.Where(i => !states.TryGetValue(i.Key, out var s) || (!s.Dismissed && (s.LastMilestone is null || i.Milestone < s.LastMilestone)))
             .ToList();

    /// <summary>Records that the current mark of each item was notified; returns the states to persist.</summary>
    public static List<ReminderState> MarkNotified(IEnumerable<ReminderItem> due, IDictionary<string, ReminderState> states)
    {
        var changed = new List<ReminderState>();
        foreach (var i in due)
        {
            if (!states.TryGetValue(i.Key, out var s)) states[i.Key] = s = new ReminderState { Key = i.Key };
            s.LastMilestone = i.Milestone;
            changed.Add(s);
        }
        return changed;
    }

    /// <summary>Date of the next count-down mark after the current one (null once the post has ended).</summary>
    public static DateTime? NextReminderDate(ReminderItem item, int interval)
    {
        var next = item.EndDate.AddDays(-(item.Milestone - Math.Max(1, interval)));
        return next.Date > DateTime.Today ? next : null;
    }

    public static string Summary(IReadOnlyList<ReminderItem> due, int maxNames = 4)
    {
        var names = due.Take(maxNames).Select(i => $"{i.Record.FullName} ({i.Record.Position}) – {i.CountdownText.ToLowerInvariant()}");
        var more = due.Count > maxNames ? $"\n… và {due.Count - maxNames} chức vụ khác" : "";
        return string.Join("\n", names) + more;
    }
}
