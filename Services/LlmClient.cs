using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using StaffManagement.Models;

namespace StaffManagement.Services;

public sealed class LlmSettings
{
    public bool Enabled { get; set; }
    public string Endpoint { get; set; } = "";
    public string Model { get; set; } = "";
    public string ApiKey { get; set; } = "";
    public bool IsUsable => Enabled && !string.IsNullOrWhiteSpace(Endpoint) && !string.IsNullOrWhiteSpace(Model);
}

public sealed record LlmProviderPreset(string Name, string Endpoint, string Model, string Note)
{
    public override string ToString() => Name;

    public static IReadOnlyList<LlmProviderPreset> All { get; } =
    [
        new("Google AI Studio (Gemini)", "https://generativelanguage.googleapis.com/v1beta/openai", "gemini-2.5-flash", "Miễn phí, cần API key từ aistudio.google.com"),
        new("Groq", "https://api.groq.com/openai/v1", "llama-3.3-70b-versatile", "Miễn phí, rất nhanh — API key từ console.groq.com"),
        new("OpenRouter", "https://openrouter.ai/api/v1", "meta-llama/llama-3.3-70b-instruct:free", "Có model miễn phí — API key từ openrouter.ai"),
        new("Ollama (máy cục bộ)", "http://localhost:11434/v1", "qwen2.5:3b", "Chạy offline trên máy, không cần API key"),
    ];
}

