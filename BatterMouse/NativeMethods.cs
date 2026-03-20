using System.Runtime.InteropServices;

namespace BatterMouse;

internal static class NativeMethods
{
    private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
    private const int DWMWA_BORDER_COLOR             = 34;
    private const int DWMWCP_ROUND                   = 2;
    private const int DWMWA_COLOR_NONE               = unchecked((int)0xFFFFFFFE);

    [DllImport("user32.dll")]
    internal static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int dwAttribute, ref int pvAttribute, int cbAttribute);

    /// <summary>
    /// Requests Windows 11 DWM rounded corners and removes the DWM accent border
    /// for the given window. No-op on older OS versions.
    /// </summary>
    internal static void EnableRoundedCorners(IntPtr hwnd)
    {
        try
        {
            int preference = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE,
                ref preference, sizeof(int));

            int noBorder = DWMWA_COLOR_NONE;
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR,
                ref noBorder, sizeof(int));
        }
        catch { /* best-effort — silently ignored on older Windows versions */ }
    }
}
