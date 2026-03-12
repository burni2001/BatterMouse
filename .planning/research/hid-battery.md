# HID Battery Level Research — BatterMouse

**Researched:** 2026-03-12
**Confidence:** MEDIUM (core Windows HID API: HIGH; Keychron-specific protocol: LOW — requires empirical device probing)

---

## 1. Windows HID Battery APIs

### HID Usage Pages Relevant to Battery

The HID specification defines these battery-related usage pages and usages:

| Usage Page | ID | Name | Battery-related Usages |
|---|---|---|---|
| Battery System | `0x85` | Battery System | Full set of UPS-style battery items (RemainingCapacity, etc.) — used by UPS devices, not typically wireless mice |
| Power Device | `0x84` | Power Device | Companion to Battery System page |
| Generic Desktop | `0x01` | Generic Desktop | Mouse (0x02), Keyboard (0x06) — no standard battery usage here |
| Digitizer | `0x0D` | Digitizer | Usage 0x3B = Battery Strength (for styluses/tablets) |
| Consumer | `0x0C` | Consumer | Various; 0x48 does NOT map to battery (it is Slow in the consumer spec) |
| Vendor-specific | `0xFF**` | Vendor | Most wireless mice report battery via vendor-specific usage pages |

**Key finding:** There is no universal standard HID usage for wireless mouse battery level. The Battery System page (0x85) is designed for UPS hardware. Most consumer wireless mice use vendor-specific feature reports.

### Where Battery Data Actually Comes From in Wireless Mice

Three observed patterns across devices:

1. **Vendor-specific feature report:** Device exposes a vendor usage page (e.g., `0xFF00`–`0xFFFF`). Host sends a feature report request with a specific report ID; device responds with a packet whose nth byte is battery percentage 0–100. Requires reverse engineering to find the right report ID and byte offset.

2. **Unsolicited input reports:** Device sends 32-byte input reports periodically. Battery data is encoded at a known byte offset within those reports. Requires reading and parsing, not polling via GetFeature. This is what the Keychron M3 on Linux does (see section 3).

3. **HID++ protocol (Logitech-specific):** A vendor protocol layered over HID. Feature `0x1000` = "Battery Unified Level Status." Not applicable to Keychron.

---

## 2. Windows APIs for Reading HID Feature Reports (User Mode)

### The Correct API Sequence

```
hid.dll  (user mode)
setupapi.dll  (enumeration)
```

**Step 1 — Enumerate devices:**

```c
// Get the HID class GUID
HidD_GetHidGuid(&hidGuid);  // from hid.dll

// Get a set of all installed HID device interfaces
HDEVINFO devInfo = SetupDiGetClassDevs(
    &hidGuid,
    NULL,
    NULL,
    DIGCF_PRESENT | DIGCF_DEVICEINTERFACE  // from setupapi.dll
);

// Enumerate each device interface
SP_DEVICE_INTERFACE_DATA ifaceData = { sizeof(SP_DEVICE_INTERFACE_DATA) };
for (DWORD i = 0; SetupDiEnumDeviceInterfaces(devInfo, NULL, &hidGuid, i, &ifaceData); i++) {
    // Get the device path
    // Call SetupDiGetDeviceInterfaceDetail with a NULL buffer first to get required size
    // Then call again with allocated buffer to get SP_DEVICE_INTERFACE_DETAIL_DATA
    // ifaceDetail->DevicePath is the path to pass to CreateFile
}
SetupDiDestroyDeviceInfoList(devInfo);
```

**Step 2 — Open the device:**

