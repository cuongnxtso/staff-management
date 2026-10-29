using System.Text.RegularExpressions;
using StaffManagement.Models;

namespace StaffManagement.Services;

/// <summary>
/// Offline keyword parser for common Vietnamese questions, e.g.
/// "nữ từ 40 đến 50 tuổi là đảng viên", "nam trên 55 tuổi PGS", "nghỉ hưu năm nay",
/// "trưởng bộ môn thuộc khoa Y học cổ truyền", "hết hạn bổ nhiệm năm 2027".
/// Accents are optional. Produces the same rules as the filter builder.
/// </summary>
public static partial class NaturalQueryParser
{
    public static FilterSet Parse(string? query)
    {
        var set = new FilterSet();
        if (string.IsNullOrWhiteSpace(query)) return set;

        var lower = query.Trim().ToLowerInvariant();         // keeps accents: "nam" (male) ≠ "năm" (year)
        var n = VietnameseText.Canonical(query);              // accent-free, abbreviations expanded
        int thisYear = DateTime.Today.Year;
        void Add(string field, FilterOperator op, object? v1 = null, object? v2 = null) =>
            set.Rules.Add(new FilterRule { Field = field, Operator = op, Value1 = v1?.ToString(), Value2 = v2?.ToString() });

        // Gender
        if (Female().IsMatch(lower)) Add(nameof(StaffRecord.Gender), FilterOperator.Equals, "Nữ");
        else if (Male().IsMatch(lower)) Add(nameof(StaffRecord.Gender), FilterOperator.Equals, "Nam");

        // Age
        if (AgeRange().Match(n) is { Success: true } ar)
            Add(nameof(StaffRecord.Age), FilterOperator.Between, ar.Groups["a"].Value, ar.Groups["b"].Value);
        else
        {
            if (AgeMin().Match(n) is { Success: true } amin)
                Add(nameof(StaffRecord.Age), amin.Groups["gt"].Success ? FilterOperator.GreaterThan : FilterOperator.GreaterOrEqual,
                    amin.Groups["gt"].Success ? amin.Groups["gt"].Value : amin.Groups["ge"].Value);
            if (AgeMax().Match(n) is { Success: true } amax)
                Add(nameof(StaffRecord.Age), amax.Groups["lt"].Success ? FilterOperator.LessThan : FilterOperator.LessOrEqual,
                    amax.Groups["lt"].Success ? amax.Groups["lt"].Value : amax.Groups["le"].Value);
        }

        // Party membership
        if (n.Contains("dang vien"))
            Add(nameof(StaffRecord.IsPartyMember), NotParty().IsMatch(n) ? FilterOperator.IsFalse : FilterOperator.IsTrue);

        // Academic rank / degree
        if (Pgs().IsMatch(n)) Add(nameof(StaffRecord.AcademicRank), FilterOperator.Equals, "PGS");
        else if (Gs().IsMatch(n)) Add(nameof(StaffRecord.AcademicRank), FilterOperator.Equals, "GS");
        if (Ts().IsMatch(n)) Add(nameof(StaffRecord.Degree), FilterOperator.Equals, "TS");
        else if (Ths().IsMatch(n)) Add(nameof(StaffRecord.Degree), FilterOperator.Equals, "ThS");

        // Retirement
        if (n.Contains("nghi huu") || n.Contains("ve huu"))
        {
            if (WithinMonths().Match(n) is { Success: true } wm)
                Add(nameof(StaffRecord.MonthsToRetirement), FilterOperator.Between, 0, wm.Groups[1].Value);
            else if (YearNumber().Match(n) is { Success: true } ry)
                Add(nameof(StaffRecord.RetirementYear), FilterOperator.Equals, ry.Groups[1].Value);
            else if (n.Contains("nam sau") || n.Contains("nam toi"))
                Add(nameof(StaffRecord.RetirementYear), FilterOperator.Equals, thisYear + 1);
            else
                Add(nameof(StaffRecord.RetirementYear), FilterOperator.Equals, thisYear);
        }

        // Appointment expiry
        if (n.Contains("het han") && WithinDays().Match(n) is { Success: true } wd)
        {
            // "hết hạn trong 60 ngày tới"
            Add(nameof(StaffRecord.DaysToTermEnd), FilterOperator.Between, 0, wd.Groups[1].Value);
        }
        else if (n.Contains("sap het han") || n.Contains("sap het nhiem ky"))
        {
            // "sắp hết hạn bổ nhiệm" → the reminder window (100 days)
            Add(nameof(StaffRecord.DaysToTermEnd), FilterOperator.Between, 0, 100);
        }
        else if (n.Contains("het han"))
        {
            var y = YearNumber().Match(n) is { Success: true } ey ? int.Parse(ey.Groups[1].Value)
                : n.Contains("nam sau") || n.Contains("nam toi") ? thisYear + 1 : thisYear;
            Add(nameof(StaffRecord.AppointExpiryYear), FilterOperator.Equals, y);
        }

        // Status
        if (n.Contains("da thoi") || n.Contains("thoi giu")) Add(nameof(StaffRecord.Status), FilterOperator.Contains, "thôi");
        if (n.Contains("kiem nhiem")) Add(nameof(StaffRecord.IsConcurrent), FilterOperator.IsTrue);
        if (n.Contains("bo nhiem lai") || n.Contains("da bn lai")) Add(nameof(StaffRecord.IsReappointed), FilterOperator.IsTrue);

        // Position titles
        if (PositionTitle().Match(n) is { Success: true } pt)
        {
            var title = pt.Value.Trim();
            Add(nameof(StaffRecord.Position), title.StartsWith("pho ") ? FilterOperator.Contains : FilterOperator.StartsWith, title);
        }

        // Department: "thuộc / tại / đơn vị <name>" up to the next keyword
        if (Department().Match(lower) is { Success: true } dm)
        {
            var dept = LeadingUnitWord().Replace(dm.Groups[1].Value, "").Trim(' ', ',', '.');
            if (dept.Length > 1) Add(nameof(StaffRecord.Department), FilterOperator.Contains, dept);
        }
        return set;
    }

