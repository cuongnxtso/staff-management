using System.Text.RegularExpressions;
using StaffManagement.Models;

namespace StaffManagement.Services;

/// <summary>
/// Offline, rule-based extraction from Vietnamese appointment decisions, e.g.
/// "Số: 3208/QĐ-ĐHYHN ... Hà Nội, ngày 21 tháng 5 năm 2026 ... Điều 1. Bổ nhiệm ông Nguyễn Mạnh Hà,
/// sinh ngày 20/08/1974, ... giữ chức vụ Trưởng phòng Quản lý Đào tạo Sau đại học, kể từ ngày 01/6/2026.
/// Thời hạn giữ chức vụ là 05 năm".
/// </summary>
public static partial class DecisionParser
{
    /// <summary>Titles recognised at the start of the "giữ chức vụ ..." text (longest first) and their list form.</summary>
    private static readonly (string Title, string ListForm, string? DeptPrefix)[] Titles =
    [
        ("phó trưởng bộ môn", "P. Trưởng BM", null),
        ("trưởng bộ môn", "Trưởng BM", null),
        ("phó trưởng phòng", "P. Trưởng phòng", "Phòng"),
        ("trưởng phòng", "Trưởng phòng", "Phòng"),
        ("phó trưởng khoa", "P. Trưởng khoa", "Khoa"),
        ("trưởng khoa", "Trưởng khoa", "Khoa"),
        ("phó trưởng ban", "Phó Trưởng ban", "Ban"),
        ("trưởng ban", "Trưởng ban", "Ban"),
        ("phó trưởng labo", "Phó Trưởng Labo", "Labo"),
        ("trưởng labo", "Trưởng Labo", "Labo"),
        ("phó chánh văn phòng", "Phó Chánh VP", "Văn phòng"),
        ("chánh văn phòng", "Chánh VP", "Văn phòng"),
        ("phó hiệu trưởng", "Phó Hiệu trưởng", null),
        ("hiệu trưởng", "Hiệu trưởng", null),
        ("phó viện trưởng", "Phó Viện trưởng", null),
        ("viện trưởng", "Viện trưởng", null),
        ("phó giám đốc", "Phó Giám đốc", null),
        ("giám đốc", "Giám đốc", null),
        ("kế toán trưởng", "Kế toán trưởng", null),
        ("trưởng thư viện", "Trưởng Thư viện", null),
        ("phụ trách", "Phụ trách", null),
    ];

