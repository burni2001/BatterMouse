using Microsoft.Win32;
using System.Diagnostics;

namespace BatterMouse;

/// <summary>
/// Manages the HKCU registry auto-start entry for BatterMouse.
/// Covers STARTUP-01: no elevation required — HKCU is always writable by the current user.
/// </summary>
public static class StartupManager
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppName = "BatterMouse";

    /// <summary>
    /// Enables or disables the auto-start registry entry.
    /// SetStartup(true) writes a quoted path value under HKCU Run key (Pitfall 7: quoted for paths with spaces).
    /// SetStartup(false) removes the value; no-op if absent.
    /// </summary>
    public static void SetStartup(bool enable)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        if (key == null) return;

        if (enable)
        {
            string path = Process.GetCurrentProcess().MainModule!.FileName;
            key.SetValue(AppName, $"\"{path}\"", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(AppName, throwOnMissingValue: false);
        }
    }

    /// <summary>
    /// Returns true if the auto-start registry entry exists under HKCU Run key.
    /// </summary>
    public static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue(AppName) != null;
    }
}
