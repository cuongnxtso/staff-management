using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StaffManagement.Models;
using StaffManagement.Services;

namespace StaffManagement.ViewModels;

/// <summary>Editor for one field, generated from <see cref="FieldCatalog"/>.</summary>
public partial class FieldEditorViewModel : ObservableObject
{
    public FieldDefinition Field { get; }
    public IReadOnlyList<string> Suggestions { get; }

    [ObservableProperty] private string? _text;
    [ObservableProperty] private DateTime? _date;
    [ObservableProperty] private bool _flag;

    public FieldEditorViewModel(FieldDefinition field, StaffRecord record, IReadOnlyList<string> suggestions)
    {
        Field = field;
        Suggestions = suggestions;
        Load(record);
    }

    public bool IsText => Field.Kind == FieldKind.Text && !Field.Multiline;
    public bool IsMultiline => Field.Kind == FieldKind.Text && Field.Multiline;
    public bool IsNumber => Field.Kind == FieldKind.Number;
    public bool IsDate => Field.Kind == FieldKind.Date;
    public bool IsBool => Field.Kind == FieldKind.Bool;

    public void Load(StaffRecord r)
    {
        var v = Field.Get(r);
        switch (Field.Kind)
        {
            case FieldKind.Date: Date = v as DateTime?; break;
            case FieldKind.Bool: Flag = v is true; break;
            default: Text = v?.ToString(); break;
        }
    }

    /// <summary>Writes the value into the record; returns an error message when invalid.</summary>
    public string? Apply(StaffRecord r)
    {
        switch (Field.Kind)
        {
            case FieldKind.Date:
                Field.Set!(r, Date?.Date);
                break;
            case FieldKind.Bool:
                Field.Set!(r, Flag);
                break;
            case FieldKind.Number:
                if (string.IsNullOrWhiteSpace(Text)) { Field.Set!(r, null); break; }
                var n = VietnameseText.ParseInt(Text);
                if (n is null) return $"\"{Field.Header}\" phải là số.";
                Field.Set!(r, n);
                break;
            default:
                Field.Set!(r, VietnameseText.CleanText(Text));
                break;
        }
        return null;
    }
}

public sealed record EditorGroup(string Header, IReadOnlyList<FieldEditorViewModel> Editors);

public partial class StaffEditViewModel : ObservableObject
{
    private readonly StaffRecord _record;

    public event Action<bool>? CloseRequested;

    public string WindowTitle { get; }
    public IReadOnlyList<EditorGroup> Groups { get; }
    public StaffRecord? Result { get; private set; }

    [ObservableProperty] private bool _isFormer;
    [ObservableProperty] private string? _errorText;
    [ObservableProperty] private string _computedInfo = "";

    public StaffEditViewModel(StaffRecord record, Func<FieldDefinition, IReadOnlyList<string>> suggestions, bool isNew)
    {
        _record = record;
        _isFormer = record.IsFormer;
        WindowTitle = isNew ? "Thêm cán bộ / chức vụ" : $"Sửa: {record.FullName}";
        Groups = FieldCatalog.All
            .Where(f => !f.IsComputed && f.Group is not null)
            .GroupBy(f => f.Group!)
            .Select(g => new EditorGroup(g.Key, g.Select(f => new FieldEditorViewModel(f, record, suggestions(f))).ToList()))
            .ToList();
        UpdateComputedInfo();
    }

    private FieldEditorViewModel Editor(string key) => Groups.SelectMany(g => g.Editors).First(e => e.Field.Key == key);

    private void UpdateComputedInfo()
    {
        ComputedInfo = _record.Age is { } age
            ? $"Tuổi: {age} · Số năm giữ CV: {_record.YearsInPosition?.ToString() ?? "–"} · Số năm NK hiện tại: {_record.YearsInTerm?.ToString() ?? "–"}"
            : "";
    }

    [RelayCommand]
    private void SuggestRetirement()
    {
        var suggestion = RetirementCalculator.Suggest(Editor(nameof(StaffRecord.DateOfBirth)).Date, Editor(nameof(StaffRecord.Gender)).Text);
        if (suggestion is null)
        {
            ErrorText = "Cần nhập Ngày sinh và Giới để tính thời điểm nghỉ hưu.";
            return;
        }
        Editor(nameof(StaffRecord.RetirementMonth)).Text = suggestion.Value.Month.ToString();
        Editor(nameof(StaffRecord.RetirementYear)).Text = suggestion.Value.Year.ToString();
        ErrorText = null;
    }

    [RelayCommand]
    private void Save()
    {
        var copy = _record.Clone();
        foreach (var editor in Groups.SelectMany(g => g.Editors))
        {
            if (editor.Apply(copy) is { } error)
            {
                ErrorText = error;
                return;
            }
        }
        if (string.IsNullOrWhiteSpace(copy.FullName))
        {
            ErrorText = "Vui lòng nhập Họ và tên.";
            return;
        }
        if (copy.RetirementYear is null && RetirementCalculator.Suggest(copy.DateOfBirth, copy.Gender) is { } ret)
            (copy.RetirementMonth, copy.RetirementYear) = ret;
        copy.IsFormer = IsFormer;
        Result = copy;
        CloseRequested?.Invoke(true);
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);
}
