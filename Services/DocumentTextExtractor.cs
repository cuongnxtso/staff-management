using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace StaffManagement.Services;

/// <summary>Extracts plain text from decision files (.pdf with a text layer, .docx, .txt).</summary>
public static class DocumentTextExtractor
{
    public static readonly string[] SupportedExtensions = [".pdf", ".docx", ".txt"];

    public static bool IsSupported(string path) =>
        SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static string Extract(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        var text = ext switch
        {
            ".pdf" => ExtractPdf(path),
            ".docx" => ExtractDocx(path),
            ".txt" => File.ReadAllText(path),
            ".doc" => throw new NotSupportedException("Định dạng .doc (Word 97-2003) chưa được hỗ trợ. Hãy lưu lại thành .docx hoặc PDF."),
            _ => throw new NotSupportedException($"Định dạng {ext} chưa được hỗ trợ."),
        };
        text = text.Normalize(NormalizationForm.FormC);
        if (text.Count(char.IsLetter) < 30)
            throw new InvalidDataException(ext == ".pdf"
                ? "Tệp PDF không có lớp chữ (có thể là bản scan ảnh). Hãy dùng bản PDF/Word gốc hoặc chạy OCR trước."
                : "Tệp không có nội dung văn bản.");
        return text;
    }

    private static string ExtractPdf(string path)
    {
        using var doc = PdfDocument.Open(path);
        var sb = new StringBuilder();
        foreach (var page in doc.GetPages())
        {
            // Number / date stamped into signature widgets by e-office systems are not part of the page text.
            var stamped = PdfAnnotationText.Extract(doc, page);
            sb.AppendLine(stamped.Count > 0 ? PdfAnnotationText.MergeIntoLines(page, stamped) : ContentOrderTextExtractor.GetText(page));
        }
        return sb.ToString();
    }

    private static string ExtractDocx(string path)
    {
        XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        using var zip = ZipFile.OpenRead(path);
        var entry = zip.GetEntry("word/document.xml") ?? throw new InvalidDataException("Tệp .docx không hợp lệ.");
        using var s = entry.Open();
        var doc = XDocument.Load(s);
        var sb = new StringBuilder();
        foreach (var p in doc.Descendants(w + "p"))
        {
            foreach (var node in p.Descendants())
            {
                if (node.Name == w + "t") sb.Append(node.Value);
                else if (node.Name == w + "tab") sb.Append("    ");
                else if (node.Name == w + "br") sb.Append('\n');
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
