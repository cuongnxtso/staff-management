# Staff Management (Cán bộ lãnh đạo, quản lý) — updated plan & design

This revises the original plan (`plan.pdf`) after analysing the real data file (`Resources.xlsx`,
sheet *"LĐ. QL 07.10.2026"*) and building the app.

## 1. What the data actually looks like (changes to the original analysis)

| Original assumption | Reality in `Resources.xlsx` | Design consequence |
|---|---|---|
| One row = one person | **One row = one position** held by a person. 291 rows, 230 distinct people; the same Mã CB appears on several rows (KN = kiêm nhiệm, Kiêm chức). | Records are *positions*. Statistics offer "count people once". Editing personal data offers to propagate it to the person's other rows. |
| — | **"Lặp" marks duplicate rows of a person:** in each section every person has exactly **one main row** (Lặp empty), and their other positions are marked Lặp. The main row is not necessarily the first one: Võ Mai Trang's main row is TT 31, and TT 7 is Lặp. So "rows without Lặp" = number of people. | A person is identified by Mã CB **or** name + birth date, so a row missing its Mã CB still joins its person. The app keeps the convention when it adds, moves (đang giữ ↔ đã thôi) or deletes rows (`PersonIdentity.RepeatFlagChanges`). The view option "Mỗi cán bộ 1 dòng" shows one row per person: the main row, or a matching Lặp row when only that one matches the filter. |
| — | Two inconsistencies in the file: Cầm Bá Thức's TT 161 has no Mã CB, which would split him into 2 people; Vũ Mạnh Tuấn has 2 rows without Lặp (TT 202, TT 223), so he is counted twice when counting rows without Lặp. | Reported as warnings on import; the data is not changed silently. |
| 38 columns | 38 headed columns (A–AL) **+ 2 unlabelled columns** (AM: notes e.g. "Dân tộc Tày", AN: professional rank e.g. "Giảng viên cao cấp (hạng I)"). | 40 stored fields + derived fields. |
| One flat table | Two sections: active list, then a **"THÔI GIỮ CHỨC VỤ LÃNH ĐẠO QUẢN LÝ 2026"** section (24 rows). | `IsFormer` flag; two Excel-style **sheet tabs** under the list ("Đang giữ chức vụ" / "Thôi giữ chức vụ", each showing its row count for the current filter); export rebuilds both sections. |
| Clean dates | Mixed: `20/10/1968`, ` 09/09/1987`, `18/3/1984`, Excel serials (`26939`), and `Chưa` / `có` in the Party columns. | Tolerant date parser; Party columns kept as text plus a derived "Là đảng viên" flag. |
| Simple headers | Two-level merged headers ("Ngày sinh" → Ngày/Tháng/Năm; "Thời điểm" → Từ ngày/Từ tháng/Năm BN/Hết hạn BN). Some columns are formulas on `TODAY()` (Tuổi, Số năm giữ CV…). | Columns are mapped **by header text** (with letter fallback), so a re-ordered template still imports. Formula columns are computed live, the same way as in Excel. |
| — | Retirement month/year (J/K) mostly follow NĐ 135/2020 (250 of 284 rows match), the rest are individual extensions. | Retirement is **suggested** (button + default for new rows) but stays editable. |

## 2. Technology (final)

| Layer | Choice | Why / change vs. original plan |
|---|---|---|
| UI | **WPF, .NET 10**, MVVM with CommunityToolkit.Mvvm | As planned. No third-party UI suite. |
| Storage | **SQLite** via Microsoft.Data.Sqlite + **Dapper** | As planned. All rows are loaded into memory; filtering is plain LINQ (instant for thousands of rows). |
| Excel | **Built-in minimal OpenXML reader/writer** (`Services/Xlsx.cs`) | *Changed* from MiniExcel/ClosedXML: no dependency, and the export reproduces the real template (title rows, two-level merged headers, "Thôi giữ chức vụ" section, frozen panes, landscape). |
| Decisions (QĐ) | **PdfPig** for PDF; .docx read directly from its XML; .txt | *Changed* from DocX (licence change). Scanned PDFs (images) need OCR first; the app explains this. |
| Natural language | **Offline Vietnamese keyword parser** + optional **OpenAI-compatible LLM** (Gemini, Groq, OpenRouter, Ollama) via plain `HttpClient` | *Changed*: no Semantic Kernel. Both produce **filter-builder rules** the user can see and edit, rather than an opaque query. |
| Packaging | Self-contained **single-file + ReadyToRun** | *Corrected*: WPF does **not** support trimming or Native AOT. |

### Measured footprint (Release, 291 rows loaded)

| Build | File size | Private RAM | Window shown |
|---|---|---|---|
| Single file, uncompressed (**default**) | 155 MB | ≈108 MB | ≈1.2 s |
| Single file, compressed | 69 MB | ≈186 MB | ≈1.0 s |
| Framework-dependent (needs .NET 10 Desktop Runtime) | ≈3 MB + DLLs | lowest | — |

The original estimate of "30–60 MB RAM / 35–50 MB installer" is not realistic for WPF. If the
target PCs can install the .NET 10 Desktop Runtime once, the framework-dependent build is the lightest option:
`dotnet publish StaffManagement.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=false`.

## 3. Modules

1. **Excel import / export** (`ExcelService`, `XlsxReader`, `XlsxWriter`)
   - Import finds the header row ("Họ và tên"), handles two-level and merged headers, detects the
     "THÔI GIỮ CHỨC VỤ" section, and reports a warning for each unreadable value instead of failing.
   - On import: *replace all* or *append*; the database is backed up automatically first.
   - Export writes the **currently filtered and sorted** list, either with all template columns
     (can be re-imported) or with the visible columns only.