    public static DecisionInfo Parse(string text)
    {
        var flat = Spaces().Replace(text, " ").Trim();
        var info = new DecisionInfo();

        // Number and signing date are only taken from the header (before the "QUYẾT ĐỊNH" title): the "Căn cứ"
        // part cites other decisions and laws ("Nghị định số 115/2020/NĐ-CP ngày 25/9/2020") that must not be picked up.
        int titleAt = flat.IndexOf("QUYẾT ĐỊNH", StringComparison.CurrentCulture);
        var header = titleAt > 0 ? flat[..titleAt] : flat[..Math.Min(flat.Length, 600)];
        var num = NumberRegex().Match(header);
        if (num.Success) info.DecisionNumber = $"{num.Groups[1].Value}/{Spaces().Replace(num.Groups[2].Value, "")}";
        else info.Warnings.Add("Không đọc được số quyết định (có thể số được đóng sau khi ký, dạng ảnh) — hãy nhập tay theo bản gốc.");
        var date = WordDate().Match(header);
        if (date.Success && VietnameseText.TryDate(date.Groups[3].Value, date.Groups[2].Value, date.Groups[1].Value, out var dd))
            info.DecisionDate = dd;
        else info.Warnings.Add("Không đọc được ngày ký quyết định — hãy nhập tay theo bản gốc.");

        // Issuer from "/QĐ-VĐTRHM" even when the number itself is blank
        (info.AppointingUnit, info.AppointingLevel) = UnitFromNumber(info.DecisionNumber ?? header);

        // Main article: "Điều 1 ... Điều 2"
        var article = Article().Match(flat) is { Success: true } a ? a.Groups[1].Value : AfterDecide(flat);
        var na = VietnameseText.Normalize(article);
        info.Kind = na.Contains("thoi giu chuc vu") || na.Contains("mien nhiem") || na.Contains("cho thoi")
            ? DecisionKind.Dismiss
            : na.Contains("bo nhiem lai") ? DecisionKind.Reappoint
            : !na.Contains("bo nhiem") && (na.Contains("phu trach") || na.Contains("giao nhiem vu")) ? DecisionKind.Assign
            : DecisionKind.Appoint;

        // Person
        var person = PersonRegex().Match(article);
        if (person.Success)
        {
            info.FullName = person.Groups[2].Value.Trim();
            info.Gender = person.Groups[1].Value.Equals("bà", StringComparison.OrdinalIgnoreCase) ? "Nữ"
                : person.Groups[1].Value.Equals("ông", StringComparison.OrdinalIgnoreCase) ? "Nam" : null;
        }
        else if (PersonAfterVerb().Match(article) is { Success: true } p2)
        {
            info.FullName = p2.Groups[1].Value.Trim();
        }

        var personTail = person.Success ? article[person.Index..] : article;
        var dob = DobNumeric().Match(personTail);
        if (dob.Success && VietnameseText.TryDate(dob.Groups[3].Value, dob.Groups[2].Value, dob.Groups[1].Value, out var b))
            info.DateOfBirth = b;
        else if (DobWords().Match(personTail) is { Success: true } dw && VietnameseText.TryDate(dw.Groups[3].Value, dw.Groups[2].Value, dw.Groups[1].Value, out b))
            info.DateOfBirth = b;

        var near = personTail.Length > 400 ? personTail[..400] : personTail;
        if (StaffCodeRegex().Match(near) is { Success: true } code) info.StaffCode = code.Groups[1].Value;
        if (ProfessionalTitleRegex().Match(near) is { Success: true } pt)
            info.ProfessionalTitle = $"{pt.Groups[1].Value}{pt.Groups[2].Value} (hạng {pt.Groups[3].Value.ToUpperInvariant()})";
        info.AcademicRank = PgsRegex().IsMatch(near) ? "PGS" : GsRegex().IsMatch(near) ? "GS" : null;
        info.Degree = TsRegex().IsMatch(near) ? "TS" : ThsRegex().IsMatch(near) ? "ThS" : CkRegex().IsMatch(near) ? "BSCKII" : null;

        var home = HomeUnitRegex().Match(near);
        if (home.Success) info.HomeUnit = home.Groups[1].Value.Trim();

        // Position
        var pos = PositionRegex().Match(article);
        if (pos.Success)
        {
            info.PositionText = pos.Groups[1].Value.Trim();
            (info.Position, info.Department) = SplitPosition(info.PositionText);
        }

        // Effective date and term. The start is often in Điều 2 ("Thời hạn giữ chức vụ là 05 năm kể từ ngày 01/7/2026"),
        // so look at the whole operative part, preferring the date attached to "thời hạn".
        var operative = AfterDecide(flat);
        foreach (var (regex, scope) in new[] { (TermStartNumeric(), operative), (EffectiveNumeric(), article), (EffectiveNumeric(), operative),
                                               (EffectiveWords(), article), (EffectiveWords(), operative) })
        {
            if (regex.Match(scope) is { Success: true } m && VietnameseText.TryDate(m.Groups[3].Value, m.Groups[2].Value, m.Groups[1].Value, out var e))
            {
                info.EffectiveDate = e;
                break;
            }
        }

        var term = TermRegex().Match(flat);
        if (term.Success) info.TermYears = int.Parse(term.Groups[1].Value);
        return info;
    }

    /// <summary>"Trưởng phòng Quản lý Đào tạo Sau đại học, Trường ĐHYHN" → ("Trưởng phòng", "Phòng Quản lý Đào tạo Sau đại học").</summary>
    public static (string? Position, string? Department) SplitPosition(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return (null, null);
        var t = text.Trim();
        foreach (var (title, listForm, prefix) in Titles)
        {
            if (!t.StartsWith(title, StringComparison.CurrentCultureIgnoreCase)) continue;
            var rest = t[title.Length..].Trim(' ', ',', '-', '–');
            if (rest.Length == 0) return (listForm, null);
            var dept = prefix is null || rest.StartsWith(prefix, StringComparison.CurrentCultureIgnoreCase) ? rest : $"{prefix} {rest}";
            return (listForm, dept);
        }
        return (t, null);
    }

    public static (string? Unit, string? Level) UnitFromNumber(string? number)
    {
        if (string.IsNullOrWhiteSpace(number)) return (null, null);
        var m = IssuerRegex().Match(number);
        if (!m.Success) return (null, null);
        var issuer = m.Groups[1].Value.Trim('-', ' ');
        return VietnameseText.Normalize(issuer) switch
        {
            "byt" => ("Bộ Y tế", "BYT"),
            "dhyhn" => ("ĐHYHN", "Trường"),
            _ => (issuer, "Đơn vị"),
        };
    }