```c
// For mouse/keyboard TLCs (top-level collections):
// Windows grabs them exclusively. Open with access=0 to read metadata only.
HANDLE hDevice = CreateFile(
    devicePath,
    0,                             // NO read/write — required for mouse/keyboard TLCs
    FILE_SHARE_READ | FILE_SHARE_WRITE,
    NULL,
    OPEN_EXISTING,
    0,
    NULL
);
// With access=0, HidD_GetAttributes still works.
// HidD_GetFeature does NOT work with access=0.

// For vendor-specific TLCs (not captured by mouhid/kbdhid):
HANDLE hDevice = CreateFile(
    devicePath,
    GENERIC_READ | GENERIC_WRITE,
    FILE_SHARE_READ | FILE_SHARE_WRITE,
    NULL,
    OPEN_EXISTING,
    FILE_FLAG_OVERLAPPED,
    NULL
);
```

**Step 3 — Identify the device:**

```c
HIDD_ATTRIBUTES attrs;
attrs.Size = sizeof(HIDD_ATTRIBUTES);
HidD_GetAttributes(hDevice, &attrs);
// attrs.VendorID, attrs.ProductID, attrs.VersionNumber
```

**Step 4 — Understand report structure:**

```c
PHIDP_PREPARSED_DATA preparsedData;
HidD_GetPreparsedData(hDevice, &preparsedData);

HIDP_CAPS caps;
HidP_GetCaps(preparsedData, &caps);
// caps.FeatureReportByteLength — buffer size for feature reports
// caps.InputReportByteLength — buffer size for input reports

HidD_FreePreparsedData(preparsedData);
```

**Step 5 — Read a feature report:**

```c
// Buffer size must equal caps.FeatureReportByteLength
BYTE buffer[caps.FeatureReportByteLength];
memset(buffer, 0, sizeof(buffer));
buffer[0] = reportId;  // Set report ID in first byte (0 if device has no report IDs)

BOOL ok = HidD_GetFeature(hDevice, buffer, sizeof(buffer));
// buffer[0] = report ID (echoed)
// buffer[1..n] = report data
```

**Alternative — read unsolicited input reports:**

```c
// For devices that push input reports (like Keychron M3)
// ReadFile on the device handle (must be opened with GENERIC_READ)
BYTE inputBuf[caps.InputReportByteLength];
DWORD bytesRead;
ReadFile(hDevice, inputBuf, sizeof(inputBuf), &bytesRead, NULL);
// Then parse known byte offset for battery
```

### Critical Windows Constraint: Mouse/Keyboard TLC Exclusivity

Windows's `mouhid.sys` (mouse) and `kbdhid.sys` (keyboard) drivers open the standard HID mouse (Usage Page `0x01`, Usage `0x02`) and keyboard (Usage Page `0x01`, Usage `0x06`) top-level collections **exclusively**. Attempting to open these paths with `GENERIC_READ | GENERIC_WRITE` will return `ERROR_ACCESS_DENIED`.

**Workaround:** Wireless mouse/keyboard dongles typically enumerate as **multiple top-level collections** (TLCs). The mouse movement TLC is locked by `mouhid.sys`, but vendor-specific TLCs (Usage Page `0xFF**`) are not claimed by any system driver and can be opened with full read/write access.

**How to find the right TLC:** Enumerate all HID devices with the same VID+PID combination. Check `HidD_GetAttributes` + `HidP_GetCaps` (specifically `caps.UsagePage` and `caps.Usage`) to identify the vendor-specific interface. Open that path.

### C# P/Invoke Signatures (hid.dll)

```csharp
[DllImport("hid.dll")]
static extern void HidD_GetHidGuid(out Guid hidGuid);

[DllImport("hid.dll", SetLastError = true)]
static extern bool HidD_GetAttributes(IntPtr device, ref HIDD_ATTRIBUTES attributes);

[DllImport("hid.dll", SetLastError = true)]
static extern bool HidD_GetPreparsedData(IntPtr device, out IntPtr preparsedData);

[DllImport("hid.dll", SetLastError = true)]
static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

[DllImport("hid.dll", SetLastError = true)]
static extern bool HidD_GetFeature(IntPtr device, byte[] reportBuffer, int reportBufferLength);

[DllImport("hid.dll", SetLastError = true)]
static extern bool HidD_GetInputReport(IntPtr device, byte[] reportBuffer, int reportBufferLength);

// From hid.dll (actually hidpi.dll — linked via setupapi)
[DllImport("hid.dll")]
static extern int HidP_GetCaps(IntPtr preparsedData, ref HIDP_CAPS caps);
```

