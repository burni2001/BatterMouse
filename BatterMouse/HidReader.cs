using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using HidSharp;

namespace BatterMouse;

/// <summary>
/// Reads battery level from the Keychron M6 wireless mouse dongle via HID.
/// VID 0x3434 / PID 0xD030 (Keychron Link 2.4 GHz dongle) — confirmed empirically 2026-03-13.
/// PID 0xD037 (wired) is unverified — enumerated as fallback.
///
/// How it works:
///   1. FindDevice() locates the FF60 vendor interface (mi_03) on the MOUSE dongle,
///      disambiguating from a co-plugged keyboard dongle via CfgMgr32 USB parent matching.
///   2. A Battery TLC listener (mi_01 / usage_page=0x008C) is opened on the same dongle.
///   3. The device spontaneously pushes a 54-E2 report when the wireless link is established
///      and whenever the battery drops by 1%.  Battery% is at report[5].
///
/// Report formats on mi_01 (Battery System TLC):
///   54-E2 xx xx xx xx BATT STATUS ...  — MOUSE battery (pushed on connect + 1% change)
///   54-E4 xx ...                        — KEYBOARD battery = 100% (not used)
/// </summary>
public class HidReader
{
    public const int  VID                = 0x3434;
    public const int  PID_WIRELESS      = 0xD030;
    public const int  PID_WIRED         = 0xD037;  // unconfirmed — enumerate as fallback
    /// <summary>
    /// Enumerates as a USB HID composite device when the USB-C charging cable is plugged
    /// into the mouse.  Disappears when the cable is unplugged.  Confirmed empirically
    /// 2026-03-16 via DeviceList logging: 20 devices (D03F present) → 16 (D03F absent).
    /// </summary>
    public const int  PID_WIRED_CHARGING = 0xD03F;
    public const byte BatteryReportId      = 0x54;  // report ID confirmed empirically 2026-03-13
    public const int  BatteryByteOffset    = 5;    // offset of battery% within a 54-E2 report
    public const int  ChargingStatusOffset = 6;    // STATUS byte in 54-E2 report — non-zero = charging (empirical assumption; log output confirms value)
    public const byte MouseBatterySubType    = 0xE2;  // r[1] == 0xE2 → mouse battery
    public const byte KeyboardBatteryMinByte = 0x80;  // r[1] >= 0x80 (non-E2) → keyboard battery

    public event Action<int>?  BatteryLevelReceived;
    /// <summary>
    /// Fired when a 54-E4 (or any 54-r[1]>=0x80 non-E2) report arrives from any VID=0x3434 dongle.
    /// Value is battery%, decoded as (r[1] &amp; 0x7F).  Runs independently of the mouse loop.
    /// </summary>
    public event Action<int>?  KeyboardBatteryLevelReceived;
    /// <summary>
    /// Fired whenever a 54-E2 report is received.  <c>true</c> = charging (r[6] != 0);
    /// <c>false</c> = on battery.  Raw STATUS byte is logged for empirical verification.
    /// </summary>
    public event Action<bool>? ChargingStatusChanged;

    /// <summary>
    /// Fired when the wireless link is established (0xB2 hello on the FF60 stream).
    /// Used to reset charging state when the mouse switches from wired to wireless mode.
    /// </summary>
    public event Action? WirelessLinkEstablished;

    private CancellationTokenSource? _cts;
    private Thread? _thread;
    private bool _lastWiredState = false;
    private CancellationTokenSource? _chargingTlcCts;
    private CancellationToken _readLoopToken;
    private CancellationTokenSource? _keyboardTlcCts;

