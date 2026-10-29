using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace StaffManagement.Services;

/// <summary>A cell value read from an .xlsx file (cached values of formulas are used).</summary>
public readonly record struct XlsxCell(string? Text, double? Number, bool IsDate)
{
    public DateTime? AsDate => Number is { } n ? VietnameseText.FromExcelSerial(n) : VietnameseText.ParseDate(Text);

    public string? AsString()
    {
        if (Number is { } n)
            return IsDate && VietnameseText.FromExcelSerial(n) is { } d
                ? VietnameseText.FormatDate(d)
                : n.ToString(CultureInfo.InvariantCulture);
        return VietnameseText.CleanText(Text);
    }
}

public sealed class XlsxSheet
{
    public string Name { get; init; } = "";
    public Dictionary<(int Row, int Col), XlsxCell> Cells { get; } = [];
    public List<(int Row1, int Col1, int Row2, int Col2)> Merges { get; } = [];
    public int MaxRow { get; set; }
    public int MaxCol { get; set; }

    public XlsxCell? this[int row, int col] => Cells.TryGetValue((row, col), out var c) ? c : null;
    public string? Text(int row, int col) => this[row, col]?.AsString();
}

/// <summary>Minimal, dependency-free reader for the first worksheet of an .xlsx file.</summary>
public static partial class XlsxReader
{
    private static readonly XNamespace M = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace PR = "http://schemas.openxmlformats.org/package/2006/relationships";

    public static XlsxSheet ReadFirstSheet(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);

        var workbook = Load(zip, "xl/workbook.xml") ?? throw new InvalidDataException("Không tìm thấy workbook.xml — tệp không phải định dạng .xlsx hợp lệ.");
        var firstSheet = workbook.Root!.Element(M + "sheets")?.Elements(M + "sheet").FirstOrDefault()
                         ?? throw new InvalidDataException("Tệp Excel không có trang tính nào.");
        var relId = (string?)firstSheet.Attribute(R + "id");
        var rels = Load(zip, "xl/_rels/workbook.xml.rels");
        var target = rels?.Root!.Elements(PR + "Relationship").FirstOrDefault(e => (string?)e.Attribute("Id") == relId)?.Attribute("Target")?.Value
                     ?? "worksheets/sheet1.xml";
        target = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;

        var shared = ReadSharedStrings(zip);
        var dateStyles = ReadDateStyles(zip);
        var sheetDoc = Load(zip, target) ?? throw new InvalidDataException("Không đọc được trang tính.");