2. **Filter builder + quick search + presets** (`FilterEngine`, `FilterRuleViewModel`)
   - Rows of *[Column] [Operator] [Value] ([Value 2])* with AND/OR. Operators depend on the column type (text / number / date / yes-no).
   - Text matching ignores accents and expands abbreviations: "pho truong bo mon" = "P. Trưởng BM".
   - Value drop-downs list the distinct values already in the data.
   - Derived fields for common questions: *Là đảng viên*, *Số tháng đến nghỉ hưu*, *Thời điểm nghỉ hưu*, *Ngày hiệu lực BN*, *Tình trạng*.
   - Saved filters (presets) are stored in SQLite.
3. **Natural-language question → rules** (`NaturalQueryParser`, `LlmClient.ParseQueryAsync`)
   - Works offline, e.g. "nữ từ 40 đến 55 tuổi là đảng viên PGS" → 4 rules. Also handles retirement this year / within N months, appointment expiry, titles and units.
   - If AI is configured, the LLM receives the field catalogue and sample values and returns the same rule JSON. If the AI call fails, the app falls back to the offline parser.
4. **Statistics**
   - The live summary bar shows rows, people, gender split, average age, Party members, retiring this year and appointments expiring this year.
   - The *Thống kê* tab groups by gender, age band, academic rank, degree, highest political-theory level, title, unit, retirement year and more. Each group can count people or positions.
5. **Decision (QĐ) import** (`DocumentTextExtractor`, `DecisionParser`, `FuzzyMatcher`, `DecisionApplier`)
   - Drag and drop, or pick, .pdf / .docx / .txt files.
   - Extracts: decision type (bổ nhiệm / bổ nhiệm lại / giao phụ trách / thôi giữ chức vụ), name, gender (ông/bà), birth date, rank and degree, title + unit, home unit, decision number and date, effective date, term, and issuing level (from the "/QĐ-BYT" or "/QĐ-ĐHYHN" suffix).
   - **E-signed PDFs** (eoffice.hmu.edu.vn): the number and signing date are stamped into signature widgets after signing, so the page text reads "Số: /QĐ-…, ngày tháng năm". The app decodes the widgets' appearance streams (`PdfAnnotationText`, fonts via ToUnicode) and puts the text back in reading order; no OCR is needed. If a value still can't be read, the review shows a warning to type it in.
   - The decision number is only taken from the header and must contain "QĐ", so laws and decisions cited under *Căn cứ* are never picked up. The effective date is also searched in Điều 2 ("Thời hạn … kể từ ngày").
   - "(MCB: 01079)" is used to find the person. A decision already recorded (same number) proposes *update*, not a duplicate row. The same post keeps the list's wording ("Phó Giám đốc - Phụ trách"), and the decision only fills blank personal details, e.g. chức danh nghề nghiệp "Giảng viên chính (hạng II)".
   - Titles and units are matched to the wording **already used in the list**: "Phòng Quản lý Đào tạo Sau đại học" → "Phòng QL ĐT SĐH", via initials matching.
   - Finds the person's existing rows and proposes an action: *add a new position row* (copying the personal details), *update an existing row* (re-appointment), or *move a row to "Đã thôi"* (dismissal, with an audit note). For promotions it can also retire the old post.
   - Side-by-side review against the source text before anything is saved. An optional "re-parse with AI" button is available.
6. **Manual entry / edit form** — generated from the field catalogue, in 4 tabs, with retirement suggestion, "Đã thôi" toggle, and propagation of personal fields to the person's other rows.
7. **Term-end reminders** (`ReminderService`, `RemindersWindow`, `ReminderScheduler`, `TrayNotifier`)
   - **End of a post:** whichever comes first of the term end or retirement.
     - Term end: the day before the appointment anniversary in the *Hết hạn BN* year (BN từ 01/10/2024, hết hạn 2029 → 30/09/2029). If the day/month is missing, the decision date is used.
     - Retirement: the day before the retirement month.
   - **Count-down:** from 100 days, at every 5-day mark (100, 95, … 0, then every 5 days while overdue).
     - The last mark notified is stored per post, keyed by row and end date, so missed days are caught up once.
     - A re-appointment (new end date) restarts the count-down. *Đã xử lý* silences one post.
   - **Notification channels:**
     - in the app: a banner, a badge on the bell, and an hourly re-check while the app is open;
     - in Windows: an optional per-user Task Scheduler job runs `--remind` daily. It is registered from XML: there is no command-length limit, and it runs after a missed start. It shows a notification-area toast only when a new mark is reached (≈21 MB while waiting, otherwise it exits immediately). Clicking the toast opens the app on the reminder list (`--reminders`).
8. **Safety** — automatic backups before import, delete and restore (latest 20 kept), manual backup and restore, and an API key encrypted with Windows DPAPI.

## 4. Roadmap status

| Phase | Status |
|---|---|
| 1. Data structuring & database | ✅ Done (SQLite schema generated from the model; auto-migrates new columns) |
| 2. Desktop UI foundation | ✅ Done (grid with column chooser, search, add/edit/delete, former-staff section) |
| 3. Filtering & analytics | ✅ Done (filter builder, presets, natural-language → rules, statistics) |
| 4. Smart decision parser | ✅ Done for text PDFs / .docx / .txt. Next: OCR for scanned PDFs (Windows.Media.Ocr has Vietnamese support), and batch review of many QĐ at once |
| 5. Packaging | ✅ Single-file publish. Next: installer with a desktop shortcut (e.g. Inno Setup / MSIX) |

Possible next steps: change history per row (audit table), printable reports by unit, reminders for
upcoming retirements / appointment expiries, and multi-user access via a shared network folder.
