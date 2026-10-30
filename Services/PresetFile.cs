using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;
using StaffManagement.Models;

namespace StaffManagement.Services;

/// <summary>Saved filters as a .json file, to copy them to another PC or keep a copy.</summary>
public static class PresetFile
{
    public const string DialogFilter = "Bộ lọc (*.json)|*.json";
    private const string AppTag = "StaffManagement.FilterPresets";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All), // readable Vietnamese
        Converters = { new JsonStringEnumConverter() },       // "Between", not 10: survives enum changes
    };

    private sealed class Content
    {
        public string App { get; set; } = AppTag;
        public int Version { get; set; } = 1;
        public List<FilterPreset>? Presets { get; set; }
    }

    public static void Save(string path, IEnumerable<FilterPreset> presets) =>
        File.WriteAllText(path, JsonSerializer.Serialize(new Content { Presets = presets.ToList() }, Options));

    /// <summary>The valid filters in the file; entries without a name or conditions are dropped.</summary>
    /// <exception cref="InvalidDataException">Not a filter file of this app.</exception>
    public static List<FilterPreset> Load(string path)
    {
        Content? content;
        try
        {
            content = JsonSerializer.Deserialize<Content>(File.ReadAllText(path), Options);
        }
        catch (JsonException)
        {
            content = null;
        }
        if (content?.App != AppTag || content.Presets is null)
            throw new InvalidDataException("Tệp này không phải là tệp bộ lọc xuất từ phần mềm.");
        return content.Presets
            .Where(p => !string.IsNullOrWhiteSpace(p.Name) && p.Filter?.Rules is { Count: > 0 } rules && rules.All(r => !string.IsNullOrWhiteSpace(r.Field)))
            .Select(p => new FilterPreset { Name = p.Name.Trim(), Filter = p.Filter, SavedAt = p.SavedAt })
            .ToList();
    }

    public static bool SameFilter(FilterPreset a, FilterPreset b) =>
        JsonSerializer.Serialize(a.Filter, Options) == JsonSerializer.Serialize(b.Filter, Options);
}
