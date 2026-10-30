using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StaffManagement.Infrastructure;
using StaffManagement.Models;
using StaffManagement.Services;

namespace StaffManagement.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private const string ExcelFilter = "Excel (*.xlsx)|*.xlsx";
    private const string DecisionFilter = "Quyết định (*.pdf;*.docx;*.txt)|*.pdf;*.docx;*.txt|Tất cả|*.*";

    /// <summary>Fields describing the person (shared by all rows of the same person).</summary>
    private static readonly string[] PersonalFieldKeys =
    [
        nameof(StaffRecord.StaffCode), nameof(StaffRecord.FullName), nameof(StaffRecord.DateOfBirth), nameof(StaffRecord.Gender),
        nameof(StaffRecord.RetirementMonth), nameof(StaffRecord.RetirementYear), nameof(StaffRecord.PartyJoinDate),
        nameof(StaffRecord.PartyOfficialDate), nameof(StaffRecord.AcademicRank), nameof(StaffRecord.Degree),
        nameof(StaffRecord.PoliticalTheory), nameof(StaffRecord.HomeUnit), nameof(StaffRecord.Phone),
        nameof(StaffRecord.AllPositions), nameof(StaffRecord.ProfessionalTitle),
    ];

    private readonly StaffRepository _repo;
    private readonly SettingsStore _settings;
    private readonly IDialogService _dialogs;
    private readonly Dictionary<string, IReadOnlyList<string>> _suggestionCache = [];
    private Func<StaffRecord, bool> _predicate = _ => true;
    private Dictionary<StaffRecord, string> _personOf = [];
    private Func<StaffRecord, bool> _matchesAnySheet = _ => true;
    private bool _suspendRefresh; // also marks programmatic rule changes (applying a preset / question, clearing)
    private string? _lastPresetName;

    public ObservableCollection<StaffRecord> Records { get; } = [];
    public ICollectionView View { get; }
    public ObservableCollection<FilterRuleViewModel> Rules { get; } = [];
    public ObservableCollection<FilterPreset> Presets { get; } = [];
    public ObservableCollection<ColumnOption> Columns { get; }
    public ObservableCollection<StatRow> StatRows { get; } = [];
    public IReadOnlyList<StatOption> StatOptions => StatOption.All;
    /// <summary>Sheet tabs under the list, mirroring the two sections of the Excel template.</summary>
    public IReadOnlyList<SectionOption> Sections { get; } =
    [
        new(false, "Đang giữ chức vụ"),
        new(true, "Thôi giữ chức vụ"),
    ];

    /// <summary>Set by the view (DataGrid multi-selection).</summary>
    public IList<StaffRecord> SelectedRecords { get; set; } = [];

    [ObservableProperty] private string? _quickSearch;
    [ObservableProperty] private bool _matchAll = true;
    [ObservableProperty] private SectionOption _section;
    [ObservableProperty] private string? _naturalQuery;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusText = "Sẵn sàng";
    [ObservableProperty] private FilterPreset? _selectedPreset;
    [ObservableProperty] private StatOption _statOption;
    [ObservableProperty] private bool _statCountPeople = true;
    [ObservableProperty] private StaffRecord? _selectedRecord;
    [ObservableProperty] private bool _isFilterExpanded = true;
    [ObservableProperty] private bool _onePerPerson;
    [ObservableProperty] private string _aiModeText = "";

    // Summary bar
    [ObservableProperty] private int _filteredRows;
    [ObservableProperty] private int _filteredPeople;
    [ObservableProperty] private string _maleText = "";
    [ObservableProperty] private string _femaleText = "";
    [ObservableProperty] private string _averageAgeText = "";
    [ObservableProperty] private int _partyCount;
    [ObservableProperty] private int _retiringThisYear;
    [ObservableProperty] private int _expiringThisYear;

    // Reminders (posts ending soon)
    private ReminderSettings _reminderSettings;
    private Dictionary<string, ReminderState> _reminderStates;
    private IReadOnlyList<ReminderItem> _reminders = [];
    private readonly System.Windows.Threading.DispatcherTimer _reminderTimer = new() { Interval = TimeSpan.FromHours(1) };
    private DateTime _remindersCheckedOn;
    /// <summary>Reminders not dismissed — the number on the bell button.</summary>
    [ObservableProperty] private int _reminderCount;
    /// <summary>Text of the yellow banner when new count-down marks were reached (null = hidden).</summary>
    [ObservableProperty] private string? _reminderBanner;

    public int TotalRows => Records.Count;
    public string RulesHeader => Rules.Count == 0 ? "Bộ lọc nâng cao" : $"Bộ lọc nâng cao ({Rules.Count} điều kiện)";
    public bool HasData => Records.Count > 0;

    public MainViewModel(StaffRepository repo, SettingsStore settings, IDialogService dialogs)
    {
        _repo = repo;
        _settings = settings;
        _dialogs = dialogs;
        _section = Sections[0];
        _statOption = StatOption.All[0];

        Columns = [];

        View = CollectionViewSource.GetDefaultView(Records);
        View.Filter = o => o is StaffRecord r && _predicate(r);
        Rules.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(RulesHeader)); OnRulesEdited(); Refresh(); };
        Records.CollectionChanged += (_, _) => { OnPropertyChanged(nameof(TotalRows)); OnPropertyChanged(nameof(HasData)); };

        foreach (var p in _repo.LoadPresets()) Presets.Add(p);
        UpdateAiModeText();
        _reminderSettings = _settings.LoadReminders();
        _reminderStates = _repo.LoadReminderStates();
        Reload();

        // Check at start-up, then every hour (the app may stay open for days).
        CheckReminders(notify: true);
        _reminderTimer.Tick += (_, _) => CheckReminders(notify: DateTime.Today != _remindersCheckedOn);
        _reminderTimer.Start();
    }

    // ------------------------------------------------------------------ Reminders

    /// <summary>
    /// Recomputes the count-down. With <paramref name="notify"/>, reminders that reached a new mark
    /// (100, 95, 90 … days left) are shown in the banner and recorded as notified.
    /// </summary>
    private void CheckReminders(bool notify)
    {
        _reminders = ReminderService.Compute(Records, _reminderSettings, DateTime.Today);
        ReminderCount = _reminders.Count(i => !(_reminderStates.TryGetValue(i.Key, out var s) && s.Dismissed));
        if (!notify) return;
        _remindersCheckedOn = DateTime.Today;
        var due = ReminderService.Due(_reminders, _reminderStates);
        if (due.Count == 0) return;
        _repo.SaveReminderStates(ReminderService.MarkNotified(due, _reminderStates));
        ReminderBanner = $"{due.Count} chức vụ vừa đến mốc nhắc hạn: "
                         + string.Join("; ", due.Take(3).Select(i => $"{i.Record.FullName} – {i.Record.Position} ({i.CountdownText.ToLowerInvariant()})"))
                         + (due.Count > 3 ? $"; … và {due.Count - 3} chức vụ khác" : "");
    }

    [RelayCommand]
    private void OpenReminders()
    {
        ReminderBanner = null;
        var vm = new RemindersViewModel(() => _reminders, _reminderStates, s =>
            {
                _repo.SaveReminderStates(s);
                CheckReminders(notify: false);
            },
            r => EditRecord(Records.FirstOrDefault(x => x.Id == r.Id)), _reminderSettings);
        _dialogs.ShowReminders(vm);
    }

    [RelayCommand]
    private void CloseReminderBanner() => ReminderBanner = null;

    [RelayCommand]
    private void OpenHelp() => _dialogs.ShowHelp();

    /// <summary>First start on this data: point the user at the manual, once.</summary>
    public void OfferHelpOnFirstStart()
    {
        if (_settings.HelpOffered) return;
        _settings.HelpOffered = true;
        if (_dialogs.Confirm(
                "Chào mừng bạn đến với phần mềm Quản lý cán bộ lãnh đạo, quản lý!\n\n" +
                "Phần mềm có sẵn Hướng dẫn sử dụng: từng bước cho mỗi chức năng kèm mẹo, tìm được không cần gõ dấu.\n\n" +
                "Mở Hướng dẫn ngay bây giờ?\n\n" +
                "(Có thể mở lại bất cứ lúc nào bằng nút  ?  ở góc trên bên phải, hoặc phím F1.)",
                "Lần đầu sử dụng"))
            _dialogs.ShowHelp();
    }

    // ------------------------------------------------------------------ Loading & filtering

    private void Reload()
    {
        _suspendRefresh = true;
        Records.Clear();
        foreach (var r in _repo.LoadAll()) Records.Add(r);
        _suggestionCache.Clear();
        if (FieldCatalog.SetExtraColumns(Records) || Columns.Count == 0) RebuildColumns();
        _suspendRefresh = false;
        Refresh();
    }

    /// <summary>Raised after <see cref="Columns"/> is rebuilt (extra Excel columns appeared or went away), so the grid recreates its columns.</summary>
    public event Action? ColumnsRebuilt;

    private void RebuildColumns()
    {
        var visible = _settings.VisibleColumns?.ToHashSet();
        var shown = Columns.Select(c => c.Field.Key).ToHashSet();
        // A column that just appeared (first import of a file with extra columns) is shown; otherwise the saved choice wins.
        bool IsVisible(FieldDefinition f) => Columns.Count > 0 && !shown.Contains(f.Key) ? f.DefaultVisible : visible?.Contains(f.Key) ?? f.DefaultVisible;
        var options = FieldCatalog.All.Select(f => new ColumnOption(f, IsVisible(f))).ToList();

        Columns.Clear();
        foreach (var c in options)
        {
            c.PropertyChanged += (_, _) => SaveVisibleColumns();
            Columns.Add(c);
        }
        if (visible is not null) SaveVisibleColumns();
        ColumnsRebuilt?.Invoke();
    }

    private void SaveVisibleColumns() => _settings.VisibleColumns = Columns.Where(x => x.IsVisible).Select(x => x.Field.Key).ToList();

    partial void OnQuickSearchChanged(string? value) => Refresh();
    partial void OnMatchAllChanged(bool value)
    {
        OnRulesEdited();
        Refresh();
    }
    partial void OnSectionChanged(SectionOption value) => Refresh();
    partial void OnOnePerPersonChanged(bool value) => Refresh();

    /// <summary>Ctrl+PageUp / Ctrl+PageDown, like switching sheets in Excel.</summary>
    [RelayCommand]
    private void SelectSheet(string former) => Section = Sections.First(s => s.IsFormer == (former == "1"));
    partial void OnStatOptionChanged(StatOption value) => RefreshStats();
    partial void OnStatCountPeopleChanged(bool value) => RefreshStats();

    public FilterSet CurrentFilter() => new() { MatchAll = MatchAll, Rules = Rules.Select(r => r.ToModel()).ToList() };

    public void Refresh()
    {
        if (_suspendRefresh) return;
        var rules = FilterEngine.Compile(CurrentFilter());
        var quick = FilterEngine.QuickSearch(QuickSearch);
        _personOf = PersonIdentity.Build(Records);
        HashSet<StaffRecord> other = [];
        foreach (var sheet in Sections)
        {
            IEnumerable<StaffRecord> shown = Records.Where(r => r.IsFormer == sheet.IsFormer && quick(r) && rules(r));
            // One row per person among the matching rows: the main row (not Lặp) when it matches, otherwise another matching row.
            if (OnePerPerson) shown = shown.GroupBy(r => _personOf[r]).Select(PersonIdentity.MainRow);
            var set = shown.ToHashSet();
            sheet.Count = set.Count;
            if (sheet == Section) _predicate = set.Contains;
            else other = set;
        }
        var current = _predicate;
        _matchesAnySheet = r => current(r) || other.Contains(r);
        View.Refresh();
        UpdateSummary();
        RefreshStats();
        if (_reminderSettings is not null) CheckReminders(notify: false);
    }

    private List<StaffRecord> Filtered() => Records.Where(_predicate).ToList();

    private void UpdateSummary()
    {
        var list = Filtered();
        FilteredRows = list.Count;
        var people = People(list);
        FilteredPeople = people.Count;
        int male = people.Count(r => VietnameseText.Normalize(r.Gender) == "nam");
        int female = people.Count(r => VietnameseText.Normalize(r.Gender) == "nu");
        MaleText = $"{male} ({Pct(male, people.Count)})";
        FemaleText = $"{female} ({Pct(female, people.Count)})";
        var ages = people.Where(r => r.Age.HasValue).Select(r => r.Age!.Value).ToList();
        AverageAgeText = ages.Count > 0 ? $"{ages.Average():0.0}" : "–";
        PartyCount = people.Count(r => r.IsPartyMember);
        RetiringThisYear = people.Count(r => r.RetirementYear == DateTime.Today.Year);
        ExpiringThisYear = list.Count(r => !r.IsFormer && r.AppointExpiryYear == DateTime.Today.Year);
    }

    /// <summary>One row per person (their main row), so personal attributes are counted once.</summary>
    private List<StaffRecord> People(IEnumerable<StaffRecord> rows) =>
        rows.GroupBy(r => _personOf.TryGetValue(r, out var p) ? p : "#" + r.Id).Select(PersonIdentity.MainRow).ToList();

    /// <summary>
    /// Keeps the template's "Lặp" convention after the app adds / moves / deletes rows:
    /// each person keeps exactly one main row per section. Returns the number of rows whose flag changed.
    /// </summary>
    private int SyncRepeatFlags(IEnumerable<StaffRecord> affected, IEnumerable<StaffRecord>? incoming = null)
    {
        var flips = PersonIdentity.RepeatFlagChanges(Records.ToList(), affected, (incoming ?? []).ToHashSet());
        foreach (var r in flips)
        {
            var copy = r.Clone();
            copy.IsRepeat = !r.IsRepeat;
            _repo.Update(copy);
            ReplaceRecord(r, copy);
        }
        return flips.Count;
    }

    /// <summary>Applies the grid's current column sorting (if any) to rows outside the view.</summary>
    private IEnumerable<StaffRecord> SortLikeView(IEnumerable<StaffRecord> rows)
    {
        IOrderedEnumerable<StaffRecord>? ordered = null;
        foreach (var sd in View.SortDescriptions)
        {
            var prop = typeof(StaffRecord).GetProperty(sd.PropertyName);
            if (prop is null) continue;
            Func<StaffRecord, object?> key = prop.GetValue;
            bool desc = sd.Direction == ListSortDirection.Descending;
            ordered = ordered is null
                ? (desc ? rows.OrderByDescending(key, Comparer<object?>.Default) : rows.OrderBy(key, Comparer<object?>.Default))
                : (desc ? ordered.ThenByDescending(key, Comparer<object?>.Default) : ordered.ThenBy(key, Comparer<object?>.Default));
        }
        return ordered ?? rows;
    }

    private StaffRecord? Current(long id) => Records.FirstOrDefault(r => r.Id == id);

    private static string RepeatNote(int flipped) => $" Đã tự điều chỉnh cột Lặp ở {flipped} dòng (mỗi cán bộ giữ 1 dòng chính).";

    private static string Pct(int part, int total) => total == 0 ? "0%" : $"{100.0 * part / total:0.#}%";

    private void RefreshStats()
    {
        StatRows.Clear();
        var list = Filtered();
        if (StatCountPeople && StatOption.PerPerson)
            list = People(list);
        if (list.Count == 0) return;

        var groups = list.GroupBy(r => VietnameseText.CleanText(StatOption.KeySelector(r)) ?? "(Trống)", StringComparer.CurrentCultureIgnoreCase)
            .Select(g => new StatRow(g.Key, g.Count(), 100.0 * g.Count() / list.Count));
        groups = StatOption.SortByKey
            ? groups.OrderBy(g => g.Value == "(Trống)").ThenBy(g => g.Value, StringComparer.CurrentCulture)
            : groups.OrderByDescending(g => g.Count).ThenBy(g => g.Value, StringComparer.CurrentCulture);
        foreach (var g in groups) StatRows.Add(g);
    }

    /// <summary>Distinct values of a text field, for the value drop-downs.</summary>
    public IReadOnlyList<string> Suggestions(FieldDefinition field)
    {
        if (field.Kind != FieldKind.Text) return [];
        if (_suggestionCache.TryGetValue(field.Key, out var cached)) return cached;
        var values = Records.Select(r => VietnameseText.CleanText(field.Get(r)?.ToString()))
            .OfType<string>()
            .Where(s => s.Length <= 80)
            .GroupBy(s => VietnameseText.Normalize(s))
            .Select(g => g.GroupBy(x => x).OrderByDescending(x => x.Count()).First().Key)
            .OrderBy(s => s, StringComparer.CurrentCulture)
            .Take(400)
            .ToList();
        if (field.Key == nameof(StaffRecord.Gender) && values.Count == 0) values = ["Nam", "Nữ"];
        return _suggestionCache[field.Key] = values;
    }

    // ------------------------------------------------------------------ Filter builder

    [RelayCommand]
    private void AddRule() => AddRuleFrom(null);

    private FilterRuleViewModel AddRuleFrom(FilterRule? model)
    {
        var vm = new FilterRuleViewModel(Suggestions, r => Rules.Remove(r), model);
        vm.Changed += (_, _) =>
        {
            OnRulesEdited();
            Refresh();
        };
        Rules.Add(vm);
        IsFilterExpanded = true;
        return vm;
    }

    [RelayCommand]
    private void ClearFilters()
    {
        _suspendRefresh = true;
        Rules.Clear();
        QuickSearch = null;
        NaturalQuery = null;
        SelectedPreset = null;
        _suspendRefresh = false;
        Refresh();
    }

    private void ApplyFilterSet(FilterSet set)
    {
        _suspendRefresh = true;
        Rules.Clear();
        MatchAll = set.MatchAll;
        foreach (var r in set.Rules)
        {
            // "Tình trạng" is what the sheet tabs show: switch sheet instead of adding a rule that empties the other sheet.
            if (r.Field == nameof(StaffRecord.Status) && VietnameseText.Normalize(r.Value1) is { Length: > 0 } status)
            {
                bool former = status.Contains("thoi");
                Section = Sections.First(s => s.IsFormer == (r.Operator is FilterOperator.NotContains or FilterOperator.NotEquals ? !former : former));
                continue;
            }
            AddRuleFrom(r);
        }
        _suspendRefresh = false;
        Refresh();
    }

    partial void OnSelectedPresetChanged(FilterPreset? value)
    {
        if (value is null || _suspendRefresh) return;
        _lastPresetName = value.Name;
        ApplyFilterSet(value.Filter);
    }

    /// <summary>
    /// The user changed the rules by hand: they no longer match the selected preset, so clear the selection.
    /// Otherwise the drop-down keeps showing the preset and choosing it again does nothing (no selection change).
    /// </summary>
    private void OnRulesEdited()
    {
        if (!_suspendRefresh && SelectedPreset is not null) SelectedPreset = null;
    }

    [RelayCommand]
    private void SavePreset()
    {
        if (Rules.Count == 0)
        {
            _dialogs.Info("Hãy thêm ít nhất một điều kiện lọc trước khi lưu.");
            return;
        }
        var name = _dialogs.Prompt("Lưu bộ lọc", "Tên bộ lọc (ví dụ: Cán bộ nam 20–40 tuổi):", SelectedPreset?.Name ?? _lastPresetName);
        if (string.IsNullOrWhiteSpace(name)) return;
        var preset = new FilterPreset { Name = name.Trim(), Filter = CurrentFilter() };
        _repo.SavePreset(preset);
        var existing = Presets.FirstOrDefault(p => p.Name == preset.Name);
        if (existing is not null) Presets.Remove(existing);
        Presets.Add(preset);
        // Show the saved preset as selected without re-applying it (the rules on screen are already these).
        _suspendRefresh = true;
        SelectedPreset = preset;
        _suspendRefresh = false;
        _lastPresetName = preset.Name;
        StatusText = $"Đã lưu bộ lọc \"{preset.Name}\".";
    }

    [RelayCommand]
    private void DeletePreset()
    {
        if (SelectedPreset is not { } p) return;
        if (!_dialogs.Confirm($"Xóa bộ lọc đã lưu \"{p.Name}\"?")) return;
        _repo.DeletePreset(p.Name);
        Presets.Remove(p);
        SelectedPreset = null;
    }

    [RelayCommand]
    private async Task ParseNaturalQueryAsync()
    {
        if (string.IsNullOrWhiteSpace(NaturalQuery)) return;
        var llm = _settings.LoadLlm();
        FilterSet set;
        string source;
        try
        {
            IsBusy = true;
            if (llm.IsUsable)
            {
                StatusText = "Đang hỏi AI…";
                var samples = new Dictionary<string, IReadOnlyList<string>>();
                foreach (var key in new[] { nameof(StaffRecord.Gender), nameof(StaffRecord.AcademicRank), nameof(StaffRecord.Degree),
                             nameof(StaffRecord.Position), nameof(StaffRecord.AppointingLevel), nameof(StaffRecord.Department) })
                    samples[key] = Suggestions(FieldCatalog.Find(key)!).Take(key == nameof(StaffRecord.Department) ? 40 : 20).ToList();
                set = await new LlmClient(llm).ParseQueryAsync(NaturalQuery, samples);
                source = "AI";
            }
            else
            {
                set = NaturalQueryParser.Parse(NaturalQuery);
                source = "bộ phân tích offline";
            }
        }
        catch (Exception ex)
        {
            set = NaturalQueryParser.Parse(NaturalQuery);
            source = $"bộ phân tích offline (AI lỗi: {ex.Message})";
        }
        finally
        {
            IsBusy = false;
        }

        if (set.Rules.Count == 0)
        {
            StatusText = "Không nhận ra điều kiện nào — hãy thử diễn đạt khác hoặc dùng bộ lọc nâng cao.";
            return;
        }
        var sheetBefore = Section;
        SelectedPreset = null; // rules from a question are not a saved preset
        ApplyFilterSet(set);
        var sheetNote = Section != sheetBefore ? $" Đã chuyển sang trang \"{Section.Label}\"." : "";
        StatusText = Rules.Count > 0
            ? $"Đã tạo {Rules.Count} điều kiện bằng {source}.{sheetNote} Bạn có thể chỉnh lại trong bộ lọc nâng cao."
            : $"Đã hiểu câu hỏi bằng {source}.{sheetNote}";
    }

    private void UpdateAiModeText()
    {
        var llm = _settings.LoadLlm();
        AiModeText = llm.IsUsable ? $"AI: {llm.Model}" : "Offline (không dùng AI)";
    }

    // ------------------------------------------------------------------ Excel

    [RelayCommand]
    private async Task ImportExcelAsync()
    {
        var path = _dialogs.OpenFile("Chọn tệp Excel danh sách cán bộ", ExcelFilter);
        if (path is not null) await ImportExcelFileAsync(path);
    }

    public async Task ImportExcelFileAsync(string path)
    {
        ExcelImportResult result;
        try
        {
            IsBusy = true;
            StatusText = "Đang đọc tệp Excel…";
            result = await Task.Run(() => ExcelService.Import(path));
        }
        catch (Exception ex)
        {
            _dialogs.Error($"Không đọc được tệp Excel:\n{ex.Message}");
            StatusText = "Nhập Excel thất bại.";
            return;
        }
        finally
        {
            IsBusy = false;
        }

        if (result.Records.Count == 0)
        {
            _dialogs.Info("Không tìm thấy dòng dữ liệu nào trong tệp.");
            return;
        }

        bool replace = true;
        if (Records.Count > 0)
        {
            var answer = _dialogs.YesNoCancel(
                $"Tệp có {result.Records.Count} dòng ({result.ActiveCount} đang giữ CV, {result.FormerCount} đã thôi).\n\n" +
                $"• Có: THAY THẾ toàn bộ {Records.Count} dòng hiện có\n• Không: THÊM vào dữ liệu hiện có\n\n" +
                "(Dữ liệu cũ được tự động sao lưu trước khi thay đổi.)", "Nhập Excel");
            if (answer is null) return;
            replace = answer.Value;
        }

        try
        {
            if (Records.Count > 0) _repo.AutoBackup(replace ? "truoc-thay-the" : "truoc-them");
            _repo.InsertMany(result.Records, replace);
            if (result.OrganizationName is not null) _settings.Organization = result.OrganizationName;
            if (result.Title is not null) _settings.Title = result.Title;
            if (replace || _settings.ExportColumnOrder is null) _settings.ExportColumnOrder = result.ColumnOrder;
        }
        catch (Exception ex)
        {
            _dialogs.Error($"Lỗi khi lưu dữ liệu:\n{ex.Message}");
            return;
        }

        Reload();
        StatusText = $"Đã nhập {result.Records.Count} dòng từ {Path.GetFileName(path)}.";
        if (result.Warnings.Count > 0)
        {
            var sb = new StringBuilder($"Đã nhập {result.Records.Count} dòng. Có {result.Warnings.Count} cảnh báo:\n\n");
            foreach (var w in result.Warnings.Take(15)) sb.AppendLine("• " + w);
            if (result.Warnings.Count > 15) sb.AppendLine($"… và {result.Warnings.Count - 15} cảnh báo khác.");
            _dialogs.Info(sb.ToString(), "Kết quả nhập Excel");
        }
    }

    [RelayCommand]
    private void ExportExcel()
    {
        // Like the Excel template, the export contains both sheets (current search / filter / sort applied to each).
        var rows = SortLikeView(Records.Where(_matchesAnySheet)).ToList();
        if (rows.Count == 0)
        {
            _dialogs.Info("Không có dòng nào thỏa bộ lọc hiện tại để xuất.");
            return;
        }
        int active = rows.Count(r => !r.IsFormer), former = rows.Count - active;
        var allColumns = _dialogs.YesNoCancel(
            $"Xuất {active} dòng \"Đang giữ chức vụ\" và {former} dòng \"Thôi giữ chức vụ\" (theo bộ lọc hiện tại).\n\n" +
            "• Có: đủ các cột, đúng vị trí cột như tệp Excel đã nhập (có thể nhập lại vào phần mềm)\n• Không: chỉ các cột đang hiển thị",
            "Xuất Excel");
        if (allColumns is null) return;

        var path = _dialogs.SaveFile("Xuất danh sách ra Excel", ExcelFilter, $"DanhSach_LDQL_{DateTime.Today:yyyyMMdd}.xlsx");
        if (path is null) return;

        var columns = ExcelService.InFileOrder(allColumns.Value
                ? [.. FieldCatalog.ExcelFields, .. FieldCatalog.Extras]
                : Columns.Where(c => c.IsVisible).Select(c => c.Field),
            _settings.ExportColumnOrder, rows, dropEmpty: allColumns.Value);
        try
        {
            ExcelService.Export(path, rows, columns, _settings.Organization, _settings.Title);
        }
        catch (IOException ex)
        {
            _dialogs.Error($"Không ghi được tệp (có thể tệp đang mở trong Excel):\n{ex.Message}");
            return;
        }
        StatusText = $"Đã xuất {rows.Count} dòng ra {Path.GetFileName(path)}.";
        if (_dialogs.Confirm("Xuất thành công. Mở tệp vừa xuất?")) _dialogs.OpenWithShell(path);
    }

    // ------------------------------------------------------------------ Manual entry

    [RelayCommand]
    private void AddRecord()
    {
        var vm = new StaffEditViewModel(new StaffRecord { IsFormer = Section.IsFormer }, Suggestions, isNew: true);
        if (!_dialogs.EditStaff(vm)) return;
        var record = vm.Result!;
        _repo.Insert(record);
        Records.Add(record);
        int flipped = SyncRepeatFlags([record], [record]);
        _suggestionCache.Clear();
        Refresh();
        SelectedRecord = Current(record.Id);
        StatusText = $"Đã thêm {record.FullName}." + (flipped > 0 ? RepeatNote(flipped) : "");
    }

    [RelayCommand]
    private void EditRecord(StaffRecord? record)
    {
        record ??= SelectedRecord;
        if (record is null) return;
        var vm = new StaffEditViewModel(record.Clone(), Suggestions, isNew: false);
        if (!_dialogs.EditStaff(vm)) return;
        var updated = vm.Result!;
        var before = PersonIdentity.Build(Records);
        var others = Records.Where(r => r != record && before[r] == before[record]).ToList();
        _repo.Update(updated);
        ReplaceRecord(record, updated);

        // Keep the person's details consistent across their other rows.
        var changed = PersonalFieldKeys.Select(FieldCatalog.Find).OfType<FieldDefinition>()
            .Where(f => !Equals(f.Get(record), f.Get(updated))).ToList();
        if (changed.Count > 0 && others.Count > 0 &&
            _dialogs.Confirm($"Cán bộ này còn {others.Count} dòng chức vụ khác.\nCập nhật thông tin cá nhân đã sửa ({string.Join(", ", changed.Select(f => f.Header))}) cho các dòng đó?"))
        {
            foreach (var other in others)
            {
                var copy = other.Clone();
                foreach (var f in changed) f.Set!(copy, f.Get(updated));
                _repo.Update(copy);
                ReplaceRecord(other, copy);
            }
        }
        // Moved between "đang giữ" and "đã thôi" with the form's checkbox → keep one main row per section.
        // (A plain edit keeps whatever the user chose in the Lặp box.)
        int flipped = updated.IsFormer != record.IsFormer ? SyncRepeatFlags([updated], [updated]) : 0;
        _suggestionCache.Clear();
        Refresh();
        SelectedRecord = Current(updated.Id);
        StatusText = $"Đã cập nhật {updated.FullName}." + (flipped > 0 ? RepeatNote(flipped) : "");
    }

    private void ReplaceRecord(StaffRecord oldRecord, StaffRecord newRecord)
    {
        var idx = Records.IndexOf(oldRecord);
        if (idx >= 0) Records[idx] = newRecord;
    }

    [RelayCommand]
    private void DeleteRecords()
    {
        var selected = SelectedRecords.ToList();
        if (selected.Count == 0) return;
        var names = string.Join(", ", selected.Take(5).Select(r => r.FullName)) + (selected.Count > 5 ? "…" : "");
        if (!_dialogs.Confirm($"Xóa vĩnh viễn {selected.Count} dòng?\n{names}\n\nGợi ý: nếu cán bộ thôi giữ chức vụ, hãy dùng \"Chuyển sang Đã thôi\" để giữ lịch sử.", "Xóa dữ liệu"))
            return;
        _repo.AutoBackup("truoc-xoa");
        var before = PersonIdentity.Build(Records);
        var deletedPeople = selected.Select(r => before[r]).ToHashSet();
        var related = Records.Where(r => !selected.Contains(r) && deletedPeople.Contains(before[r])).ToList();
        _repo.Delete(selected.Select(r => r.Id));
        foreach (var r in selected) Records.Remove(r);
        // If a main row was deleted, one of the person's remaining rows becomes the main row.
        int flipped = SyncRepeatFlags(related);
        Refresh();
        StatusText = $"Đã xóa {selected.Count} dòng." + (flipped > 0 ? RepeatNote(flipped) : "");
    }

    [RelayCommand]
    private void ToggleFormer()
    {
        var selected = SelectedRecords.ToList();
        if (selected.Count == 0) return;
        bool toFormer = selected.Any(r => !r.IsFormer);
        string? reason = null;
        if (toFormer)
        {
            reason = _dialogs.Prompt("Thôi giữ chức vụ", $"Lý do / ghi chú cho {selected.Count} dòng (ví dụ: Đến tuổi nghỉ hưu từ 01/10/2026):");
            if (reason is null) return;
        }
        var moved = new List<StaffRecord>();
        foreach (var r in selected)
        {
            var copy = r.Clone();
            copy.IsFormer = toFormer;
            if (!string.IsNullOrWhiteSpace(reason))
                copy.Notes = string.IsNullOrWhiteSpace(copy.Notes) ? reason.Trim() : $"{copy.Notes}; {reason.Trim()}";
            _repo.Update(copy);
            ReplaceRecord(r, copy);
            moved.Add(copy);
        }
        int flipped = SyncRepeatFlags(moved, moved);
        Refresh();
        StatusText = (toFormer ? $"Đã chuyển {selected.Count} dòng sang \"Đã thôi giữ chức vụ\"." : $"Đã khôi phục {selected.Count} dòng.")
                     + (flipped > 0 ? RepeatNote(flipped) : "");
    }

    // ------------------------------------------------------------------ Decision (QĐ) import

    [RelayCommand]
    private async Task ImportDecisionAsync()
    {
        var files = _dialogs.OpenFiles("Chọn tệp Quyết định (PDF / Word)", DecisionFilter);
        if (files.Length > 0) await ImportDecisionFilesAsync(files);
    }

    public async Task ImportDecisionFilesAsync(IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            string text;
            try
            {
                IsBusy = true;
                StatusText = $"Đang đọc {Path.GetFileName(path)}…";
                text = await Task.Run(() => DocumentTextExtractor.Extract(path));
            }
            catch (Exception ex)
            {
                _dialogs.Error($"{Path.GetFileName(path)}:\n{ex.Message}", "Không đọc được quyết định");
                continue;
            }
            finally
            {
                IsBusy = false;
            }

            var info = DecisionParser.Parse(text);
            var vm = new DecisionImportViewModel(path, text, info, Records.ToList(), Suggestions, _settings.LoadLlm());
            if (!_dialogs.ReviewDecision(vm))
            {
                StatusText = $"Đã bỏ qua {Path.GetFileName(path)}.";
                continue;
            }
            ApplyDecision(vm);
        }
    }

    private void ApplyDecision(DecisionImportViewModel vm)
    {
        var info = vm.ToDecisionInfo();
        // Rows added or moved between sections: their Lặp flag is recomputed afterwards (one main row per person and section).
        var touched = new List<StaffRecord>();
        long? select = null;
        switch (vm.SelectedAction.Action)
        {
            case DecisionAction.AddNew:
                var row = DecisionApplier.CreateRow(info, vm.PersonRows.FirstOrDefault());
                _repo.Insert(row);
                Records.Add(row);
                touched.Add(row);
                if (vm is { RetireOldRow: true, CanRetireOldRow: true, OldRowToRetire: { } old })
                {
                    var retired = old.Clone();
                    DecisionApplier.MoveToFormer(retired, info);
                    _repo.Update(retired);
                    ReplaceRecord(old, retired);
                    touched.Add(retired);
                }
                select = row.Id;
                StatusText = $"Đã thêm {row.FullName} — {row.Position} {row.Department}.";
                break;
            case DecisionAction.UpdateRow when vm.SelectedTarget is { } target:
                var updated = target.Clone();
                DecisionApplier.ApplyAppointment(updated, info, isNewPosition: false);
                _repo.Update(updated);
                ReplaceRecord(target, updated);
                if (updated.IsFormer != target.IsFormer) touched.Add(updated); // a former row was re-appointed
                select = updated.Id;
                StatusText = $"Đã cập nhật QĐ {info.DecisionNumber} cho {updated.FullName}.";
                break;
            case DecisionAction.MoveToFormer when vm.SelectedTarget is { } target:
                var moved = target.Clone();
                DecisionApplier.MoveToFormer(moved, info);
                _repo.Update(moved);
                ReplaceRecord(target, moved);
                touched.Add(moved);
                StatusText = $"Đã chuyển {moved.FullName} ({moved.Position}) sang \"Đã thôi giữ chức vụ\".";
                break;
        }
        int flipped = touched.Count > 0 ? SyncRepeatFlags(touched, touched) : 0;
        if (flipped > 0) StatusText += RepeatNote(flipped);
        _suggestionCache.Clear();
        Refresh();
        if (select is { } id) SelectedRecord = Current(id);
    }

    // ------------------------------------------------------------------ Settings

    [RelayCommand]
    private void OpenSettings()
    {
        var vm = new SettingsViewModel(_repo, _settings, _dialogs);
        _dialogs.ShowSettings(vm);
        UpdateAiModeText();
        _reminderSettings = _settings.LoadReminders();
        CheckReminders(notify: false);
        if (vm.DataRestored) Reload();
    }

    // ------------------------------------------------------------------ Drag & drop

    public async Task HandleDroppedFilesAsync(string[] files)
    {
        var excel = files.FirstOrDefault(f => Path.GetExtension(f).Equals(".xlsx", StringComparison.OrdinalIgnoreCase));
        if (excel is not null)
        {
            await ImportExcelFileAsync(excel);
            return;
        }
        var decisions = files.Where(DocumentTextExtractor.IsSupported).ToList();
        if (decisions.Count == 0)
        {
            _dialogs.Info("Chỉ hỗ trợ kéo thả tệp Excel (.xlsx) hoặc Quyết định (.pdf, .docx, .txt).");
            return;
        }
        await ImportDecisionFilesAsync(decisions);
    }
}
