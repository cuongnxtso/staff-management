using CommunityToolkit.Mvvm.ComponentModel;
using StaffManagement.Models;
using StaffManagement.Services;

namespace StaffManagement.ViewModels;

/// <summary>Grid column with a user-controlled visibility toggle.</summary>
public partial class ColumnOption(FieldDefinition field, bool isVisible) : ObservableObject
{
    public FieldDefinition Field { get; } = field;

    [ObservableProperty]
    private bool _isVisible = isVisible;
}

/// <summary>Grouping for the statistics tab. PerPerson = count each person once (personal attributes).</summary>
public sealed record StatOption(string Label, Func<StaffRecord, string?> KeySelector, bool SortByKey = false, bool PerPerson = true)
{
    public override string ToString() => Label;

    public static IReadOnlyList<StatOption> All { get; } =
    [
        new("Giới tính", r => r.Gender),
        new("Nhóm tuổi", r => r.Age switch
        {
            null => null,
            < 40 => "Dưới 40",
            < 50 => "40 – 49",
            < 55 => "50 – 54",
            < 60 => "55 – 59",
            _ => "Từ 60 trở lên",
        }, SortByKey: true),
        new("Học hàm", r => r.AcademicRank),
        new("Trình độ chuyên môn", r => r.Degree),
        new("Trình độ LLCT (cao nhất)", r => VietnameseText.Normalize(r.PoliticalTheory) switch
        {
            var s when s.Contains("cao cap") => "Cao cấp",
            var s when s.Contains("cu nhan") => "Cử nhân",
            var s when s.Contains("trung cap") => "Trung cấp",
            var s when s.Contains("so cap") => "Sơ cấp",
            _ => null,
        }),
        new("Đảng viên", r => r.IsPartyMember ? "Đảng viên" : "Chưa / không có thông tin"),
        new("Chức vụ", r => r.Position, PerPerson: false),
        new("Đơn vị", r => r.Department, PerPerson: false),
        new("Đơn vị cơ hữu", r => r.HomeUnit),
        new("Cấp bổ nhiệm", r => r.AppointingLevel, PerPerson: false),
        new("Đơn vị bổ nhiệm", r => r.AppointingUnit, PerPerson: false),
        new("Năm nghỉ hưu", r => r.RetirementYear?.ToString(), SortByKey: true),
        new("Năm hết hạn bổ nhiệm", r => r.AppointExpiryYear?.ToString(), SortByKey: true, PerPerson: false),
        new("Năm sinh", r => r.BirthYear?.ToString(), SortByKey: true),
        new("Kiêm nhiệm (KN)", r => r.IsConcurrent ? "Kiêm nhiệm" : "Không", PerPerson: false),
        new("Đã bổ nhiệm lại", r => r.IsReappointed ? "Đã BN lại" : "Chưa", PerPerson: false),
        new("Chức danh nghề nghiệp", r => r.ProfessionalTitle),
    ];
}

public sealed record StatRow(string Value, int Count, double Percent)
{
    public string PercentText => $"{Percent:0.#}%";
}

/// <summary>A sheet tab under the list (like Excel): the active list or the "Thôi giữ chức vụ" section.</summary>
public partial class SectionOption(bool isFormer, string label) : ObservableObject
{
    public bool IsFormer { get; } = isFormer;
    public string Label { get; } = label;

    /// <summary>Rows of this sheet matching the current search / filter.</summary>
    [ObservableProperty]
    private int _count;

    public override string ToString() => Label;
}