/// <summary>
/// Minimal client for any OpenAI-compatible chat-completions endpoint (Gemini, Groq, OpenRouter, Ollama...).
/// Used only for optional features; the app works fully offline without it.
/// </summary>
public sealed class LlmClient(LlmSettings settings)
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public async Task<string> CompleteJsonAsync(string system, string user, CancellationToken ct = default)
    {
        try
        {
            return await SendAsync(system, user, jsonMode: true, ct);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.BadRequest)
        {
            // Some providers / models do not support response_format — retry without it.
            return await SendAsync(system, user, jsonMode: false, ct);
        }
    }

    private async Task<string> SendAsync(string system, string user, bool jsonMode, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = settings.Model,
            ["temperature"] = 0,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user },
            },
        };
        if (jsonMode) body["response_format"] = new JsonObject { ["type"] = "json_object" };

        using var req = new HttpRequestMessage(HttpMethod.Post, settings.Endpoint.TrimEnd('/') + "/chat/completions")
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrWhiteSpace(settings.ApiKey))
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey.Trim());

        using var resp = await Http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Máy chủ AI trả lỗi {(int)resp.StatusCode}: {Truncate(text, 300)}", null, resp.StatusCode);

        var content = JsonNode.Parse(text)?["choices"]?[0]?["message"]?["content"]?.GetValue<string>()
                      ?? throw new InvalidDataException("Phản hồi AI không có nội dung.");
        return ExtractJson(content);
    }

    /// <summary>Strips ```json fences / surrounding prose and returns the outermost JSON object.</summary>
    public static string ExtractJson(string content)
    {
        int start = content.IndexOf('{');
        int end = content.LastIndexOf('}');
        if (start < 0 || end <= start) throw new InvalidDataException("AI không trả về JSON hợp lệ: " + Truncate(content, 200));
        return content[start..(end + 1)];
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n] + "…";

    // ------------------------------------------------------------------ Natural-language → filter rules

    public async Task<FilterSet> ParseQueryAsync(string query, IReadOnlyDictionary<string, IReadOnlyList<string>> sampleValues, CancellationToken ct = default)
    {
        var fields = new StringBuilder();
        foreach (var f in FieldCatalog.All)
        {
            fields.Append($"- {f.Key} ({f.Kind}): {f.Header}");
            if (sampleValues.TryGetValue(f.Key, out var samples) && samples.Count > 0)
                fields.Append(" | ví dụ: ").Append(string.Join("; ", samples.Take(15)));
            fields.AppendLine();
        }

        var system = $$"""
            Bạn chuyển câu hỏi tiếng Việt về danh sách cán bộ lãnh đạo, quản lý thành bộ lọc JSON.
            Hôm nay là {{DateTime.Today:dd/MM/yyyy}}. Mỗi dòng dữ liệu là một chức vụ của một người.
            Các trường (Key (kiểu): mô tả):
            {{fields}}
            Toán tử hợp lệ: Contains, NotContains, Equals, NotEquals, StartsWith, InList (Value1 ngăn cách bằng ;),
            GreaterThan, GreaterOrEqual, LessThan, LessOrEqual, Between (Value1..Value2), IsEmpty, IsNotEmpty, IsTrue, IsFalse (cho Bool).
            Ngày viết dạng dd/MM/yyyy. So sánh văn bản không phân biệt dấu/hoa thường; ưu tiên Contains cho Đơn vị và Chức vụ.
            Chỉ trả về JSON dạng: {"MatchAll": true, "Rules": [{"Field": "Gender", "Operator": "Equals", "Value1": "Nam", "Value2": null}]}
            """;

        var json = await CompleteJsonAsync(system, query, ct);
        return FilterSetFromJson(json);
    }

    /// <summary>Lenient mapping: unknown fields / operators are skipped instead of failing.</summary>
    public static FilterSet FilterSetFromJson(string json)
    {
        var node = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("JSON bộ lọc không hợp lệ.");
        var set = new FilterSet { MatchAll = Get(node, "MatchAll") is not { } m || !bool.TryParse(m, out var all) || all };
        if (GetNode(node, "Rules") is not JsonArray rules) return set;
        foreach (var item in rules.OfType<JsonObject>())
        {
            var field = FieldCatalog.Find(Get(item, "Field"));
            if (field is null || !Enum.TryParse<FilterOperator>(Get(item, "Operator"), true, out var op)) continue;
            set.Rules.Add(new FilterRule { Field = field.Key, Operator = op, Value1 = Get(item, "Value1"), Value2 = Get(item, "Value2") });
        }
        return set;
    }

    private static JsonNode? GetNode(JsonObject o, string key) =>
        o.FirstOrDefault(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase)).Value;

    private static string? Get(JsonObject o, string key) => GetNode(o, key) switch
    {
        null => null,
        JsonValue v when v.TryGetValue<string>(out var s) => string.IsNullOrWhiteSpace(s) || s == "null" ? null : s.Trim(),
        JsonValue v => v.ToJsonString(),
        _ => null,
    };

    // ------------------------------------------------------------------ Decision text → DecisionInfo

    public async Task<DecisionInfo> ParseDecisionAsync(string documentText, CancellationToken ct = default)
    {
        const string system = """
            Bạn trích xuất thông tin từ quyết định (QĐ) về cán bộ của một trường đại học Việt Nam.
            Trả về JSON với các khóa:
            Kind ("Appoint" = bổ nhiệm, "Reappoint" = bổ nhiệm lại, "Assign" = giao/cử phụ trách, "Dismiss" = thôi giữ chức vụ/miễn nhiệm),
            StaffCode (mã cán bộ, ví dụ từ "(MCB: 01079)"), FullName, Gender ("Nam"/"Nữ"), DateOfBirth (yyyy-MM-dd),
            ProfessionalTitle (chức danh nghề nghiệp, ví dụ "Giảng viên chính (hạng II)"), AcademicRank ("GS"/"PGS"/null), Degree ("TS"/"ThS"/"BSCKII"/null),
            Position (chức vụ, ví dụ "Trưởng phòng", "P. Trưởng BM", "Phó Giám đốc"), Department (đơn vị nơi giữ chức vụ),
            HomeUnit (đơn vị công tác/cơ hữu), DecisionNumber (ví dụ "3208/QĐ-ĐHYHN"), DecisionDate (yyyy-MM-dd),
            EffectiveDate (ngày có hiệu lực, yyyy-MM-dd), TermYears (số năm giữ chức vụ), AppointingUnit, AppointingLevel ("BYT"/"Trường"/"Đơn vị").
            Dùng null cho thông tin không có. Chỉ trả về JSON.
            """;
        var text = documentText.Length > 12000 ? documentText[..12000] : documentText;
        var json = await CompleteJsonAsync(system, text, ct);
        return DecisionFromJson(json);
    }

    public static DecisionInfo DecisionFromJson(string json)
    {
        var o = JsonNode.Parse(json) as JsonObject ?? throw new InvalidDataException("JSON quyết định không hợp lệ.");
        var info = new DecisionInfo
        {
            Kind = Enum.TryParse<DecisionKind>(Get(o, "Kind"), true, out var kind) ? kind : DecisionKind.Appoint,
            StaffCode = Get(o, "StaffCode"),
            FullName = Get(o, "FullName"),
            ProfessionalTitle = Get(o, "ProfessionalTitle"),
            Gender = Get(o, "Gender"),
            DateOfBirth = VietnameseText.ParseDate(Get(o, "DateOfBirth")),
            AcademicRank = Get(o, "AcademicRank"),
            Degree = Get(o, "Degree"),
            Position = Get(o, "Position"),
            Department = Get(o, "Department"),
            HomeUnit = Get(o, "HomeUnit"),
            DecisionNumber = Get(o, "DecisionNumber"),
            DecisionDate = VietnameseText.ParseDate(Get(o, "DecisionDate")),
            EffectiveDate = VietnameseText.ParseDate(Get(o, "EffectiveDate")),
            TermYears = Get(o, "TermYears") is { } t && System.Text.RegularExpressions.Regex.Match(t, @"\d+") is { Success: true } tm ? int.Parse(tm.Value) : null,
            AppointingUnit = Get(o, "AppointingUnit"),
            AppointingLevel = Get(o, "AppointingLevel"),
        };
        info.PositionText = $"{info.Position} {info.Department}".Trim();
        return info;
    }

    public async Task<string> TestAsync(CancellationToken ct = default)
    {
        var json = await CompleteJsonAsync("Trả về JSON {\"ok\": true}.", "ping", ct);
        return json;
    }
}
