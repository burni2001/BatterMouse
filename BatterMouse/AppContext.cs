using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace BatterMouse;

/// <summary>
/// Central ApplicationContext subclass. Owns the tray icon, wires all components,
/// and manages the application lifecycle.
///
/// Satisfies:
///   TRAY-01    — system tray icon with full context menu
///   STARTUP-01 — auto-start registered in constructor via StartupManager
///   BG-01      — no Form is constructed or shown; the process has no visible window
///
/// Phase 3 additions:
///   - Real embedded tray.ico (not SystemIcons.Application)
///   - Full context menu: battery label, separator, Start with Windows toggle, Exit
///   - Thread-safe tooltip + battery label update on each HID reading
///
/// Program.cs calls: Application.Run(new AppContext())
/// </summary>
internal sealed class AppContext : ApplicationContext
{
    private readonly IContainer _components = new Container();
    private readonly NotifyIcon _trayIcon;
    private readonly HidReader _hidReader;
    private readonly BatteryMonitor _batteryMonitor;

    // Menu items held as fields so the HID event handler can update them
    private ToolStripMenuItem _batteryLabel = null!;
    private ToolStripMenuItem _startupItem = null!;

    // Current dynamically-generated battery icon (disposed when replaced)
    private Icon? _batteryIcon;

    // Charging + last-level state — updated on UI thread only
    private bool _isCharging;
    private int  _lastLevel = -1;


    public AppContext()
    {
        // Register auto-start on every run (idempotent — re-writing the same value is harmless)
        StartupManager.SetStartup(true);

        // BatteryMonitor wired to the real toast notification callback
        _batteryMonitor = new BatteryMonitor(ToastHelper.ShowLowBattery);

        // HidReader raises BatteryLevelReceived; BatteryMonitor.OnBatteryLevel handles threshold logic
        _hidReader = new HidReader();
        _hidReader.BatteryLevelReceived += _batteryMonitor.OnBatteryLevel;
        _hidReader.Start();

        // Load real tray icon from embedded resource
        using var iconStream = typeof(AppContext).Assembly
            .GetManifestResourceStream("BatterMouse.Resources.tray.ico")
            ?? throw new InvalidOperationException("Embedded resource 'BatterMouse.Resources.tray.ico' not found.");
        var icon = new Icon(iconStream);  // reads stream immediately; stream can be disposed after

        _trayIcon = new NotifyIcon(_components)
        {
            Icon = icon,
            Text = "BatterMouse",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        ToastHelper.Register(_trayIcon);

        _trayIcon.MouseDoubleClick += (_, _) => LaunchKeychronLauncher();

        // Restore last known battery level immediately (before first HID report arrives)
        int? cached = HidReader.LoadLastLevel();
        if (cached.HasValue)
        {
            _lastLevel = cached.Value;
            _batteryLabel.Text = $"Battery: {cached.Value}%";
            SetBatteryIcon(cached.Value);
            _trayIcon.Text = $"BatterMouse ({cached.Value}%)";
        }

        // Capture UI SynchronizationContext for thread-safe updates when handle not yet created
        var uiContext = SynchronizationContext.Current;

        void Dispatch(Action action)
        {
            if (_trayIcon.ContextMenuStrip?.IsHandleCreated == true)
                _trayIcon.ContextMenuStrip.BeginInvoke(action);
            else
                uiContext?.Post(_ => action(), null);
        }

        // Wire HID event to update battery label and tray icon on the UI thread
        _hidReader.BatteryLevelReceived += level =>
        {
            Dispatch(() =>
            {
                _lastLevel = level;
                _batteryLabel.Text = _isCharging ? $"Battery: {level}% (charging)" : $"Battery: {level}%";
                RefreshTrayIcon();
            });
        };

        // Charging state is driven by PID=0xD03F device presence (USB-C cable plugged in),
        // detected via DeviceList.Changed in HidReader.  No watchdog or STATUS-byte heuristics needed.
        _hidReader.ChargingStatusChanged += isCharging =>
        {
            Dispatch(() =>
            {
                _isCharging = isCharging;
                if (_lastLevel >= 0)
                    _batteryLabel.Text = _isCharging ? $"Battery: {_lastLevel}% (charging)" : $"Battery: {_lastLevel}%";
                HidReader.Log($"[AppContext] Charging {(isCharging ? "started" : "stopped")}");
                RefreshTrayIcon();
            });
        };
    }

    /// <summary>
    /// Builds the context menu with 4 items in the required order:
    ///   0 — Battery label (disabled, initial text "Battery: --")
    ///   1 — Separator
    ///   2 — "Start with Windows" toggle (Checked = current registry state)
    ///   3 — "Exit"
    ///
    /// Exposed as internal so AppContextMenuTests can call it directly via InternalsVisibleTo.
    /// The container parameter allows callers to pass their own Container for lifetime management.
    /// </summary>
    internal static ContextMenuStrip BuildMenuInternal(IContainer container)
    {
        var menu = new Win11ContextMenuStrip(container);

        var batteryLabel = new ToolStripMenuItem("Battery: --") { Enabled = false };
        menu.Items.Add(batteryLabel);

        menu.Items.Add(new ToolStripSeparator());

        var startupItem = new ToolStripMenuItem("Start with Windows")
        {
            Checked = StartupManager.IsStartupEnabled(),
            CheckOnClick = false   // state is managed manually via ToggleStartup
        };
        // Click handler is added by the instance method; static version doesn't add it
        menu.Items.Add(startupItem);

        menu.Items.Add("Exit", null, (_, _) => { });  // handler replaced by instance method below
        return menu;
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = BuildMenuInternal(_components);

        // Keep references to mutable items for later updates
        _batteryLabel = (ToolStripMenuItem)menu.Items[0];
        _startupItem  = (ToolStripMenuItem)menu.Items[2];

        // Wire startup toggle (replaces no-op added by static helper)
        _startupItem.Click += ToggleStartup;

        // Replace the no-op Exit handler with the real one
        menu.Items[3].Click += (_, _) => ExitThread();

        return menu;
    }

    private void RefreshTrayIcon()
    {
        var newIcon = _isCharging ? CreateChargingIcon() : CreateBatteryIcon(_lastLevel);
        _trayIcon.Icon = newIcon;
        _batteryIcon?.Dispose();
        _batteryIcon = newIcon;
        if (_isCharging)
            _trayIcon.Text = _lastLevel >= 0 ? $"Charging ({_lastLevel}%)" : "Charging";
        else if (_lastLevel >= 0)
            _trayIcon.Text = $"BatterMouse ({_lastLevel}%)";
    }

    private void SetBatteryIcon(int level)
    {
        var newIcon = CreateBatteryIcon(level);
        _trayIcon.Icon = newIcon;
        _batteryIcon?.Dispose();
        _batteryIcon = newIcon;
    }

    /// <summary>
    /// Renders a green lightning bolt icon used when the mouse is charging.
    /// </summary>
    private static Icon CreateChargingIcon()
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

        // Classic lightning bolt polygon (top-right → middle-right → far-right →
        // bottom-left → middle-left → far-left → back)
        PointF[] bolt =
        [
            new(20f,  2f),
            new(20f, 14f),
            new(29f, 14f),
            new(12f, 30f),
            new(12f, 18f),
            new( 3f, 18f),
        ];

        using var brush = new SolidBrush(Color.LimeGreen);
        g.FillPolygon(brush, bolt);

        var hIcon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        NativeMethods.DestroyIcon(hIcon);
        return icon;
    }

