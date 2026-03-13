using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using HidSharp;

namespace BatterMouse;

/// <summary>
/// Reads battery level from the Keychron wireless mouse dongle via HID.
/// VID 0x3434 / PID 0xD030 (Keychron Link dongle) — confirmed empirically 2026-03-13.
/// PID 0xD037 (wired) is unverified — enumerated as fallback.
/// </summary>
public class HidReader
{
    public const int VID = 0x3434;
    public const int PID_WIRELESS = 0xD030;
    public const int PID_WIRED    = 0xD037;  // unconfirmed — enumerate as fallback
    public const int BatteryByteOffset = 5;

    public event Action<int>? BatteryLevelReceived;

    private CancellationTokenSource? _cts;
    private Thread? _thread;

    /// <summary>
    /// Returns report[5] as int when report.Length > 5; null otherwise.
    /// Called by unit tests directly (no instance required).
    /// </summary>
    public static int? ParseBattery(byte[] report)
    {
        if (report.Length <= BatteryByteOffset)
            return null;
        return report[BatteryByteOffset];
    }

    /// <summary>
    /// Starts a background thread that enumerates HID devices, opens the dongle,
    /// and raises BatteryLevelReceived whenever a valid battery report is received.
    ///
    /// The thread is IsBackground=true so it does not prevent process exit.
    /// </summary>
    public void Start()
    {
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _thread = new Thread(() => ReadLoop(token))
        {
            IsBackground = true,
            Name = "HidReader"
        };
        _thread.Start();
    }

    /// <summary>
    /// Signals the background thread to stop via CancellationToken.
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
    }

    private void ReadLoop(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            HidDevice? device = FindDevice();

            if (device == null)
            {
                Debug.WriteLine("[HidReader] Device not found — retrying in 5s");
                WaitOrCancel(5000, token);
                continue;
            }

            if (!device.TryOpen(out HidStream? stream))
            {
                Debug.WriteLine("[HidReader] TryOpen failed — retrying in 5s");
                WaitOrCancel(5000, token);
                continue;
            }

            using (stream)
            {
                // Wireless reports arrive infrequently (>20s between updates).
                // Do NOT set a finite ReadTimeout — it will cause spurious IOExceptions.
                stream.ReadTimeout = Timeout.Infinite;

                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        byte[] report = stream.Read();
                        int? level = ParseBattery(report);
                        if (level.HasValue)
                        {
                            BatteryLevelReceived?.Invoke(level.Value);
                        }
                    }
                }
                catch (IOException ex)
                {
                    // Device disconnected — re-enumerate after short delay
                    Debug.WriteLine($"[HidReader] IOException (device disconnect?): {ex.Message}");
                    WaitOrCancel(5000, token);
                }
            }
        }
    }

    // HID usage encoding: upper 16 bits = usage page, lower 16 bits = usage ID
    private const uint BatterySystemUsagePage = 0x008C;
    private const uint GenericDesktopPage     = 0x0001;
    private const uint MouseUsage             = 0x0002;

    private static HidDevice? FindDevice()
    {
        // The Keychron Link dongle exposes 16 HID top-level collections (TLCs).
        // We must open the Battery System TLC (usage_page=0x008C, MI_01), not MI_00.
        //
        // When two Keychron dongles are present (e.g. keyboard + mouse), we prefer
        // the dongle that also has a mouse TLC (usage_page=0x0001, usage=0x0002).
        // Dongles are grouped by their Windows instance ID (the segment between the
        // second and third '#' in the HID device path).

        foreach (var pid in new[] { PID_WIRELESS, PID_WIRED })
        {
            var all = DeviceList.Local.GetHidDevices(VID, pid).ToList();
            if (all.Count == 0) continue;

            // Group TLCs that belong to the same physical dongle
            var groups = all.GroupBy(d => ExtractInstanceId(d.DevicePath)).ToList();

            HidDevice? fallback = null;

            foreach (var group in groups)
            {
                var members = group.ToList();

                var batteryDev = members.FirstOrDefault(d => HasUsagePage(d, BatterySystemUsagePage));
                if (batteryDev == null) continue;

                // Best match: the dongle that also has a mouse TLC
                bool hasMouse = members.Any(d => HasUsage(d, GenericDesktopPage, MouseUsage));
                if (hasMouse)
                {
                    Debug.WriteLine($"[HidReader] Found mouse+battery dongle: {batteryDev.DevicePath}");
                    return batteryDev;
                }

                fallback ??= batteryDev;  // keep as fallback if no mouse TLC found
            }

            if (fallback != null)
            {
                Debug.WriteLine($"[HidReader] Using fallback battery device: {fallback.DevicePath}");
                return fallback;
            }
        }

        return null;
    }

    /// <summary>
    /// Extracts the Windows HID instance ID from a device path.
    /// Path format: \\?\hid#vid_XXXX&amp;pid_XXXX&amp;mi_XX#&lt;instanceId&gt;#{guid}
    /// </summary>
    private static string ExtractInstanceId(string path)
    {
        var parts = path.Split('#');
        return parts.Length >= 3 ? parts[2] : path;
    }

    private static bool HasUsagePage(HidDevice d, uint usagePage)
    {
        try
        {
            var desc = d.GetReportDescriptor();
            return desc.DeviceItems.Any(item =>
                item.Usages.GetAllValues().Any(u => (u >> 16) == usagePage));
        }
        catch { return false; }
    }

    private static bool HasUsage(HidDevice d, uint usagePage, uint usageId)
    {
        try
        {
            var desc = d.GetReportDescriptor();
            return desc.DeviceItems.Any(item =>
                item.Usages.GetAllValues().Any(u => (u >> 16) == usagePage && (u & 0xFFFF) == usageId));
        }
        catch { return false; }
    }

    private static void WaitOrCancel(int milliseconds, CancellationToken token)
    {
        try
        {
            Task.Delay(milliseconds, token).Wait();
        }
        catch (AggregateException)
        {
            // Cancelled — that's fine, loop will exit naturally
        }
    }
}
