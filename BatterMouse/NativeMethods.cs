using System.Runtime.InteropServices;

namespace BatterMouse;

internal static class NativeMethods
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWCP_ROUND = 2;

    [DllImport("user32.dll")]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    /// <summary>
    /// Requests Windows 11 DWM rounded corners for the given window.
    /// No-op if DWM is unavailable or the OS does not support this attribute.
    /// </summary>
    internal static void EnableRoundedCorners(IntPtr hwnd)
    {
        try
        {
            int preference = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE,
                ref preference, sizeof(int));
        }
        catch { /* best-effort — silently ignored on older Windows versions */ }
    }
}
