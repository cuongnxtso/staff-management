using StaffManagement.Services;

namespace StaffManagement.Models;

/// <summary>
/// One row of the leadership list = one position held by one person.
/// The same person (Mã CB) can appear in several rows; in each section exactly one of them is the
/// main row and the others are marked "Lặp" (see <see cref="PersonIdentity"/>).
/// Column letters refer to the original Excel template.
/// </summary>
public class StaffRecord
{
    public long Id { get; set; }

    /// <summary>True when the row belongs to the "THÔI GIỮ CHỨC VỤ" section.</summary>
    public bool IsFormer { get; set; }

    public int? Stt { get; set; }                       // A  TT
    public bool IsConcurrent { get; set; }              // B  KN
    public bool IsRepeat { get; set; }                  // C  Lặp
    public string? StaffCode { get; set; }              // D  Mã CB
    public string? FullName { get; set; }               // E  Họ và tên
    public DateTime? DateOfBirth { get; set; }          // F  Ngày, tháng, năm sinh
    public int? RetirementMonth { get; set; }           // J  Thời điểm nghỉ hưu - Tháng
    public int? RetirementYear { get; set; }            // K  Thời điểm nghỉ hưu - Năm
    public string? Gender { get; set; }                 // M  Giới
    public string? PartyJoinDate { get; set; }          // N  Đảng viên - Ngày KN (text: may be "Chưa", "có")
    public string? PartyOfficialDate { get; set; }      // O  Đảng viên - Ngày CT
    public string? AcademicRank { get; set; }           // P  Học hàm
    public string? Degree { get; set; }                 // Q  Trình độ chuyên môn
    public string? PoliticalTheory { get; set; }        // R  Trình độ LLCT
    public string? Position { get; set; }               // S  Chức vụ ĐHYHN
    public string? Department { get; set; }             // T  Đơn vị
    public string? DecisionNumber { get; set; }         // U  Số QĐ bổ nhiệm (hiện tại)
    public DateTime? DecisionDate { get; set; }         // V  Ngày QĐ
    public int? AppointDay { get; set; }                // W  Thời điểm - Từ ngày
    public int? AppointMonth { get; set; }              // X  Thời điểm - Từ tháng
    public int? AppointYear { get; set; }               // Y  Thời điểm - Năm BN
    public int? AppointExpiryYear { get; set; }         // Z  Thời điểm - Hết hạn BN
    public string? AppointingUnit { get; set; }         // AA Đơn vị BN
    public string? AppointingLevel { get; set; }        // AB Cấp BN
    public bool IsReappointed { get; set; }             // AC Đã BN lại
    public bool IsConcurrentPost { get; set; }          // AD Kiêm chức
    public int? PositionSinceYear { get; set; }         // AE Giữ CV từ
    public int? StoredYearsInPosition { get; set; }     // AF (only used when it cannot be computed)
    public int? StoredYearsInTerm { get; set; }         // AG (only used when it cannot be computed)
    public string? HomeUnit { get; set; }               // AH Đơn vị cơ hữu
    public string? Notes { get; set; }                  // AI Ghi chú
    public string? Phone { get; set; }                  // AJ Điện thoại
    public string? AllPositions { get; set; }           // AK Tổng hợp chức vụ trong Trường
    public string? PracticeSitePosition { get; set; }   // AL Chức vụ tại CSTH
    public string? ExtraNote { get; set; }              // AM (no header in template)
    public string? ProfessionalTitle { get; set; }      // AN (no header in template, e.g. "Giảng viên cao cấp (hạng I)")
    public DateTime UpdatedAt { get; set; }

    // ---- Computed columns (formulas in the Excel template) ----

    public int? BirthDay => DateOfBirth?.Day;
    public int? BirthMonth => DateOfBirth?.Month;
    public int? BirthYear => DateOfBirth?.Year;

    /// <summary>Same rule as the template: YEAR(TODAY()) - YEAR(birth).</summary>
    public int? Age => DateOfBirth is { } d ? DateTime.Today.Year - d.Year : null;

    public int? YearsInPosition =>
        !IsFormer && PositionSinceYear is { } y ? DateTime.Today.Year - y : StoredYearsInPosition;

    public int? YearsInTerm =>
        !IsFormer && AppointYear is { } y ? DateTime.Today.Year - y : StoredYearsInTerm;

    public bool IsPartyMember => VietnameseText.IsPartyMemberText(PartyJoinDate);

    public DateTime? RetirementDate =>
        RetirementYear is int y && RetirementMonth is int m && m is >= 1 and <= 12 && y is > 1900 and < 2200
            ? new DateTime(y, m, 1) : null;

    public int? MonthsToRetirement => RetirementDate is { } r
        ? (r.Year - DateTime.Today.Year) * 12 + r.Month - DateTime.Today.Month
        : null;

    public DateTime? EffectiveDate
    {
        get
        {
            if (AppointYear is not int y || y < 1900) return null;
            int m = AppointMonth is int mm && mm is >= 1 and <= 12 ? mm : 1;
            int d = AppointDay is int dd && dd >= 1 ? Math.Min(dd, DateTime.DaysInMonth(y, m)) : 1;
            return new DateTime(y, m, d);
        }
    }

    /// <summary>
    /// Last day of the current term: the day before the appointment anniversary in the expiry year
    /// (BN từ 01/10/2024, hết hạn 2029 → 30/09/2029). Without an appointment day/month the decision date is used;
    /// without either, 01/01 of the expiry year (remind early rather than late — see <see cref="IsTermEndEstimated"/>).
    /// </summary>
    public DateTime? TermEndDate
    {
        get
        {
            if (AppointExpiryYear is not int y || y is < 1900 or > 2200) return null;
            int month, day;
            if (AppointMonth is int m && m is >= 1 and <= 12)
            {
                month = m;
                day = AppointDay is int d && d >= 1 ? d : 1;
            }
            else if (DecisionDate is { } dd)
            {
                month = dd.Month;
                day = dd.Day;
            }
            else
            {
                return new DateTime(y, 1, 1);
            }
            return new DateTime(y, month, Math.Min(day, DateTime.DaysInMonth(y, month))).AddDays(-1);
        }
    }

    public bool IsTermEndEstimated => AppointExpiryYear is not null && AppointMonth is not (>= 1 and <= 12) && DecisionDate is null;

    public int? DaysToTermEnd => TermEndDate is { } e ? (e - DateTime.Today).Days : null;

    public string Status => IsFormer ? "Đã thôi giữ CV" : "Đang giữ CV";

    public StaffRecord Clone() => (StaffRecord)MemberwiseClone();
}
