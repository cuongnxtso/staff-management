namespace StaffManagement.Services;

/// <summary>
/// Parent unit (Viện, Trung tâm, Khoa, Phân hiệu) of a "Đơn vị" value. The list writes a constituent unit as
/// "&lt;parent abbreviation&gt; - &lt;unit&gt;" (e.g. "YHCT - BM Dược YHCT"), with several spellings of the same abbreviation.
/// </summary>
public static class UnitHierarchy
{
    // ponytail: built from the units in the 2026 list; add a row here when the list uses a new parent or abbreviation.
    private static readonly (string Parent, string[] Names, string[] Prefixes)[] Parents =
    [
        ("Khoa Y học cổ truyền", ["Khoa Y học cổ truyền"], ["YHCT"]),
        ("Khoa Kỹ thuật Y học", ["Khoa Kỹ thuật Y học"], ["KTYH"]),
        ("Khoa Điều dưỡng - Hộ sinh", ["Khoa Điều dưỡng - Hộ sinh", "Khoa Điều dưỡng-Hộ sinh"], ["ĐDHS"]),
        ("Viện ĐT YHDP và YTCC", ["Viện ĐT YHDP và YTCC", "Viện ĐT YHDP&YTCC"], ["VĐTYHDP&YTCC", "Viện ĐT YHDP&YTCC"]),
        ("Viện Đào tạo RHM", ["Viện Đào tạo RHM", "Viện ĐTRHM"], ["Viện ĐTRHM", "VĐTRHM", "Viện Đào tạo RHM"]),
        ("Phân hiệu Thanh Hóa", ["Phân hiệu Thanh Hóa"], ["PH", "Phân hiệu Thanh Hóa"]),
        ("TT Đào tạo và Chuyển giao công nghệ", ["TT Đào tạo và Chuyển giao công nghệ"], ["TTĐT&CGCN"]),
        ("TT Kiểm chuẩn CL xét nghiệm y học", ["TT Kiểm chuẩn CL xét nghiệm y học"], ["TTKCCLXNYH"]),
    ];

    /// <summary>The parent unit, or the unit itself when it is not a constituent of one of the parents above.</summary>
    public static string? ParentOf(string? unit)
    {
        // Spaces are dropped so "VĐT RHM" = "VĐTRHM" and "PH - x" = "PH-x".
        var key = Key(unit);
        if (key.Length == 0) return unit;
        foreach (var (parent, names, prefixes) in Parents)
            if (names.Any(n => Key(n) == key) || prefixes.Any(p => key.StartsWith(Key(p) + "-")))
                return parent;
        return unit;
    }

    private static string Key(string? s) => VietnameseText.Normalize(s).Replace(" ", "");
}