    [GeneratedRegex(@"\b(nữ|nu)\b(?!\s*\d)")]
    private static partial Regex Female();

    [GeneratedRegex(@"\b(nam|nam giới|đàn ông)\b(?!\s*\d)(?!\s*(nay|sau|tới|toi|trước))")]
    private static partial Regex Male();

    // "từ 20 đến 40 tuổi", "20-40 tuổi", "tuổi từ 20 đến 40"
    [GeneratedRegex(@"(?<a>\d{2})\s*(?:den|toi|-|–)\s*(?<b>\d{2})\s*tuoi|tuoi\s*(?:tu\s*)?(?<a>\d{2})\s*(?:den|toi|-|–)\s*(?<b>\d{2})")]
    private static partial Regex AgeRange();

    // "trên 50 tuổi", "tuổi trên 50", "từ 30 tuổi trở lên", "tuổi từ 30 trở lên", "≥ 30 tuổi"
    [GeneratedRegex(@"(?:tren|lon hon|hon|>)\s*(?<gt>\d{2})\s*tuoi|tuoi\s*(?:tren|lon hon|>)\s*(?<gt>\d{2})|(?<ge>\d{2})\s*(?:tuoi\s*)?tro len|(?:>=|≥)\s*(?<ge>\d{2})\s*tuoi|tuoi\s*(?:>=|≥)\s*(?<ge>\d{2})")]
    private static partial Regex AgeMin();

    [GeneratedRegex(@"(?:duoi|nho hon|<)\s*(?<lt>\d{2})\s*tuoi|tuoi\s*(?:duoi|nho hon|<)\s*(?<lt>\d{2})|(?<le>\d{2})\s*(?:tuoi\s*)?tro xuong|(?:<=|≤)\s*(?<le>\d{2})\s*tuoi|tuoi\s*(?:<=|≤)\s*(?<le>\d{2})")]
    private static partial Regex AgeMax();

    [GeneratedRegex(@"\b(khong|chua)\s+(la\s+)?dang vien")]
    private static partial Regex NotParty();

    [GeneratedRegex(@"\bpgs\b|pho giao su")]
    private static partial Regex Pgs();

    [GeneratedRegex(@"\bgs\b|giao su")]
    private static partial Regex Gs();

    [GeneratedRegex(@"\bts\b|tien si")]
    private static partial Regex Ts();

    [GeneratedRegex(@"\bths\b|thac si")]
    private static partial Regex Ths();

    [GeneratedRegex(@"trong\s+(\d{1,3})\s+thang")]
    private static partial Regex WithinMonths();

    [GeneratedRegex(@"trong\s+(\d{1,4})\s+ngay")]
    private static partial Regex WithinDays();

    [GeneratedRegex(@"\b(20\d{2}|19\d{2})\b")]
    private static partial Regex YearNumber();

    [GeneratedRegex(@"(pho\s+)?(hieu truong|giam doc|vien truong|truong khoa|truong phong|truong bo mon|truong ban|ke toan truong|truong labo)")]
    private static partial Regex PositionTitle();

    [GeneratedRegex(@"^\s*(?:đơn vị|don vi)\s+")]
    private static partial Regex LeadingUnitWord();

    [GeneratedRegex(@"(?:thuộc|thuoc|tại|tai|đơn vị|don vi)\s+(.+?)(?=\s+(?:là|la|có|co|từ|tu|trên|tren|dưới|duoi|nam|nữ|nu|sinh|đảng|dang|nghỉ|nghi|hết|het|tuổi|tuoi)\b|[,;]|$)")]
    private static partial Regex Department();
}
