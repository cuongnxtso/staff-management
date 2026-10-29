using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Filters;
using UglyToad.PdfPig.Tokens;

namespace StaffManagement.Services;

/// <summary>
/// Text drawn by annotations rather than by the page itself. E-office signing systems (e.g. eoffice.hmu.edu.vn)
/// stamp the decision number and signing date into signature widgets after the document is signed:
/// the page text then reads "Số: /QĐ-…" and "ngày tháng năm 2026", while the number and date only exist in the
/// widgets' appearance streams. This reads those streams (fonts decoded through their ToUnicode maps).
/// </summary>
public static partial class PdfAnnotationText
{
    public sealed record Item(string Text, PdfRectangle Rect);

    /// <summary>Widgets taller than this are seals / handwritten signatures, not text fields.</summary>
    private const double MaxTextFieldHeight = 40;

    public static List<Item> Extract(PdfDocument doc, Page page)
    {
        var items = new List<Item>();
        foreach (var a in page.GetAnnotations())
        {
            if (a.Rectangle.Height > MaxTextFieldHeight) continue;
            if (!a.AnnotationDictionary.Data.TryGetValue("AP", out var apToken) || Resolve(doc, apToken) is not DictionaryToken ap) continue;
            if (!ap.Data.TryGetValue("N", out var n) || Resolve(doc, n) is not StreamToken normal) continue;
            try
            {
                var text = VietnameseText.CleanText(new AppearanceReader(doc).Read(normal, 0));
                if (text is not null) items.Add(new Item(text, a.Rectangle));
            }
            catch (Exception)
            {
                // A malformed appearance stream must not prevent reading the rest of the document.
            }
        }
        return items;
    }

    /// <summary>
    /// Rebuilds the page text line by line from the page words plus the annotation texts, so that e.g. the widget
    /// "418" lands between "Số:" and "/QĐ-VĐTRHM".
    /// </summary>
    public static string MergeIntoLines(Page page, IReadOnlyList<Item> items)
    {
        var tokens = page.GetWords()
            .Select(w => (Text: w.Text, Left: w.BoundingBox.Left, Cy: (w.BoundingBox.Top + w.BoundingBox.Bottom) / 2, H: w.BoundingBox.Height))
            .Concat(items.Select(i => (i.Text, i.Rect.Left, Cy: (i.Rect.Top + i.Rect.Bottom) / 2, H: Math.Min(i.Rect.Height, 14))))
            .OrderByDescending(t => t.Cy)
            .ToList();

        var lines = new List<List<(string Text, double Left, double Cy, double H)>>();
        foreach (var t in tokens)
        {
            var line = lines.LastOrDefault();
            if (line is not null)
            {
                double cy = line.Average(x => x.Cy), h = Math.Min(t.H, line.Min(x => x.H));
                if (Math.Abs(t.Cy - cy) <= Math.Max(3, 0.45 * h))
                {
                    line.Add(t);
                    continue;
                }
            }
            lines.Add([t]);
        }
        return string.Join("\n", lines.Select(l => string.Join(" ", l.OrderBy(x => x.Left).Select(x => x.Text))));
    }

    private static IToken Resolve(PdfDocument doc, IToken token) =>
        token is IndirectReferenceToken r ? doc.Structure.GetObject(r.Data).Data : token;

    /// <summary>Minimal content-stream interpreter: text operators, fonts and nested form XObjects.</summary>
    private sealed class AppearanceReader(PdfDocument doc)
    {
        private readonly Dictionary<IndirectReference, FontDecoder> _fontCache = [];

