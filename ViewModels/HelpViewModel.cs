using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using StaffManagement.Services;

namespace StaffManagement.ViewModels;

/// <summary>One step on a help page: the badge is its number, or the key for a shortcut list.</summary>
public sealed record HelpStep(string Badge, string Text);

/// <summary>
/// One page of the user manual: what it is for, the steps, and tips.
/// With <paramref name="KeyList"/>, each step is "key — action" and the key is shown in the badge.
/// </summary>
public sealed record HelpTopic(string Icon, string Title, string Summary, string[] Steps, string[] Tips, bool KeyList = false)
{
    public IReadOnlyList<HelpStep> StepItems { get; } = Steps
        .Select((s, i) => KeyList && s.Split(" — ", 2) is [var key, var text] ? new HelpStep(key, text) : new HelpStep($"{i + 1}", s))
        .ToList();

    public bool HasTips => Tips.Length > 0;

    /// <summary>Accent-free text of the whole page, for the search box.</summary>
    public string SearchText { get; } = VietnameseText.Normalize(string.Join(" ", [Title, Summary, .. Steps, .. Tips]));
}

/// <summary>Help window (F1 / "?" button): topic list with an accent-insensitive search.</summary>
public partial class HelpViewModel : ObservableObject
{
    public ObservableCollection<HelpTopic> Topics { get; } = new(All);

    [ObservableProperty] private string? _query;
    [ObservableProperty] private HelpTopic? _selected = All[0];

    public bool NothingFound => Topics.Count == 0;

    partial void OnQueryChanged(string? value)
    {
        var words = VietnameseText.Normalize(value).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var keep = Selected;
        Topics.Clear();
        foreach (var t in All.Where(t => words.All(t.SearchText.Contains))) Topics.Add(t);
        Selected = keep is not null && Topics.Contains(keep) ? keep : Topics.FirstOrDefault();
        OnPropertyChanged(nameof(NothingFound));
    }

