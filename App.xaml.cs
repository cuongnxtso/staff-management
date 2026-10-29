using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using System.Windows.Threading;
using StaffManagement.Infrastructure;
using StaffManagement.Services;
using StaffManagement.ViewModels;
using StaffManagement.Views;

namespace StaffManagement;

/// <summary>
/// Command line:
///   StaffManagement.exe [file.xlsx|file.pdf|…]   open and import the file
///   --data "folder"                               use another data folder (also: env STAFFMANAGEMENT_DATA)
///   --remind                                      background check (Task Scheduler): notify if a reminder mark is reached, no window
///   --reminders                                   open the app on the reminder list
/// </summary>
public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Vietnamese formats everywhere (dd/MM/yyyy in DatePickers and grid).
        var culture = VietnameseText.Culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;
        FrameworkElement.LanguageProperty.OverrideMetadata(typeof(FrameworkElement),
            new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(culture.IetfLanguageTag)));

        DispatcherUnhandledException += OnUnhandledException;

        var args = e.Args.ToList();
        bool remindOnly = TakeFlag(args, "--remind");
        bool openReminders = TakeFlag(args, "--reminders");
        var dataFolder = TakeOption(args, "--data")
                         ?? (Environment.GetEnvironmentVariable("STAFFMANAGEMENT_DATA") is { Length: > 0 } env ? env : null);

        StaffRepository repo;
        try
        {
            repo = new StaffRepository(dataFolder);
        }
        catch (Exception ex)
        {
            if (!remindOnly)
                MessageBox.Show($"Không mở được cơ sở dữ liệu:\n{ex.Message}", "Lỗi khởi động", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
            return;
        }

        if (remindOnly)
        {
            RunBackgroundReminder(repo, dataFolder);
            return;
        }

        var dialogs = new DialogService();
        var vm = new MainViewModel(repo, new SettingsStore(repo), dialogs);
        MainWindow = new MainWindow { DataContext = vm };
        MainWindow.Show();

        if (openReminders)
            MainWindow.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () => vm.OpenRemindersCommand.Execute(null));
        else if (args.Count > 0)
            _ = vm.HandleDroppedFilesAsync(args.Where(File.Exists).ToArray());
    }

    /// <summary>
    /// Started by the Task Scheduler: if posts reached a new count-down mark, show a Windows notification and keep a
    /// tray icon until the user opens the list (or for 8 hours); otherwise exit immediately.
    /// </summary>
    private void RunBackgroundReminder(StaffRepository repo, string? dataFolder)
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        var settings = new SettingsStore(repo).LoadReminders();
        var items = ReminderService.Compute(repo.LoadAll(), settings, DateTime.Today);
        var states = repo.LoadReminderStates();
        var due = ReminderService.Due(items, states);
        if (due.Count == 0)
        {
            Shutdown(0);
            return;
        }
        repo.SaveReminderStates(ReminderService.MarkNotified(due, states));

        int active = items.Count(i => !(states.TryGetValue(i.Key, out var s) && s.Dismissed));
        var tray = new TrayNotifier($"Nhắc hạn: {active} chức vụ sắp kết thúc");
        var lifetime = new DispatcherTimer { Interval = TimeSpan.FromHours(8) };
        void Exit()
        {
            lifetime.Stop();
            tray.Dispose();
            Shutdown(0);
        }
        tray.Opened += () =>
        {
            var exe = Environment.ProcessPath!;
            var psi = new ProcessStartInfo(exe) { UseShellExecute = false };
            psi.ArgumentList.Add("--reminders");
            if (dataFolder is not null)
            {
                psi.ArgumentList.Add("--data");
                psi.ArgumentList.Add(dataFolder);
            }
            Process.Start(psi);
            Exit();
        };
        tray.Closed += Exit;
        lifetime.Tick += (_, _) => Exit();
        lifetime.Start();
        tray.Show($"Nhắc hạn chức vụ – {due.Count} mục đến mốc nhắc", ReminderService.Summary(due, 3));
    }

    private static bool TakeFlag(List<string> args, string flag)
    {
        int i = args.FindIndex(a => a.Equals(flag, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return false;
        args.RemoveAt(i);
        return true;
    }

    private static string? TakeOption(List<string> args, string name)
    {
        int i = args.FindIndex(a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (i < 0 || i + 1 >= args.Count) return null;
        var value = args[i + 1];
        args.RemoveRange(i, 2);
        return value;
    }

    private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"Đã xảy ra lỗi không mong muốn:\n{e.Exception.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
