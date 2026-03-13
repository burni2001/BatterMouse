using System;
using System.Diagnostics;
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

    private static HidDevice? FindDevice()
    {
        // Enumerate PID_WIRELESS first (confirmed), then PID_WIRED (unverified fallback)
        var candidates = DeviceList.Local.GetHidDevices(VID, PID_WIRELESS);
        foreach (var d in candidates)
            return d;

        var wiredCandidates = DeviceList.Local.GetHidDevices(VID, PID_WIRED);
        foreach (var d in wiredCandidates)
            return d;

        return null;
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
