using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StaffManagement.Infrastructure;
using StaffManagement.Services;

namespace StaffManagement.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private const string DbFilter = "Cơ sở dữ liệu (*.db)|*.db";
    private readonly StaffRepository _repo;
    private readonly SettingsStore _settings;
    private readonly IDialogService _dialogs;

    public event Action? CloseRequested;

    public IReadOnlyList<LlmProviderPreset> Providers => LlmProviderPreset.All;
    public string DataFolder => _repo.DataFolder;
    public bool DataRestored { get; private set; }

    [ObservableProperty] private bool _llmEnabled;
    [ObservableProperty] private LlmProviderPreset? _selectedProvider;
    [ObservableProperty] private string _endpoint = "";
    [ObservableProperty] private string _model = "";
    [ObservableProperty] private string _apiKey = "";
    [ObservableProperty] private string? _testResult;
    [ObservableProperty] private bool _isTesting;
    [ObservableProperty] private string? _organization;
    [ObservableProperty] private string? _listTitle;

    // Reminders
    private readonly bool _dailyCheckWasEnabled;
    private readonly string _dailyTimeWas;
    [ObservableProperty] private string _reminderLeadDays;
    [ObservableProperty] private string _reminderIntervalDays;
    [ObservableProperty] private bool _includeRetirement;
    [ObservableProperty] private bool _dailyCheckEnabled;
    [ObservableProperty] private string _dailyCheckTime;
    [ObservableProperty] private string? _reminderError;

    public SettingsViewModel(StaffRepository repo, SettingsStore settings, IDialogService dialogs)
    {
        _repo = repo;
        _settings = settings;
        _dialogs = dialogs;
        var llm = settings.LoadLlm();
        _llmEnabled = llm.Enabled;
        _endpoint = llm.Endpoint;
        _model = llm.Model;
        _apiKey = llm.ApiKey;
        _selectedProvider = Providers.FirstOrDefault(p => p.Endpoint == llm.Endpoint);
        _organization = settings.Organization ?? ExcelService.DefaultOrganization;
        _listTitle = settings.Title ?? ExcelService.DefaultTitle;

        var reminders = settings.LoadReminders();
        _reminderLeadDays = reminders.LeadDays.ToString();
        _reminderIntervalDays = reminders.IntervalDays.ToString();
        _includeRetirement = reminders.IncludeRetirement;
        _dailyCheckTime = _dailyTimeWas = settings.DailyCheckTime;
        try { _dailyCheckEnabled = _dailyCheckWasEnabled = ReminderScheduler.IsEnabled(); }
        catch (Exception ex) { _reminderError = "Không đọc được Task Scheduler: " + ex.Message; }
    }

    partial void OnSelectedProviderChanged(LlmProviderPreset? value)
    {
        if (value is null) return;
        Endpoint = value.Endpoint;
        Model = value.Model;
        TestResult = value.Note;
    }

    private LlmSettings CurrentLlm() => new() { Enabled = true, Endpoint = Endpoint, Model = Model, ApiKey = ApiKey };

    [RelayCommand]
    private async Task TestAsync()
    {
        try
        {
            IsTesting = true;
            TestResult = "Đang kết nối…";
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await new LlmClient(CurrentLlm()).TestAsync();
            TestResult = $"✓ Kết nối thành công ({sw.ElapsedMilliseconds} ms).";
        }
        catch (Exception ex)
        {
            TestResult = "✗ " + ex.Message;
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private void Save()
    {
        if (!SaveReminders()) return;
        var llm = CurrentLlm();
        llm.Enabled = LlmEnabled;
        _settings.SaveLlm(llm);
        _settings.Organization = string.IsNullOrWhiteSpace(Organization) ? null : Organization.Trim();
        _settings.Title = string.IsNullOrWhiteSpace(ListTitle) ? null : ListTitle.Trim();
        CloseRequested?.Invoke();
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke();

    /// <summary>Validates and saves the reminder options; creates / removes the daily Task Scheduler job.</summary>
    private bool SaveReminders()
    {
        if (!int.TryParse(ReminderLeadDays, out var lead) || lead is < 1 or > 3650 ||
            !int.TryParse(ReminderIntervalDays, out var every) || every is < 1 or > 365)
        {
            ReminderError = "Số ngày nhắc phải là số nguyên dương (ví dụ 100 và 5).";
            return false;
        }
        if (!TimeOnly.TryParseExact(DailyCheckTime?.Trim(), ["H:mm", "HH:mm"], out var time))
        {
            ReminderError = "Giờ kiểm tra phải có dạng HH:mm, ví dụ 08:00.";
            return false;
        }
        _settings.SaveReminders(new ReminderSettings { LeadDays = lead, IntervalDays = every, IncludeRetirement = IncludeRetirement });
        var hhmm = time.ToString("HH:mm");
        _settings.DailyCheckTime = hhmm;

        string? error = null;
        if (DailyCheckEnabled && (!_dailyCheckWasEnabled || hhmm != _dailyTimeWas))
            error = ReminderScheduler.Enable(hhmm, _repo.IsDefaultFolder ? null : _repo.DataFolder);
        else if (!DailyCheckEnabled && _dailyCheckWasEnabled)
            error = ReminderScheduler.Disable();
        if (error is not null)
        {
            ReminderError = "Task Scheduler: " + error;
            return false;
        }
        return true;
    }

    [RelayCommand]
    private void OpenDataFolder() => _dialogs.OpenWithShell(_repo.DataFolder);

    [RelayCommand]
    private void Backup()
    {
        var path = _dialogs.SaveFile("Sao lưu dữ liệu", DbFilter, $"staff_backup_{DateTime.Now:yyyyMMdd_HHmm}.db");
        if (path is null) return;
        try
        {
            _repo.Backup(path);
            _dialogs.Info($"Đã sao lưu vào:\n{path}");
        }
        catch (Exception ex)
        {
            _dialogs.Error("Sao lưu thất bại: " + ex.Message);
        }
    }

    [RelayCommand]
    private void Restore()
    {
        var path = _dialogs.OpenFile("Chọn bản sao lưu để khôi phục", DbFilter);
        if (path is null) return;
        if (!IsSqliteFile(path))
        {
            _dialogs.Error("Tệp đã chọn không phải cơ sở dữ liệu SQLite.");
            return;
        }
        if (!_dialogs.Confirm("Toàn bộ dữ liệu hiện tại sẽ được thay bằng bản sao lưu đã chọn.\n(Dữ liệu hiện tại được tự động sao lưu trước.)\n\nTiếp tục?"))
            return;
        try
        {
            _repo.AutoBackup("truoc-khoi-phuc");
            File.Copy(path, _repo.DatabasePath, overwrite: true);
            _repo.EnsureSchema();
            DataRestored = true;
            _dialogs.Info("Đã khôi phục dữ liệu.");
        }
        catch (Exception ex)
        {
            _dialogs.Error("Khôi phục thất bại: " + ex.Message);
        }
    }

    private static bool IsSqliteFile(string path)
    {
        var header = new byte[16];
        using var fs = File.OpenRead(path);
        return fs.Read(header, 0, 16) == 16 && Encoding.ASCII.GetString(header, 0, 15) == "SQLite format 3";
    }
}
