namespace StaffManagement.Models;

public enum DecisionKind { Appoint, Reappoint, Assign, Dismiss }

/// <summary>Information extracted from an appointment decision (Quyết định).</summary>
public sealed class DecisionInfo
{
    public DecisionKind Kind { get; set; } = DecisionKind.Appoint;
    /// <summary>"(MCB: 01079)" — when present, the most reliable way to find the person.</summary>
    public string? StaffCode { get; set; }
    public string? FullName { get; set; }
    /// <summary>Chức danh nghề nghiệp, e.g. "Giảng viên chính (hạng II)".</summary>
    public string? ProfessionalTitle { get; set; }
    /// <summary>Things the reviewer must check by hand (e.g. number missing from the text).</summary>
    public List<string> Warnings { get; set; } = [];
    public string? Gender { get; set; }
    public DateTime? DateOfBirth { get; set; }
    public string? AcademicRank { get; set; }
    public string? Degree { get; set; }
    /// <summary>Raw "giữ chức vụ ..." text, before splitting into position / department.</summary>
    public string? PositionText { get; set; }
    public string? Position { get; set; }
    public string? Department { get; set; }
    public string? HomeUnit { get; set; }
    public string? DecisionNumber { get; set; }
    public DateTime? DecisionDate { get; set; }
    public DateTime? EffectiveDate { get; set; }
    public int? TermYears { get; set; }
    public string? AppointingUnit { get; set; }
    public string? AppointingLevel { get; set; }
}
