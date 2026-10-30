namespace StaffManagement.Models;

public enum FieldKind { Text, Number, Date, Bool }

/// <summary>Metadata of one column: display name, type, Excel mapping and accessors.</summary>
public sealed class FieldDefinition
{
    public required string Key { get; init; }
    public required string Header { get; init; }
    public required FieldKind Kind { get; init; }
    public required Func<StaffRecord, object?> Get { get; init; }
    /// <summary>Null for computed (read-only) fields.</summary>
    public Action<StaffRecord, object?>? Set { get; init; }
    public string? Group { get; init; }
    /// <summary>Default column letter in the Excel template (null = not exported).</summary>
    public string? ExcelColumn { get; init; }
    public string? ExcelTop { get; init; }
    public string? ExcelSub { get; init; }
    /// <summary>Another header the importer accepts for this column (e.g. "Hạng" for Chức danh nghề nghiệp).</summary>
    public string? ExcelAlias { get; init; }
    public bool DefaultVisible { get; init; }
    public double Width { get; init; } = 110;
    public bool Multiline { get; init; }

    public bool IsComputed => Set is null;
    public override string ToString() => Header;
}

public static class FieldCatalog
{
    public const string GroupGeneral = "Thông tin chung";
    public const string GroupQualification = "Trình độ & Đảng";
    public const string GroupPosition = "Chức vụ & Bổ nhiệm";
    public const string GroupContact = "Liên hệ & Ghi chú";
    public const string GroupExtra = "Cột bổ sung (từ Excel)";
    public const string ExtraKeyPrefix = "Extra:";

    private static readonly IReadOnlyList<FieldDefinition> Known = Build();

    /// <summary>Template columns plus the <see cref="Extras"/>.</summary>
    public static IReadOnlyList<FieldDefinition> All { get; private set; } = Known;

    /// <summary>Template columns only: what the importer looks for.</summary>
    public static IReadOnlyList<FieldDefinition> ExcelFields { get; } =
        Known.Where(f => f.ExcelColumn is not null).ToList();

    /// <summary>Text columns the user added to the Excel file (e.g. "Trưởng phòng"), taken from the rows' <see cref="StaffRecord.ExtraColumns"/>.</summary>
    public static IReadOnlyList<FieldDefinition> Extras { get; private set; } = [];

    /// <summary>Rebuilds <see cref="Extras"/> from the data, in order of first appearance. Returns false when nothing changed.</summary>
    public static bool SetExtraColumns(IEnumerable<StaffRecord> records)
    {
        var headers = records.SelectMany(r => r.ExtraValues().Keys).Distinct().ToList();
        if (headers.SequenceEqual(Extras.Select(f => f.Header))) return false;
        Extras = headers.Select(Extra).ToList();
        All = [.. Known, .. Extras];
        return true;
    }

    // No commas in keys: the visible-column and column-order settings are stored comma-separated.
    public static string ExtraKey(string header) => ExtraKeyPrefix + header.Replace(",", "");

    private static FieldDefinition Extra(string header) => new()
    {
        Key = ExtraKey(header), Header = header, Kind = FieldKind.Text, Group = GroupExtra, ExcelTop = header,
        DefaultVisible = true, Width = 120,
        Get = r => r.GetExtra(header), Set = (r, v) => r.SetExtra(header, v as string),
    };

