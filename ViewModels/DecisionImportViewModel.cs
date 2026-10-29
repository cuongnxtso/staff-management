using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StaffManagement.Models;
using StaffManagement.Services;

namespace StaffManagement.ViewModels;

public sealed record ActionOption(DecisionAction Action, string Label)
{
    public override string ToString() => Label;
}

public sealed record KindOption(DecisionKind Kind, string Label)
{
    public override string ToString() => Label;
}

/// <summary>Review &amp; confirm screen for a parsed decision (QĐ) before it is written to the database.</summary>
public partial class DecisionImportViewModel : ObservableObject
{
    private readonly List<StaffRecord> _all;
    private readonly LlmSettings _llm;
    private bool _loading;

    public event Action<bool>? CloseRequested;

    public string FileName { get; }
    public string RawText { get; }
    public bool CanUseAi => _llm.IsUsable;

    public IReadOnlyList<KindOption> Kinds { get; } =
    [
        new(DecisionKind.Appoint, "Bổ nhiệm"),
        new(DecisionKind.Reappoint, "Bổ nhiệm lại"),
        new(DecisionKind.Assign, "Giao / cử phụ trách"),
        new(DecisionKind.Dismiss, "Thôi giữ chức vụ / miễn nhiệm"),
    ];

    public IReadOnlyList<ActionOption> Actions { get; } =
    [
        new(DecisionAction.AddNew, "Thêm dòng chức vụ mới"),
        new(DecisionAction.UpdateRow, "Cập nhật dòng đã có"),
        new(DecisionAction.MoveToFormer, "Chuyển dòng sang \"Đã thôi giữ chức vụ\""),
    ];

    public IReadOnlyList<string> PositionSuggestions { get; }
    public IReadOnlyList<string> DepartmentSuggestions { get; }
    public IReadOnlyList<string> HomeUnitSuggestions { get; }
    public IReadOnlyList<string> GenderSuggestions { get; } = ["Nam", "Nữ"];
    public IReadOnlyList<string> RankSuggestions { get; } = ["GS", "PGS"];
    public IReadOnlyList<string> DegreeSuggestions { get; }
    public IReadOnlyList<string> LevelSuggestions { get; } = ["BYT", "Trường", "Đơn vị"];
    public IReadOnlyList<string> UnitSuggestions { get; }

    public ObservableCollection<StaffRecord> PersonRows { get; } = [];

