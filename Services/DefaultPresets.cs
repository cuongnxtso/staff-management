using StaffManagement.Models;

namespace StaffManagement.Services;

/// <summary>
/// Saved filters every data folder starts with. They are added once per <see cref="Version"/>: a filter the user
/// deleted does not come back, and one with the same name is left alone. Raise <see cref="Version"/> when adding more.
/// Time-relative filters use "in the next N days/months" so they don't go stale.
/// </summary>
public static class DefaultPresets
{
    public const int Version = 1;

    public static IReadOnlyList<FilterPreset> All() =>
    [
        Preset("Sắp hết hạn bổ nhiệm (100 ngày tới)", Rule(nameof(StaffRecord.DaysToTermEnd), FilterOperator.Between, "0", "100")),
        Preset("Hết nhiệm kỳ trong 12 tháng tới", Rule(nameof(StaffRecord.DaysToTermEnd), FilterOperator.Between, "0", "365")),
        Preset("Trưởng bộ môn nghỉ hưu trong 12 tháng tới",
            Rule(nameof(StaffRecord.Position), FilterOperator.StartsWith, "truong bo mon"),
            Rule(nameof(StaffRecord.MonthsToRetirement), FilterOperator.Between, "0", "12")),
        Preset("Nữ từ 40 đến 55 tuổi là đảng viên PGS",
            Rule(nameof(StaffRecord.Gender), FilterOperator.Equals, "Nữ"),
            Rule(nameof(StaffRecord.Age), FilterOperator.Between, "40", "55"),
            Rule(nameof(StaffRecord.IsPartyMember), FilterOperator.IsTrue),
            Rule(nameof(StaffRecord.AcademicRank), FilterOperator.Equals, "PGS")),
        Preset("Nam trên 55 tuổi",
            Rule(nameof(StaffRecord.Gender), FilterOperator.Equals, "Nam"),
            Rule(nameof(StaffRecord.Age), FilterOperator.GreaterThan, "55")),
        Preset("Sinh trước 1965", Rule(nameof(StaffRecord.BirthYear), FilterOperator.LessThan, "1965")),
        Preset("Trưởng phòng", Rule(nameof(StaffRecord.Position), FilterOperator.StartsWith, "truong phong")),
        Preset("Thuộc Khoa Y học cổ truyền", Rule(nameof(StaffRecord.ParentUnit), FilterOperator.Equals, "Khoa Y học cổ truyền")),
        Preset("Hạng I", Rule(nameof(StaffRecord.ProfessionalTitle), FilterOperator.Contains, "hang i)")),
        // Columns the client added to their Excel file; skipped with a note when the data has no such column.
        Preset("Dân tộc Tày", Rule(FieldCatalog.ExtraKey("Dân tộc"), FilterOperator.Contains, "Tày")),
        Preset("Chức vụ cơ hữu", Rule(FieldCatalog.ExtraKey("Chức vụ cơ hữu"), FilterOperator.IsNotEmpty)),
        Preset("Phân loại chức vụ Trưởng", Rule(FieldCatalog.ExtraKey("Phân loại chức vụ"), FilterOperator.Equals, "Trưởng")),
    ];

    // No save date: the user's own filters stay above the defaults in the picker.
    private static FilterPreset Preset(string name, params FilterRule[] rules) =>
        new() { Name = name, Filter = new FilterSet { Rules = [.. rules] } };

    private static FilterRule Rule(string field, FilterOperator op, string? value1 = null, string? value2 = null) =>
        new() { Field = field, Operator = op, Value1 = value1, Value2 = value2 };
}
