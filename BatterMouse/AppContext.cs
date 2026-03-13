using System.ComponentModel;
using System.Windows.Forms;

namespace BatterMouse;

/// <summary>
/// Central ApplicationContext subclass. Owns the tray icon, wires all components,
/// and manages the application lifecycle.
///
/// Satisfies:
///   TRAY-01   — system tray icon with "Exit" context menu item
///   STARTUP-01 — auto-start registered in constructor via StartupManager
///   BG-01     — no Form is constructed or shown; the process has no visible window
///
/// Program.cs calls: Application.Run(new AppContext())
/// </summary>
internal sealed class AppContext : ApplicationContext
{
    private readonly IContainer _components = new Container();
    private readonly NotifyIcon _trayIcon;
    private readonly HidReader _hidReader;
    private readonly BatteryMonitor _batteryMonitor;

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

        // Tray icon — SystemIcons.Application is a placeholder until Phase 3 provides tray.ico
        // TODO Phase 3: replace with new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "tray.ico"))
        _trayIcon = new NotifyIcon(_components)
        {
            Icon = SystemIcons.Application,
            Text = "BatterMouse",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip(_components);
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        return menu;
    }

    protected override void ExitThreadCore()
    {
        _hidReader.Stop();
        _trayIcon.Visible = false;   // prevents ghost tray icon (Pitfall 3 from RESEARCH.md)
        ToastHelper.Cleanup();       // ToastNotificationManagerCompat.Uninstall()
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _components.Dispose();
        base.Dispose(disposing);
    }
}
