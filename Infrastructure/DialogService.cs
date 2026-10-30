using System.Diagnostics;
using System.Windows;
using Microsoft.Win32;
using StaffManagement.ViewModels;
using StaffManagement.Views;

namespace StaffManagement.Infrastructure;

public interface IDialogService
{
    string? OpenFile(string title, string filter, bool multiselect = false);
    string[] OpenFiles(string title, string filter);
    string? SaveFile(string title, string filter, string defaultName);
    bool Confirm(string message, string title = "Xác nhận");
    bool? YesNoCancel(string message, string title);
    void Info(string message, string title = "Thông báo");
    void Error(string message, string title = "Lỗi");
    string? Prompt(string title, string label, string? initial = null);
    bool EditStaff(StaffEditViewModel vm);
    bool ReviewDecision(DecisionImportViewModel vm);
    void ShowSettings(SettingsViewModel vm);
    void ShowReminders(RemindersViewModel vm);
    void ShowHelp();
    void OpenWithShell(string path);
}

public sealed class DialogService : IDialogService
{
    private static Window? Owner => Application.Current?.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                                    ?? Application.Current?.MainWindow;

    public string? OpenFile(string title, string filter, bool multiselect = false) => OpenFiles(title, filter).FirstOrDefault();

    public string[] OpenFiles(string title, string filter)
    {
        var dlg = new OpenFileDialog { Title = title, Filter = filter, Multiselect = true };
        return dlg.ShowDialog(Owner) == true ? dlg.FileNames : [];
    }

    public string? SaveFile(string title, string filter, string defaultName)
    {
        var dlg = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultName, OverwritePrompt = true };
        return dlg.ShowDialog(Owner) == true ? dlg.FileName : null;
    }

    public bool Confirm(string message, string title = "Xác nhận") =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public bool? YesNoCancel(string message, string title) =>
        Show(message, title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        {
            MessageBoxResult.Yes => true,
            MessageBoxResult.No => false,
            _ => null,
        };

    public void Info(string message, string title = "Thông báo") => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
    public void Error(string message, string title = "Lỗi") => Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage icon) =>
        Owner is { } o ? MessageBox.Show(o, message, title, buttons, icon) : MessageBox.Show(message, title, buttons, icon);

    public string? Prompt(string title, string label, string? initial = null)
    {
        var dlg = new InputDialog(title, label, initial) { Owner = Owner };
        return dlg.ShowDialog() == true ? dlg.Value : null;
    }

    public bool EditStaff(StaffEditViewModel vm) => new StaffEditWindow { DataContext = vm, Owner = Owner }.ShowDialog() == true;

    public bool ReviewDecision(DecisionImportViewModel vm) => new DecisionImportWindow { DataContext = vm, Owner = Owner }.ShowDialog() == true;

    public void ShowSettings(SettingsViewModel vm) => new SettingsWindow { DataContext = vm, Owner = Owner }.ShowDialog();

    public void ShowReminders(RemindersViewModel vm) => new RemindersWindow { DataContext = vm, Owner = Owner }.ShowDialog();

    private HelpWindow? _help;

    /// <summary>Not modal, so the manual can stay open next to the list; a second F1 brings the same window back.</summary>
    public void ShowHelp()
    {
        if (_help is { IsVisible: true })
        {
            if (_help.WindowState == WindowState.Minimized) _help.WindowState = WindowState.Normal;
            _help.Activate();
            return;
        }
        _help = new HelpWindow { DataContext = new HelpViewModel(), Owner = Application.Current.MainWindow };
        _help.Show();
    }

    public void OpenWithShell(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { Error($"Không mở được: {ex.Message}"); }
    }
}
