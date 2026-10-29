using System.Diagnostics;
using System.Security;
using System.Text;

namespace StaffManagement.Infrastructure;

/// <summary>
/// Daily background check through the Windows Task Scheduler (per user, no admin rights needed):
/// runs "StaffManagement.exe --remind", which shows a notification only when a reminder mark is reached and then exits.
/// Nothing stays resident in memory — important on low-end PCs. If the PC is off at the scheduled time,
/// the check runs at the next opportunity (StartWhenAvailable), e.g. right after logging in.
/// </summary>
public static class ReminderScheduler
{
    public const string TaskName = "StaffManagement - Nhac han chuc vu";

    public static bool IsEnabled(string taskName = TaskName) => Run("/Query", "/TN", taskName).ExitCode == 0;

    /// <returns>null on success, otherwise the error message.</returns>
    public static string? Enable(string time, string? dataFolder, string taskName = TaskName)
    {
        var exe = Environment.ProcessPath ?? throw new InvalidOperationException("Không xác định được đường dẫn chương trình.");
        var arguments = "--remind" + (dataFolder is null ? "" : $" --data \"{dataFolder}\"");

        // Registered from XML: no 261-character limit on the command (unlike /TR) and StartWhenAvailable can be set.
        var xmlPath = Path.Combine(Path.GetTempPath(), $"staffmanagement_task_{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(xmlPath, TaskXml(exe, arguments, time), Encoding.Unicode);
            var (code, output) = Run("/Create", "/F", "/TN", taskName, "/XML", xmlPath);
            return code == 0 ? null : output.Trim();
        }
        finally
        {
            File.Delete(xmlPath);
        }
    }

    public static string? Disable(string taskName = TaskName)
    {
        if (!IsEnabled(taskName)) return null;
        var (code, output) = Run("/Delete", "/F", "/TN", taskName);
        return code == 0 ? null : output.Trim();
    }

    private static string TaskXml(string exe, string arguments, string time) => $"""
        <?xml version="1.0" encoding="UTF-16"?>
        <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
          <RegistrationInfo>
            <Description>Nhắc cán bộ sắp hết thời hạn giữ chức vụ (Quản lý cán bộ lãnh đạo, quản lý).</Description>
          </RegistrationInfo>
          <Triggers>
            <CalendarTrigger>
              <StartBoundary>{DateTime.Today:yyyy-MM-dd}T{time}:00</StartBoundary>
              <Enabled>true</Enabled>
              <ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>
            </CalendarTrigger>
          </Triggers>
          <Principals>
            <Principal id="Author">
              <LogonType>InteractiveToken</LogonType>
              <RunLevel>LeastPrivilege</RunLevel>
            </Principal>
          </Principals>
          <Settings>
            <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
            <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
            <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
            <StartWhenAvailable>true</StartWhenAvailable>
            <ExecutionTimeLimit>PT9H</ExecutionTimeLimit>
            <Enabled>true</Enabled>
          </Settings>
          <Actions Context="Author">
            <Exec>
              <Command>{SecurityElement.Escape(exe)}</Command>
              <Arguments>{SecurityElement.Escape(arguments)}</Arguments>
            </Exec>
          </Actions>
        </Task>
        """;

    private static (int ExitCode, string Output) Run(params string[] args)
    {
        var psi = new ProcessStartInfo("schtasks.exe")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd();
        p.WaitForExit(15000);
        return (p.ExitCode, output);
    }
}
