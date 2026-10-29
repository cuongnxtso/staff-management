namespace StaffManagement.Services;

/// <summary>
/// Matches a full name like "Phòng Quản lý Đào tạo Sau đại học" to the abbreviated values used
/// in the list (e.g. "Phòng QL ĐT SĐH"): a candidate word matches either a word of the value or
/// the initials of consecutive words ("quản lý" → "ql").
/// </summary>
public static class FuzzyMatcher
{
    public static string? BestMatch(string? value, IEnumerable<string> candidates, double threshold = 0.6)
    {
        var words = Words(value);
        if (words.Length == 0) return null;

        string? best = null;
        double bestScore = 0;
        foreach (var candidate in candidates)
        {
            var score = Score(words, Words(candidate));
            if (score > bestScore + 1e-9)
            {
                bestScore = score;
                best = candidate;
            }
        }
        return bestScore >= threshold ? best : null;
    }

    public static double Score(string? value, string? candidate) => Score(Words(value), Words(candidate));

    private static string[] Words(string? s) =>
        VietnameseText.Canonical(s)
            .Split([' ', ',', '-', '–', '&', '(', ')', '/', '.', ';', ':'], StringSplitOptions.RemoveEmptyEntries);

    private static double Score(string[] value, string[] candidate)
    {
        if (value.Length == 0 || candidate.Length == 0) return 0;
        var covered = new bool[value.Length];
        int matched = 0;
        foreach (var token in candidate)
        {
            if (MatchToken(token, value, covered)) matched++;
        }
        double candidateCoverage = (double)matched / candidate.Length;
        double valueCoverage = (double)covered.Count(c => c) / value.Length;
        return candidateCoverage * valueCoverage;
    }

    private static bool MatchToken(string token, string[] value, bool[] covered)
    {
        // Whole word, preferring an uncovered occurrence
        for (int i = 0; i < value.Length; i++)
            if (!covered[i] && value[i] == token) { covered[i] = true; return true; }

        // Initials of consecutive words ("sau đại học" → "sdh", "phó" → "p")
        int len = token.Length;
        if (len <= 5)
        {
            for (int i = 0; i + len <= value.Length; i++)
            {
                bool ok = true;
                for (int k = 0; k < len && ok; k++)
                    ok = !covered[i + k] && value[i + k][0] == token[k];
                if (!ok) continue;
                for (int k = 0; k < len; k++) covered[i + k] = true;
                return true;
            }
        }
        // Already-covered whole word (e.g. repeated tokens)
        for (int i = 0; i < value.Length; i++)
            if (value[i] == token) return true;
        return false;
    }
}
