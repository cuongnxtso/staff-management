using System.Text.RegularExpressions;
using StaffManagement.Models;

namespace StaffManagement.Services;

public enum DecisionAction { AddNew, UpdateRow, MoveToFormer }

/// <summary>Turns an extracted decision into a new row / an update of an existing row.</summary>
public static partial class DecisionApplier
{
    /// <summary>
    /// Rows of the same person: by Mã CB when the decision gives one, otherwise same name (accent-insensitive)
    /// and, when both are known, same birth date.
    /// </summary>
    public static List<StaffRecord> FindPerson(IEnumerable<StaffRecord> all, DecisionInfo info)
    {
        var list = all as IReadOnlyCollection<StaffRecord> ?? all.ToList();
        var code = info.StaffCode?.Trim();
        var byCode = string.IsNullOrEmpty(code) ? [] : list.Where(r => string.Equals(r.StaffCode?.Trim(), code, StringComparison.OrdinalIgnoreCase)).ToList();
        var name = VietnameseText.Normalize(info.FullName);
        if (byCode.Count == 0 && name.Length == 0) return [];
        var rows = byCode.Count > 0
            ? byCode
            : list.Where(r => VietnameseText.Normalize(r.FullName) == name
                              && (info.DateOfBirth is null || r.DateOfBirth is null || r.DateOfBirth.Value.Date == info.DateOfBirth.Value.Date));
        return rows
            .OrderBy(r => r.IsFormer)
            .ThenBy(r => r.IsRepeat) // main row first: personal details are copied from it
            .ToList();
    }

    public static StaffRecord? BestRow(IEnumerable<StaffRecord> rows, DecisionInfo info)
    {
        var target = $"{info.Position} {info.Department}";
        return rows.Where(r => !r.IsFormer)
            .Select(r => (Row: r, Score: FuzzyMatcher.Score(target, $"{r.Position} {r.Department}")))
            .OrderByDescending(x => x.Score)
            .FirstOrDefault(x => x.Score > 0.35).Row;
    }

    /// <summary>The person's row that already records this decision number (the decision was imported / typed before).</summary>
    public static StaffRecord? RowWithSameDecision(IEnumerable<StaffRecord> personRows, DecisionInfo info) =>
        string.IsNullOrWhiteSpace(info.DecisionNumber) ? null
            : personRows.FirstOrDefault(r => SameNumber(r.DecisionNumber, info.DecisionNumber));