    private static readonly string LogPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "BatterMouse", "hid.log");

    private static readonly string CachePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "BatterMouse", "battery.cache");

    private static readonly string KeyboardCachePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                     "BatterMouse", "keyboard_battery.cache");

    private static void SaveLastLevel(int level)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath)!);
            File.WriteAllText(CachePath, level.ToString());
        }
        catch { }
    }

    private static void SaveLastKeyboardLevel(int level)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(KeyboardCachePath)!);
            File.WriteAllText(KeyboardCachePath, level.ToString());
        }
        catch { }
    }

    /// <summary>Returns the last battery level written to disk, or null if unavailable.</summary>
    public static int? LoadLastLevel()
    {
        try
        {
            if (!File.Exists(CachePath)) return null;
            return int.TryParse(File.ReadAllText(CachePath).Trim(), out int v) && v > 0 && v <= 100
                ? v : null;
        }
        catch { return null; }
    }

    /// <summary>Returns the last keyboard battery level written to disk, or null if unavailable.</summary>
    public static int? LoadLastKeyboardLevel()
    {
        try
        {
            if (!File.Exists(KeyboardCachePath)) return null;
            return int.TryParse(File.ReadAllText(KeyboardCachePath).Trim(), out int v) && v > 0 && v <= 100
                ? v : null;
        }
        catch { return null; }
    }

    internal static void Log(string msg)
    {
        string line = $"{DateTime.Now:HH:mm:ss.fff} {msg}";
        Debug.WriteLine($"[HidReader] {line}");
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, line + Environment.NewLine);
        }
        catch { }
    }

    /// <summary>
    /// Extracts battery percentage from a 0x54 HID report (Battery TLC, mi_01).
    ///
    /// The mouse dongle pushes a 54-E2 report spontaneously: r[1]=0xE2, r[5]=battery%.
    /// The keyboard dongle pushes 54-E4: r[1]=0xE4, r[1]&amp;0x7F = 100 (keyboard battery).
    /// This method handles both; callers must filter by dongle identity when relevant.
    ///
    /// Called by unit tests directly.
    /// </summary>
    public static int? ParseBattery(byte[] report)
    {
        if (report.Length <= BatteryByteOffset)
            return null;
        if (report[0] != BatteryReportId)
            return null;

        // 54-E2 format (mouse battery): r[1]=0xE2, battery% at r[5]
        if (report[1] == 0xE2)
            return report[BatteryByteOffset];

        // 54-E4 format (keyboard battery): battery encoded in lower 7 bits of r[1]
        if (report[1] >= 0x80)
            return report[1] & 0x7F;

        // Legacy spontaneous format: battery at r[5]
        return report[BatteryByteOffset];
    }

    /// <summary>
    /// Returns <c>true</c> if the 54-E2 STATUS byte (r[6]) indicates the mouse is charging.
    /// Returns <c>false</c> if discharging, <c>null</c> if the report is not a 54-E2 or too short.
    ///
    /// Assumption: r[6] != 0 means charging.  Log output ("STATUS=0x??") should be checked
    /// against hardware to confirm the exact encoding (e.g. 0x01 = charging, 0x02 = full).
    /// </summary>
    public static bool? ParseChargingStatus(byte[] report)
    {
        if (report.Length <= ChargingStatusOffset) return null;
        if (report[0] != BatteryReportId || report[1] != 0xE2) return null;
        return report[ChargingStatusOffset] != 0;
    }

    /// <summary>
    /// Starts a background thread that enumerates HID devices, opens the dongle,
    /// and raises BatteryLevelReceived whenever a 54-E2 battery report is received.
    /// Also starts an independent keyboard scan loop that raises KeyboardBatteryLevelReceived
    /// from 54-E4 reports, regardless of whether the mouse dongle is present.
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

        new Thread(() => KeyboardScanLoop(token))
        {
            IsBackground = true,
            Name = "KeyboardScan"
        }.Start();
    }

    /// <summary>
    /// Signals the background thread to stop via CancellationToken.
    /// </summary>
    public void Stop()
    {
        _cts?.Cancel();
        _keyboardTlcCts?.Cancel();
    }

    private void ReadLoop(CancellationToken token)
    {
        _readLoopToken = token;
        using var deviceChanged = new SemaphoreSlim(0, 1);
        EventHandler<DeviceListChangedEventArgs> onChanged = (_, _) =>
        {
            CheckAndFireWiredState();
            if (deviceChanged.CurrentCount == 0)
                deviceChanged.Release();
        };
        DeviceList.Local.Changed += onChanged;
        // Fire immediately in case the cable is already plugged in when the app starts.
        CheckAndFireWiredState();

        try
        {
            while (!token.IsCancellationRequested)
            {
                HidDevice? device = FindDevice();

                if (device == null)
                {
                    Log("Device not found — waiting for device change or 5s");
                    WaitForDeviceOrTimeout(deviceChanged, 5000, token);
                    continue;
                }

                if (!device.TryOpen(out HidStream? stream))
                {
                    Log($"TryOpen failed on {device.DevicePath} — retrying in 5s");
                    WaitForDeviceOrTimeout(deviceChanged, 5000, token);
                    continue;
                }

                Log($"Stream opened: {device.DevicePath}");

                using (stream)
                {
                    // Battery reports arrive on mi_01 (Battery TLC), not on mi_03 (FF60).
                    // Open the Battery TLC now, tied to this stream's lifetime.
                    using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    StartBatteryTlcListener(streamCts.Token);

                    stream.ReadTimeout = Timeout.Infinite;

                    try
                    {
                        while (!token.IsCancellationRequested)
                        {
                            byte[] report = stream.Read();

                            // 0xB2 = device hello: wireless link established.
                            // Fires after the mouse switches from wired-charging to wireless.
                            if (report.Length >= 2 && report[1] == 0xB2)
                            {
                                Log("Device hello received (wireless link established)");
                                WirelessLinkEstablished?.Invoke();
                            }
                        }
                    }
                    catch (IOException ex)
                    {
                        Log($"[FF60] IOException — {ex.Message}");
                        streamCts.Cancel();
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
    private const uint VendorUsagePage        = 0xFF60;  // Keychron custom/raw HID (mi_03)

    // --- Windows Configuration Manager API ---
    // Used to walk up the device tree to the USB composite device, so we can
    // correlate HID TLCs (mi_00, mi_01, mi_03) that belong to the same physical dongle.
    // TLCs on the same dongle share a common USB composite device ancestor.

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Locate_DevNodeW(out uint pdnDevInst, string pDeviceID, uint ulFlags);

    [DllImport("CfgMgr32.dll")]
    private static extern int CM_Get_Parent(out uint pdnDevInst, uint dnDevInst, uint ulFlags);

    [DllImport("CfgMgr32.dll", CharSet = CharSet.Unicode)]
    private static extern int CM_Get_Device_IDW(uint dnDevInst, StringBuilder Buffer, uint BufferLen, uint ulFlags);

    private const int CR_SUCCESS = 0;

    /// <summary>
    /// Returns the USB composite device instance ID (e.g. "USB\VID_3434&amp;PID_D030\...")
    /// for a HID device path.  All TLCs on the same physical USB dongle share this parent
    /// even though their own instance IDs differ per USB interface.
    /// Returns null if the device tree walk fails (non-fatal; falls back to heuristics).
    /// </summary>
    private static string? GetUsbCompositeParentId(string hidDevicePath)
    {
        // \\?\hid#vid_3434&pid_d030&mi_01&col01#9&3b58df5&0&0000#{guid}
        // → HID\VID_3434&PID_D030&MI_01&COL01\9&3B58DF5&0&0000
        var parts = hidDevicePath.Split('#');
        if (parts.Length < 3) return null;
        string instanceId = $"HID\\{parts[1]}\\{parts[2]}".ToUpperInvariant();

        if (CM_Locate_DevNodeW(out uint devNode, instanceId, 0) != CR_SUCCESS)
            return null;

        // Walk up the device tree, looking for the USB composite device.
        // The composite device path looks like "USB\VID_3434&PID_D030\{port_or_serial}"
        // (no "&MI_" segment, distinguishing it from USB interface devices like
        // "USB\VID_3434&PID_D030&MI_01\...").
        uint current = devNode;
        for (int depth = 0; depth < 6; depth++)
        {
            if (CM_Get_Parent(out uint parent, current, 0) != CR_SUCCESS)
                return null;

            var sb = new StringBuilder(300);
            if (CM_Get_Device_IDW(parent, sb, 300, 0) == CR_SUCCESS)
            {
                string id = sb.ToString();
                if (id.StartsWith("USB\\", StringComparison.OrdinalIgnoreCase) &&
                    id.IndexOf("&MI_", StringComparison.OrdinalIgnoreCase) < 0)
                    return id.ToUpperInvariant();
            }

            current = parent;
        }

        return null;
    }

    private static HidDevice? FindDevice()
    {
        // The Keychron Link dongle exposes several HID top-level collections (TLCs).
        //
        // mi_03 (usage_page=0xFF60) is the vendor/raw HID interface used by the
        // Keychron Launcher — we open this as the "primary" stream to detect the
        // wireless hello (0xB2) and monitor for disconnect.
        //
        // When two Keychron dongles are present (keyboard + mouse) there will be two
        // FF60 interfaces.  We pick the one whose USB composite ancestor also hosts a
        // Mouse TLC (mi_00, usage=0x0001/0x0002).  Falls back to the first FF60 found.

        // Log all Keychron devices at startup so the K5 Max keyboard dongle PID is
        // discoverable from hid.log without needing Device Manager.
        foreach (var d in DeviceList.Local.GetHidDevices(VID))
            Log($"[Enumerate] VID=0x{VID:X4} PID=0x{d.ProductID:X4} path={d.DevicePath}");

        foreach (var pid in new[] { PID_WIRELESS, PID_WIRED })
        {
            var all = DeviceList.Local.GetHidDevices(VID, pid).ToList();
            if (all.Count == 0) continue;

            var vendorDevs = all.Where(d => HasUsagePage(d, VendorUsagePage)).ToList();

            if (vendorDevs.Count == 1)
            {
                Log($"Found vendor (FF60) device: {vendorDevs[0].DevicePath}");
                return vendorDevs[0];
            }

            if (vendorDevs.Count > 1)
            {
                var mouseDevs = all.Where(d => HasUsage(d, GenericDesktopPage, MouseUsage)).ToList();
                var mouseParents = new HashSet<string>(
                    mouseDevs
                        .Select(d => GetUsbCompositeParentId(d.DevicePath))
                        .Where(s => s != null)
                        .Select(s => s!));

                HidDevice? best = null;
                if (mouseParents.Count > 0)
                {
                    best = vendorDevs.FirstOrDefault(d =>
                    {
                        var p = GetUsbCompositeParentId(d.DevicePath);
                        return p != null && mouseParents.Contains(p);
                    });
                }

                var chosen = best ?? vendorDevs[0];
                Log($"Found vendor (FF60) device (multi-dongle pick{(best != null ? " via USB parent" : " fallback")}): {chosen.DevicePath}");
                return chosen;
            }

            // No FF60 found — fall back to the Battery System TLC
            var groups = all.GroupBy(d => ExtractInstanceId(d.DevicePath)).ToList();
            HidDevice? fallback = null;

            foreach (var group in groups)
            {
                var members = group.ToList();
                var batteryDev = members.FirstOrDefault(d => HasUsagePage(d, BatterySystemUsagePage));
                if (batteryDev == null) continue;

                bool hasMouse = members.Any(d => HasUsage(d, GenericDesktopPage, MouseUsage));
                if (hasMouse)
                {
                    Log($"Found mouse+battery dongle (Battery TLC fallback): {batteryDev.DevicePath}");
                    return batteryDev;
                }

                fallback ??= batteryDev;
            }

            if (fallback != null)
            {
                Log($"Using battery TLC fallback: {fallback.DevicePath}");
                return fallback;
            }
        }

        return null;
    }

    /// <summary>
    /// Opens the Battery System TLC (mi_01, usage_page=0x008C) on the same physical dongle
    /// as <paramref name="primaryDevice"/> and reads 54-E2 battery reports.
    ///
    /// The device pushes a 54-E2 report spontaneously when the wireless link is established
    /// and whenever the battery drops by 1%.  No query command is needed.
    /// The thread runs until <paramref name="token"/> is cancelled.
    /// </summary>
    private void StartBatteryTlcListener(CancellationToken token)
    {
        // Open ALL Battery System TLCs across both dongles.
        // We rely on the report type to identify the mouse:
        //   54-E2 (r[1]==0xE2, r[5]=batt%) — MOUSE battery (accept)
        //   54-E4 (r[1]==0xE4)              — KEYBOARD battery (ignore)
        // This avoids a fragile USB-parent match that can pick the wrong dongle
        // when instance IDs change between sessions.
        foreach (var pid in new[] { PID_WIRELESS, PID_WIRED })
        {
            foreach (var dev in DeviceList.Local.GetHidDevices(VID, pid))
            {
                if (!HasUsagePage(dev, BatterySystemUsagePage)) continue;

                var capture = dev;
                new Thread(() =>
                {
                    if (!capture.TryOpen(out HidStream? s))
                    {
                        Log($"[BatteryTLC] TryOpen FAILED: {capture.DevicePath}");
                        return;
                    }
                    using (s)
                    {
                        s.ReadTimeout = Timeout.Infinite;
                        Log($"[BatteryTLC] Opened: {capture.DevicePath}");

                        try
                        {
                            while (!token.IsCancellationRequested)
                            {
                                byte[] r = s.Read();

                                // 54-E2: mouse battery notification.
                                // r[0]=0x54 (reportId), r[1]=0xE2 (type), r[5]=battery%
                                // Pushed spontaneously on wireless connect and on each 1% drop.
                                // Confirmed empirically 2026-03-15 on Keychron M6 2.4G, firmware d.3.0.
                                if (r.Length >= 7 && r[0] == BatteryReportId && r[1] == 0xE2)
                                {
                                    int batt = r[5];
                                    // STATUS byte (r[6]) is always 0x04 regardless of charging state —
                                    // charging is detected via PID=0xD03F device list events instead.
                                    Log($"[BatteryTLC] Battery {batt}%, STATUS=0x{r[ChargingStatusOffset]:X2}");
                                    if (batt > 0 && batt <= 100)
                                    {
                                        SaveLastLevel(batt);
                                        BatteryLevelReceived?.Invoke(batt);
                                    }
                                }
                            }
                        }
                        catch (IOException) { }
                    }
                })
                {
                    IsBackground = true,
                    Name = "BatteryTLC"
                }.Start();
            }
        }
    }

    /// <summary>
    /// Independent loop that watches for any VID=0x3434 device with a Battery System TLC
    /// and fires <see cref="KeyboardBatteryLevelReceived"/> on 54-E4 reports.
    /// Runs separately from the mouse ReadLoop so keyboard battery works even when
    /// the mouse dongle is not connected.
    /// </summary>
    private void KeyboardScanLoop(CancellationToken token)
    {
        using var deviceChanged = new SemaphoreSlim(0, 1);
        EventHandler<DeviceListChangedEventArgs> onChanged = (_, _) =>
        {
            // Restart keyboard TLC listeners on device change
            _keyboardTlcCts?.Cancel();
            _keyboardTlcCts?.Dispose();
            _keyboardTlcCts = null;

            if (deviceChanged.CurrentCount == 0)
                deviceChanged.Release();
        };
        DeviceList.Local.Changed += onChanged;

        try
        {
            while (!token.IsCancellationRequested)
            {
                _keyboardTlcCts?.Dispose();
                _keyboardTlcCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                StartKeyboardTlcListener(_keyboardTlcCts.Token);

                WaitForDeviceOrTimeout(deviceChanged, 5000, token);
            }
        }
        finally
        {
            DeviceList.Local.Changed -= onChanged;
            _keyboardTlcCts?.Cancel();
            _keyboardTlcCts?.Dispose();
            _keyboardTlcCts = null;
        }
    }

    /// <summary>
    /// Opens the Battery System TLC on ALL VID=0x3434 devices (any PID) and listens for
    /// 54-E4 keyboard battery reports.  Mouse 54-E2 reports are deliberately ignored here
    /// — they are handled exclusively by <see cref="StartBatteryTlcListener"/>.
    /// This PID-agnostic scan automatically discovers the K5 Max keyboard dongle PID;
    /// check hid.log for [Enumerate] lines to identify it.
    /// </summary>
    private void StartKeyboardTlcListener(CancellationToken token)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var dev in DeviceList.Local.GetHidDevices(VID))
        {
            if (!HasUsagePage(dev, BatterySystemUsagePage)) continue;
            if (!seen.Add(dev.DevicePath)) continue;

            Log($"[KeyboardTLC] Candidate PID=0x{dev.ProductID:X4} path={dev.DevicePath}");

            var capture = dev;
            new Thread(() =>
            {
                if (!capture.TryOpen(out HidStream? s))
                {
                    Log($"[KeyboardTLC] TryOpen FAILED: {capture.DevicePath}");
                    return;
                }
                using (s)
                {
                    s.ReadTimeout = Timeout.Infinite;
                    Log($"[KeyboardTLC] Opened: {capture.DevicePath}");

                    try
                    {
                        while (!token.IsCancellationRequested)
                        {
                            byte[] r = s.Read();

                            if (r.Length >= 2 && r[0] == BatteryReportId
                                && r[1] != MouseBatterySubType && r[1] >= KeyboardBatteryMinByte)
                            {
                                int batt = r[1] & 0x7F;
                                Log($"[KeyboardTLC] Battery {batt}%  path={capture.DevicePath}");
                                if (batt > 0 && batt <= 100)
                                {
                                    SaveLastKeyboardLevel(batt);
                                    KeyboardBatteryLevelReceived?.Invoke(batt);
                                }
                            }
                        }
                    }
                    catch (IOException) { }
                }
            })
            {
                IsBackground = true,
                Name = "KeyboardTLC"
            }.Start();
        }
    }

    /// <summary>
    /// Checks whether a PID=0xD03F device (USB-C cable plugged into mouse) is present
    /// and fires <see cref="ChargingStatusChanged"/> if the state has changed.
    /// Called on every DeviceList.Changed event and once at startup.
    /// </summary>
    private void CheckAndFireWiredState()
    {
        bool wired = DeviceList.Local.GetHidDevices(VID, PID_WIRED_CHARGING).Any();
        if (wired == _lastWiredState) return;
        _lastWiredState = wired;
        Log($"[Charging] PID=0xD03F {(wired ? "appeared" : "disappeared")} — charging={wired}");

        _chargingTlcCts?.Cancel();
        _chargingTlcCts?.Dispose();
        _chargingTlcCts = null;

        if (wired)
        {
            _chargingTlcCts = CancellationTokenSource.CreateLinkedTokenSource(_readLoopToken);
            StartChargingBatteryTlcListener(_chargingTlcCts.Token);
        }

        ChargingStatusChanged?.Invoke(wired);
    }

    /// <summary>
    /// Opens Battery System TLC streams on the PID_WIRED_CHARGING device so that battery
    /// level reports received while the USB-C cable is plugged in are forwarded to
    /// <see cref="BatteryLevelReceived"/>.  Called when the charging device appears.
    /// </summary>
    private void StartChargingBatteryTlcListener(CancellationToken token)
    {
        foreach (var dev in DeviceList.Local.GetHidDevices(VID, PID_WIRED_CHARGING))
        {
            if (!HasUsagePage(dev, BatterySystemUsagePage)) continue;

            var capture = dev;
            new Thread(() =>
            {
                if (!capture.TryOpen(out HidStream? s))
                {
                    Log($"[ChargingTLC] TryOpen FAILED: {capture.DevicePath}");
                    return;
                }
                using (s)
                {
                    s.ReadTimeout = Timeout.Infinite;
                    Log($"[ChargingTLC] Opened: {capture.DevicePath}");
                    try
                    {
                        while (!token.IsCancellationRequested)
                        {
                            byte[] r = s.Read();
                            if (r.Length >= 7 && r[0] == BatteryReportId && r[1] == 0xE2)
                            {
                                int batt = r[5];
                                Log($"[ChargingTLC] Battery {batt}%");
                                if (batt > 0 && batt <= 100)
                                {
                                    SaveLastLevel(batt);
                                    BatteryLevelReceived?.Invoke(batt);
                                }
                            }
                        }
                    }
                    catch (IOException) { }
                }
            })
            {
                IsBackground = true,
                Name = "ChargingBatteryTLC"
            }.Start();
        }
    }

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

    private static void WaitForDeviceOrTimeout(SemaphoreSlim signal, int milliseconds, CancellationToken token)
    {
        try
        {
            signal.Wait(milliseconds, token);
        }
        catch (OperationCanceledException) { }
    }
}
