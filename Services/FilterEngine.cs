using System.Globalization;
using StaffManagement.Models;

namespace StaffManagement.Services;

/// <summary>Turns a <see cref="FilterSet"/> into an in-memory predicate (LINQ over the loaded list).</summary>
public static class FilterEngine
{
    public static Func<StaffRecord, bool> Compile(FilterSet set)
    {
        var predicates = set.Rules
            .Select(CompileRule)
            .Where(p => p is not null)
            .Cast<Func<StaffRecord, bool>>()
            .ToList();

        if (predicates.Count == 0) return _ => true;
        return set.MatchAll
            ? r => predicates.All(p => p(r))
            : r => predicates.Any(p => p(r));
    }

    /// <summary>Returns null when the rule is incomplete (it is then ignored).</summary>
    public static Func<StaffRecord, bool>? CompileRule(FilterRule rule)
    {
        var field = FieldCatalog.Find(rule.Field);
        if (field is null) return null;
        var op = rule.Operator;

        switch (op)
        {
            case FilterOperator.IsEmpty: return r => IsEmpty(field.Get(r));
            case FilterOperator.IsNotEmpty: return r => !IsEmpty(field.Get(r));
            case FilterOperator.IsTrue: return r => AsBool(field.Get(r));
            case FilterOperator.IsFalse: return r => !AsBool(field.Get(r));
        }

        return field.Kind switch
        {
            FieldKind.Number => CompileComparable(field, op, ParseNumber(rule.Value1), ParseNumber(rule.Value2), v => v is int i ? i : null),
            FieldKind.Date => CompileComparable(field, op, ParseDateValue(rule.Value1), ParseDateValue(rule.Value2), v => v is DateTime d ? d.Date : null),
            FieldKind.Bool => null,
            _ => CompileText(field, op, rule.Value1),
        };
    }

    private static Func<StaffRecord, bool>? CompileText(FieldDefinition field, FilterOperator op, string? value)
    {
        var needle = VietnameseText.Canonical(value);
        if (needle.Length == 0) return null;
        string Hay(StaffRecord r) => VietnameseText.Canonical(field.Get(r)?.ToString());

        switch (op)
        {
            case FilterOperator.Contains: return r => Hay(r).Contains(needle);
            case FilterOperator.NotContains: return r => !Hay(r).Contains(needle);
            case FilterOperator.Equals: return r => Hay(r) == needle;
            case FilterOperator.NotEquals: return r => Hay(r) != needle;
            case FilterOperator.StartsWith: return r => Hay(r).StartsWith(needle);
            case FilterOperator.InList:
                var items = value!.Split([';', '|'], StringSplitOptions.RemoveEmptyEntries)
                    .Select(VietnameseText.Canonical).Where(s => s.Length > 0).ToHashSet();
                return items.Count == 0 ? null : r => items.Contains(Hay(r));
            default: return null;
        }
    }

    private static Func<StaffRecord, bool>? CompileComparable<T>(FieldDefinition field, FilterOperator op, T? a, T? b,
        Func<object?, T?> convert) where T : struct, IComparable<T>
    {
        if (a is null) return null;
        var x = a.Value;
        Func<T, bool>? test = op switch
        {
            FilterOperator.Equals => v => v.CompareTo(x) == 0,
            FilterOperator.NotEquals => v => v.CompareTo(x) != 0,
            FilterOperator.GreaterThan => v => v.CompareTo(x) > 0,
            FilterOperator.GreaterOrEqual => v => v.CompareTo(x) >= 0,
            FilterOperator.LessThan => v => v.CompareTo(x) < 0,
            FilterOperator.LessOrEqual => v => v.CompareTo(x) <= 0,
            FilterOperator.Between when b is { } y => v => v.CompareTo(Min(x, y)) >= 0 && v.CompareTo(Max(x, y)) <= 0,
            FilterOperator.Between => v => v.CompareTo(x) >= 0,
            _ => null,
        };
        if (test is null) return null;
        return r => convert(field.Get(r)) is { } v && test(v);
    }

    private static T Min<T>(T a, T b) where T : IComparable<T> => a.CompareTo(b) <= 0 ? a : b;
    private static T Max<T>(T a, T b) where T : IComparable<T> => a.CompareTo(b) >= 0 ? a : b;

    private static bool IsEmpty(object? v) => v switch
    {
        null => true,
        string s => string.IsNullOrWhiteSpace(s),
        bool b => !b,
        _ => false,
    };

    private static bool AsBool(object? v) => v switch
    {
        bool b => b,
        string s => VietnameseText.ParseFlag(s),
        null => false,
        _ => true,
    };

    private static int? ParseNumber(string? s) => VietnameseText.ParseInt(s);

    private static DateTime? ParseDateValue(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        // A bare year ("2026") is accepted for dates: treat as 1 January.
        if (int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var y) && y is > 1900 and < 2200)
            return new DateTime(y, 1, 1);
        return VietnameseText.ParseDate(s);
    }

    /// <summary>Free-text quick search over the most useful columns (accent-insensitive).</summary>
    public static Func<StaffRecord, bool> QuickSearch(string? text)
    {
        var terms = VietnameseText.Canonical(text).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (terms.Length == 0) return _ => true;
        return r =>
        {
            var hay = VietnameseText.Canonical(string.Join(" | ",
                r.StaffCode, r.FullName, r.Position, r.Department, r.HomeUnit, r.AllPositions, r.Phone, r.DecisionNumber));
            return terms.All(hay.Contains);
        };
    }
}
