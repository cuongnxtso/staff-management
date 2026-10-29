using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StaffManagement.Models;
using StaffManagement.Services;

namespace StaffManagement.ViewModels;

/// <summary>One row of the filter builder: [Column] [Operator] [Value] ([Value 2]) [✕].</summary>
public partial class FilterRuleViewModel : ObservableObject
{
    private readonly Func<FieldDefinition, IReadOnlyList<string>> _suggestions;
    private readonly Action<FilterRuleViewModel> _remove;
    private bool _syncing;

    public static IReadOnlyList<FieldDefinition> Fields => FieldCatalog.All;

    public event EventHandler? Changed;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsText), nameof(IsNumber), nameof(IsDate), nameof(ShowValue2), nameof(ShowNumber2), nameof(ShowDate2))]
    private FieldDefinition _field;

    [ObservableProperty]
    private IReadOnlyList<OperatorOption> _operators = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsText), nameof(IsNumber), nameof(IsDate), nameof(ShowValue2), nameof(ShowNumber2), nameof(ShowDate2))]
    private OperatorOption? _selectedOperator;

    [ObservableProperty] private string? _value1;
    [ObservableProperty] private string? _value2;
    [ObservableProperty] private DateTime? _date1;
    [ObservableProperty] private DateTime? _date2;
    [ObservableProperty] private IReadOnlyList<string> _suggestionList = [];

    public FilterRuleViewModel(Func<FieldDefinition, IReadOnlyList<string>> suggestions, Action<FilterRuleViewModel> remove,
        FilterRule? model = null)
    {
        _suggestions = suggestions;
        _remove = remove;
        _syncing = true;
        _field = FieldCatalog.Find(model?.Field) ?? FieldCatalog.Find(nameof(StaffRecord.Gender))!;
        Operators = FilterOperators.For(_field.Kind);
        SelectedOperator = Operators.FirstOrDefault(o => o.Operator == model?.Operator) ?? Operators[0];
        Value1 = model?.Value1;
        Value2 = model?.Value2;
        Date1 = VietnameseText.ParseDate(model?.Value1);
        Date2 = VietnameseText.ParseDate(model?.Value2);
        SuggestionList = _suggestions(_field);
        _syncing = false;
    }

    private int ValueCount => SelectedOperator?.ValueCount ?? 0;
    public bool IsText => ValueCount > 0 && Field.Kind == FieldKind.Text;
    public bool IsNumber => ValueCount > 0 && Field.Kind == FieldKind.Number;
    public bool IsDate => ValueCount > 0 && Field.Kind == FieldKind.Date;
    public bool ShowValue2 => ValueCount == 2;
    public bool ShowNumber2 => ShowValue2 && Field.Kind == FieldKind.Number;
    public bool ShowDate2 => ShowValue2 && Field.Kind == FieldKind.Date;

    partial void OnFieldChanged(FieldDefinition value)
    {
        if (_syncing) return;
        _syncing = true;
        Operators = FilterOperators.For(value.Kind);
        SelectedOperator = Operators[0];
        Value1 = Value2 = null;
        Date1 = Date2 = null;
        SuggestionList = _suggestions(value);
        _syncing = false;
        RaiseChanged();
    }

    partial void OnSelectedOperatorChanged(OperatorOption? value) => RaiseChanged();
    partial void OnValue1Changed(string? value) => RaiseChanged();
    partial void OnValue2Changed(string? value) => RaiseChanged();

    partial void OnDate1Changed(DateTime? value)
    {
        if (_syncing) return;
        Value1 = value is null ? null : VietnameseText.FormatDate(value);
    }

    partial void OnDate2Changed(DateTime? value)
    {
        if (_syncing) return;
        Value2 = value is null ? null : VietnameseText.FormatDate(value);
    }

    private void RaiseChanged()
    {
        if (!_syncing) Changed?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    private void Remove() => _remove(this);

    public FilterRule ToModel() => new()
    {
        Field = Field.Key,
        Operator = SelectedOperator?.Operator ?? FilterOperator.Contains,
        Value1 = Value1,
        Value2 = Value2,
    };
}
