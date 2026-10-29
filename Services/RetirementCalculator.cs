namespace StaffManagement.Services;

/// <summary>
/// Standard retirement age under Nghị định 135/2020/NĐ-CP:
/// men 60y3m in 2021 (+3 months/year, capped at 62 from 2028),
/// women 55y4m in 2021 (+4 months/year, capped at 60 from 2035).
/// Retirement starts the month after reaching that age.
/// This is only a suggestion — special cases (e.g. extended service) are entered manually.
/// </summary>
public static class RetirementCalculator
{
    public static (int Month, int Year)? Suggest(DateTime? birth, string? gender)
    {
        if (birth is not { } b) return null;
        bool female = VietnameseText.Normalize(gender) == "nu";

        DateTime reach = b.AddMonths(RequiredMonths(female, b.Year + 60));
        for (int year = b.Year + 55; year <= b.Year + 63; year++)
        {
            var candidate = b.AddMonths(RequiredMonths(female, year));
            if (candidate.Year == year) { reach = candidate; break; }
        }
        var start = new DateTime(reach.Year, reach.Month, 1).AddMonths(1);
        return (start.Month, start.Year);
    }

    private static int RequiredMonths(bool female, int year)
    {
        int over = Math.Max(0, year - 2020);
        return female ? Math.Min(55 * 12 + 4 * over, 60 * 12) : Math.Min(60 * 12 + 3 * over, 62 * 12);
    }
}
