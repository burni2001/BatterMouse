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
    public const int  VID              = 0x3434;
    public const int  PID_WIRELESS     = 0xD030;
    public const int  PID_WIRED        = 0xD037;  // unconfirmed — enumerate as fallback
    public const byte BatteryReportId  = 0x54;    // report ID confirmed empirically 2026-03-13
    public const int  BatteryByteOffset = 5;

    public event Action<int>? BatteryLevelReceived;

    private CancellationTokenSource? _cts;
    private Thread? _thread;

    /// <summary>
    /// Returns report[5] as int when the report has the expected report ID (0x54) and is
    /// long enough; null otherwise.  Ignoring reports with a different ID prevents misreads
    /// when Keychron Launcher is open and the dongle emits additional report types whose
    /// byte 5 is a small value (0x02, 0x04) unrelated to battery percentage.
    /// Called by unit tests directly (no instance required).
    /// </summary>
    public static int? ParseBattery(byte[] report)
    {
        if (report.Length <= BatteryByteOffset)
            return null;
        if (report[0] != BatteryReportId)
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
        // Fires immediately when USB/HID device list changes (cable plug, dongle reconnect, etc.)
        using var deviceChanged = new SemaphoreSlim(0, 1);
        EventHandler<DeviceListChangedEventArgs> onChanged = (_, _) =>
        {
            if (deviceChanged.CurrentCount == 0)
                deviceChanged.Release();
        };
        DeviceList.Local.Changed += onChanged;

        try
        {
            while (!token.IsCancellationRequested)
            {
                HidDevice? device = FindDevice();

                if (device == null)
                {
                    Debug.WriteLine("[HidReader] Device not found — waiting for device change or 5s");
                    WaitForDeviceOrTimeout(deviceChanged, 5000, token);
                    continue;
                }

                if (!device.TryOpen(out HidStream? stream))
                {
                    Debug.WriteLine("[HidReader] TryOpen failed — retrying in 5s");
                    WaitForDeviceOrTimeout(deviceChanged, 5000, token);
                    continue;
                }

                using (stream)
                {
                    // Wireless reports arrive infrequently (>20s between updates).
                    // Do NOT set a finite ReadTimeout — it will cause spurious IOExceptions.
                    stream.ReadTimeout = Timeout.Infinite;

                    // Poll immediately on connect so battery shows without waiting for a
                    // spontaneous report (the device may not send one unprompted).
                    TryPollBattery(stream);

                    // Also re-poll every 30 s to keep the reading fresh.
                    using var pollTimer = new System.Threading.Timer(
                        _ => TryPollBattery(stream),
                        null,
                        TimeSpan.FromSeconds(30),
                        TimeSpan.FromSeconds(30));

                    try
                    {
                        while (!token.IsCancellationRequested)
                        {
                            byte[] report = stream.Read();
                            int? level = ParseBattery(report);
                            // level == 0 is emitted on cable-unplug as a disconnect marker — ignore it
                            if (level is > 0)
                            {
                                BatteryLevelReceived?.Invoke(level.Value);
                            }
                        }
                    }
                    catch (IOException ex)
                    {
                        // Device disconnected — re-enumerate immediately on next device change
                        Debug.WriteLine($"[HidReader] IOException (device disconnect?): {ex.Message}");
                        WaitForDeviceOrTimeout(deviceChanged, 5000, token);
                    }
                }
            }
        }
        finally
        {
            DeviceList.Local.Changed -= onChanged;
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

    /// <summary>
    /// Waits until <paramref name="signal"/> is released, <paramref name="milliseconds"/> elapse,
    /// or <paramref name="token"/> is cancelled — whichever comes first.
    /// </summary>
    private static void WaitForDeviceOrTimeout(SemaphoreSlim signal, int milliseconds, CancellationToken token)
    {
        try
        {
            signal.Wait(milliseconds, token);
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>
    /// Attempts to read battery level on demand via a HID GetFeature call.
    /// This makes the initial reading reliable — the device may not emit report 0x54
    /// spontaneously until polled.  Safe to call from a timer thread.
    /// </summary>
    private void TryPollBattery(HidStream stream)
    {
        try
        {
            int len = Math.Max(8, stream.Device.GetMaxFeatureReportLength());
            var buf = new byte[len];
            buf[0] = BatteryReportId;
            stream.GetFeature(buf);
            int? level = ParseBattery(buf);
            if (level is > 0)
            {
                Debug.WriteLine($"[HidReader] TryPollBattery: {level}%");
                BatteryLevelReceived?.Invoke(level.Value);
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HidReader] TryPollBattery failed: {ex.Message}");
        }
    }
}
