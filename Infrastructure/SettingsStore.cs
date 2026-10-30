using System.Security.Cryptography;
using System.Text;
using StaffManagement.Services;

namespace StaffManagement.Infrastructure;

/// <summary>Typed access to the Settings table. The API key is encrypted for the current Windows user (DPAPI).</summary>
public sealed class SettingsStore(StaffRepository repo)
{
    private const string KeyOrganization = "Export.Organization";
    private const string KeyTitle = "Export.Title";
    private const string KeyColumns = "Grid.VisibleColumns";
    private const string KeyColumnOrder = "Export.ColumnOrder";
    private const string KeyLlmEnabled = "Llm.Enabled";
    private const string KeyLlmEndpoint = "Llm.Endpoint";
    private const string KeyLlmModel = "Llm.Model";
    private const string KeyLlmApiKey = "Llm.ApiKey";

    public string? Organization
    {
        get => repo.GetSetting(KeyOrganization);
        set => repo.SetSetting(KeyOrganization, value);
    }

    public string? Title
    {
        get => repo.GetSetting(KeyTitle);
        set => repo.SetSetting(KeyTitle, value);
    }

    public IReadOnlyList<string>? VisibleColumns
    {
        get => repo.GetSetting(KeyColumns)?.Split(',', StringSplitOptions.RemoveEmptyEntries);
        set => repo.SetSetting(KeyColumns, value is null ? null : string.Join(",", value));
    }

    /// <summary>Field keys in the column order of the last Excel file imported with "replace".</summary>
    public IReadOnlyList<string>? ExportColumnOrder
    {
        get => repo.GetSetting(KeyColumnOrder)?.Split(',', StringSplitOptions.RemoveEmptyEntries);
        set => repo.SetSetting(KeyColumnOrder, value is null ? null : string.Join(",", value));
    }

    /// <summary>Whether the first-start "open the manual?" question was asked for this data folder.</summary>
    public bool HelpOffered
    {
        get => repo.GetSetting("App.HelpOffered") == "1";
        set => repo.SetSetting("App.HelpOffered", value ? "1" : null);
    }

    /// <summary>Which <see cref="DefaultPresets.Version"/> was last added to this data folder (0 = none).</summary>
    public int DefaultPresetsVersion
    {
        get => int.TryParse(repo.GetSetting("Presets.DefaultsVersion"), out var v) ? v : 0;
        set => repo.SetSetting("Presets.DefaultsVersion", value.ToString());
    }

    public ReminderSettings LoadReminders() => new()
    {
        LeadDays = int.TryParse(repo.GetSetting("Reminder.LeadDays"), out var lead) && lead > 0 ? lead : 100,
        IntervalDays = int.TryParse(repo.GetSetting("Reminder.IntervalDays"), out var every) && every > 0 ? every : 5,
        IncludeRetirement = repo.GetSetting("Reminder.IncludeRetirement") != "0",
    };

    public void SaveReminders(ReminderSettings s)
    {
        repo.SetSetting("Reminder.LeadDays", s.LeadDays.ToString());
        repo.SetSetting("Reminder.IntervalDays", s.IntervalDays.ToString());
        repo.SetSetting("Reminder.IncludeRetirement", s.IncludeRetirement ? "1" : "0");
    }

    /// <summary>Time of the daily background check ("HH:mm").</summary>
    public string DailyCheckTime
    {
        get => repo.GetSetting("Reminder.DailyTime") ?? "08:00";
        set => repo.SetSetting("Reminder.DailyTime", value);
    }

    public LlmSettings LoadLlm() => new()
    {
        Enabled = repo.GetSetting(KeyLlmEnabled) == "1",
        Endpoint = repo.GetSetting(KeyLlmEndpoint) ?? "",
        Model = repo.GetSetting(KeyLlmModel) ?? "",
        ApiKey = Unprotect(repo.GetSetting(KeyLlmApiKey)),
    };

    public void SaveLlm(LlmSettings s)
    {
        repo.SetSetting(KeyLlmEnabled, s.Enabled ? "1" : "0");
        repo.SetSetting(KeyLlmEndpoint, s.Endpoint.Trim());
        repo.SetSetting(KeyLlmModel, s.Model.Trim());
        repo.SetSetting(KeyLlmApiKey, Protect(s.ApiKey));
    }

    private static string? Protect(string? plain)
    {
        if (string.IsNullOrEmpty(plain)) return null;
        var bytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), null, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(bytes);
    }

    private static string Unprotect(string? cipher)
    {
        if (string.IsNullOrEmpty(cipher)) return "";
        try
        {
            var bytes = ProtectedData.Unprotect(Convert.FromBase64String(cipher), null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return ""; // created by another Windows user / machine
        }
    }
}
