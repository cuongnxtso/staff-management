using System.Text.RegularExpressions;
using StaffManagement.Models;

namespace StaffManagement.Services;

public sealed class ExcelImportResult
{
    public List<StaffRecord> Records { get; } = [];
    public List<string> Warnings { get; } = [];
    public string? OrganizationName { get; set; }
    public string? Title { get; set; }
    /// <summary>Field keys in the file's column order, so an export can use the same layout.</summary>
    public List<string> ColumnOrder { get; } = [];
    public int ActiveCount => Records.Count(r => !r.IsFormer);
    public int FormerCount => Records.Count(r => r.IsFormer);
}

/// <summary>Reads / writes the "DANH SÁCH CÁN BỘ LÃNH ĐẠO, QUẢN LÝ" template (two-level header, former-staff section).</summary>
public static partial class ExcelService
{
    public const string DefaultOrganization = "TRƯỜNG ĐẠI HỌC Y HÀ NỘI";
    public const string DefaultTitle = "DANH SÁCH CÁN BỘ LÃNH ĐẠO, QUẢN LÝ THUỘC TRƯỜNG";
    public const string FormerSectionTitle = "THÔI GIỮ CHỨC VỤ LÃNH ĐẠO QUẢN LÝ";

    private static readonly HashSet<string> PartyDateKeys = [nameof(StaffRecord.PartyJoinDate), nameof(StaffRecord.PartyOfficialDate)];

    // ------------------------------------------------------------------ Import

    public static ExcelImportResult Import(string path)
    {
        var sheet = XlsxReader.ReadFirstSheet(path);
        var result = new ExcelImportResult();

        int headerRow = FindHeaderRow(sheet)
            ?? throw new InvalidDataException("Không tìm thấy dòng tiêu đề có cột \"Họ và tên\" trong 30 dòng đầu của trang tính.");

        // Title rows above the header
        var titles = Enumerable.Range(1, headerRow - 1)
            .Select(r => sheet.Text(r, 1)).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        result.OrganizationName = titles.ElementAtOrDefault(0);
        result.Title = titles.ElementAtOrDefault(1);

        var subRowCandidates = FieldCatalog.ExcelFields.Where(f => f.ExcelSub is not null)
            .Select(f => VietnameseText.Normalize(f.ExcelSub)).ToHashSet();
        bool twoLevel = Enumerable.Range(1, sheet.MaxCol)
            .Any(c => subRowCandidates.Contains(VietnameseText.Normalize(sheet.Text(headerRow + 1, c))));

        var (map, extras) = MapColumns(sheet, headerRow, twoLevel, result.Warnings);
        if (!map.ContainsKey(nameof(StaffRecord.FullName)))
            throw new InvalidDataException("Không xác định được cột \"Họ và tên\".");
        result.ColumnOrder.AddRange(map.Select(m => (Col: m.Value, m.Key))
            .Concat(extras.Select(e => (e.Col, Key: FieldCatalog.ExtraKey(e.Header))))
            .OrderBy(x => x.Col).Select(x => x.Key));

        int nameCol = map[nameof(StaffRecord.FullName)];
        bool former = false;
        int first = headerRow + (twoLevel ? 2 : 1);

        for (int row = first; row <= sheet.MaxRow; row++)
        {
            var name = sheet.Text(row, nameCol);
            if (string.IsNullOrWhiteSpace(name))
            {
                // Section separator row such as "THÔI GIỮ CHỨC VỤ LÃNH ĐẠO QUẢN LÝ 2026"
                var rowText = string.Join(" ", Enumerable.Range(1, Math.Min(sheet.MaxCol, 15)).Select(c => sheet.Text(row, c)));
                if (VietnameseText.Normalize(rowText).Contains("thoi giu chuc vu")) former = true;
                continue;
            }

            var record = new StaffRecord { IsFormer = former, UpdatedAt = DateTime.Now };
            foreach (var field in FieldCatalog.ExcelFields)
            {
                if (!map.TryGetValue(field.Key, out var col) || sheet[row, col] is not { } cell) continue;
                AssignCell(record, field, cell);
            }
            foreach (var (col, header) in extras) record.SetExtra(header, sheet.Text(row, col));

            // Birth date missing / unparseable: rebuild from the day / month / year columns.
            if (record.DateOfBirth is null
                && map.TryGetValue(nameof(StaffRecord.BirthYear), out var yc) && sheet[row, yc]?.Number is { } y)
            {
                int m = map.TryGetValue(nameof(StaffRecord.BirthMonth), out var mc) && sheet[row, mc]?.Number is { } mm ? (int)mm : 1;
                int d = map.TryGetValue(nameof(StaffRecord.BirthDay), out var dc) && sheet[row, dc]?.Number is { } dd ? (int)dd : 1;
                if (VietnameseText.TryDate(((int)y).ToString(), m.ToString(), d.ToString(), out var dob)) record.DateOfBirth = dob;
            }
            if (record.DateOfBirth is null)
                result.Warnings.Add($"Dòng {row}: không đọc được ngày sinh của \"{record.FullName}\".");

            result.Records.Add(record);
        }
        result.Warnings.AddRange(PersonIdentity.CheckRepeatFlags(result.Records));
        return result;
    }