    private static Icon CreateBatteryIcon(int level)
    {
        const int size = 32;
        using var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);

        Color textColor = level >= 70 ? Color.LimeGreen
                        : level >= 40 ? Color.Gold
                        :               Color.OrangeRed;

        string text = level.ToString();
        int fontSize = text.Length >= 3 ? 14 : text.Length == 2 ? 24 : 30;
        using var font = new Font("Arial", fontSize, FontStyle.Bold, GraphicsUnit.Pixel);

        TextRenderer.DrawText(g, text, font, new Rectangle(0, 0, size, size), textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);

        var hIcon = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(hIcon).Clone();
        NativeMethods.DestroyIcon(hIcon);
        return icon;
    }

    private static void LaunchKeychronLauncher()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName         = @"C:\Program Files\Google\Chrome\Application\chrome_proxy.exe",
            Arguments        = "--profile-directory=Default --app-id=cbfedpnlilnlbdcikokpfoibmlbghlhg",
            WorkingDirectory = @"C:\Program Files\Google\Chrome\Application",
            UseShellExecute  = true
        });
    }

    private void ToggleStartup(object? sender, EventArgs e)
    {
        bool current = StartupManager.IsStartupEnabled();
        StartupManager.SetStartup(!current);
        _startupItem.Checked = !current;
    }

    protected override void ExitThreadCore()
    {
        _hidReader.Stop();
        _trayIcon.Visible = false;   // prevents ghost tray icon
        ToastHelper.Cleanup();       // ToastNotificationManagerCompat.Uninstall()
        _batteryIcon?.Dispose();
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _components.Dispose();
        base.Dispose(disposing);
    }
}
