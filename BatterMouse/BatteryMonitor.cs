using System;

namespace BatterMouse;

/// <summary>
/// Monitors battery level and fires a notification callback exactly once when the level
/// drops to or below 20%, suppressing re-fires until the level recovers above 20%.
/// </summary>
public class BatteryMonitor
{
    private const int Threshold = 20;

    private readonly Action<int> _notifyCallback;
    private bool _notified = false;

    /// <summary>
    /// Creates a BatteryMonitor with an injectable callback (allows tests to use a spy).
    /// In production, AppContext wires this to ToastHelper.ShowLowBattery.
    /// </summary>
    public BatteryMonitor(Action<int> notifyCallback)
    {
        _notifyCallback = notifyCallback;
    }

    /// <summary>
    /// Called by HidReader.BatteryLevelReceived (wired in AppContext, plan 02-03).
    /// Fires the notification callback on first drop to/below threshold.
    /// Resets state when level recovers above threshold.
    /// </summary>
    public void OnBatteryLevel(int level)
    {
        if (level <= Threshold && !_notified)
        {
            _notified = true;
            _notifyCallback(level);
        }
        else if (level > Threshold)
        {
            _notified = false;
        }
    }
}