    private static void AssignCell(StaffRecord record, FieldDefinition field, XlsxCell cell)
    {
        switch (field.Key)
        {
            case nameof(StaffRecord.YearsInPosition):
                record.StoredYearsInPosition = cell.Number is { } a ? (int)a : VietnameseText.ParseInt(cell.Text);
                return;
            case nameof(StaffRecord.YearsInTerm):
                record.StoredYearsInTerm = cell.Number is { } b ? (int)b : VietnameseText.ParseInt(cell.Text);
                return;
        }
        if (field.Set is null) return;

        object? value = field.Kind switch
        {
            FieldKind.Date => cell.AsDate,
            FieldKind.Number => cell.Number is { } n ? (int)Math.Round(n) : VietnameseText.ParseInt(cell.Text),
            FieldKind.Bool => VietnameseText.ParseFlag(cell.AsString()),
            _ => PartyDateKeys.Contains(field.Key) && cell.Number is { } serial && VietnameseText.FromExcelSerial(serial) is { } pd
                ? VietnameseText.FormatDate(pd)
                : cell.AsString(),
        };
        field.Set(record, value);
    }

    private static int? FindHeaderRow(XlsxSheet sheet)
    {
        for (int r = 1; r <= Math.Min(30, sheet.MaxRow); r++)
            for (int c = 1; c <= sheet.MaxCol; c++)
                if (VietnameseText.Normalize(sheet.Text(r, c)) == "ho va ten") return r;
        return null;
    }