Alternatively, avoid raw P/Invoke entirely and use **HidSharp** (NuGet: `HidSharp 2.6.4`) or **HidApi.Net** (NuGet: `HidApi.Net 1.2.0`), both of which wrap these calls.

### UWP/WinRT Alternative: Windows.Devices.HumanInterfaceDevice

Available since Windows 10 (contract v1.0). Provides `HidDevice.GetDeviceSelector(usagePage, usageId, vendorId, productId)` + `HidDevice.GetFeatureReportAsync(reportId)`. Works from packaged WinUI 3 or UWP apps. Requires manifest `<DeviceCapability>` declarations. **Not available in plain Win32/WinForms apps** without WinRT interop. Adds packaging/deployment complexity. Not recommended for a minimal tray app.

---

## 3. Keychron-Specific HID Battery Reporting

### Confirmed Device Identifiers

| Model | VID | PID (2.4GHz dongle) | PID (wired/charging) |
|---|---|---|---|
| Keychron M3 | `0x3434` | `0xD034` | `0xD037` |
| Other Keychron mice | `0x3434` | Unknown — must probe | Unknown |

Vendor ID `0x3434` = Keychron, Inc. (confirmed via USB-IF database).

### What Is Known About the Protocol

**Confidence: LOW** — The following is inferred from the `keychron-m3-linux` open source project and community reports, not from official Keychron documentation.

