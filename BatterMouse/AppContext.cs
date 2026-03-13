using System.ComponentModel;
using System.Drawing;
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
            Text = "BatterMouse \u2014 reading...",   // em dash: shown until first HID reading
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };

        ToastHelper.Register(_trayIcon);

        // Capture UI SynchronizationContext for thread-safe updates when handle not yet created
        var uiContext = SynchronizationContext.Current;

        // Wire HID event to update battery label and tooltip on the UI thread
        _hidReader.BatteryLevelReceived += level =>
        {
            void Update()
            {
                _batteryLabel.Text = $"Battery: {level}%";
                _trayIcon.Text = $"{level}%";
            }

            if (_trayIcon.ContextMenuStrip?.IsHandleCreated == true)
                _trayIcon.ContextMenuStrip.BeginInvoke(Update);
            else
                uiContext?.Post(_ => Update(), null);
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
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _components.Dispose();
        base.Dispose(disposing);
    }
}