        public string Read(StreamToken stream, int depth)
        {
            if (depth > 6) return "";
            var resources = stream.StreamDictionary.Data.TryGetValue("Resources", out var r) ? Resolve(doc, r) as DictionaryToken : null;
            var fonts = SubDictionary(resources, "Font");
            var xobjects = SubDictionary(resources, "XObject");

            var content = Encoding.Latin1.GetString(stream.Decode(DefaultFilterProvider.Instance).ToArray());
            var sb = new StringBuilder();
            var operands = new List<object>();
            FontDecoder? font = null;

            void Space()
            {
                if (sb.Length > 0 && !char.IsWhiteSpace(sb[^1])) sb.Append(' ');
            }

            foreach (var token in Lexer.Tokens(content))
            {
                if (token is not Lexer.Operator op)
                {
                    operands.Add(token);
                    continue;
                }
                switch (op.Name)
                {
                    case "Tf" when operands.Count >= 2 && operands[^2] is Lexer.Name fontName:
                        font = fonts?.Data.TryGetValue(fontName.Value, out var f) == true ? GetFont(f) : null;
                        break;
                    case "Tj" or "'" or "\"" when operands.LastOrDefault() is byte[] s:
                        if (op.Name != "Tj") Space();
                        sb.Append(Decode(font, s));
                        break;
                    case "TJ" when operands.LastOrDefault() is List<object> array:
                        foreach (var e in array)
                        {
                            if (e is byte[] part) sb.Append(Decode(font, part));
                            else if (e is double kern && kern < -200) Space();
                        }
                        break;
                    case "Td" or "TD" or "Tm" or "T*" or "ET":
                        Space();
                        break;
                    case "Do" when operands.LastOrDefault() is Lexer.Name xName
                                   && xobjects?.Data.TryGetValue(xName.Value, out var x) == true
                                   && Resolve(doc, x) is StreamToken form
                                   && form.StreamDictionary.Data.TryGetValue("Subtype", out var st) && st is NameToken { Data: "Form" }:
                        Space();
                        sb.Append(Read(form, depth + 1));
                        break;
                }
                operands.Clear();
            }
            return sb.ToString();
        }

        private DictionaryToken? SubDictionary(DictionaryToken? resources, string key) =>
            resources is not null && resources.Data.TryGetValue(key, out var t) ? Resolve(doc, t) as DictionaryToken : null;

        private FontDecoder? GetFont(IToken token)
        {
            if (token is IndirectReferenceToken r && _fontCache.TryGetValue(r.Data, out var cached)) return cached;
            if (Resolve(doc, token) is not DictionaryToken dict) return null;
            bool twoByte = dict.Data.TryGetValue("Subtype", out var sub) && sub is NameToken { Data: "Type0" };
            var map = dict.Data.TryGetValue("ToUnicode", out var tu) && Resolve(doc, tu) is StreamToken cmap
                ? ParseToUnicode(Encoding.Latin1.GetString(cmap.Decode(DefaultFilterProvider.Instance).ToArray()))
                : [];
            var decoder = new FontDecoder(twoByte, map);
            if (token is IndirectReferenceToken r2) _fontCache[r2.Data] = decoder;
            return decoder;
        }

        private static string Decode(FontDecoder? font, byte[] bytes)
        {
            if (font is null) return Encoding.Latin1.GetString(bytes);
            var sb = new StringBuilder();
            int step = font.TwoByte ? 2 : 1;
            for (int i = 0; i + step <= bytes.Length; i += step)
            {
                int code = font.TwoByte ? (bytes[i] << 8) | bytes[i + 1] : bytes[i];
                if (font.ToUnicode.TryGetValue(code, out var s)) sb.Append(s);
                else if (!font.TwoByte) sb.Append((char)code);
            }
            return sb.ToString();
        }
    }

    private sealed record FontDecoder(bool TwoByte, Dictionary<int, string> ToUnicode);

    /// <summary>Parses the bfchar / bfrange sections of a ToUnicode CMap.</summary>
    private static Dictionary<int, string> ParseToUnicode(string cmap)
    {
        var map = new Dictionary<int, string>();
        foreach (Match block in BfCharBlock().Matches(cmap))
            foreach (Match m in HexPair().Matches(block.Groups[1].Value))
                map[HexInt(m.Groups[1].Value)] = Utf16(m.Groups[2].Value);

        foreach (Match block in BfRangeBlock().Matches(cmap))
            foreach (Match m in RangeEntry().Matches(block.Groups[1].Value))
            {
                int lo = HexInt(m.Groups[1].Value), hi = HexInt(m.Groups[2].Value);
                if (hi - lo > 0xFFFF) continue;
                if (m.Groups[3].Success)
                {
                    var start = Utf16(m.Groups[3].Value);
                    for (int c = lo; c <= hi; c++)
                        map[c] = start.Length == 0 ? "" : start[..^1] + (char)(start[^1] + (c - lo));
                }
                else
                {
                    var targets = HexString().Matches(m.Groups[4].Value).Select(t => Utf16(t.Groups[1].Value)).ToList();
                    for (int c = lo; c <= hi && c - lo < targets.Count; c++) map[c] = targets[c - lo];
                }
            }
        return map;
    }