- The M3 mouse sends **32-byte HID input reports** continuously via the 2.4GHz dongle.
- The battery percentage is encoded at **a known byte offset within those 32-byte input reports**.
- The exact byte offset is in the `main.py` source of [byte-bandit/keychron-m3-linux](https://github.com/byte-bandit/keychron-m3-linux) but is not documented in the README. The source file must be read directly.
- The device is "not very talkative when idle" — reports are not sent at a fixed high rate when the mouse is motionless.
- The project uses two threads: one monitoring each PID (2.4GHz and wired), and caches the last known battery value between report gaps.

### What Chrome WebHID Is Doing

Chrome's WebHID can read the Keychron battery because:
1. The dongle presents a vendor-specific TLC (Usage Page likely `0xFF**`) that is NOT claimed by `mouhid.sys`.
2. WebHID (via the browser's HID backend) opens that vendor-specific interface.
3. A feature report or input report at a known report ID contains the battery byte.

This confirms the battery data is accessible from user mode without kernel drivers. The blocking factor on Windows is finding the right TLC path, not elevated permissions.

### QMK Raw HID Interface (for QMK-based Keychron models)

Keychron keyboards (not mice) running QMK firmware expose a **Raw HID interface**:
- Usage Page: `0xFF60`
- Usage ID: `0x61`
- Report size: 32 bytes

This interface is NOT claimed by the system and can be opened freely. However:
- This applies to **keyboards**, not the M3/M6 mice.
- QMK's battery feature does not currently emit battery level over Raw HID automatically.
- A custom QMK firmware modification could send battery level over Raw HID, but that requires flashing firmware — not a user-facing solution.

### Recommended Discovery Procedure for Any Keychron Mouse

Because Keychron does not publish HID report descriptors or battery byte offsets:

1. **Capture the report descriptor** using `HidD_GetReportDescriptor` or the Windows built-in HID descriptor viewer. This reveals all usage pages, report IDs, and field definitions the firmware declares.

2. **Use USBPcap + Wireshark** to capture live USB traffic while the mouse is connected. Look for:
   - Feature report responses (URB_FUNCTION_CONTROL_TRANSFER with GET_REPORT)
   - Input report content when battery is known (e.g., fresh charge vs. nearly dead)
   - The byte that changes as battery level changes

3. **Brute-force scan** all report IDs 0x00–0xFF via `HidD_GetFeature` on the vendor-specific TLC. Log responses and look for a byte that contains a value consistent with the known battery percentage.

4. **Reference the keychron-m3-linux source** — the actual `main.py` code contains the byte offset for the M3. For other models, repeat the above.

---

## 4. Language and Technology Stack Comparison

### Option A: C# (.NET 8 + WinForms)

**HID access:** P/Invoke to `hid.dll` + `setupapi.dll`, or HidSharp NuGet package.

```csharp
// Minimal tray app skeleton with HidSharp
using HidSharp;
using System.Windows.Forms;

var device = DeviceList.Local.GetHidDeviceOrNull(vendorID: 0x3434, productID: 0xD034);
if (device != null) {
    using var stream = device.Open();
    stream.ReadTimeout = 1000;
    var report = new byte[32];
    stream.Read(report, 0, report.Length);
    int batteryPercent = report[/* known offset */];
}

// System tray
var tray = new NotifyIcon {
    Icon = SystemIcons.Application,
    Text = $"Battery: {batteryPercent}%",
    Visible = true
};
Application.Run();
```

**Pros:**
- Fastest path from zero to working app. WinForms `NotifyIcon` is mature and reliable.
- HidSharp (NuGet) handles multi-platform HID enumeration, report parsing, and the vendor-specific TLC selection automatically via `UsagePage`/`UsageId` filtering.
- .NET 8 produces a single-file executable with `PublishSingleFile=true`.
- No manifest complexity (unlike UWP/WinRT HID).
- Large community, abundant examples for HID tray apps.

**Cons:**
- WinForms `NotifyIcon` has a known Windows 11 bug where tooltip text doesn't update correctly in some builds (issue #12373 on dotnet/winforms). Workaround: destroy and recreate the icon, or use `H.NotifyIcon` NuGet package.
- Not a "native" Windows 11 look (though for a tray app this is irrelevant).

**Verdict: Recommended for BatterMouse.**

### Option B: C++ (Win32)

**HID access:** Direct calls to `hid.dll` / `setupapi.dll`. No wrapper overhead.

**Pros:**
- Smallest binary, lowest overhead.
- Complete control over the HID API surface.

**Cons:**
- Shell_NotifyIcon tray implementation requires manual message loop and WNDCLASSEX registration — significant boilerplate for a simple tray app.
- Memory management burden.
- Much slower development cycle than C#.
- No meaningful performance advantage for a battery poller that wakes up every 60 seconds.

**Verdict: Avoid unless binary size is a hard constraint.**

### Option C: Rust (windows-rs crate)

**HID access:** `hidapi` crate (wraps libhidapi), or direct `windows` crate bindings to `SetupDi*` + `HidD_*`.

```toml
[dependencies]
hidapi = "2.6"
tray-icon = "0.19"  # or notify-icon crate
```

**Pros:**
- Real-world precedent: `aarol/headset-battery-indicator` uses exactly this pattern (Rust + hidapi + tray-icon) to read headset battery via HID and show it in the tray. Proven feasible.
- Memory safety with no GC pauses.
- Small binaries (~2 MB cited for the headset project).

**Cons:**
- Rust HID tray ecosystem is fragmented: `tray-icon`, `tray-item`, `systray-rs`, `notify-icon-rs` all exist but are less mature than WinForms NotifyIcon.
- Longer initial setup vs C# (no NuGet-equivalent one-liner for tray).
- Requires MSVC toolchain to compile C dependencies (`hidapi` links to a C library).
- Steeper learning curve if the team is not already in Rust.

**Verdict: Good choice if you want a minimal binary or already use Rust. Secondary recommendation.**

### Option D: Python (hid / pywin32)

**HID access:** `hid` PyPI package (wrapper around hidapi).

```python
import hid

device = hid.device()
device.open(0x3434, 0xD034)
device.set_nonblocking(1)
report = device.read(32)
battery = report[BATTERY_BYTE_OFFSET]
```

**Pros:**
- Fastest to prototype — ideal for the discovery/reverse-engineering phase.
- Interactive: run from REPL to brute-force report IDs and inspect bytes in real time.

**Cons:**
- Poor fit for a production Windows tray app. Requires Python runtime or a bulky PyInstaller bundle (50–150 MB).
- No good cross-platform tray library; pystray exists but is unmaintained.
- No single-file distribution story.

**Verdict: Use Python only for the initial protocol discovery/reverse-engineering phase. Do not ship it.**

### Recommendation Summary

| Criterion | C# .NET 8 | C++ Win32 | Rust | Python |
|---|---|---|---|---|
| Time to working app | Fast | Slow | Medium | Very fast |
| Distribution | Single .exe | Single .exe | Single .exe | Heavy bundle |
| HID library quality | HidSharp (excellent) | Raw Win32 (solid) | hidapi (good) | hid (good for prototyping) |
| Tray support | NotifyIcon (mature) | Shell_NotifyIcon (verbose) | Fragmented | pystray (poor) |
| Recommended for | Production app | No | Production app | Prototyping only |

**Use C# .NET 8 + WinForms + HidSharp for the shipping app. Use Python + hid for the initial protocol reverse-engineering phase.**

---

## 5. Step-by-Step Implementation Plan

### Phase 0: Protocol Discovery (Python)

```python
import hid, time

# Enumerate all interfaces for the Keychron dongle
for d in hid.enumerate(0x3434, 0):
    print(f"PID: {d['product_id']:#06x}  UsagePage: {d['usage_page']:#06x}  "
          f"Usage: {d['usage']:#06x}  Interface: {d['interface_number']}")
    print(f"  Path: {d['path']}")
```

Then, for each vendor-specific interface (UsagePage >= 0xFF00):

```python
import hid

dev = hid.device()
dev.open_path(b"/path/to/device")  # use path from enumerate, not VID/PID
dev.set_nonblocking(0)

# Method 1: Scan feature reports
for report_id in range(0x01, 0x20):
    buf = [report_id] + [0x00] * 31
    try:
        dev.send_feature_report(buf)
        resp = dev.get_feature_report(report_id, 32)
        print(f"Report {report_id:#04x}: {resp.hex()}")
    except Exception as e:
        pass

# Method 2: Read unsolicited input reports
while True:
    data = dev.read(32, timeout_ms=2000)
    if data:
        print(' '.join(f'{b:02x}' for b in data))
    time.sleep(0.1)

dev.close()
```

Move the mouse to trigger reports. Watch for a byte that holds 0–100.

### Phase 1: Production App (C# .NET 8)

1. Create a WinForms project targeting `net8.0-windows`.
2. Add NuGet: `HidSharp 2.6.4`.
3. Enumerate HID devices filtering by VID `0x3434`.
4. For each matching device, check `UsagePage` via HidSharp's `HidDevice.ReportDescriptor` or filter by `UsagePage != 0x01` to skip the mouse TLC.
5. Open the vendor-specific TLC stream.
6. Start a background thread that reads reports at a configurable interval (e.g., every 30 seconds).
7. Parse battery byte from the known offset.
8. Update `NotifyIcon.Text` with the percentage.

```csharp
var list = DeviceList.Local;
list.Changed += (s, e) => RefreshDevices();  // hot-plug support

HidDevice FindBatteryInterface() {
    return list.GetHidDevices(vendorID: 0x3434)
               .FirstOrDefault(d => d.VendorID == 0x3434
                                 && d.ProductID == 0xD034
                                 && d.GetReportDescriptor().DeviceItems
                                    .Any(i => i.Usages.GetAllValues()
                                               .Any(u => /* vendor usage page check */)));
}
```

---

## 6. Key Pitfalls and Mitigations

| Pitfall | What Goes Wrong | Mitigation |
|---|---|---|
| Opening the mouse TLC | `ERROR_ACCESS_DENIED` — mouhid.sys owns it | Enumerate all TLCs for the VID/PID; select vendor-specific usage page (0xFF**) |
| Report ID = 0 assumption | Sending GetFeature with buffer[0]=0 on multi-report device returns wrong data | Always query `HidP_GetCaps` to get report byte lengths; inspect the report descriptor for declared IDs |
| Mouse idle silence | No input reports arrive when mouse is not moving | Cache last known battery; do not treat silence as 0% |
| Windows 11 NotifyIcon bug | Icon tooltip text doesn't update | Use `H.NotifyIcon` NuGet or recreate the icon on update |
| Keychron model variation | M3 byte offset may differ from M6, M4, M7 | Each model needs independent protocol discovery; do not hardcode M3 offsets for other models |
| Python discovery → wrong PID | Python `hid.open(vid, pid)` picks the first matching interface, which may be the mouse TLC | Always use `hid.open_path()` with the specific path from `hid.enumerate()`, filtering by UsagePage |
| WebHID confirmation is from a different interface | Chrome may be reading the keyboard TLC or a different collection than expected | Use USBPcap to confirm which interface Chrome is actually using during battery reads |

---

## 7. Tools for Protocol Discovery on Windows

| Tool | Purpose |
|---|---|
| **USBPcap** + **Wireshark** | Capture raw USB traffic. Filter by `usbhid` in Wireshark display filters. The HID report descriptor is visible in the first few packets after device connect. Use to find which report ID returns battery data. |
| **Device Manager → Details → Hardware IDs** | Reveals VID, PID, and UsagePage/UsageId for each TLC enumerated from the dongle. |
| **Python `hid.enumerate()`** | Lists all TLCs with paths, usage pages, and usage IDs without opening any device. Safe to run at any time. |
| **HID Descriptor Tool** (USB-IF) | Parses raw HID report descriptor bytes into human-readable form. Useful after capturing the descriptor via USBPcap or `HidD_GetReportDescriptor`. |

---

## Sources

- [Finding and Opening a HID Collection — Microsoft Docs](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/finding-and-opening-a-hid-collection)
- [Obtaining HID Reports — Microsoft Docs](https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/obtaining-hid-reports)
- [HidD_GetFeature — hidsdi.h reference](https://learn.microsoft.com/en-us/windows-hardware/drivers/ddi/hidsdi/nf-hidsdi-hidd_getfeature)
- [HidDevice Class — Windows.Devices.HumanInterfaceDevice](https://learn.microsoft.com/en-us/uwp/api/windows.devices.humaninterfacedevice.hiddevice?view=winrt-26100)
- [QMK Raw HID — usage page 0xFF60](https://docs.qmk.fm/features/rawhid)
- [keychron-m3-linux — battery monitor for Keychron M3 (Linux)](https://github.com/byte-bandit/keychron-m3-linux)
- [keychron_battery_widget — MrAdrianPl (mentions USBPcap approach)](https://github.com/MrAdrianPl/keychron_battery_widget)
- [aarol/headset-battery-indicator — Rust + hidapi + tray](https://github.com/aarol/headset-battery-indicator)
- [aarol.dev: Creating a battery indicator app (SteelSeries Arctis)](https://aarol.dev/posts/arctis-hid/)
- [andyvorld/LGSTrayBattery — C# .NET 8 + hidapi + Logitech HID++](https://github.com/andyvorld/LGSTrayBattery)
- [HIDAPI discussions: HID keyboard/mouse access on Windows](https://github.com/libusb/hidapi/discussions/403)
- [HidSharp NuGet](https://www.nuget.org/packages/hidsharp/)
- [HidApi.Net NuGet](https://www.nuget.org/packages/HidApi.Net)
- [H.NotifyIcon NuGet (WinForms/WPF/WinUI)](https://github.com/HavenDV/H.NotifyIcon)
- [USBPcap — USB capture for Windows](https://desowin.org/usbpcap/)
- [USB HID Usage Tables 1.4 — USB-IF](https://usb.org/sites/default/files/hut1_4.pdf)