    public static IReadOnlyList<HelpTopic> All { get; } =
    [
        new("🚀", "Bắt đầu nhanh",
            "Bốn bước để có danh sách cần tìm và xuất ra Excel.",
            [
                "Bấm \"📥 Nhập Excel\" (Ctrl+O) và chọn tệp danh sách — hoặc kéo thả tệp .xlsx vào cửa sổ.",
                "Chọn trang ở dưới danh sách: \"Đang giữ chức vụ\" hoặc \"Thôi giữ chức vụ\" (giống các sheet trong Excel).",
                "Gõ vào ô \"Tìm nhanh\", hoặc gõ câu hỏi vào ô \"Hỏi nhanh\" rồi Enter, hoặc thêm điều kiện ở \"Bộ lọc nâng cao\".",
                "Bấm \"📤 Xuất Excel\" (Ctrl+E) để xuất đúng danh sách đang lọc, giữ nguyên vị trí cột như tệp đã nhập.",
            ],
            [
                "Nhấp đúp vào một dòng để xem chi tiết đầy đủ thông tin của cán bộ đó (bấm Hủy hoặc Esc để đóng mà không thay đổi gì).",
                "Bấm F1 ở bất kỳ đâu để mở lại hướng dẫn này. Cửa sổ hướng dẫn có thể để mở bên cạnh trong lúc làm việc.",
                "Rê chuột lên một nút để xem giải thích ngắn.",
                "Dữ liệu được tự động sao lưu trước mỗi lần thay thế hoặc xóa.",
                "Phiên bản đang dùng hiện trên thanh tiêu đề (ví dụ \"v1.2.0\"). Chỉ nên giữ 1 bản: khi cập nhật, thay tệp .exe cũ bằng tệp mới — dữ liệu vẫn giữ nguyên.",
                "Chỉ mở 1 cửa sổ phần mềm: nếu phần mềm báo đang mở ở cửa sổ khác, chọn \"Không\" và dùng cửa sổ đó, tránh mất thay đổi.",
            ]),

        new("📥", "Nhập dữ liệu từ Excel",
            "Nạp danh sách cán bộ từ tệp Excel theo mẫu \"Danh sách cán bộ lãnh đạo, quản lý\".",
            [
                "Bấm \"📥 Nhập Excel\" (Ctrl+O) hoặc kéo thả tệp .xlsx vào cửa sổ.",
                "Lần đầu: toàn bộ danh sách được nạp.",
                "Những lần sau phần mềm hỏi: \"Có\" = THAY THẾ toàn bộ dữ liệu hiện có, \"Không\" = THÊM vào dữ liệu hiện có.",
                "Đọc bảng cảnh báo sau khi nhập (dòng thiếu ngày sinh, cột Lặp chưa đúng, cột bổ sung…).",
            ],
            [
                "Phần mềm tìm cột theo TÊN tiêu đề, không theo vị trí — đổi thứ tự cột trong Excel vẫn nhập đúng.",
                "Riêng 2 cột không có tiêu đề trong mẫu (Ghi chú thêm, Chức danh nghề nghiệp — cột AM, AN) được tìm theo vị trí: nên thêm cột mới vào SAU cột cuối cùng; nếu chèn trước chúng, 2 cột này sẽ bị bỏ qua (có cảnh báo).",
                "Cột anh/chị tự thêm vào Excel (ví dụ \"Trưởng phòng\", \"Phó phòng\") cũng được giữ lại thành \"cột bổ sung\" — xem mục \"Cột bổ sung\".",
                "Cột tiêu đề \"Hạng\" được hiểu là cột \"Chức danh nghề nghiệp\".",
                "Vị trí các cột của tệp được ghi nhớ: khi xuất Excel, mỗi cột nằm đúng chỗ như trong tệp đã nhập.",
                "Trước khi thay thế, dữ liệu cũ được sao lưu tự động; có thể khôi phục trong \"⚙ Cài đặt\".",
            ]),

        new("🔎", "Tìm nhanh",
            "Tìm ngay theo họ tên, mã CB, chức vụ, đơn vị hoặc số điện thoại.",
            [
                "Gõ vào ô \"Tìm nhanh\" trên cùng — danh sách lọc ngay khi gõ.",
                "Xóa nội dung ô để hiện lại toàn bộ.",
            ],
            [
                "Không cần gõ dấu: \"nguyen van\" tìm được \"Nguyễn Văn\".",
                "Tìm nhanh kết hợp được với Bộ lọc nâng cao và trang đang chọn.",
            ]),

        new("💬", "Hỏi nhanh bằng câu tiếng Việt",
            "Gõ câu hỏi như nói chuyện, phần mềm tự đổi thành các điều kiện lọc.",
            [
                "Gõ câu hỏi vào ô \"Hỏi nhanh\", ví dụ: \"nữ từ 40 đến 55 tuổi là đảng viên PGS\", \"trưởng bộ môn nghỉ hưu năm nay\", \"hết hạn bổ nhiệm năm 2027\".",
                "Lọc theo một cột bất kỳ (kể cả cột tự thêm trong Excel): gõ TÊN CỘT rồi GIÁ TRỊ, ví dụ \"Dân tộc Tày\", \"phân loại chức vụ Trưởng\", \"mã CB TG1195\", \"năm sinh 1970\", \"hạng I\", \"đơn vị cơ hữu Bạch Mai\".",
                "Cột đánh dấu x: chỉ gõ tên cột (\"chức vụ cơ hữu\") để lấy các dòng có x; thêm \"không\" (\"chức vụ cơ hữu không\") để lấy các dòng trống.",
                "Ghép nhiều điều kiện bằng dấu phẩy: \"dân tộc Tày, nữ, trên 50 tuổi\".",
                "Nhấn Enter hoặc bấm \"Tạo bộ lọc\".",
                "Kiểm tra các điều kiện vừa tạo trong \"Bộ lọc nâng cao\" và sửa lại nếu cần.",
            ],
            [
                "Các cách gõ phần mềm hiểu: giới (nữ, nam); tuổi (trên 50 tuổi, dưới 40 tuổi, từ 40 đến 55 tuổi); đảng viên / chưa là đảng viên; GS, PGS, TS, ThS; sinh năm 1970, sinh trước 1965; nghỉ hưu năm nay / năm 2027 / trong 12 tháng; hết hạn bổ nhiệm năm 2027 / trong 60 ngày / sắp hết hạn; đã thôi; kiêm nhiệm; bổ nhiệm lại; tên chức vụ (trưởng bộ môn, phó trưởng khoa…); thuộc + tên đơn vị.",
                "\"thuộc Khoa Y học cổ truyền\" (hoặc \"thuộc YHCT\") lấy cả các đơn vị trực thuộc khoa.",
                "Báo \"Không nhận ra điều kiện nào\": thêm tên cột trước giá trị — \"chức danh bác sĩ cao cấp\" thay vì \"bác sĩ cao cấp\" — hoặc dùng Bộ lọc nâng cao.",
                "Giá trị phải viết giống trong danh sách: \"cấp BN BYT\" (danh sách ghi BYT, không ghi Bộ Y tế). Không cần gõ dấu.",
                "Không cần AI: phần mềm có bộ phân tích từ khóa chạy offline. Bật AI trong \"⚙ Cài đặt\" để hiểu được câu phức tạp hơn.",
                "Câu hỏi chỉ tạo điều kiện lọc, không làm thay đổi dữ liệu.",
            ]),

        new("🧰", "Bộ lọc nâng cao",
            "Kết hợp nhiều điều kiện theo bất kỳ cột nào, lưu lại để dùng lần sau.",
            [
                "Mở \"Bộ lọc nâng cao\", bấm \"➕ Thêm điều kiện\".",
                "Chọn cột → phép so sánh (Chứa, Bằng, Lớn hơn, Trong khoảng, Trống…) → giá trị.",
                "Chọn \"Thỏa TẤT CẢ (AND)\" hoặc \"Thỏa BẤT KỲ (OR)\" khi có nhiều điều kiện.",
                "Bấm \"💾 Lưu bộ lọc\" để đặt tên và dùng lại.",
                "Dùng lại: bấm ô \"Bộ lọc đã lưu\" — bộ lọc mới lưu nằm trên cùng; gõ vài chữ trong tên (không cần dấu) để tìm, rồi nhấp chọn hoặc nhấn Enter.",
                "Chuyển bộ lọc sang máy khác: trong ô \"Bộ lọc đã lưu\" bấm \"⤓ Xuất…\" để lưu tất cả ra tệp .json; ở máy kia bấm \"⤒ Nhập…\" và chọn tệp đó.",
                "Bấm \"⟲ Xóa toàn bộ lọc\" để quay về toàn bộ danh sách.",
            ],
            [
                "Cột dạng tích (KN, Lặp, Đã BN lại, Kiêm chức…) lọc bằng \"Có\" / \"Không\". Ví dụ bỏ các dòng có tích ở cột Lặp: Lặp → Không.",
                "Cột bổ sung (như \"Trưởng phòng\" có đánh dấu x): dùng \"Không trống\" để lấy các dòng có đánh dấu, \"Trống\" để lấy các dòng không có.",
                "Muốn lấy cả bộ môn / phòng trực thuộc một Khoa, Viện, Trung tâm, Phân hiệu: lọc theo \"Đơn vị cấp trên\" thay cho \"Đơn vị\".",
                "Lọc \"Chức vụ Bằng Trưởng phòng\" để không lẫn \"Phó trưởng phòng\" (vì \"Chứa\" sẽ lấy cả hai).",
                "Phần mềm có sẵn một số bộ lọc mẫu (sắp hết hạn bổ nhiệm, nghỉ hưu trong 12 tháng tới, trưởng phòng…), nằm cuối danh sách. Xóa bộ lọc mẫu không cần dùng — nó sẽ không tự thêm lại.",
                "Khi nhập: bộ lọc trùng tên và giống hệt được bỏ qua; trùng tên nhưng khác điều kiện thì phần mềm hỏi giữ bộ lọc cũ hay thay bằng bộ lọc trong tệp.",
                "Điều kiện theo cột mà dữ liệu không có (ví dụ cột tự thêm ở máy khác) được bỏ qua, có thông báo ở thanh trạng thái.",
            ]),

        new("👤", "Mỗi cán bộ 1 dòng (cột Lặp)",
            "Một người giữ nhiều chức vụ có nhiều dòng; các dòng phụ được đánh dấu ở cột Lặp.",
            [
                "Tích \"Mỗi cán bộ 1 dòng\" (phía trên bảng, bên phải) để ẩn các dòng Lặp — mỗi cán bộ chỉ còn dòng chức vụ chính.",
                "Bỏ tích để hiện lại đầy đủ các dòng chức vụ.",
            ],
            [
                "Nếu dòng chính không thỏa bộ lọc, phần mềm hiện dòng chức vụ khác của người đó có thỏa bộ lọc.",
                "Áp dụng cả khi xuất Excel.",
                "Phần mềm tự giữ đúng cột Lặp khi thêm, chuyển hoặc xóa dòng: mỗi cán bộ có đúng 1 dòng chính trong mỗi trang.",
            ]),

        new("🏢", "Đơn vị cấp trên (gồm đơn vị trực thuộc)",
            "Gom các bộ môn, phòng, trung tâm trực thuộc về Viện, Trung tâm, Khoa, Phân hiệu.",
            [
                "Nhanh nhất: gõ vào ô \"Hỏi nhanh\" \"thuộc Khoa Y học cổ truyền\" (hoặc \"thuộc YHCT\") rồi Enter.",
                "Trong \"Bộ lọc nâng cao\": chọn cột \"Đơn vị cấp trên\" → Bằng → ví dụ \"Khoa Y học cổ truyền\". Kết quả gồm cả các dòng \"YHCT - BM …\".",
                "Trong tab \"Thống kê\": chọn \"Đơn vị cấp trên (gồm đơn vị trực thuộc)\" để đếm theo Viện / Khoa / Trung tâm / Phân hiệu.",
                "Muốn xem cột này trong bảng: bật nó ở \"▦ Cột hiển thị\".",
            ],
            [
                "Phần mềm nhận đơn vị trực thuộc theo phần viết tắt trước dấu \"-\" ở cột Đơn vị (YHCT, KTYH, ĐDHS, VĐTYHDP&YTCC, Viện ĐTRHM, PH, TTĐT&CGCN, TTKCCLXNYH).",
                "Đơn vị không trực thuộc ai thì Đơn vị cấp trên chính là đơn vị đó.",
                "Nếu danh sách dùng một cách viết tắt mới, báo cho bộ phận kỹ thuật để bổ sung.",
            ]),

        new("✏", "Thêm, sửa, xóa cán bộ",
            "Cập nhật danh sách trực tiếp trong phần mềm.",
            [
                "Thêm: bấm \"➕ Thêm cán bộ\" (Ctrl+N).",
                "Sửa: nhấp đúp vào dòng, hoặc chọn dòng rồi nhấn Enter / bấm \"✎ Sửa\".",
                "Cán bộ thôi giữ chức vụ: chọn dòng rồi bấm \"⇄ Đã thôi / Khôi phục\" — dòng chuyển sang trang \"Thôi giữ chức vụ\".",
                "Xóa: chọn dòng rồi nhấn Delete / bấm \"🗑 Xóa\".",
            ],
            [
                "Xem chi tiết một cán bộ: nhấp đúp vào dòng — cửa sổ hiện đầy đủ thông tin theo nhóm; bấm Hủy hoặc Esc để đóng mà không thay đổi gì.",
                "Nên dùng \"Đã thôi\" thay cho Xóa để giữ lịch sử.",
                "Sửa thông tin cá nhân (họ tên, ngày sinh…) của người có nhiều dòng: phần mềm hỏi có cập nhật luôn các dòng còn lại không.",
                "Chọn nhiều dòng bằng Ctrl+nhấp hoặc Shift+nhấp; nhấp chuột phải để có thêm lệnh.",
            ]),

        new("📄", "Đọc Quyết định (QĐ) bổ nhiệm",
            "Đọc QĐ từ PDF / Word và cập nhật vào danh sách, không cần gõ lại.",
            [
                "Kéo thả tệp QĐ (.pdf, .docx, .txt) vào cửa sổ, hoặc bấm ô \"Kéo thả Quyết định\" (Ctrl+D).",
                "Đối chiếu các thông tin phần mềm đọc được với văn bản gốc hiển thị bên cạnh; sửa nếu cần.",
                "Chọn cách cập nhật: \"Thêm dòng chức vụ mới\", \"Cập nhật dòng đã có\" hoặc \"Chuyển dòng sang Đã thôi giữ chức vụ\".",
                "Bấm \"💾 Lưu vào dữ liệu\".",
            ],
            [
                "Bổ nhiệm lại cùng chức vụ: chọn \"Cập nhật dòng đã có\" — QĐ cũ được tự động lưu lại (xem mục \"QĐ bổ nhiệm trước\").",
                "Được đề bạt sang chức vụ mới: chọn \"Thêm dòng chức vụ mới\" và tích \"Đồng thời chuyển chức vụ cũ sang Đã thôi\".",
                "Có thể chọn nhiều tệp QĐ cùng lúc; phần mềm xem lần lượt từng tệp.",
            ]),

        new("🗂", "QĐ bổ nhiệm trước (lịch sử QĐ)",
            "Một người được bổ nhiệm lại vẫn giữ 1 dòng; QĐ cũ được tự động lưu lại trước khi thay bằng QĐ mới.",
            [
                "Khi một dòng nhận QĐ mới (qua \"Đọc Quyết định\" → \"Cập nhật dòng đã có\"), QĐ đang ghi trên dòng được lưu lại trước khi thay.",
                "Trong cửa sổ Sửa: đổi CẢ Số QĐ và Ngày QĐ thì QĐ cũ cũng được lưu lại.",
                "Tệp Excel có các cột \"Số QĐ bổ nhiệm (cũ1)\", \"(cũ2)\", \"Đơn vị BN (cũ)\", \"(cũ 3)\", \"ngày QĐ BN Cũ 3\": QĐ cũ vào cũ1 (ngày vào cũ2, đơn vị BN vào \"Đơn vị BN (cũ)\"); QĐ cũ1 trước đó được nối thêm vào cuối cột cũ 3, ngày của nó vào \"ngày QĐ BN Cũ 3\".",
                "Tệp không có các cột đó: QĐ cũ được thêm vào cột \"QĐ bổ nhiệm trước\" (bật ở \"▦ Cột hiển thị\").",
            ],
            [
                "Ví dụ cột \"QĐ bổ nhiệm trước\": \"1234/QĐ-ĐHYHN ngày 01/07/2010 (BN 2010–2015); 2345/QĐ-ĐHYHN ngày 15/06/2015 (BN 2015–2020)\".",
                "Chỉ sửa Số QĐ (giữ nguyên Ngày QĐ) được coi là sửa lỗi gõ, nên không lưu vào lịch sử.",
                "QĐ đã có trong các cột QĐ cũ thì không được ghi thêm lần nữa.",
                "Tìm mọi người từng được bổ nhiệm theo một QĐ: lọc cột QĐ cũ (ví dụ \"Số QĐ bổ nhiệm (cũ 3)\") → Chứa → số QĐ.",
            ]),

        new("➕", "Cột bổ sung (cột tự thêm trong Excel)",
            "Cột có tiêu đề trong tệp Excel nhưng không có trong mẫu vẫn được nhập và dùng được.",
            [
                "Thêm cột vào tệp Excel, đặt tiêu đề ở dòng tiêu đề (dòng có \"Họ và tên\"), ví dụ \"Trưởng phòng\".",
                "Nhập Excel như bình thường — bảng cảnh báo sẽ liệt kê các cột bổ sung tìm thấy.",
                "Cột mới hiện trong bảng, trong Bộ lọc nâng cao, trong cửa sổ Sửa (nhóm \"Cột bổ sung (từ Excel)\") và khi xuất Excel.",
                "Lọc nhanh: gõ vào ô \"Hỏi nhanh\" tên cột rồi giá trị, ví dụ \"Dân tộc Tày\", \"phân loại chức vụ Trưởng\"; cột đánh dấu x chỉ cần gõ tên cột (\"chức vụ cơ hữu\").",
            ],
            [
                "Cột bổ sung lưu dạng văn bản: lọc bằng Chứa, Bằng, Trống, Không trống.",
                "Cột không có tiêu đề sẽ bị bỏ qua.",
                "Chưa sắp xếp được theo cột bổ sung; hãy dùng bộ lọc.",
            ]),

        new("🔔", "Nhắc hạn chức vụ",
            "Danh sách chức vụ sắp hết nhiệm kỳ hoặc cán bộ sắp nghỉ hưu, đếm ngược từ 100 ngày.",
            [
                "Bấm \"🔔 Nhắc hạn\" — số màu đỏ trên nút là số chức vụ cần chú ý.",
                "Nhấp đúp một dòng (hoặc \"✎ Sửa / cập nhật QĐ\") để cập nhật khi đã bổ nhiệm lại.",
                "Bấm \"✓ Đã xử lý / Nhắc lại\" để tạm ẩn một mục cho đến khi ngày kết thúc thay đổi.",
            ],
            [
                "Phần mềm nhắc ở mỗi mốc 5 ngày (100, 95, … 5, 0 ngày); ngày bỏ lỡ vẫn được nhắc bù.",
                "\"⚙ Cài đặt\" → \"Tự động kiểm tra hằng ngày\" để Windows tự nhắc kể cả khi phần mềm đang tắt.",
                "Nhiệm kỳ kết thúc vào ngày trước ngày bổ nhiệm của năm \"Hết hạn BN\", hoặc khi cán bộ nghỉ hưu nếu sớm hơn.",
            ]),

        new("📊", "Thống kê",
            "Đếm theo giới tính, độ tuổi, học hàm, trình độ, chức vụ, đơn vị… cho danh sách đang lọc.",
            [
                "Mở tab \"Thống kê\" (bên cạnh tab \"Danh sách\").",
                "Chọn tiêu chí cần thống kê.",
                "Tích \"Đếm theo cán bộ (mỗi người 1 lần)\" để không đếm trùng người giữ nhiều chức vụ.",
            ],
            [
                "Thống kê luôn theo bộ lọc và trang đang chọn — lọc trước rồi xem thống kê.",
                "Thanh dưới cùng cửa sổ luôn hiện nhanh: số dòng, số cán bộ, nam/nữ, tuổi trung bình, đảng viên.",
            ]),

        new("📤", "Xuất Excel",
            "Xuất danh sách đang lọc ra tệp Excel theo mẫu gốc, gồm cả phần \"Thôi giữ chức vụ\".",
            [
                "Lọc danh sách như mong muốn.",
                "Bấm \"📤 Xuất Excel\" (Ctrl+E).",
                "Chọn \"Có\" = đủ các cột, đúng vị trí cột như tệp Excel đã nhập (nhập lại được vào phần mềm), \"Không\" = chỉ các cột đang hiển thị.",
                "Chọn nơi lưu; phần mềm hỏi có mở tệp vừa xuất không.",
            ],
            [
                "Thứ tự sắp xếp hiện tại trên bảng được giữ khi xuất.",
                "Thứ tự cột theo tệp Excel nhập gần nhất (khi chọn \"Thay thế\"). Cột của mẫu mà tệp không có được đặt ở cuối, hoặc bỏ đi nếu trống.",
                "Đóng tệp trong Excel trước khi xuất đè lên cùng tên.",
            ]),

        new("▦", "Chọn cột hiển thị",
            "Ẩn / hiện các cột trong bảng cho gọn.",
            [
                "Bấm \"▦ Cột hiển thị ▾\" (phía trên bảng, bên phải).",
                "Tích hoặc bỏ tích các cột.",
            ],
            [
                "Lựa chọn được ghi nhớ cho lần mở sau.",
                "Bấm vào tiêu đề cột để sắp xếp; bấm lần nữa để đảo chiều.",
            ]),

        new("⚙", "Cài đặt, sao lưu và khôi phục",
            "AI (không bắt buộc), tiêu đề khi xuất báo cáo, nhắc hạn và sao lưu dữ liệu.",
            [
                "Bấm \"⚙ Cài đặt\".",
                "AI: tích \"Bật AI\", chọn nhà cung cấp, dán khóa API, bấm \"Kiểm tra kết nối\".",
                "Sao lưu: \"💾 Sao lưu ngay…\"; khôi phục: \"♻ Khôi phục từ bản sao lưu…\"; xem tệp: \"📁 Mở thư mục dữ liệu\".",
                "Bấm \"Lưu\".",
            ],
            [
                "Không có AI phần mềm vẫn hoạt động đầy đủ, offline.",
                "Khóa API được mã hóa cho tài khoản Windows hiện tại.",
                "Phần mềm giữ 20 bản sao lưu tự động gần nhất.",
            ]),

        new("⌨", "Phím tắt",
            "Làm nhanh hơn với bàn phím. Bấm F1 bất cứ lúc nào để mở hướng dẫn này.",
            [
                "F1 — Mở Hướng dẫn",
                "Ctrl+O — Nhập Excel",
                "Ctrl+E — Xuất Excel",
                "Ctrl+N — Thêm cán bộ",
                "Ctrl+D — Đọc Quyết định",
                "Ctrl+PageUp — Trang \"Đang giữ chức vụ\"",
                "Ctrl+PageDown — Trang \"Thôi giữ chức vụ\"",
                "Enter — Sửa dòng đang chọn (nhấp đúp vào dòng cũng được); trong ô Hỏi nhanh: tạo bộ lọc",
                "Delete — Xóa dòng đang chọn",
                "Esc — Đóng cửa sổ phụ (Hướng dẫn, Nhắc hạn)",
            ],
            [], KeyList: true),
    ];
}