    /// <summary>Template field → column, plus the other headed columns (kept as <see cref="FieldCatalog.Extras"/>).</summary>
    private static (Dictionary<string, int> Map, List<(int Col, string Header)> Extras) MapColumns(
        XlsxSheet sheet, int headerRow, bool twoLevel, List<string> warnings)
    {
        int maxCol = Math.Max(sheet.MaxCol, FieldCatalog.ExcelFields.Max(f => XlsxReader.ColumnIndex(f.ExcelColumn!)));
        var rawTop = new string[maxCol + 1];
        var rawSub = new string[maxCol + 1];
        for (int c = 1; c <= maxCol; c++)
        {
            rawTop[c] = sheet.Text(headerRow, c)?.Trim() ?? "";
            rawSub[c] = twoLevel ? sheet.Text(headerRow + 1, c)?.Trim() ?? "" : "";
        }
        // Horizontally merged top headers apply to every column of the range.
        foreach (var (r1, c1, r2, c2) in sheet.Merges)
            if (r1 <= headerRow && r2 >= headerRow && c2 > c1)
                for (int c = c1 + 1; c <= Math.Min(c2, maxCol); c++)
                    if (rawTop[c].Length == 0) rawTop[c] = rawTop[c1];
        // A sub-header without a top header belongs to the group on its left (e.g. Z "Hết hạn BN" under "Thời điểm").
        for (int c = 2; c <= maxCol; c++)
            if (rawTop[c].Length == 0 && rawSub[c].Length > 0) rawTop[c] = rawTop[c - 1];
        var top = rawTop.Select(t => VietnameseText.Normalize(t)).ToArray();
        var sub = rawSub.Select(s => VietnameseText.Normalize(s)).ToArray();

        var map = new Dictionary<string, int>();
        var used = new HashSet<int>();
        foreach (var f in FieldCatalog.ExcelFields)
        {
            var t = VietnameseText.Normalize(f.ExcelTop);
            var s = VietnameseText.Normalize(f.ExcelSub);
            var h = VietnameseText.Normalize(f.Header);
            var a = VietnameseText.Normalize(f.ExcelAlias);
            for (int c = 1; c <= maxCol; c++)
            {
                if (used.Contains(c)) continue;
                bool match = (top[c] == t && sub[c] == s) || (sub[c].Length == 0 && top[c].Length > 0 && (top[c] == h || top[c] == a));
                if (!match) continue;
                map[f.Key] = c;
                used.Add(c);
                break;
            }
        }
        // Columns without a header in the template (e.g. AM, AN): fall back to the default letter.
        foreach (var f in FieldCatalog.ExcelFields.Where(f => !map.ContainsKey(f.Key)))
        {
            int c = XlsxReader.ColumnIndex(f.ExcelColumn!);
            if (!used.Contains(c) && top[c].Length == 0 && sub[c].Length == 0)
            {
                map[f.Key] = c;
                used.Add(c);
            }
            else if (!f.IsComputed)
            {
                warnings.Add($"Không tìm thấy cột \"{f.ExcelTop}{(f.ExcelSub is null ? "" : " / " + f.ExcelSub)}\" — bỏ qua.");
            }
        }

        var extras = new List<(int, string)>();
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int c = 1; c <= sheet.MaxCol; c++)
        {
            if (used.Contains(c) || (top[c].Length == 0 && sub[c].Length == 0)) continue;
            var label = rawSub[c].Length == 0 ? rawTop[c] : $"{rawTop[c]} - {rawSub[c]}";
            var header = label;
            for (int n = 2; !names.Add(header); n++) header = $"{label} ({n})";
            extras.Add((c, header));
        }
        if (extras.Count > 0)
            warnings.Add($"Cột bổ sung (không có trong mẫu, nhập dạng văn bản): {string.Join(", ", extras.Select(e => $"\"{e.Item2}\""))}.");
        return (map, extras);
    }

    // ------------------------------------------------------------------ Export

    /// <summary>
    /// The columns in the order of the last imported file. Columns the file did not have go to the end;
    /// with <paramref name="dropEmpty"/> they are left out when no row has a value in them.
    /// </summary>
    public static List<FieldDefinition> InFileOrder(IEnumerable<FieldDefinition> columns, IReadOnlyList<string>? order,
        IReadOnlyCollection<StaffRecord> rows, bool dropEmpty)
    {
        if (order is null) return columns.ToList();
        var position = order.Select((key, i) => (key, i)).ToDictionary(x => x.key, x => x.i);
        return columns
            .Where(f => !dropEmpty || position.ContainsKey(f.Key) || rows.Any(r => f.Get(r) is not (null or "" or false)))
            .OrderBy(f => position.GetValueOrDefault(f.Key, int.MaxValue))
            .ToList();
    }

    public static void Export(string path, IEnumerable<StaffRecord> records, IReadOnlyList<FieldDefinition> columns,
        string? organization, string? title)
    {
        var list = records.ToList();
        var active = list.Where(r => !r.IsFormer).ToList();
        var former = list.Where(r => r.IsFormer).ToList();
        var today = DateTime.Today;

        var w = new XlsxWriter { SheetName = $"LĐ. QL {today:dd.MM.yyyy}" };
        int lastCol = columns.Count;

        w.Set(1, 1, string.IsNullOrWhiteSpace(organization) ? DefaultOrganization : organization, XlsxWriter.Style.Org);
        w.Merge(1, 1, 1, Math.Min(5, lastCol));
        w.Set(2, 1, BuildTitle(title, today), XlsxWriter.Style.Title);
        w.Merge(2, 1, 2, lastCol);
        w.SetHeight(2, 24);

        const int h1 = 4, h2 = 5;
        for (int i = 0; i < columns.Count;)
        {
            var f = columns[i];
            int c = i + 1;
            var topText = f.ExcelTop ?? f.Header;
            if (f.ExcelSub is null)
            {
                w.Set(h1, c, topText, XlsxWriter.Style.Header);
                w.Set(h2, c, "", XlsxWriter.Style.Header);
                w.Merge(h1, c, h2, c);
                i++;
                continue;
            }
            int j = i;
            while (j < columns.Count && columns[j].ExcelTop == f.ExcelTop && columns[j].ExcelSub is not null)
            {
                w.Set(h1, j + 1, j == i ? topText : "", XlsxWriter.Style.Header);
                w.Set(h2, j + 1, columns[j].ExcelSub, XlsxWriter.Style.Header);
                j++;
            }
            w.Merge(h1, c, h1, j);
            i = j;
        }
        w.SetHeight(h1, 32);
        w.SetHeight(h2, 32);
        for (int i = 0; i < columns.Count; i++)
            w.SetWidth(i + 1, Math.Clamp(columns[i].Width / 6.5, 5, 45));

        int row = h2 + 1;
        row = WriteRows(w, row, active, columns);
        if (former.Count > 0)
        {
            row += 2;
            w.Set(row, 1, $"{FormerSectionTitle} {today.Year}", XlsxWriter.Style.Section);
            for (int c = 2; c <= lastCol; c++) w.Set(row, c, "", XlsxWriter.Style.Section);
            w.Merge(row, 1, row, Math.Min(13, lastCol));
            WriteRows(w, row + 1, former, columns);
        }

        w.FreezeRows = h2;
        w.FreezeCols = Math.Min(columns.Count, columns.ToList().FindIndex(f => f.Key == nameof(StaffRecord.FullName)) + 1);
        w.Save(path);
    }

    private static int WriteRows(XlsxWriter w, int row, List<StaffRecord> records, IReadOnlyList<FieldDefinition> columns)
    {
        int seq = 1;
        foreach (var r in records)
        {
            for (int i = 0; i < columns.Count; i++)
            {
                var f = columns[i];
                var value = f.Key == nameof(StaffRecord.Stt) ? seq : f.Get(r);
                switch (f.Kind)
                {
                    case FieldKind.Date:
                        w.Set(row, i + 1, value is DateTime d ? d : "", XlsxWriter.Style.DateCell);
                        break;
                    case FieldKind.Number:
                        w.Set(row, i + 1, value is int n ? n : "", XlsxWriter.Style.CenterCell);
                        break;
                    case FieldKind.Bool:
                        w.Set(row, i + 1, value is true ? (f.Key == nameof(StaffRecord.IsConcurrent) ? "KN" : "x") : "", XlsxWriter.Style.CenterCell);
                        break;
                    default:
                        w.Set(row, i + 1, value?.ToString() ?? "", XlsxWriter.Style.Cell);
                        break;
                }
            }
            seq++;
            row++;
        }
        return row;
    }

    public static string BuildTitle(string? title, DateTime date)
    {
        var t = string.IsNullOrWhiteSpace(title) ? DefaultTitle : title.Trim();
        t = TrailingDate().Replace(t, "").TrimEnd();
        return $"{t} {date:dd/MM/yyyy}";
    }

    [GeneratedRegex(@"\s*\d{1,2}[/.]\d{1,2}[/.]\d{4}\s*$")]
    private static partial Regex TrailingDate();
}
