using System.Reflection;
using System.Text.Json;
using Dapper;
using Microsoft.Data.Sqlite;
using StaffManagement.Models;

namespace StaffManagement.Services;

/// <summary>SQLite storage (Dapper). All rows are loaded into memory; filtering happens with LINQ.</summary>
public sealed class StaffRepository
{
    private static readonly PropertyInfo[] StoredProperties = typeof(StaffRecord)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(p => p.CanWrite && p.Name != nameof(StaffRecord.Id))
        .ToArray();

    private static readonly string Columns = string.Join(", ", StoredProperties.Select(p => p.Name));
    private static readonly string Parameters = string.Join(", ", StoredProperties.Select(p => "@" + p.Name));
    private static readonly string Assignments = string.Join(", ", StoredProperties.Select(p => $"{p.Name} = @{p.Name}"));

    private readonly string _connectionString;

    public string DatabasePath { get; }
    public string DataFolder { get; }

    public static string DefaultFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StaffManagement");

    public bool IsDefaultFolder => string.Equals(Path.GetFullPath(DataFolder).TrimEnd('\\'), Path.GetFullPath(DefaultFolder).TrimEnd('\\'),
        StringComparison.OrdinalIgnoreCase);

    static StaffRepository()
    {
        SqlMapper.AddTypeHandler(new NullableDateHandler());
        SqlMapper.AddTypeHandler(new DateHandler());
    }