        var sheet = new XlsxSheet { Name = (string?)firstSheet.Attribute("name") ?? "" };
        foreach (var c in sheetDoc.Descendants(M + "c"))
        {
            var reference = (string?)c.Attribute("r");
            if (reference is null) continue;
            var (row, col) = ParseReference(reference);
            var type = (string?)c.Attribute("t");
            var style = (int?)c.Attribute("s") ?? 0;
            var v = c.Element(M + "v")?.Value;

            XlsxCell cell;
            switch (type)
            {
                case "s":
                    cell = new XlsxCell(int.TryParse(v, out var idx) && idx < shared.Count ? shared[idx] : null, null, false);
                    break;
                case "inlineStr":
                    cell = new XlsxCell(string.Concat(c.Descendants(M + "t").Select(t => t.Value)), null, false);
                    break;
                case "str":
                case "e":
                    cell = new XlsxCell(v, null, false);
                    break;
                case "b":
                    cell = new XlsxCell(v == "1" ? "x" : null, null, false);
                    break;
                default:
                    if (v is null) continue;
                    cell = double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var num)
                        ? new XlsxCell(null, num, dateStyles.Contains(style))
                        : new XlsxCell(v, null, false);
                    break;
            }
            if (cell.Text is null && cell.Number is null) continue;
            sheet.Cells[(row, col)] = cell;
            sheet.MaxRow = Math.Max(sheet.MaxRow, row);
            sheet.MaxCol = Math.Max(sheet.MaxCol, col);
        }

        foreach (var mc in sheetDoc.Descendants(M + "mergeCell"))
        {
            var parts = ((string?)mc.Attribute("ref"))?.Split(':');
            if (parts is not { Length: 2 }) continue;
            var (r1, c1) = ParseReference(parts[0]);
            var (r2, c2) = ParseReference(parts[1]);
            sheet.Merges.Add((r1, c1, r2, c2));
        }
        return sheet;
    }

    private static XDocument? Load(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name);
        if (entry is null) return null;
        using var s = entry.Open();
        return XDocument.Load(s);
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var doc = Load(zip, "xl/sharedStrings.xml");
        if (doc is null) return [];
        // Ignore phonetic runs (rPh) — only the visible text.
        return doc.Root!.Elements(M + "si")
            .Select(si => string.Concat(si.Descendants(M + "t")
                .Where(t => t.Parent?.Name != M + "rPh")
                .Select(t => t.Value)))
            .ToList();
    }

    private static HashSet<int> ReadDateStyles(ZipArchive zip)
    {
        var result = new HashSet<int>();
        var doc = Load(zip, "xl/styles.xml");
        if (doc is null) return result;

        var custom = doc.Root!.Element(M + "numFmts")?.Elements(M + "numFmt")
            .ToDictionary(e => (int)e.Attribute("numFmtId")!, e => (string?)e.Attribute("formatCode") ?? "") ?? [];

        var xfs = doc.Root.Element(M + "cellXfs")?.Elements(M + "xf").ToList() ?? [];
        for (int i = 0; i < xfs.Count; i++)
        {
            var id = (int?)xfs[i].Attribute("numFmtId") ?? 0;
            bool isDate = id is >= 14 and <= 22 or >= 27 and <= 36 or >= 45 and <= 47 or >= 50 and <= 58
                          || (custom.TryGetValue(id, out var code) && IsDateFormat(code));
            if (isDate) result.Add(i);
        }
        return result;
    }

    private static bool IsDateFormat(string code)
    {
        var stripped = QuotedOrBracketed().Replace(code, "");
        return stripped.IndexOfAny(['d', 'D', 'm', 'M', 'y', 'Y']) >= 0 && !stripped.Contains("General", StringComparison.OrdinalIgnoreCase);
    }

    public static (int Row, int Col) ParseReference(string reference)
    {
        int col = 0, i = 0;
        while (i < reference.Length && char.IsLetter(reference[i]))
        {
            col = col * 26 + (char.ToUpperInvariant(reference[i]) - 'A' + 1);
            i++;
        }
        int.TryParse(reference.AsSpan(i), out var row);
        return (row, col);
    }

    public static int ColumnIndex(string letters) => ParseReference(letters + "1").Col;

    public static string ColumnLetters(int col)
    {
        var sb = new StringBuilder();
        while (col > 0)
        {
            col--;
            sb.Insert(0, (char)('A' + col % 26));
            col /= 26;
        }
        return sb.ToString();
    }

    [GeneratedRegex("\"[^\"]*\"|\\[[^\\]]*\\]")]
    private static partial Regex QuotedOrBracketed();
}

/// <summary>Minimal .xlsx writer: inline strings, a fixed style sheet, merges, widths and frozen panes.</summary>
public sealed class XlsxWriter
{
    public enum Style { Default = 0, Title = 1, Header = 2, Cell = 3, DateCell = 4, Section = 5, Org = 6, CenterCell = 7 }

    private readonly SortedDictionary<int, SortedDictionary<int, (object Value, Style Style)>> _rows = [];
    private readonly List<string> _merges = [];
    private readonly Dictionary<int, double> _widths = [];
    private readonly Dictionary<int, double> _heights = [];

    public string SheetName { get; set; } = "Sheet1";
    public int FreezeRows { get; set; }
    public int FreezeCols { get; set; }

    public void Set(int row, int col, object? value, Style style = Style.Cell)
    {
        if (!_rows.TryGetValue(row, out var cells)) _rows[row] = cells = [];
        cells[col] = (value ?? "", style);
    }

    public void Merge(int r1, int c1, int r2, int c2)
    {
        if (r1 == r2 && c1 == c2) return;
        _merges.Add($"{XlsxReader.ColumnLetters(c1)}{r1}:{XlsxReader.ColumnLetters(c2)}{r2}");
    }

    public void SetWidth(int col, double width) => _widths[col] = width;
    public void SetHeight(int row, double height) => _heights[row] = height;

