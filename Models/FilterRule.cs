namespace StaffManagement.Models;

public enum FilterOperator
{
    Contains, NotContains, Equals, NotEquals, StartsWith, InList,
    GreaterThan, GreaterOrEqual, LessThan, LessOrEqual, Between,
    IsEmpty, IsNotEmpty, IsTrue, IsFalse,
}

/// <summary>Serializable filter condition (used by the builder UI, presets and the AI/keyword parser).</summary>
public sealed class FilterRule
{
    public string Field { get; set; } = "";
    public FilterOperator Operator { get; set; }
    public string? Value1 { get; set; }
    public string? Value2 { get; set; }
}

public sealed class FilterSet
{
    public bool MatchAll { get; set; } = true;
    public List<FilterRule> Rules { get; set; } = [];
}

public sealed class FilterPreset
{
    public string Name { get; set; } = "";
    public FilterSet Filter { get; set; } = new();
    /// <summary>When it was last saved; null for the built-in defaults and filters saved before v1.4 (listed last).</summary>
    public DateTime? SavedAt { get; set; }

    public override string ToString() => Name; // list item name for screen readers
}

public sealed record OperatorOption(FilterOperator Operator, string Label, int ValueCount)
{
    public override string ToString() => Label;
}

public static class FilterOperators
{
    private static readonly OperatorOption[] TextOps =
    [
        new(FilterOperator.Contains, "Chứa", 1),
        new(FilterOperator.Equals, "Bằng", 1),
        new(FilterOperator.NotContains, "Không chứa", 1),
        new(FilterOperator.NotEquals, "Khác", 1),
        new(FilterOperator.StartsWith, "Bắt đầu bằng", 1),
        new(FilterOperator.InList, "Thuộc danh sách (;)", 1),
        new(FilterOperator.IsEmpty, "Để trống", 0),
        new(FilterOperator.IsNotEmpty, "Có dữ liệu", 0),
    ];

    private static readonly OperatorOption[] NumberOps =
    [
        new(FilterOperator.Equals, "=", 1),
        new(FilterOperator.Between, "Trong khoảng", 2),
        new(FilterOperator.GreaterOrEqual, "≥", 1),
        new(FilterOperator.LessOrEqual, "≤", 1),
        new(FilterOperator.GreaterThan, ">", 1),
        new(FilterOperator.LessThan, "<", 1),
        new(FilterOperator.NotEquals, "≠", 1),
        new(FilterOperator.IsEmpty, "Để trống", 0),
        new(FilterOperator.IsNotEmpty, "Có dữ liệu", 0),
    ];

    private static readonly OperatorOption[] DateOps =
    [
        new(FilterOperator.Between, "Trong khoảng", 2),
        new(FilterOperator.Equals, "Đúng ngày", 1),
        new(FilterOperator.LessThan, "Trước ngày", 1),
        new(FilterOperator.GreaterThan, "Sau ngày", 1),
        new(FilterOperator.IsEmpty, "Để trống", 0),
        new(FilterOperator.IsNotEmpty, "Có dữ liệu", 0),
    ];

    private static readonly OperatorOption[] BoolOps =
    [
        new(FilterOperator.IsTrue, "Có", 0),
        new(FilterOperator.IsFalse, "Không", 0),
    ];

    public static IReadOnlyList<OperatorOption> For(FieldKind kind) => kind switch
    {
        FieldKind.Number => NumberOps,
        FieldKind.Date => DateOps,
        FieldKind.Bool => BoolOps,
        _ => TextOps,
    };
}
