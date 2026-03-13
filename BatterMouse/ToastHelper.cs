using Microsoft.Toolkit.Uwp.Notifications;

namespace BatterMouse;

/// <summary>
/// Wrapper for Windows toast notifications.
/// Uses ToastContentBuilder (Microsoft.Toolkit.Uwp.Notifications) — NOT ShowBalloonTip
/// (ShowBalloonTip is banned: Windows 11 Action Center does not display balloon tips).
/// </summary>
public static class ToastHelper
{
    /// <summary>
    /// Shows a low battery toast notification with the current battery level.
    /// </summary>
    public static void ShowLowBattery(int level)
    {
        new ToastContentBuilder()
            .AddText("BatterMouse — Low Battery")
            .AddText($"Mouse battery is at {level}%. Connect USB cable to charge.")
            .Show();
    }

    /// <summary>
    /// Cleans up toast notification COM registration.
    /// Called from AppContext.ExitThreadCore (plan 02-03) on app exit.
    /// </summary>
    public static void Cleanup()
    {
        ToastNotificationManagerCompat.Uninstall();
    }
}
