using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace BatterMouse;

/// <summary>
/// ContextMenuStrip subclass that hooks <see cref="OnHandleCreated"/> to apply
/// Windows 11 DWM rounded corners to the popup window.
/// </summary>
internal sealed class Win11ContextMenuStrip : ContextMenuStrip
{
    public Win11ContextMenuStrip(IContainer container) : base(container)
    {
        Renderer = new Win11MenuRenderer();
        Font = CreateFont();
        Padding = new Padding(0, 4, 0, 4);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        NativeMethods.EnableRoundedCorners(Handle);
    }

    private static Font CreateFont()
    {
        try { return new Font("Segoe UI Variable Text", 10f); }
        catch { return new Font("Segoe UI", 10f); }
    }
}

/// <summary>
/// Custom renderer applying Windows 11 visual design to ContextMenuStrip items.
/// Automatically adapts to the system light/dark mode setting.
/// </summary>
internal sealed class Win11MenuRenderer : ToolStripRenderer
{
    // Light theme
    private static readonly Color LightBg       = Color.FromArgb(249, 249, 249);
    private static readonly Color LightHover     = Color.FromArgb(218, 218, 218);
    private static readonly Color LightText      = Color.FromArgb(0,   0,   0  );
    private static readonly Color LightDisabled  = Color.FromArgb(160, 160, 160);
    private static readonly Color LightSeparator = Color.FromArgb(217, 217, 217);
    private static readonly Color LightBorder    = Color.FromArgb(200, 200, 200);
    private static readonly Color LightCheck     = Color.FromArgb(0,   103, 192);

    // Dark theme
    private static readonly Color DarkBg        = Color.FromArgb(44,  44,  44 );
    private static readonly Color DarkHover      = Color.FromArgb(65,  65,  65 );
    private static readonly Color DarkText       = Color.FromArgb(255, 255, 255);
    private static readonly Color DarkDisabled   = Color.FromArgb(130, 130, 130);
    private static readonly Color DarkSeparator  = Color.FromArgb(63,  63,  63 );
    private static readonly Color DarkBorder     = Color.FromArgb(63,  63,  63 );
    private static readonly Color DarkCheck      = Color.FromArgb(76,  194, 255);

    private readonly bool _dark;

    private Color Bg        => _dark ? DarkBg        : LightBg;
    private Color Hover     => _dark ? DarkHover     : LightHover;
    private Color Text      => _dark ? DarkText      : LightText;
    private Color Disabled  => _dark ? DarkDisabled  : LightDisabled;
    private Color Separator => _dark ? DarkSeparator : LightSeparator;
    private Color Border    => _dark ? DarkBorder    : LightBorder;
    private Color Check     => _dark ? DarkCheck     : LightCheck;

    public Win11MenuRenderer()
    {
        _dark = IsSystemDarkMode();
    }

    private static bool IsSystemDarkMode()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    /// <summary>Solid background fill for the entire menu popup.</summary>
    protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        => e.Graphics.Clear(Bg);

    /// <summary>1 px flat border around the menu in the theme's border color.</summary>
    protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
    {
        using var pen = new Pen(Border);
        e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
    }

    /// <summary>Image-margin gutter — intentionally empty so the menu background shows through.</summary>
    protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

    /// <summary>Rounded highlight on the hovered/selected item, matching Windows 11 style.</summary>
    protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
    {
        if (!e.Item.Selected || !e.Item.Enabled) return;

        var r = new Rectangle(2, 1, e.Item.Width - 4, e.Item.Height - 2);
        using var path = CreateRoundedRect(r, 4);
        using var brush = new SolidBrush(Hover);

        var prev = e.Graphics.SmoothingMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillPath(brush, path);
        e.Graphics.SmoothingMode = prev;
    }

    /// <summary>Text color: full opacity when enabled, muted when disabled.</summary>
    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? Text : Disabled;
        base.OnRenderItemText(e);
    }

    /// <summary>Horizontal rule inset from both edges.</summary>
    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        int y = e.Item.Height / 2;
        using var pen = new Pen(Separator);
        e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
    }

    /// <summary>Check mark drawn as '✓' in the accent color inside the image rectangle.</summary>
    protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
    {
        if (e.Item is not ToolStripMenuItem { Checked: true }) return;

        using var brush = new SolidBrush(Check);
        using var sf = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center
        };

        var prev = e.Graphics.TextRenderingHint;
        e.Graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
        e.Graphics.DrawString("✓", e.Item.Font, brush, e.ImageRectangle, sf);
        e.Graphics.TextRenderingHint = prev;
    }

    private static GraphicsPath CreateRoundedRect(Rectangle r, int radius)
    {
        int d = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(r.X,          r.Y,           d, d, 180, 90);
        path.AddArc(r.Right - d,  r.Y,           d, d, 270, 90);
        path.AddArc(r.Right - d,  r.Bottom - d,  d, d,   0, 90);
        path.AddArc(r.X,          r.Bottom - d,  d, d,  90, 90);
        path.CloseFigure();
        return path;
    }
}