    [ObservableProperty] private KindOption _selectedKind;
    [ObservableProperty] private string? _staffCode;
    [ObservableProperty] private string? _fullName;
    [ObservableProperty] private string? _professionalTitle;
    /// <summary>What the reader could not find (shown in orange above the form).</summary>
    [ObservableProperty] private string? _warningsText;
    [ObservableProperty] private string? _gender;
    [ObservableProperty] private DateTime? _dateOfBirth;
    [ObservableProperty] private string? _academicRank;
    [ObservableProperty] private string? _degree;
    [ObservableProperty] private string? _position;
    [ObservableProperty] private string? _department;
    [ObservableProperty] private string? _homeUnit;
    [ObservableProperty] private string? _decisionNumber;
    [ObservableProperty] private DateTime? _decisionDate;
    [ObservableProperty] private DateTime? _effectiveDate;
    [ObservableProperty] private string? _termYears;
    [ObservableProperty] private string? _appointingUnit;
    [ObservableProperty] private string? _appointingLevel;
    [ObservableProperty] private string? _positionTextHint;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsTarget), nameof(CanRetireOldRow))]
    private ActionOption _selectedAction;

    [ObservableProperty] private StaffRecord? _selectedTarget;
    [ObservableProperty] private string? _matchInfo;

    /// <summary>When adding a new position (e.g. a promotion): optionally move one of the person's current posts to "Đã thôi".</summary>
    [ObservableProperty] private bool _retireOldRow;
    [ObservableProperty] private StaffRecord? _oldRowToRetire;
    public ObservableCollection<StaffRecord> ActivePersonRows { get; } = [];
    public bool CanRetireOldRow => SelectedAction.Action == DecisionAction.AddNew && ActivePersonRows.Count > 0;
    [ObservableProperty] private string? _errorText;
    [ObservableProperty] private bool _isBusy;

    public bool NeedsTarget => SelectedAction.Action != DecisionAction.AddNew;

    public DecisionImportViewModel(string path, string rawText, DecisionInfo info, List<StaffRecord> all,
        Func<FieldDefinition, IReadOnlyList<string>> suggestions, LlmSettings llm)
    {
        _all = all;
        _llm = llm;
        FileName = Path.GetFileName(path);
        RawText = rawText;
        IReadOnlyList<string> S(string key) => suggestions(FieldCatalog.Find(key)!);
        PositionSuggestions = S(nameof(StaffRecord.Position));
        DepartmentSuggestions = S(nameof(StaffRecord.Department));
        HomeUnitSuggestions = S(nameof(StaffRecord.HomeUnit));
        DegreeSuggestions = S(nameof(StaffRecord.Degree)) is { Count: > 0 } d ? d : ["TS", "ThS", "BSCKII"];
        UnitSuggestions = S(nameof(StaffRecord.AppointingUnit));
        _selectedKind = Kinds[0];
        _selectedAction = Actions[0];
        Load(info);
    }

    private void Load(DecisionInfo info)
    {
        _loading = true;
        SelectedKind = Kinds.First(k => k.Kind == info.Kind);
        StaffCode = info.StaffCode;
        FullName = info.FullName;
        ProfessionalTitle = info.ProfessionalTitle;
        WarningsText = info.Warnings.Count == 0 ? null : string.Join("\n", info.Warnings.Select(w => "⚠ " + w));
        Gender = info.Gender;
        DateOfBirth = info.DateOfBirth;
        AcademicRank = info.AcademicRank;
        Degree = info.Degree;
        // Map to the wording already used in the list ("Phòng Quản lý Đào tạo Sau đại học" → "Phòng QL ĐT SĐH")
        Position = FuzzyMatcher.BestMatch(info.Position, PositionSuggestions, 0.7) ?? info.Position;
        Department = FuzzyMatcher.BestMatch(info.Department, DepartmentSuggestions, 0.55) ?? info.Department;
        HomeUnit = FuzzyMatcher.BestMatch(info.HomeUnit, HomeUnitSuggestions, 0.55) ?? info.HomeUnit;
        DecisionNumber = info.DecisionNumber;
        DecisionDate = info.DecisionDate;
        EffectiveDate = info.EffectiveDate;
        TermYears = info.TermYears?.ToString();
        AppointingUnit = info.AppointingUnit;
        AppointingLevel = info.AppointingLevel;
        PositionTextHint = string.IsNullOrWhiteSpace(info.PositionText) ? null : $"Trích từ QĐ: \"{info.PositionText}\"";
        _loading = false;
        FindPerson();
    }

    partial void OnFullNameChanged(string? value) => FindPerson();
    partial void OnStaffCodeChanged(string? value) => FindPerson();
    partial void OnDateOfBirthChanged(DateTime? value) => FindPerson();
    partial void OnSelectedKindChanged(KindOption value) => FindPerson();

    private void FindPerson()
    {
        if (_loading) return;
        var info = ToDecisionInfo();
        PersonRows.Clear();
        foreach (var r in DecisionApplier.FindPerson(_all, info)) PersonRows.Add(r);
        ActivePersonRows.Clear();
        foreach (var r in PersonRows.Where(r => !r.IsFormer)) ActivePersonRows.Add(r);
        RetireOldRow = false;
        OldRowToRetire = DecisionApplier.BestRow(ActivePersonRows, info) ?? ActivePersonRows.FirstOrDefault();

        var (action, target) = DecisionApplier.Suggest(info, PersonRows.ToList());
        SelectedAction = Actions.First(a => a.Action == action);
        SelectedTarget = target ?? PersonRows.FirstOrDefault();
        OnPropertyChanged(nameof(CanRetireOldRow));
        bool byCode = !string.IsNullOrWhiteSpace(StaffCode) && PersonRows.Any(r => string.Equals(r.StaffCode?.Trim(), StaffCode.Trim(), StringComparison.OrdinalIgnoreCase));
        var how = byCode ? "khớp Mã CB" : $"khớp họ tên{(DateOfBirth is null ? "" : " và ngày sinh")}";
        MatchInfo = PersonRows.Count == 0
            ? "Chưa có cán bộ này trong danh sách → sẽ thêm mới."
            : DecisionApplier.RowWithSameDecision(PersonRows, info) is { } same
                ? $"QĐ {info.DecisionNumber} đã có trong danh sách ({same.Position} – {same.Department}) → đề xuất cập nhật dòng này, không thêm trùng."
                : $"Tìm thấy {PersonRows.Count} dòng của cán bộ này ({how}).";
    }

    [RelayCommand]
    private async Task ParseWithAiAsync()
    {
        try
        {
            IsBusy = true;
            ErrorText = null;
            var info = await new LlmClient(_llm).ParseDecisionAsync(RawText);
            Load(info);
        }
        catch (Exception ex)
        {
            ErrorText = "AI lỗi: " + ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public DecisionInfo ToDecisionInfo() => new()
    {
        Kind = SelectedKind.Kind,
        StaffCode = VietnameseText.CleanText(StaffCode),
        FullName = VietnameseText.CleanText(FullName),
        ProfessionalTitle = VietnameseText.CleanText(ProfessionalTitle),
        Gender = VietnameseText.CleanText(Gender),
        DateOfBirth = DateOfBirth,
        AcademicRank = VietnameseText.CleanText(AcademicRank),
        Degree = VietnameseText.CleanText(Degree),
        Position = VietnameseText.CleanText(Position),
        Department = VietnameseText.CleanText(Department),
        HomeUnit = VietnameseText.CleanText(HomeUnit),
        DecisionNumber = VietnameseText.CleanText(DecisionNumber),
        DecisionDate = DecisionDate,
        EffectiveDate = EffectiveDate,
        TermYears = VietnameseText.ParseInt(TermYears),
        AppointingUnit = VietnameseText.CleanText(AppointingUnit),
        AppointingLevel = VietnameseText.CleanText(AppointingLevel),
    };

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(FullName)) { ErrorText = "Thiếu Họ và tên."; return; }
        if (NeedsTarget && SelectedTarget is null) { ErrorText = "Hãy chọn dòng cần cập nhật."; return; }
        if (SelectedAction.Action != DecisionAction.MoveToFormer && string.IsNullOrWhiteSpace(Position))
        {
            ErrorText = "Thiếu Chức vụ.";
            return;
        }
        if (!string.IsNullOrWhiteSpace(TermYears) && VietnameseText.ParseInt(TermYears) is null)
        {
            ErrorText = "Thời hạn (năm) phải là số.";
            return;
        }
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
