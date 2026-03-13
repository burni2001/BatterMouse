using System.Windows.Forms;

namespace BatterMouse;

/// <summary>
/// Shows notifications via NotifyIcon.ShowBalloonTip.
/// On Windows 10+ the shell converts balloon tips to proper toast notifications
/// that appear in the Action Center — no WinRT projection layer required.
/// Register() must be called after the NotifyIcon is created.
/// </summary>
public static class ToastHelper
{
    private static NotifyIcon? _icon;

    public static void Register(NotifyIcon icon) => _icon = icon;

    public static void ShowLowBattery(int level)
    {
        _icon?.ShowBalloonTip(
            timeout: 5000,
            tipTitle: "BatterMouse \u2014 Low Battery",
            tipText: $"Mouse battery is at {level}%. Connect USB cable to charge.",
            tipIcon: ToolTipIcon.Warning);
    }

    public static void Cleanup() { }
}
