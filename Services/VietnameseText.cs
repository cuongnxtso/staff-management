using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace StaffManagement.Services;

/// <summary>Helpers for accent-insensitive Vietnamese text matching and loose date parsing.</summary>
public static partial class VietnameseText
{
    public static readonly CultureInfo Culture = CultureInfo.GetCultureInfo("vi-VN");

    /// <summary>Lower-case, remove diacritics (đ → d), collapse whitespace.</summary>
    public static string Normalize(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return string.Empty;
        var decomposed = s.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        bool lastSpace = false;
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            var c = ch == 'đ' ? 'd' : ch;
            if (char.IsWhiteSpace(c))
            {
                if (!lastSpace) sb.Append(' ');
                lastSpace = true;
                continue;
            }
            lastSpace = false;
            sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>
    /// <see cref="Normalize"/> plus expansion of the abbreviations used in the list,
    /// so "P. Trưởng BM" and "Phó Trưởng bộ môn" compare equal.
    /// </summary>
    public static string Canonical(string? s)
    {
        var n = Normalize(s);
        if (n.Length == 0) return n;
        n = AbbrevPho().Replace(n, "pho ");
        n = AbbrevBoMon().Replace(n, "bo mon");
        return WhitespaceRegex().Replace(n, " ").Trim();
    }

    public static string? CleanText(string? s)
    {
        if (s is null) return null;
        var t = WhitespaceRegex().Replace(s, " ").Trim();
        return t.Length == 0 ? null : t;
    }

    /// <summary>"Chưa" / empty → not a member; a date or "có" → member.</summary>
    public static bool IsPartyMemberText(string? s)
    {
        var n = Normalize(s);
        if (n.Length == 0) return false;
        return !(n.StartsWith("chua") || n.StartsWith("khong") || n == "0");
    }

    private static readonly string[] DateFormats =
    [
        "d/M/yyyy", "dd/MM/yyyy", "d-M-yyyy", "d.M.yyyy", "yyyy-MM-dd", "yyyy-MM-dd HH:mm:ss", "d/M/yy", "M/yyyy"
    ];

    /// <summary>Parses "20/10/1968", " 09/09/1987", "18.3.1984", ISO strings or Excel serial numbers.</summary>
    public static DateTime? ParseDate(string? s)
    {
        var t = CleanText(s);
        if (t is null) return null;
        t = t.Replace(" ", "");
        if (DateTime.TryParseExact(t, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
            return d;
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var serial))
            return FromExcelSerial(serial);
        var m = VnDateRegex().Match(s!);
        if (m.Success && TryDate(m.Groups[3].Value, m.Groups[2].Value, m.Groups[1].Value, out d)) return d;
        return null;
    }

    public static DateTime? FromExcelSerial(double serial) =>
        serial is > 1000 and < 80000 ? DateTime.FromOADate(serial).Date : null;

    public static bool TryDate(string y, string m, string d, out DateTime date)
    {
        date = default;
        if (!int.TryParse(y, out var yy) || !int.TryParse(m, out var mm) || !int.TryParse(d, out var dd)) return false;
        if (yy < 1900 || yy > 2200 || mm is < 1 or > 12 || dd < 1 || dd > DateTime.DaysInMonth(yy, mm)) return false;
        date = new DateTime(yy, mm, dd);
        return true;
    }

    public static string FormatDate(DateTime? d) => d?.ToString("dd/MM/yyyy") ?? string.Empty;

    public static int? ParseInt(string? s)
    {
        var t = CleanText(s);
        if (t is null) return null;
        if (int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) return i;
        if (double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var dbl)) return (int)Math.Round(dbl);
        return null;
    }

    /// <summary>Excel template flags: "x" / "X" / "KN" → true.</summary>
    public static bool ParseFlag(string? s)
    {
        var n = Normalize(s);
        return n.Length > 0 && n != "0" && n != "khong" && n != "false";
    }

    [GeneratedRegex(@"\bp\.\s*")]
    private static partial Regex AbbrevPho();

    [GeneratedRegex(@"\bbm\b")]
    private static partial Regex AbbrevBoMon();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(@"ngày\s+(\d{1,2})\s+tháng\s+(\d{1,2})\s+năm\s+(\d{4})", RegexOptions.IgnoreCase)]
    private static partial Regex VnDateRegex();
}