    private static int HexInt(string hex) => int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);

    private static string Utf16(string hex)
    {
        if (hex.Length % 4 != 0) hex = hex.PadLeft((hex.Length + 3) / 4 * 4, '0');
        var bytes = Convert.FromHexString(hex);
        return Encoding.BigEndianUnicode.GetString(bytes);
    }

    [GeneratedRegex(@"beginbfchar(.*?)endbfchar", RegexOptions.Singleline)]
    private static partial Regex BfCharBlock();

    [GeneratedRegex(@"beginbfrange(.*?)endbfrange", RegexOptions.Singleline)]
    private static partial Regex BfRangeBlock();

    [GeneratedRegex(@"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]*)>")]
    private static partial Regex HexPair();

    [GeneratedRegex(@"<([0-9A-Fa-f]+)>\s*<([0-9A-Fa-f]+)>\s*(?:<([0-9A-Fa-f]*)>|\[(.*?)\])", RegexOptions.Singleline)]
    private static partial Regex RangeEntry();

    [GeneratedRegex(@"<([0-9A-Fa-f]*)>")]
    private static partial Regex HexString();

    /// <summary>PDF content-stream tokenizer (enough for appearance streams).</summary>
    private static class Lexer
    {
        public sealed record Operator(string Name);
        public sealed record Name(string Value);

        public static IEnumerable<object> Tokens(string s)
        {
            var stack = new Stack<List<object>>();
            int i = 0;
            while (i < s.Length)
            {
                char c = s[i];
                object? token = null;
                if (char.IsWhiteSpace(c) || c == '\0') { i++; continue; }
                if (c == '%') { while (i < s.Length && s[i] is not ('\n' or '\r')) i++; continue; }
                if (c == '(') token = ReadLiteral(s, ref i);
                else if (c == '<' && i + 1 < s.Length && s[i + 1] == '<') { i += 2; token = new Operator("<<"); }
                else if (c == '>' && i + 1 < s.Length && s[i + 1] == '>') { i += 2; token = new Operator(">>"); }
                else if (c == '<')
                {
                    int end = s.IndexOf('>', i);
                    if (end < 0) yield break;
                    var hex = new string(s[(i + 1)..end].Where(Uri.IsHexDigit).ToArray());
                    if (hex.Length % 2 == 1) hex += "0";
                    token = Convert.FromHexString(hex);
                    i = end + 1;
                }
                else if (c == '[') { stack.Push([]); i++; continue; }
                else if (c == ']')
                {
                    i++;
                    if (stack.Count == 0) continue;
                    token = stack.Pop();
                }
                else if (c == '/')
                {
                    int start = ++i;
                    while (i < s.Length && !IsDelimiter(s[i])) i++;
                    token = new Name(s[start..i]);
                }
                else
                {
                    int start = i;
                    while (i < s.Length && !IsDelimiter(s[i])) i++;
                    if (i == start) { i++; continue; }
                    var word = s[start..i];
                    token = double.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out var num) ? num : new Operator(word);
                    if (token is Operator { Name: "ID" })
                    {
                        // Inline image data: skip to "EI".
                        int ei = s.IndexOf("EI", i, StringComparison.Ordinal);
                        i = ei < 0 ? s.Length : ei + 2;
                        continue;
                    }
                }

                if (stack.Count > 0) stack.Peek().Add(token);
                else yield return token;
            }
        }

        private static bool IsDelimiter(char c) => char.IsWhiteSpace(c) || c is '(' or ')' or '<' or '>' or '[' or ']' or '{' or '}' or '/' or '%';

        private static byte[] ReadLiteral(string s, ref int i)
        {
            var bytes = new List<byte>();
            int depth = 0;
            i++; // (
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '\\' && i < s.Length)
                {
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': bytes.Add((byte)'\n'); break;
                        case 'r': bytes.Add((byte)'\r'); break;
                        case 't': bytes.Add((byte)'\t'); break;
                        case 'b': bytes.Add(8); break;
                        case 'f': bytes.Add(12); break;
                        case '\r': if (i < s.Length && s[i] == '\n') i++; break;
                        case '\n': break;
                        case >= '0' and <= '7':
                            int value = e - '0';
                            for (int k = 0; k < 2 && i < s.Length && s[i] is >= '0' and <= '7'; k++) value = value * 8 + (s[i++] - '0');
                            bytes.Add((byte)value);
                            break;
                        default: bytes.Add((byte)e); break;
                    }
                    continue;
                }
                if (c == '(') depth++;
                else if (c == ')' && depth-- == 0) break;
                bytes.Add((byte)c);
            }
            return [.. bytes];
        }
    }
}