    private static string AfterDecide(string flat)
    {
        var idx = flat.LastIndexOf("QUYẾT ĐỊNH", StringComparison.CurrentCulture);
        return idx >= 0 ? flat[idx..] : flat;
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    // "Số: 418 /QĐ-VĐTRHM" — the number must be followed by QĐ (not "số 115/2020/NĐ-CP")
    [GeneratedRegex(@"Số\s*[:.]?\s*(\d+)\s*/\s*(Q[ĐD](?:\s*[-–]\s*[\p{L}\d&]+)+)", RegexOptions.IgnoreCase)]
    private static partial Regex NumberRegex();

    [GeneratedRegex(@"\(\s*(?:MCB|MSCB|Mã\s*CB|Mã\s+cán\s+bộ|Mã\s+số(?:\s+cán\s+bộ)?)\s*[:.]?\s*([A-Za-z]{0,3}\d{3,})\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex StaffCodeRegex();

    // "Giảng viên chính (Hạng II)" → Chức danh nghề nghiệp
    [GeneratedRegex(@"(Giảng viên|Bác sĩ|Dược sĩ|Chuyên viên|Nghiên cứu viên|Kỹ sư|Điều dưỡng|Kỹ thuật viên|Kế toán viên)(\s+(?:cao cấp|chính))?\s*\(\s*hạng\s+([IV]+)\s*\)", RegexOptions.IgnoreCase)]
    private static partial Regex ProfessionalTitleRegex();

    [GeneratedRegex(@"thời\s+hạn[^.]{0,120}?từ\s+ngày\s+(\d{1,2})\s*[/.-]\s*(\d{1,2})\s*[/.-]\s*(\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex TermStartNumeric();

    [GeneratedRegex(@"/\s*Q[ĐD]\s*[-–]\s*([\p{L}\d&\-–]+)", RegexOptions.IgnoreCase)]
    private static partial Regex IssuerRegex();

    [GeneratedRegex(@"ngày\s+(\d{1,2})\s+tháng\s+(\d{1,2})\s+năm\s+(\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex WordDate();

    [GeneratedRegex(@"Điều\s*1\s*[.:]?(.+?)(?=Điều\s*2\b|$)", RegexOptions.IgnoreCase)]
    private static partial Regex Article();

    // Names: 2–6 capitalised words. Case-insensitivity is limited to the keywords so \p{Lu} stays strict.
    [GeneratedRegex(@"\b(?i:(ông|bà|đồng chí))\s+((?:\p{Lu}\p{Ll}*\s+){1,5}\p{Lu}\p{Ll}*)")]
    private static partial Regex PersonRegex();

    [GeneratedRegex(@"(?i:bổ nhiệm(?: lại)?|giao|cử)\s+(?:(?:GS|PGS|TS|ThS|BS|BSCKII|CKII)\.?\s*)*((?:\p{Lu}\p{Ll}*\s+){1,5}\p{Lu}\p{Ll}*)")]
    private static partial Regex PersonAfterVerb();

    [GeneratedRegex(@"sinh\s+(?:ngày\s+)?(\d{1,2})\s*[/.-]\s*(\d{1,2})\s*[/.-]\s*(\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex DobNumeric();

    [GeneratedRegex(@"sinh\s+ngày\s+(\d{1,2})\s+tháng\s+(\d{1,2})\s+năm\s+(\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex DobWords();

    [GeneratedRegex(@"\bPhó\s+Giáo\s+sư\b|\bPGS\b", RegexOptions.IgnoreCase)]
    private static partial Regex PgsRegex();

    [GeneratedRegex(@"\bGiáo\s+sư\b|\bGS\b", RegexOptions.IgnoreCase)]
    private static partial Regex GsRegex();

    [GeneratedRegex(@"\bTiến\s+sĩ\b|\bTS\b", RegexOptions.IgnoreCase)]
    private static partial Regex TsRegex();

    [GeneratedRegex(@"\bThạc\s+sĩ\b|\bThS\b", RegexOptions.IgnoreCase)]
    private static partial Regex ThsRegex();

    [GeneratedRegex(@"chuyên\s+khoa\s+(?:cấp\s+)?II\b|\bBSCKII\b|\bCKII\b", RegexOptions.IgnoreCase)]
    private static partial Regex CkRegex();

    // "giảng viên chính (Hạng II) Trung tâm Công nghệ 3D … giữ chức vụ …" → the unit, up to "giữ chức vụ" / punctuation
    [GeneratedRegex(@"(?:giảng viên|viên chức|chuyên viên|bác sĩ|nghiên cứu viên)(?:\s+(?:cao cấp|chính))?(?:\s*\([^)]*\))?\s*,?\s*((?:Bộ môn|Phòng|Khoa|Viện|Trung tâm|Văn phòng|Labo)\s.+?)(?=\s*(?:[,;]|\.\s|\bgiữ\s+chức\s+vụ\b|\bkể từ\b|$))", RegexOptions.IgnoreCase)]
    private static partial Regex HomeUnitRegex();

    [GeneratedRegex(@"giữ\s+chức\s+vụ\s+(.+?)(?=\s*(?:[,;]|\.\s|\.$|\bkể từ\b|\btừ ngày\b|\bnhiệm kỳ\b|\bthời hạn\b|\bđối với\b|$))", RegexOptions.IgnoreCase)]
    private static partial Regex PositionRegex();

    [GeneratedRegex(@"từ\s+ngày\s+(\d{1,2})\s*[/.-]\s*(\d{1,2})\s*[/.-]\s*(\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex EffectiveNumeric();

    [GeneratedRegex(@"từ\s+ngày\s+(\d{1,2})\s+tháng\s+(\d{1,2})\s+năm\s+(\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex EffectiveWords();

    [GeneratedRegex(@"thời\s+hạn[^.]{0,80}?(\d{1,2})\s*\(?[\p{L}\s]*\)?\s*năm", RegexOptions.IgnoreCase)]
    private static partial Regex TermRegex();
}