    public static bool SameNumber(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && VietnameseText.Normalize(a).Replace(" ", "") == VietnameseText.Normalize(b).Replace(" ", "");

    /// <summary>"Phó Giám đốc" and "Phó Giám đốc - Phụ trách" are the same post; "P. Trưởng phòng" and "Trưởng phòng" are not.</summary>
    private static bool SameTitle(string? existing, string? decided)
    {
        string a = VietnameseText.Canonical(existing), b = VietnameseText.Canonical(decided);
        return b.Length > 0 && (a == b || a.StartsWith(b + " ") || a.StartsWith(b + ","));
    }

    public static (DecisionAction Action, StaffRecord? Target) Suggest(DecisionInfo info, List<StaffRecord> personRows)
    {
        // Already recorded → update that row instead of adding a duplicate.
        if (info.Kind != DecisionKind.Dismiss && RowWithSameDecision(personRows, info) is { } same)
            return (DecisionAction.UpdateRow, same);

        var best = BestRow(personRows, info);
        return info.Kind switch
        {
            DecisionKind.Dismiss => (DecisionAction.MoveToFormer, best ?? personRows.FirstOrDefault(r => !r.IsFormer)),
            DecisionKind.Reappoint when best is not null => (DecisionAction.UpdateRow, best),
            // Same title in the same unit → it is the same post (e.g. a re-issued decision); a promotion
            // ("P. Trưởng phòng" → "Trưởng phòng") is a new position.
            _ when best is not null
                   && SameTitle(best.Position, info.Position)
                   && FuzzyMatcher.Score(info.Department, best.Department) >= 0.75
                => (DecisionAction.UpdateRow, best),
            _ => (DecisionAction.AddNew, null),
        };
    }

    /// <summary>
    /// New row; personal details are copied from <paramref name="sameperson"/> when the person already exists.
    /// The Lặp flag is set by the caller with <see cref="PersonIdentity.RepeatFlagChanges"/>, because it depends on
    /// whether the person still has a main row in the active list (e.g. after their old post is retired).
    /// </summary>
    public static StaffRecord CreateRow(DecisionInfo info, StaffRecord? sameperson)
    {
        var r = sameperson is null ? new StaffRecord() : CopyPersonal(sameperson);
        r.StaffCode ??= info.StaffCode;
        r.ProfessionalTitle = info.ProfessionalTitle ?? r.ProfessionalTitle;
        r.FullName = info.FullName ?? r.FullName;
        r.DateOfBirth = info.DateOfBirth ?? r.DateOfBirth;
        r.Gender = info.Gender ?? r.Gender;
        r.AcademicRank = info.AcademicRank ?? r.AcademicRank;
        r.Degree = info.Degree ?? r.Degree;
        r.HomeUnit ??= info.HomeUnit;
        if (r.RetirementYear is null && RetirementCalculator.Suggest(r.DateOfBirth, r.Gender) is { } ret)
            (r.RetirementMonth, r.RetirementYear) = ret;
        ApplyAppointment(r, info, isNewPosition: true);
        return r;
    }

    public static void ApplyAppointment(StaffRecord r, DecisionInfo info, bool isNewPosition)
    {
        // Same post written more precisely in the list ("Phó Giám đốc - Phụ trách") → keep the list's wording.
        if (!string.IsNullOrWhiteSpace(info.Position) && !SameTitle(r.Position, info.Position)) r.Position = info.Position;
        // Personal details from the decision only fill blanks.
        r.StaffCode ??= info.StaffCode;
        r.ProfessionalTitle ??= info.ProfessionalTitle;
        r.HomeUnit ??= info.HomeUnit;
        if (!string.IsNullOrWhiteSpace(info.Department)) r.Department = info.Department;
        // A new QĐ on an existing row (reappointment): the old one goes to "QĐ bổ nhiệm trước" before it is overwritten.
        if (!string.IsNullOrWhiteSpace(info.DecisionNumber) && !SameNumber(r.DecisionNumber, info.DecisionNumber))
            KeepPreviousDecision(r, r);
        r.DecisionNumber = info.DecisionNumber ?? r.DecisionNumber;
        r.DecisionDate = info.DecisionDate ?? r.DecisionDate;
        r.AppointingUnit = info.AppointingUnit ?? r.AppointingUnit;
        r.AppointingLevel = info.AppointingLevel ?? r.AppointingLevel;

        if ((info.EffectiveDate ?? info.DecisionDate) is { } eff)
        {
            r.AppointDay = eff.Day;
            r.AppointMonth = eff.Month;
            r.AppointYear = eff.Year;
            r.AppointExpiryYear = eff.Year + (info.TermYears ?? 5);
            if (isNewPosition) r.PositionSinceYear = eff.Year;
        }
        if (info.Kind == DecisionKind.Reappoint) r.IsReappointed = true;
        r.IsFormer = false;
    }

    /// <summary>
    /// Appends the QĐ of <paramref name="old"/> (number, date, term) to <paramref name="r"/>'s "QĐ bổ nhiệm trước",
    /// e.g. "1234/QĐ-ĐHYHN ngày 01/07/2010 (BN 2010–2015)". Skipped when there is no number or it is already listed.
    /// </summary>
    public static void KeepPreviousDecision(StaffRecord r, StaffRecord old)
    {
        var number = old.DecisionNumber?.Trim();
        if (string.IsNullOrEmpty(number)) return;
        if (ClientHistoryHeaders() is { } headers)
        {
            ShiftIntoClientColumns(r, old, number, headers);
            return;
        }
        var listed = (r.PreviousDecisions ?? "").Split(';', StringSplitOptions.TrimEntries)
            .Any(e => e.Equals(number, StringComparison.OrdinalIgnoreCase) || e.StartsWith(number + " ", StringComparison.OrdinalIgnoreCase));
        if (listed) return;
        var entry = number
                    + (old.DecisionDate is { } d ? $" ngày {d:dd/MM/yyyy}" : "")
                    + (old.AppointYear is { } y ? $" (BN {y}{(old.AppointExpiryYear is { } e ? $"–{e}" : "")})" : "");
        r.PreviousDecisions = string.IsNullOrWhiteSpace(r.PreviousDecisions) ? entry : $"{r.PreviousDecisions}; {entry}";
    }

    // The client's own old-QĐ columns (V–Z in their list), kept as extra columns. The newest old QĐ is in "cũ1", its date
    // in "cũ2" (despite the name; a few rows hold an older QĐ there) and its issuer in "Đơn vị BN (cũ)". Older ones pile up
    // in "cũ 3", oldest first, with the newest of their dates in "ngày QĐ BN Cũ 3".
    private static readonly string[] ClientHistoryColumns =
        ["Số QĐ bổ nhiệm (cũ1)", "Số QĐ bổ nhiệm (cũ2)", "Đơn vị BN (cũ)", "Số QĐ bổ nhiệm (cũ 3)", "ngày QĐ BN Cũ 3"];

    private static string[]? ClientHistoryHeaders()
    {
        static string Key(string? h) => VietnameseText.Normalize(h).Replace(" ", "");
        var headers = ClientHistoryColumns.Select(c => FieldCatalog.Extras.FirstOrDefault(f => Key(f.Header) == Key(c))?.Header)
            .OfType<string>().ToArray();
        return headers.Length == ClientHistoryColumns.Length ? headers : null;
    }

    /// <summary>cũ1 moves down to cũ 3, then the replaced QĐ becomes cũ1.</summary>
    private static void ShiftIntoClientColumns(StaffRecord r, StaffRecord old, string number, string[] h)
    {
        string? no1 = r.GetExtra(h[0]), date1 = r.GetExtra(h[1]), no3 = r.GetExtra(h[3]), date3 = r.GetExtra(h[4]);
        if (Mentions(no1, number) || Mentions(date1, number) || Mentions(no3, number)) return;
        bool date1IsDate = !string.IsNullOrWhiteSpace(no1) && DateOnlyRegex().IsMatch(date1 ?? "");
        var moved = new[] { date1IsDate ? null : date1, no1 }.Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        if (moved.Count > 0)
        {
            var older = string.IsNullOrWhiteSpace(date3) || (no3 ?? "").Contains(date3.Trim()) ? no3 : $"{no3} ngày {date3}".Trim();
            r.SetExtra(h[3], string.Join("; ", new[] { older }.Concat(moved).Where(t => !string.IsNullOrWhiteSpace(t))));
            r.SetExtra(h[4], date1IsDate ? date1 : DateRegex().Matches(no1 ?? "").LastOrDefault()?.Value);
        }
        r.SetExtra(h[0], number);
        r.SetExtra(h[1], old.DecisionDate?.ToString("dd/MM/yyyy"));
        r.SetExtra(h[2], old.AppointingUnit);
    }

    /// <summary>Whether a free-text cell already lists this QĐ number ("12/QĐ" is not in "112/QĐ-ĐHYHN").</summary>
    private static bool Mentions(string? text, string number) =>
        Regex.IsMatch(VietnameseText.Normalize(text), $@"(?<![\p{{L}}\d]){Regex.Escape(VietnameseText.Normalize(number))}(?![\p{{L}}\d&-])");

    [GeneratedRegex(@"^\s*'?\d{1,2}/\d{1,2}/\d{4,5}\s*$")]
    private static partial Regex DateOnlyRegex();

    [GeneratedRegex(@"(?<!\d)\d{1,2}/\d{1,2}/\d{4}")]
    private static partial Regex DateRegex();

    public static void MoveToFormer(StaffRecord r, DecisionInfo info)
    {
        r.IsFormer = true;
        var when = info.EffectiveDate ?? info.DecisionDate;
        var note = $"Thôi giữ chức vụ theo QĐ {info.DecisionNumber}{(info.DecisionDate is { } d ? $" ngày {d:dd/MM/yyyy}" : "")}"
                   + (when is { } w && w != info.DecisionDate ? $", từ {w:dd/MM/yyyy}" : "");
        r.Notes = string.IsNullOrWhiteSpace(r.Notes) ? note : $"{r.Notes}; {note}";
    }

    private static StaffRecord CopyPersonal(StaffRecord s) => new()
    {
        StaffCode = s.StaffCode,
        FullName = s.FullName,
        DateOfBirth = s.DateOfBirth,
        Gender = s.Gender,
        RetirementMonth = s.RetirementMonth,
        RetirementYear = s.RetirementYear,
        PartyJoinDate = s.PartyJoinDate,
        PartyOfficialDate = s.PartyOfficialDate,
        AcademicRank = s.AcademicRank,
        Degree = s.Degree,
        PoliticalTheory = s.PoliticalTheory,
        HomeUnit = s.HomeUnit,
        Phone = s.Phone,
        AllPositions = s.AllPositions,
        ProfessionalTitle = s.ProfessionalTitle,
        IsConcurrent = s.IsConcurrent,
    };
}
