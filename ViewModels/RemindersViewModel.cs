using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StaffManagement.Models;
using StaffManagement.Services;

namespace StaffManagement.ViewModels;

public sealed record ReminderRow(ReminderItem Item, bool Dismissed, DateTime? NextReminder)
{
    public StaffRecord Record => Item.Record;
    public string NextText => Dismissed ? "Đã ẩn" : NextReminder is { } d ? d.ToString("dd/MM/yyyy") : "–";
}

/// <summary>The "Nhắc hạn chức vụ" window: count-down of posts ending soon.</summary>
public partial class RemindersViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<ReminderItem>> _items;
    private readonly IDictionary<string, ReminderState> _states;
    private readonly Action<IEnumerable<ReminderState>> _save;
    private readonly Action<StaffRecord> _edit;
    private readonly int _interval;

    public event Action? CloseRequested;

    public string Description { get; }
    public ObservableCollection<ReminderRow> Rows { get; } = [];

    [ObservableProperty] private bool _showDismissed;
    [ObservableProperty] private ReminderRow? _selected;
    [ObservableProperty] private string _countText = "";

    public RemindersViewModel(Func<IReadOnlyList<ReminderItem>> items, IDictionary<string, ReminderState> states,
        Action<IEnumerable<ReminderState>> save, Action<StaffRecord> edit, ReminderSettings settings)
    {
        _items = items;
        _states = states;
        _save = save;
        _edit = edit;
        _interval = settings.IntervalDays;
        Description = $"Các chức vụ đang giữ sẽ kết thúc trong {settings.LeadDays} ngày tới"
                      + (settings.IncludeRetirement ? " (hết nhiệm kỳ hoặc đến thời điểm nghỉ hưu)" : " (hết nhiệm kỳ)")
                      + $". Phần mềm nhắc lại mỗi {settings.IntervalDays} ngày theo số ngày còn lại.";
        Load();
    }

    partial void OnShowDismissedChanged(bool value) => Load();

    private void Load()
    {
        var selectedKey = Selected?.Item.Key;
        Rows.Clear();
        int hidden = 0;
        foreach (var item in _items())
        {
            bool dismissed = _states.TryGetValue(item.Key, out var s) && s.Dismissed;
            if (dismissed) hidden++;
            if (dismissed && !ShowDismissed) continue;
            Rows.Add(new ReminderRow(item, dismissed, ReminderService.NextReminderDate(item, _interval)));
        }
        Selected = Rows.FirstOrDefault(r => r.Item.Key == selectedKey) ?? Rows.FirstOrDefault();
        CountText = $"{Rows.Count(r => !r.Dismissed)} chức vụ cần theo dõi" + (hidden > 0 ? $" · {hidden} đã ẩn" : "");
    }

    [RelayCommand]
    private void Edit(ReminderRow? row)
    {
        row ??= Selected;
        if (row is null) return;
        _edit(row.Record);
        Load(); // a re-appointment changes the end date → the reminder disappears or moves
    }

    /// <summary>"Đã xử lý": stop reminding for this post until its end date changes (e.g. re-appointed).</summary>
    [RelayCommand]
    private void ToggleDismiss(ReminderRow? row)
    {
        row ??= Selected;
        if (row is null) return;
        if (!_states.TryGetValue(row.Item.Key, out var s)) _states[row.Item.Key] = s = new ReminderState { Key = row.Item.Key };
        s.Dismissed = !s.Dismissed;
        s.LastMilestone ??= row.Item.Milestone;
        _save([s]);
        Load();
    }

    [RelayCommand]
    private void Close() => CloseRequested?.Invoke();
}
