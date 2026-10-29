using System.Drawing;
using Forms = System.Windows.Forms;

namespace StaffManagement.Infrastructure;

/// <summary>
/// Windows notification through a notification-area icon (shown as a toast on Windows 10/11).
/// The icon stays in the tray so the reminder can still be opened after the toast disappears.
/// </summary>
public sealed class TrayNotifier : IDisposable
{
    private readonly Forms.NotifyIcon _icon;

    public event Action? Opened;
    public event Action? Closed;

    public TrayNotifier(string tooltip)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Xem danh sách nhắc hạn", null, (_, _) => Opened?.Invoke());
        menu.Items.Add("Đóng nhắc nhở", null, (_, _) => Closed?.Invoke());

        _icon = new Forms.NotifyIcon
        {
            Icon = (Environment.ProcessPath is { } exe ? Icon.ExtractAssociatedIcon(exe) : null) ?? SystemIcons.Information,
            Text = tooltip.Length > 63 ? tooltip[..63] : tooltip,
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.BalloonTipClicked += (_, _) => Opened?.Invoke();
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) Opened?.Invoke();
        };
    }

    public void Show(string title, string text)
    {
        // Balloon text is limited to 255 characters.
        _icon.ShowBalloonTip(15000, title, text.Length > 255 ? text[..252] + "…" : text, Forms.ToolTipIcon.Warning);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