    public StaffRepository(string? folder = null)
    {
        DataFolder = folder ?? DefaultFolder;
        Directory.CreateDirectory(DataFolder);
        DatabasePath = Path.Combine(DataFolder, "staff.db");
        _connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = false }.ToString();
        EnsureSchema();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        return c;
    }

    /// <summary>Creates tables and adds missing columns (also used after restoring an older backup).</summary>
    public void EnsureSchema()
    {
        using var c = Open();
        var columnDefs = string.Join(",\n  ", StoredProperties.Select(p => $"{p.Name} {SqlType(p.PropertyType)}"));
        c.Execute($"""
            CREATE TABLE IF NOT EXISTS Staff (
              Id INTEGER PRIMARY KEY AUTOINCREMENT,
              {columnDefs}
            );
            CREATE INDEX IF NOT EXISTS IX_Staff_StaffCode ON Staff(StaffCode);
            CREATE INDEX IF NOT EXISTS IX_Staff_FullName ON Staff(FullName);
            CREATE TABLE IF NOT EXISTS Settings (Key TEXT PRIMARY KEY, Value TEXT);
            CREATE TABLE IF NOT EXISTS Presets (Name TEXT PRIMARY KEY, Json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS ReminderState (Key TEXT PRIMARY KEY, LastMilestone INTEGER, Dismissed INTEGER NOT NULL DEFAULT 0);
            """);

        // Forward-compatible: add columns introduced by newer versions of StaffRecord.
        var existing = c.Query<string>("SELECT name FROM pragma_table_info('Staff')").ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var p in StoredProperties.Where(p => !existing.Contains(p.Name)))
            c.Execute($"ALTER TABLE Staff ADD COLUMN {p.Name} {SqlType(p.PropertyType)}");
    }

    private static string SqlType(Type t)
    {
        t = Nullable.GetUnderlyingType(t) ?? t;
        return t == typeof(int) || t == typeof(long) || t == typeof(bool) ? "INTEGER" : "TEXT";
    }

    // ------------------------------------------------------------------ Staff

    public List<StaffRecord> LoadAll()
    {
        using var c = Open();
        return c.Query<StaffRecord>("SELECT * FROM Staff ORDER BY IsFormer, Id").ToList();
    }

    public void Insert(StaffRecord r)
    {
        r.UpdatedAt = DateTime.Now;
        using var c = Open();
        r.Id = c.ExecuteScalar<long>($"INSERT INTO Staff ({Columns}) VALUES ({Parameters}); SELECT last_insert_rowid();", r);
    }

    public void Update(StaffRecord r)
    {
        r.UpdatedAt = DateTime.Now;
        using var c = Open();
        c.Execute($"UPDATE Staff SET {Assignments} WHERE Id = @Id", r);
    }

    public void Delete(IEnumerable<long> ids)
    {
        using var c = Open();
        c.Execute("DELETE FROM Staff WHERE Id = @Id", ids.Select(id => new { Id = id }));
    }

    /// <summary>Bulk insert in one transaction; optionally replaces all existing rows.</summary>
    public void InsertMany(IEnumerable<StaffRecord> records, bool replaceAll)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        if (replaceAll) c.Execute("DELETE FROM Staff", transaction: tx);
        var sql = $"INSERT INTO Staff ({Columns}) VALUES ({Parameters}); SELECT last_insert_rowid();";
        foreach (var r in records)
        {
            r.UpdatedAt = DateTime.Now;
            r.Id = c.ExecuteScalar<long>(sql, r, tx);
        }
        tx.Commit();
    }

    /// <summary>Copies the database using SQLite's online backup API.</summary>
    public void Backup(string destination)
    {
        if (File.Exists(destination)) File.Delete(destination);
        using var src = Open();
        using var dst = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString());
        dst.Open();
        src.BackupDatabase(dst);
    }

    public string AutoBackup(string reason)
    {
        var folder = Path.Combine(DataFolder, "backups");
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, $"staff_{DateTime.Now:yyyyMMdd_HHmmss}_{reason}.db");
        Backup(file);
        // Keep the 20 most recent automatic backups.
        foreach (var old in new DirectoryInfo(folder).GetFiles("staff_*.db").OrderByDescending(f => f.CreationTimeUtc).Skip(20))
            old.Delete();
        return file;
    }

    // ------------------------------------------------------------------ Settings & presets

    public string? GetSetting(string key)
    {
        using var c = Open();
        return c.ExecuteScalar<string?>("SELECT Value FROM Settings WHERE Key = @key", new { key });
    }

    public void SetSetting(string key, string? value)
    {
        using var c = Open();
        c.Execute("INSERT INTO Settings (Key, Value) VALUES (@key, @value) ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value",
            new { key, value });
    }

    public List<FilterPreset> LoadPresets()
    {
        using var c = Open();
        return c.Query<(string Name, string Json)>("SELECT Name, Json FROM Presets ORDER BY Name")
            .Select(p => new FilterPreset { Name = p.Name, Filter = JsonSerializer.Deserialize<FilterSet>(p.Json) ?? new FilterSet() })
            .ToList();
    }

    public void SavePreset(FilterPreset preset)
    {
        using var c = Open();
        c.Execute("INSERT INTO Presets (Name, Json) VALUES (@Name, @Json) ON CONFLICT(Name) DO UPDATE SET Json = excluded.Json",
            new { preset.Name, Json = JsonSerializer.Serialize(preset.Filter) });
    }

    public void DeletePreset(string name)
    {
        using var c = Open();
        c.Execute("DELETE FROM Presets WHERE Name = @name", new { name });
    }

    // ------------------------------------------------------------------ Reminders

    public Dictionary<string, ReminderState> LoadReminderStates()
    {
        using var c = Open();
        return c.Query<ReminderState>("SELECT Key, LastMilestone, Dismissed FROM ReminderState").ToDictionary(s => s.Key);
    }

    public void SaveReminderStates(IEnumerable<ReminderState> states)
    {
        using var c = Open();
        using var tx = c.BeginTransaction();
        c.Execute("""
            INSERT INTO ReminderState (Key, LastMilestone, Dismissed) VALUES (@Key, @LastMilestone, @Dismissed)
            ON CONFLICT(Key) DO UPDATE SET LastMilestone = excluded.LastMilestone, Dismissed = excluded.Dismissed
            """, states, tx);
        tx.Commit();
    }

    // Dates are stored as ISO text ("yyyy-MM-dd HH:mm:ss") so the file stays readable with any SQLite tool.
    private sealed class NullableDateHandler : SqlMapper.TypeHandler<DateTime?>
    {
        public override void SetValue(System.Data.IDbDataParameter parameter, DateTime? value) =>
            parameter.Value = value is { } d ? d.ToString("yyyy-MM-dd HH:mm:ss") : DBNull.Value;

        public override DateTime? Parse(object value) => value switch
        {
            string s when DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) => d,
            DateTime d => d,
            _ => null,
        };
    }

    private sealed class DateHandler : SqlMapper.TypeHandler<DateTime>
    {
        public override void SetValue(System.Data.IDbDataParameter parameter, DateTime value) =>
            parameter.Value = value.ToString("yyyy-MM-dd HH:mm:ss");

        public override DateTime Parse(object value) => value switch
        {
            string s when DateTime.TryParse(s, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var d) => d,
            DateTime d => d,
            _ => default,
        };
    }
}