    public static FieldDefinition? Find(string? key) =>
        All.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));

    private static FieldDefinition Text(string key, string header, Func<StaffRecord, string?> get, Action<StaffRecord, string?> set,
        string group, string col, string top, string? sub = null, bool visible = false, double width = 120, bool multiline = false,
        string? alias = null) => new()
    {
        Key = key, Header = header, Kind = FieldKind.Text, Group = group, ExcelColumn = col, ExcelTop = top, ExcelSub = sub, ExcelAlias = alias,
        DefaultVisible = visible, Width = width, Multiline = multiline,
        Get = get, Set = (r, v) => set(r, v as string),
    };

    private static FieldDefinition Number(string key, string header, Func<StaffRecord, int?> get, Action<StaffRecord, int?>? set,
        string? group, string? col, string? top, string? sub = null, bool visible = false, double width = 70) => new()
    {
        Key = key, Header = header, Kind = FieldKind.Number, Group = group, ExcelColumn = col, ExcelTop = top, ExcelSub = sub,
        DefaultVisible = visible, Width = width,
        Get = r => get(r), Set = set is null ? null : (r, v) => set(r, v as int?),
    };

    private static FieldDefinition Date(string key, string header, Func<StaffRecord, DateTime?> get, Action<StaffRecord, DateTime?>? set,
        string? group, string? col, string? top, string? sub = null, bool visible = false, double width = 95) => new()
    {
        Key = key, Header = header, Kind = FieldKind.Date, Group = group, ExcelColumn = col, ExcelTop = top, ExcelSub = sub,
        DefaultVisible = visible, Width = width,
        Get = r => get(r), Set = set is null ? null : (r, v) => set(r, v as DateTime?),
    };

    private static FieldDefinition Flag(string key, string header, Func<StaffRecord, bool> get, Action<StaffRecord, bool>? set,
        string? group, string? col, string? top, bool visible = false, double width = 60) => new()
    {
        Key = key, Header = header, Kind = FieldKind.Bool, Group = group, ExcelColumn = col, ExcelTop = top,
        DefaultVisible = visible, Width = width,
        Get = r => get(r), Set = set is null ? null : (r, v) => set(r, v is true),
    };

    private static List<FieldDefinition> Build() =>
    [
        Number(nameof(StaffRecord.Stt), "TT", r => r.Stt, (r, v) => r.Stt = v, GroupGeneral, "A", "TT", visible: true, width: 45),
        Flag(nameof(StaffRecord.IsConcurrent), "KN", r => r.IsConcurrent, (r, v) => r.IsConcurrent = v, GroupGeneral, "B", "KN", width: 45),
        Flag(nameof(StaffRecord.IsRepeat), "Lặp", r => r.IsRepeat, (r, v) => r.IsRepeat = v, GroupGeneral, "C", "Lặp", width: 45),
        Text(nameof(StaffRecord.StaffCode), "Mã CB", r => r.StaffCode, (r, v) => r.StaffCode = v, GroupGeneral, "D", "Mã CB", visible: true, width: 70),
        Text(nameof(StaffRecord.FullName), "Họ và tên", r => r.FullName, (r, v) => r.FullName = v, GroupGeneral, "E", "Họ và tên", visible: true, width: 170),
        Date(nameof(StaffRecord.DateOfBirth), "Ngày sinh", r => r.DateOfBirth, (r, v) => r.DateOfBirth = v, GroupGeneral, "F", "Ngày, tháng, năm sinh", visible: true),
        Number(nameof(StaffRecord.BirthDay), "Ngày sinh (ngày)", r => r.BirthDay, null, null, "G", "Ngày sinh", "Ngày sinh"),
        Number(nameof(StaffRecord.BirthMonth), "Tháng sinh", r => r.BirthMonth, null, null, "H", "Ngày sinh", "Tháng sinh"),
        Number(nameof(StaffRecord.BirthYear), "Năm sinh", r => r.BirthYear, null, null, "I", "Ngày sinh", "Năm sinh"),
        Number(nameof(StaffRecord.RetirementMonth), "Tháng nghỉ hưu", r => r.RetirementMonth, (r, v) => r.RetirementMonth = v, GroupGeneral, "J", "Thời điểm nghỉ hưu", "Tháng"),
        Number(nameof(StaffRecord.RetirementYear), "Năm nghỉ hưu", r => r.RetirementYear, (r, v) => r.RetirementYear = v, GroupGeneral, "K", "Thời điểm nghỉ hưu", "Năm", visible: true),
        Number(nameof(StaffRecord.Age), "Tuổi", r => r.Age, null, null, "L", "Tuổi", visible: true, width: 50),
        Text(nameof(StaffRecord.Gender), "Giới", r => r.Gender, (r, v) => r.Gender = v, GroupGeneral, "M", "Giới", visible: true, width: 50),
        Text(nameof(StaffRecord.PartyJoinDate), "Ngày kết nạp Đảng", r => r.PartyJoinDate, (r, v) => r.PartyJoinDate = v, GroupQualification, "N", "Đảng viên", "Ngày KN"),
        Text(nameof(StaffRecord.PartyOfficialDate), "Ngày chính thức Đảng", r => r.PartyOfficialDate, (r, v) => r.PartyOfficialDate = v, GroupQualification, "O", "Đảng viên", "Ngày CT"),
        Text(nameof(StaffRecord.AcademicRank), "Học hàm", r => r.AcademicRank, (r, v) => r.AcademicRank = v, GroupQualification, "P", "Học hàm", visible: true, width: 60),
        Text(nameof(StaffRecord.Degree), "Trình độ CM", r => r.Degree, (r, v) => r.Degree = v, GroupQualification, "Q", "Trình độ chuyên môn", visible: true, width: 70),
        Text(nameof(StaffRecord.PoliticalTheory), "Trình độ LLCT", r => r.PoliticalTheory, (r, v) => r.PoliticalTheory = v, GroupQualification, "R", "Trình độ LLCT"),
        Text(nameof(StaffRecord.Position), "Chức vụ", r => r.Position, (r, v) => r.Position = v, GroupPosition, "S", "Chức vụ ĐHYHN", visible: true, width: 150),
        Text(nameof(StaffRecord.Department), "Đơn vị", r => r.Department, (r, v) => r.Department = v, GroupPosition, "T", "Đơn vị", visible: true, width: 200),
        Text(nameof(StaffRecord.DecisionNumber), "Số QĐ bổ nhiệm", r => r.DecisionNumber, (r, v) => r.DecisionNumber = v, GroupPosition, "U", "Số QĐ bổ nhiệm (hiện tại)", visible: true),
        Date(nameof(StaffRecord.DecisionDate), "Ngày QĐ", r => r.DecisionDate, (r, v) => r.DecisionDate = v, GroupPosition, "V", "Ngày QĐ", visible: true),
        Number(nameof(StaffRecord.AppointDay), "BN từ ngày", r => r.AppointDay, (r, v) => r.AppointDay = v, GroupPosition, "W", "Thời điểm", "Từ ngày"),
        Number(nameof(StaffRecord.AppointMonth), "BN từ tháng", r => r.AppointMonth, (r, v) => r.AppointMonth = v, GroupPosition, "X", "Thời điểm", "Từ tháng"),
        Number(nameof(StaffRecord.AppointYear), "Năm BN", r => r.AppointYear, (r, v) => r.AppointYear = v, GroupPosition, "Y", "Thời điểm", "Năm BN"),
        Number(nameof(StaffRecord.AppointExpiryYear), "Hết hạn BN", r => r.AppointExpiryYear, (r, v) => r.AppointExpiryYear = v, GroupPosition, "Z", "Thời điểm", "Hết hạn BN", visible: true),
        Text(nameof(StaffRecord.AppointingUnit), "Đơn vị BN", r => r.AppointingUnit, (r, v) => r.AppointingUnit = v, GroupPosition, "AA", "Đơn vị BN"),
        Text(nameof(StaffRecord.AppointingLevel), "Cấp BN", r => r.AppointingLevel, (r, v) => r.AppointingLevel = v, GroupPosition, "AB", "Cấp BN", width: 70),
        Flag(nameof(StaffRecord.IsReappointed), "Đã BN lại", r => r.IsReappointed, (r, v) => r.IsReappointed = v, GroupPosition, "AC", "Đã BN lại"),
        Flag(nameof(StaffRecord.IsConcurrentPost), "Kiêm chức", r => r.IsConcurrentPost, (r, v) => r.IsConcurrentPost = v, GroupPosition, "AD", "Kiêm chức"),
        Number(nameof(StaffRecord.PositionSinceYear), "Giữ CV từ", r => r.PositionSinceYear, (r, v) => r.PositionSinceYear = v, GroupPosition, "AE", "Giữ CV từ"),
        Number(nameof(StaffRecord.YearsInPosition), "Số năm giữ CV", r => r.YearsInPosition, null, null, "AF", "Số năm giữ CV"),
        Number(nameof(StaffRecord.YearsInTerm), "Số năm giữ CV NK hiện tại", r => r.YearsInTerm, null, null, "AG", "Số năm giữ CV NK hiện tại"),
        Text(nameof(StaffRecord.HomeUnit), "Đơn vị cơ hữu", r => r.HomeUnit, (r, v) => r.HomeUnit = v, GroupPosition, "AH", "Đơn vị cơ hữu", width: 160),
        Text(nameof(StaffRecord.Notes), "Ghi chú", r => r.Notes, (r, v) => r.Notes = v, GroupContact, "AI", "Ghi chú", width: 200, multiline: true),
        Text(nameof(StaffRecord.Phone), "Điện thoại", r => r.Phone, (r, v) => r.Phone = v, GroupContact, "AJ", "Điện thoại", width: 95),
        Text(nameof(StaffRecord.AllPositions), "Tổng hợp chức vụ trong Trường", r => r.AllPositions, (r, v) => r.AllPositions = v, GroupContact, "AK", "Tổng hợp chức vụ trong Trường", width: 250, multiline: true),
        Text(nameof(StaffRecord.PracticeSitePosition), "Chức vụ tại CSTH", r => r.PracticeSitePosition, (r, v) => r.PracticeSitePosition = v, GroupContact, "AL", "Chức vụ tại CSTH (đối với LĐ, QL kiêm nhiệm)", width: 200, multiline: true),
        Text(nameof(StaffRecord.ExtraNote), "Ghi chú thêm", r => r.ExtraNote, (r, v) => r.ExtraNote = v, GroupContact, "AM", "Ghi chú thêm"),
        Text(nameof(StaffRecord.ProfessionalTitle), "Chức danh nghề nghiệp", r => r.ProfessionalTitle, (r, v) => r.ProfessionalTitle = v, GroupQualification, "AN", "Chức danh nghề nghiệp", width: 160, alias: "Hạng"),
        Text(nameof(StaffRecord.PreviousDecisions), "QĐ bổ nhiệm trước", r => r.PreviousDecisions, (r, v) => r.PreviousDecisions = v, GroupPosition, "AO", "QĐ bổ nhiệm trước", width: 250, multiline: true),

        // Derived fields (not in the Excel template) — handy for filtering
        new FieldDefinition { Key = nameof(StaffRecord.Status), Header = "Tình trạng", Kind = FieldKind.Text, Get = r => r.Status, Width = 105 },
        new FieldDefinition { Key = nameof(StaffRecord.ParentUnit), Header = "Đơn vị cấp trên", Kind = FieldKind.Text, Get = r => r.ParentUnit, Width = 200 },
        Flag(nameof(StaffRecord.IsPartyMember), "Là đảng viên", r => r.IsPartyMember, null, null, null, null),
        Date(nameof(StaffRecord.RetirementDate), "Thời điểm nghỉ hưu", r => r.RetirementDate, null, null, null, null),
        Number(nameof(StaffRecord.MonthsToRetirement), "Số tháng đến nghỉ hưu", r => r.MonthsToRetirement, null, null, null, null),
        Date(nameof(StaffRecord.EffectiveDate), "Ngày hiệu lực BN", r => r.EffectiveDate, null, null, null, null),
        Date(nameof(StaffRecord.TermEndDate), "Ngày hết nhiệm kỳ", r => r.TermEndDate, null, null, null, null),
        Number(nameof(StaffRecord.DaysToTermEnd), "Số ngày đến hết nhiệm kỳ", r => r.DaysToTermEnd, null, null, null, null),
    ];
}