    public void Save(string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create);
        Add(zip, "[Content_Types].xml", ContentTypes);
        Add(zip, "_rels/.rels", RootRels);
        Add(zip, "xl/workbook.xml", WorkbookXml());
        Add(zip, "xl/_rels/workbook.xml.rels", WorkbookRels);
        Add(zip, "xl/styles.xml", StylesXml);
        var entry = zip.CreateEntry("xl/worksheets/sheet1.xml", CompressionLevel.Optimal);
        using var s = entry.Open();
        WriteSheet(s);
    }

    private static void Add(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        w.Write(content);
    }

    private void WriteSheet(Stream s)
    {
        const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        using var x = XmlWriter.Create(s, new XmlWriterSettings { Encoding = new UTF8Encoding(false) });
        x.WriteStartDocument(true);
        x.WriteStartElement("worksheet", ns);

        if (FreezeRows > 0 || FreezeCols > 0)
        {
            x.WriteStartElement("sheetViews", ns);
            x.WriteStartElement("sheetView", ns);
            x.WriteAttributeString("workbookViewId", "0");
            x.WriteStartElement("pane", ns);
            if (FreezeCols > 0) x.WriteAttributeString("xSplit", FreezeCols.ToString(CultureInfo.InvariantCulture));
            if (FreezeRows > 0) x.WriteAttributeString("ySplit", FreezeRows.ToString(CultureInfo.InvariantCulture));
            x.WriteAttributeString("topLeftCell", $"{XlsxReader.ColumnLetters(FreezeCols + 1)}{FreezeRows + 1}");
            x.WriteAttributeString("activePane", "bottomRight");
            x.WriteAttributeString("state", "frozen");
            x.WriteEndElement();
            x.WriteEndElement();
            x.WriteEndElement();
        }

        if (_widths.Count > 0)
        {
            x.WriteStartElement("cols", ns);
            foreach (var (col, width) in _widths.OrderBy(k => k.Key))
            {
                x.WriteStartElement("col", ns);
                x.WriteAttributeString("min", col.ToString(CultureInfo.InvariantCulture));
                x.WriteAttributeString("max", col.ToString(CultureInfo.InvariantCulture));
                x.WriteAttributeString("width", width.ToString(CultureInfo.InvariantCulture));
                x.WriteAttributeString("customWidth", "1");
                x.WriteEndElement();
            }
            x.WriteEndElement();
        }

        x.WriteStartElement("sheetData", ns);
        foreach (var (row, cells) in _rows)
        {
            x.WriteStartElement("row", ns);
            x.WriteAttributeString("r", row.ToString(CultureInfo.InvariantCulture));
            if (_heights.TryGetValue(row, out var h))
            {
                x.WriteAttributeString("ht", h.ToString(CultureInfo.InvariantCulture));
                x.WriteAttributeString("customHeight", "1");
            }
            foreach (var (col, (value, style)) in cells)
            {
                x.WriteStartElement("c", ns);
                x.WriteAttributeString("r", XlsxReader.ColumnLetters(col) + row);
                x.WriteAttributeString("s", ((int)style).ToString(CultureInfo.InvariantCulture));
                switch (value)
                {
                    case DateTime d:
                        x.WriteElementString("v", ns, d.ToOADate().ToString(CultureInfo.InvariantCulture));
                        break;
                    case int or long or double or decimal:
                        x.WriteElementString("v", ns, Convert.ToString(value, CultureInfo.InvariantCulture));
                        break;
                    default:
                        var text = value.ToString() ?? "";
                        if (text.Length == 0) break;
                        x.WriteAttributeString("t", "inlineStr");
                        x.WriteStartElement("is", ns);
                        x.WriteStartElement("t", ns);
                        x.WriteAttributeString("xml", "space", null, "preserve");
                        x.WriteString(text);
                        x.WriteEndElement();
                        x.WriteEndElement();
                        break;
                }
                x.WriteEndElement();
            }
            x.WriteEndElement();
        }
        x.WriteEndElement(); // sheetData

        if (_merges.Count > 0)
        {
            x.WriteStartElement("mergeCells", ns);
            x.WriteAttributeString("count", _merges.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var m in _merges)
            {
                x.WriteStartElement("mergeCell", ns);
                x.WriteAttributeString("ref", m);
                x.WriteEndElement();
            }
            x.WriteEndElement();
        }

        x.WriteStartElement("pageMargins", ns);
        foreach (var (k, v) in new[] { ("left", "0.3"), ("right", "0.3"), ("top", "0.5"), ("bottom", "0.5"), ("header", "0.3"), ("footer", "0.3") })
            x.WriteAttributeString(k, v);
        x.WriteEndElement();
        x.WriteStartElement("pageSetup", ns);
        x.WriteAttributeString("paperSize", "9");
        x.WriteAttributeString("orientation", "landscape");
        x.WriteEndElement();

        x.WriteEndElement(); // worksheet
        x.WriteEndDocument();
    }

    private string WorkbookXml()
    {
        var name = SecurityElementEscape(SanitizeSheetName(SheetName));
        return $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <workbook xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships"><sheets><sheet name="{name}" sheetId="1" r:id="rId1"/></sheets></workbook>
            """;
    }

    private static string SanitizeSheetName(string name)
    {
        var cleaned = new string(name.Where(c => "[]:*?/\\".IndexOf(c) < 0).ToArray()).Trim();
        if (cleaned.Length == 0) cleaned = "Sheet1";
        return cleaned.Length > 31 ? cleaned[..31] : cleaned;
    }

    private static string SecurityElementEscape(string s) =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    private const string ContentTypes = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/xl/workbook.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml"/><Override PartName="/xl/worksheets/sheet1.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml"/><Override PartName="/xl/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml"/></Types>
        """;

    private const string RootRels = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="xl/workbook.xml"/></Relationships>
        """;

    private const string WorkbookRels = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet" Target="worksheets/sheet1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>
        """;

    // Style indexes must match the Style enum.
    private const string StylesXml = """
        <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
        <styleSheet xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main">
        <numFmts count="1"><numFmt numFmtId="164" formatCode="dd/mm/yyyy"/></numFmts>
        <fonts count="4">
          <font><sz val="11"/><name val="Times New Roman"/><family val="1"/></font>
          <font><b/><sz val="14"/><name val="Times New Roman"/><family val="1"/></font>
          <font><b/><sz val="11"/><name val="Times New Roman"/><family val="1"/></font>
          <font><b/><sz val="12"/><name val="Times New Roman"/><family val="1"/></font>
        </fonts>
        <fills count="4">
          <fill><patternFill patternType="none"/></fill>
          <fill><patternFill patternType="gray125"/></fill>
          <fill><patternFill patternType="solid"><fgColor rgb="FFDDEBF7"/><bgColor indexed="64"/></patternFill></fill>
          <fill><patternFill patternType="solid"><fgColor rgb="FFFFF2CC"/><bgColor indexed="64"/></patternFill></fill>
        </fills>
        <borders count="2">
          <border><left/><right/><top/><bottom/><diagonal/></border>
          <border><left style="thin"><color auto="1"/></left><right style="thin"><color auto="1"/></right><top style="thin"><color auto="1"/></top><bottom style="thin"><color auto="1"/></bottom><diagonal/></border>
        </borders>
        <cellStyleXfs count="1"><xf numFmtId="0" fontId="0" fillId="0" borderId="0"/></cellStyleXfs>
        <cellXfs count="8">
          <xf numFmtId="0" fontId="0" fillId="0" borderId="0" xfId="0"/>
          <xf numFmtId="0" fontId="1" fillId="0" borderId="0" xfId="0" applyFont="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
          <xf numFmtId="0" fontId="2" fillId="2" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf>
          <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyAlignment="1"><alignment vertical="center" wrapText="1"/></xf>
          <xf numFmtId="164" fontId="0" fillId="0" borderId="1" xfId="0" applyNumberFormat="1" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center"/></xf>
          <xf numFmtId="0" fontId="2" fillId="3" borderId="1" xfId="0" applyFont="1" applyFill="1" applyBorder="1" applyAlignment="1"><alignment vertical="center"/></xf>
          <xf numFmtId="0" fontId="3" fillId="0" borderId="0" xfId="0" applyFont="1"/>
          <xf numFmtId="0" fontId="0" fillId="0" borderId="1" xfId="0" applyBorder="1" applyAlignment="1"><alignment horizontal="center" vertical="center" wrapText="1"/></xf>
        </cellXfs>
        <cellStyles count="1"><cellStyle name="Normal" xfId="0" builtinId="0"/></cellStyles>
        </styleSheet>
        """;
}
