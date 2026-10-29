# Quản lý cán bộ lãnh đạo, quản lý (StaffManagement)

A lightweight WPF (.NET 10) desktop app for the *"Danh sách cán bộ lãnh đạo, quản lý"* Excel list.
It covers import and export in the original template, a filter builder, questions in plain Vietnamese,
statistics, and reading appointment decisions (QĐ) from PDF or Word.
See [docs/PLAN.md](docs/PLAN.md) for the updated plan and design decisions.

## Build & run

```powershell
dotnet build
dotnet run
# Release, one self-contained .exe for PCs without .NET installed:
dotnet publish StaffManagement.csproj -c Release -r win-x64 -o publish
```

You can pass a file on the command line, or drop it on the window:

- an `.xlsx` file is imported;
- a `.pdf`, `.docx` or `.txt` file opens the decision review.

## Using it

1. **Nhập Excel** (Ctrl+O): pick the list file, e.g. `Resources.xlsx`. The first import loads everything. Later imports ask whether to *replace* or *append*; the current data is backed up automatically first.
2. **Sheet tabs under the list** (like Excel): *Đang giữ chức vụ* and *Thôi giữ chức vụ*. Each tab shows how many rows match the current filter; switch with Ctrl+PageUp / Ctrl+PageDown.
3. **Tìm nhanh**: search name, Mã CB, title, unit or phone. Accents are optional.
4. **Hỏi nhanh**: type a question, e.g. `nữ từ 40 đến 55 tuổi là đảng viên PGS`, `trưởng bộ môn nghỉ hưu năm nay` or `hết hạn bổ nhiệm năm 2027`. Press Enter and it becomes filter-builder rows you can adjust.
5. **Bộ lọc nâng cao**: stack conditions with AND/OR, and save them as named filters.
6. **Thống kê** tab: counts by gender, age band, rank, degree, title, unit and more, for the current filter.
7. **Quyết định**: drop a QĐ file on the window, or press Ctrl+D.
   - Check the extracted fields against the original text.
   - Choose *add new position*, *update row* or *move to "Đã thôi"*, then save.
8. **Xuất Excel** (Ctrl+E): exports the filtered list in the original template, including the "THÔI GIỮ CHỨC VỤ" section.
9. **Mỗi cán bộ 1 dòng**: hides a person's *Lặp* rows, so each person appears once (also applies to export).
   The app keeps the *Lặp* column consistent: each person has exactly one main row in each section.
10. **🔔 Nhắc hạn**: lists the current posts that end within 100 days, with a count-down.
    - A post ends at the end of its term or when the holder retires, whichever comes first.
    - The term ends the day before the appointment anniversary in the *Hết hạn BN* year.
    - You are reminded at every 5-day mark (100, 95, … 5, 0 days left); missed days are caught up.
    - Reminders show as a yellow banner when the app opens, and as a badge on the bell.
    - **Cài đặt → Nhắc hạn chức vụ → Tự động kiểm tra hằng ngày** adds a Windows Task Scheduler job (current user, no admin rights).
      It runs a short check that shows a Windows notification, even when the app is closed. If the PC was off, it runs after login.
    - *Đã xử lý* silences one post until its end date changes (e.g. after re-appointment).
    - The day count, the interval and retirement can be changed in Settings.
11. Grid shortcuts: double-click or Enter edits, Delete deletes. *Đã thôi / Khôi phục* moves rows between the two sections.

## Data & settings

- Database: `%LOCALAPPDATA%\StaffManagement\staff.db` (SQLite). Automatic backups go in its `backups` subfolder.
  To use another folder (portable or testing), set the environment variable `STAFFMANAGEMENT_DATA`.
- **Cài đặt** → AI (optional): pick Gemini / Groq / OpenRouter / Ollama, then paste an API key. Ollama needs no key.
  - The key is encrypted for the current Windows user.
  - Without AI, everything works offline.
- **Cài đặt** → export title, backup now, restore.

## Project layout

```
Models/           StaffRecord (one position row), FieldCatalog (columns: type, Excel mapping, accessors),
                  FilterRule / FilterSet, DecisionInfo
Services/         Xlsx (reader/writer), ExcelService (template mapping), StaffRepository (SQLite + Dapper),
                  FilterEngine, NaturalQueryParser, DecisionParser, DocumentTextExtractor, FuzzyMatcher,
                  DecisionApplier, RetirementCalculator, LlmClient, VietnameseText
ViewModels/       MainViewModel, FilterRuleViewModel, StaffEditViewModel, DecisionImportViewModel, SettingsViewModel
Views/            MainWindow, StaffEditWindow, DecisionImportWindow, SettingsWindow, InputDialog
Infrastructure/   DialogService, SettingsStore (DPAPI), converters
Themes/           Styles.xaml
```

To add a column, add one entry in `FieldCatalog` and a property on `StaffRecord`. The grid, edit form, filter, Excel import/export and database schema all pick it up automatically.
