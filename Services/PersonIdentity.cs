using StaffManagement.Models;

namespace StaffManagement.Services;

/// <summary>
/// Who is who across rows, and the "Lặp" convention of the template: within a section (đang giữ / đã thôi)
/// each person has exactly one main row (Lặp empty); their other position rows are marked Lặp.
/// Counting the rows without Lặp therefore counts people.
/// </summary>
public static class PersonIdentity
{
    /// <summary>
    /// Person key of every row. Rows belong to the same person when they share a Mã CB, or have the same
    /// name + birth date (unless both rows carry different codes) — so a row with a missing Mã CB still joins its person.
    /// </summary>
    public static Dictionary<StaffRecord, string> Build(IEnumerable<StaffRecord> rows)
    {
        var list = rows.ToList();
        var parent = Enumerable.Range(0, list.Count).ToArray();
        int Find(int i)
        {
            while (parent[i] != i) i = parent[i] = parent[parent[i]];
            return i;
        }
        void Union(int a, int b) => parent[Find(a)] = Find(b);

        foreach (var g in Enumerable.Range(0, list.Count).Where(i => Code(list[i]) is not null).GroupBy(i => Code(list[i])))
            foreach (var i in g.Skip(1)) Union(g.First(), i);

        foreach (var g in Enumerable.Range(0, list.Count).Where(i => NameDob(list[i]) is not null).GroupBy(i => NameDob(list[i])))
        {
            bool conflictingCodes = g.Select(i => Code(list[i])).OfType<string>().Distinct().Count() > 1;
            var members = conflictingCodes ? g.Where(i => Code(list[i]) is null).ToList() : g.ToList();
            foreach (var i in members.Skip(1)) Union(members[0], i);
        }

        var result = new Dictionary<StaffRecord, string>(list.Count);
        for (int i = 0; i < list.Count; i++) result[list[i]] = "P" + Find(i);
        return result;
    }

    private static string? Code(StaffRecord r) =>
        string.IsNullOrWhiteSpace(r.StaffCode) ? null : r.StaffCode.Trim().ToUpperInvariant();

    private static string? NameDob(StaffRecord r) =>
        r.DateOfBirth is { } d && !string.IsNullOrWhiteSpace(r.FullName) ? $"{VietnameseText.Normalize(r.FullName)}|{d:yyyyMMdd}" : null;

    /// <summary>Main row first: active before former, then rows without Lặp.</summary>
    public static StaffRecord MainRow(IEnumerable<StaffRecord> personRows) =>
        personRows.OrderBy(r => r.IsFormer).ThenBy(r => r.IsRepeat).First();

    /// <summary>Data problems with the Lặp convention / person identity, for the import report.</summary>
    public static List<string> CheckRepeatFlags(IReadOnlyList<StaffRecord> rows)
    {
        var ids = Build(rows);
        var warnings = new List<string>();
        static string Describe(StaffRecord r) => $"TT {r.Stt?.ToString() ?? "?"} – {r.Position}";

        foreach (var person in rows.GroupBy(r => ids[r]))
        {
            var name = person.First().FullName;
            foreach (var section in person.GroupBy(r => r.IsFormer))
            {
                var label = section.Key ? "mục Thôi giữ CV" : "danh sách đang giữ CV";
                var main = section.Where(r => !r.IsRepeat).ToList();
                // A former row marked Lặp for someone still in the active list is a deliberate cross-reference.
                bool listedAsActive = section.Key && person.Any(r => !r.IsFormer);
                if (main.Count == 0 && !listedAsActive)
                    warnings.Add($"Lặp: {name} ({label}) không có dòng chính — cả {section.Count()} dòng đều đánh dấu Lặp ({string.Join("; ", section.Select(Describe))}).");
                else if (main.Count > 1)
                    warnings.Add($"Lặp: {name} ({label}) có {main.Count} dòng không đánh dấu Lặp ({string.Join("; ", main.Select(Describe))}) — có thể thiếu \"x\" ở cột Lặp, cán bộ sẽ bị đếm 2 lần nếu đếm theo dòng không Lặp.");
            }

            var codes = person.Select(Code).OfType<string>().Distinct().ToList();
            var missing = person.Where(r => Code(r) is null).ToList();
            if (codes.Count == 1 && missing.Count > 0)
                warnings.Add($"Mã CB: {name} thiếu Mã CB ở {string.Join("; ", missing.Select(Describe))} (dòng khác ghi {codes[0]}) — đã được nhận là cùng một cán bộ.");
        }
        return warnings;
    }

    /// <summary>
    /// Rows whose Lặp flag must be flipped so that each affected person has exactly one main row per section.
    /// <paramref name="incoming"/> = rows just added / moved by the app: they become Lặp when the person already has
    /// a main row there. When a section has no main row left, an existing row is promoted.
    /// Pre-existing duplicates of main rows are left alone (they are reported by <see cref="CheckRepeatFlags"/>).
    /// </summary>
    public static List<StaffRecord> RepeatFlagChanges(IReadOnlyList<StaffRecord> all, IEnumerable<StaffRecord> affected,
        IReadOnlySet<StaffRecord> incoming)
    {
        var ids = Build(all);
        var changes = new List<StaffRecord>();
        foreach (var person in affected.Where(ids.ContainsKey).Select(r => ids[r]).Distinct())
        {
            foreach (var section in all.Where(r => ids[r] == person).GroupBy(r => r.IsFormer))
            {
                var rows = section.ToList();
                var main = rows.Where(r => !r.IsRepeat).ToList();
                if (main.Count == 0)
                {
                    changes.Add(rows.FirstOrDefault(r => !incoming.Contains(r)) ?? rows[0]);
                    continue;
                }
                int remaining = main.Count;
                foreach (var r in main.Where(incoming.Contains))
                {
                    if (remaining <= 1) break;
                    changes.Add(r);
                    remaining--;
                }
            }
        }
        return changes;
    }
}
