# Phase 2: Core App - Research

**Researched:** 2026-03-13
**Domain:** C# .NET 8 / WinForms / HidSharp / Windows notifications / startup
**Confidence:** HIGH (all critical claims verified against official docs or HidSharp source)

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| HID-01 | App reads Keychron mouse battery level from HID device | HidSharp 2.6.4 blocking-read pattern; VID/PID/usage_page confirmed in Phase 1 |
| TRAY-01 | System tray icon shows app is running | WinForms ApplicationContext + NotifyIcon pattern; no form needed |
| NOTF-01 | Windows toast notification fires when battery drops to or below 20% | Microsoft.Toolkit.Uwp.Notifications 7.x via ToastContentBuilder; ShowBalloonTip is wrong for Windows 11 |
| STARTUP-01 | App launches automatically on Windows startup | HKCU Run registry key; no elevation needed |
| BG-01 | App runs silently in the background (no main window required) | ApplicationContext + OutputType=WinExe; no Form.Show() needed |
</phase_requirements>

---

## Summary

Phase 2 builds a complete C# .NET 8 WinForms tray application. The hard unknowns from Phase 1 are resolved: VID=0x3434, PID=0xD030, battery at byte offset 5. All five requirements map cleanly to well-established Windows/.NET patterns with no novel engineering required.

The three non-obvious choices are: (1) use `ToastContentBuilder` (not `ShowBalloonTip`) because Windows 11 does not persist balloon tips in Action Center; (2) use a `CustomApplicationContext` inheriting `ApplicationContext` (not `Application.Run(new Form())`) to achieve a formless tray-only process; (3) use HKCU Run registry (not Task Scheduler) for zero-elevation auto-start.

HidSharp 2.6.4 is current (released October 2025). Its `GetHidDevices()` does not filter by usage page natively — filter by VID+PID then select the first device that responds to a blocking read (matching Phase 1 strategy of "try each, skip ghost entries").

**Primary recommendation:** Single .csproj, `OutputType=WinExe`, TFM `net8.0-windows10.0.19041.0`, three NuGet packages: `HidSharp`, `Microsoft.Toolkit.Uwp.Notifications`, and nothing else.

---

## Standard Stack

### Core
| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| HidSharp | 2.6.4 | HID device enumeration and blocking reads | Cross-platform, actively maintained (Oct 2025), decided in Phase 1 |
| Microsoft.Toolkit.Uwp.Notifications | 7.1.3 | Windows toast notifications via ToastContentBuilder | Official MS recommendation for unpackaged .NET desktop apps |
| .NET 8 WinForms | built-in (net8.0-windows10.0.19041.0) | ApplicationContext, NotifyIcon, ContextMenuStrip | Decided in Phase 1; TFM matches Windows 10 2004+ requirement |

### Supporting
| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| Microsoft.Win32 (built-in) | n/a | Registry access for HKCU startup key | Always — no third-party package needed |
| System.Threading (built-in) | n/a | Mutex for single instance; Thread for HID reader | Always |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| ToastContentBuilder | NotifyIcon.ShowBalloonTip | BalloonTip does NOT appear in Windows 11 Action Center — confirmed broken |
| HKCU Run registry | Task Scheduler | Scheduler requires more code; no benefit for non-elevated app |
| ApplicationContext subclass | Invisible Form | Form approach is fragile; ApplicationContext is the canonical pattern |

**Installation:**
```bash
dotnet add package HidSharp
dotnet add package Microsoft.Toolkit.Uwp.Notifications --version 7.1.3
```

---

## Architecture Patterns

### Recommended Project Structure
```
BatterMouse/
├── BatterMouse.csproj         # single project, OutputType=WinExe
├── Program.cs                 # Mutex check, Application.Run(new AppContext())
├── AppContext.cs              # CustomApplicationContext: NotifyIcon, tray menu, lifecycle
├── HidReader.cs               # Background thread: open device, blocking read loop, raise event
├── BatteryMonitor.cs          # Threshold logic: tracks last notified level, raises LowBattery event
├── StartupManager.cs          # HKCU registry read/write for auto-start
├── ToastHelper.cs             # ToastContentBuilder wrapper, single notification call
└── Resources/
    └── tray.ico               # 16x16 / 32x32 multi-size ICO
```

### Pattern 1: Formless Tray App with ApplicationContext
**What:** Subclass `ApplicationContext`, pass it to `Application.Run()` instead of a Form. The message pump stays alive until `ExitThread()` is called.
**When to use:** Any tray-only app — no main window needed.
**Example:**
```csharp
// Source: https://www.red-gate.com/simple-talk/development/dotnet-development/creating-tray-applications-in-net-a-practical-guide/
// Program.cs
static void Main()
{
    bool createdNew;
    using var mutex = new Mutex(true, "BatterMouse_SingleInstance", out createdNew);
    if (!createdNew) return;          // second instance → exit silently

    Application.EnableVisualStyles();
    Application.SetCompatibleTextRenderingDefault(false);
    Application.Run(new AppContext());
}

// AppContext.cs
internal sealed class AppContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private readonly IContainer _components = new Container();

    public AppContext()
    {
        _trayIcon = new NotifyIcon(_components)
        {
            Icon = new Icon("Resources/tray.ico"),
            Text = "BatterMouse",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        // start HID reader, wire events...
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip(_components);
        menu.Items.Add("Exit", null, (_, _) => ExitThread());
        return menu;
    }

    protected override void ExitThreadCore()
    {
        _trayIcon.Visible = false;
        base.ExitThreadCore();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _components.Dispose();
        base.Dispose(disposing);
    }
}
```

### Pattern 2: HID Blocking Read Thread
**What:** Persistent `Thread` with `IsBackground = true`. Opens the device (retrying on failure), reads in an infinite loop, raises an event on each valid battery report. Catches `IOException` on disconnect, waits, then re-enumerates.
**When to use:** Matches Phase 1 confirmed strategy: reports are infrequent, no polling needed.
**Example:**
```csharp
// Source: HidSharp docs + Phase 1 FINDINGS.md
// HidReader.cs (sketch — planner fills in details)
public event Action<int>? BatteryLevelReceived;

private void ReadLoop(CancellationToken ct)
{
    while (!ct.IsCancellationRequested)
    {
        HidDevice? device = FindBatteryDevice();
        if (device == null) { Thread.Sleep(5000); continue; }

        if (!device.TryOpen(out HidStream stream)) { Thread.Sleep(2000); continue; }
        using (stream)
        {
            stream.ReadTimeout = Timeout.Infinite;   // blocking, no timeout
            var buf = new byte[device.GetMaxInputReportLength()];
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    int count = stream.Read(buf, 0, buf.Length);
                    if (count > BatteryByteOffset)
                        BatteryLevelReceived?.Invoke(buf[BatteryByteOffset]);
                }
            }
            catch (IOException) { /* device disconnected — outer loop re-opens */ }
        }
    }
}

private static HidDevice? FindBatteryDevice()
{
    // GetHidDevices does NOT filter by UsagePage; filter by VID+PID, try first responding
    var candidates = DeviceList.Local
        .GetHidDevices(vendorID: 0x3434, productID: 0xD030)
        .ToList();
    // Also enumerate PID 0xD037 (wired/charging) — see FINDINGS.md note 1
    candidates.AddRange(DeviceList.Local.GetHidDevices(vendorID: 0x3434, productID: 0xD037));
    return candidates.FirstOrDefault();   // caller tries TryOpen on each if needed
}
```

**Critical:** `GetHidDevices()` filters by VID/PID only; usage_page is not a native filter parameter. The battery TLC is one of ~16 TLCs for the Keychron Link dongle. Multiple TLCs share the same VID/PID. Select by trying to open and read — ghost entries (like `9&248e17d0` from Phase 1) will never produce data. Use `TryOpen()` and check for responses rather than relying on usage page matching.

**Alternative approach** (more precise): call `device.GetReportDescriptor()` and check `.DeviceItems[0].Usages` for usage_page=0x008C before opening. This avoids attempting to open ghost devices. Either approach works; the simpler `TryOpen` approach matches Phase 1.

### Pattern 3: Toast Notification (de-duplicated)
**What:** Fire once when level drops to/below threshold; do not re-fire until level recovers above threshold.
**When to use:** NOTF-01 requirement.
**Example:**
```csharp
// Source: https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/send-local-toast
// ToastHelper.cs
public static void ShowLowBattery(int level)
{
    new ToastContentBuilder()
        .AddText("BatterMouse — Low Battery")
        .AddText($"Mouse battery is at {level}%. Plug in to charge.")
        .Show();
}

// BatteryMonitor.cs — de-duplication logic
private bool _notified = false;
private const int Threshold = 20;

public void OnBatteryLevel(int level)
{
    if (level <= Threshold && !_notified)
    {
        _notified = true;
        ToastHelper.ShowLowBattery(level);
    }
    else if (level > Threshold)
    {
        _notified = false;   // reset so next drop fires again
    }
}
```

### Pattern 4: Auto-Start via HKCU Registry
**What:** Write/delete a string value under `HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run`. No elevation required.
**When to use:** STARTUP-01.
**Example:**
```csharp
// Source: https://learn.microsoft.com/en-us/answers/questions/1363124/add-application-to-windows-start-up-registry-for-c
private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
private const string AppName = "BatterMouse";

public static void SetStartup(bool enable)
{
    using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
    if (key == null) return;
    if (enable)
    {
        string path = Process.GetCurrentProcess().MainModule!.FileName;
        key.SetValue(AppName, $"\"{path}\"", RegistryValueKind.String);  // quote for spaces in path
    }
    else
    {
        key.DeleteValue(AppName, throwOnMissingValue: false);
    }
}
```

### Anti-Patterns to Avoid
- **`Application.Run(new Form1())`:** Creates a visible window. Use `Application.Run(new AppContext())` with a custom `ApplicationContext` subclass instead.
- **`NotifyIcon.ShowBalloonTip()`:** On Windows 11 the balloon message disappears from Action Center when the timeout expires — confirmed broken. Use `ToastContentBuilder.Show()`.
- **Polling on a timer:** Do not replace the blocking read thread with a `System.Windows.Forms.Timer`. Reports are infrequent wirelessly; blocking reads handle this correctly and require no polling.
- **`HidDeviceLoader` (old API):** The old `HidDeviceLoader.GetDeviceOrDefault()` API is deprecated. Use `DeviceList.Local.GetHidDevices()`.
- **Forgetting to set `stream.ReadTimeout = Timeout.Infinite`:** Default timeout causes read failures when the device is idle. Always set explicitly for the battery TLC.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| HID enumeration and reads | Custom P/Invoke to hid.dll / SetupAPI | HidSharp 2.6.4 | Driver compatibility, buffer alignment, report ID stripping already handled |
| Toast XML construction | Build XML strings manually | `ToastContentBuilder` | XML schema is complex; builder validates structure |
| Notification history | Custom database of sent notifications | Tag+Group on ToastNotification | Use `toast.Tag = "lowbattery"` so duplicates replace rather than stack |
| Message pump | `while(true)` loop | `Application.Run(new AppContext())` | WinForms pump handles Windows messages, tray icon events, cross-thread marshal |

**Key insight:** The complexity in each of these domains (HID driver interfaces, WinRT notification XML, Win32 message pumps) is real but entirely solved. The value of this app is the integration, not any of these individual pieces.

---

## Common Pitfalls

### Pitfall 1: Multiple TLCs sharing the same VID/PID
**What goes wrong:** `GetHidDevices(0x3434, 0xD030)` returns ~16 devices (all TLCs of the Keychron Link dongle). Opening the wrong one gives no data, not an error.
**Why it happens:** The Keychron Link is a multi-TLC device; all share the same VID/PID, differentiated only by usage page and interface number.
**How to avoid:** Try `TryOpen()` on each candidate; the ghost TLC (`9&248e17d0` from Phase 1) never sends input reports. Alternatively, parse the report descriptor and check for usage_page=0x008C before opening.
**Warning signs:** `TryOpen()` returns true but `stream.Read()` blocks indefinitely on a device that never fires.

### Pitfall 2: Battery reports are infrequent (>20 seconds wirelessly)
**What goes wrong:** App appears to "not read battery" for 1–2 minutes after startup. Testers conclude the code is broken.
**Why it happens:** The battery TLC only emits reports on state change or on a long timer (confirmed Phase 1: >20s).
**How to avoid:** Document the expected latency. Use a USB cable for testing to trigger immediate reports. Do NOT add a timeout to the read loop.
**Warning signs:** Test passes with USB cable, fails in wireless-only testing.

### Pitfall 3: NotifyIcon not disposed on exit
**What goes wrong:** Tray icon ghost remains in the taskbar after the process exits (does not clear until mouse hover).
**Why it happens:** Known WinForms behavior — the icon only clears when the OS refreshes the tray or when `Visible = false` is set.
**How to avoid:** Set `_trayIcon.Visible = false` before calling `base.ExitThreadCore()`. Dispose the `IContainer` that holds the icon.
**Warning signs:** Icon persists in tray after `taskkill`.

### Pitfall 4: ShowBalloonTip not visible in Windows 11 Action Center
**What goes wrong:** `notifyIcon.ShowBalloonTip(3000, "Title", "Message", ToolTipIcon.Warning)` — the popup appears momentarily but leaves no trace in the Action Center.
**Why it happens:** Windows 11 changed balloon tip persistence behavior (confirmed Microsoft Q&A 2023).
**How to avoid:** Use `ToastContentBuilder.Show()` exclusively. Do not use `ShowBalloonTip`.

### Pitfall 5: TFM must be windows-versioned for toast notifications
**What goes wrong:** `Show()` method appears to be missing on `ToastContentBuilder`; or `ToastNotificationManager` is not found.
**Why it happens:** `Microsoft.Toolkit.Uwp.Notifications` requires TFM `net6.0-windows10.0.17763.0` or later; a plain `net8.0-windows` TFM is insufficient.
**How to avoid:** Set `<TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>` (already decided in Phase 1).

### Pitfall 6: Single-instance mutex ownership
**What goes wrong:** Two instances run simultaneously; each fires its own notifications and fights for the HID device.
**Why it happens:** Startup entries run on login; user may also double-click the exe.
**How to avoid:** Check `Mutex(true, "BatterMouse_SingleInstance", out bool createdNew)` at top of `Main()`; return if `createdNew == false`.

### Pitfall 7: Unquoted path in registry startup value
**What goes wrong:** App fails to start from registry if installed in a path with spaces (e.g., `C:\Program Files\...`).
**Why it happens:** Registry Run value is passed directly to `CreateProcess`; spaces in path split the command.
**How to avoid:** Always write `"\"" + path + "\""` as the registry value.

---

## Code Examples

Verified patterns from official sources:

### Minimal csproj
```xml
<!-- net8.0-windows10.0.19041.0 enables both WinForms and Windows toast APIs -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>WinExe</OutputType>
    <TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
    <UseWindowsForms>true</UseWindowsForms>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <ApplicationIcon>Resources\tray.ico</ApplicationIcon>
    <!-- Phase 3 publish flags — include here so dotnet publish just works -->
    <PublishSingleFile>true</PublishSingleFile>
    <SelfContained>true</SelfContained>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="HidSharp" Version="2.6.4" />
    <PackageReference Include="Microsoft.Toolkit.Uwp.Notifications" Version="7.1.3" />
  </ItemGroup>
</Project>
```

### ToastContentBuilder minimal call
```csharp
// Source: https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/send-local-toast
// Requires: net8.0-windows10.0.17763.0 or later TFM; Microsoft.Toolkit.Uwp.Notifications >= 7.0
new ToastContentBuilder()
    .AddText("BatterMouse — Low Battery")
    .AddText($"Mouse battery is at {level}%. Connect USB cable to charge.")
    .Show();
```

### ToastNotificationManagerCompat cleanup (unpackaged app)
```csharp
// Call on app exit if portable (no installer)
// Source: https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/send-local-toast
ToastNotificationManagerCompat.Uninstall();
```

### HidSharp device enumeration and open
```csharp
// Source: HidSharp docs + Phase 1 FINDINGS.md
// GetHidDevices filters VID+PID; usage_page must be checked manually if needed
var devices = DeviceList.Local
    .GetHidDevices(vendorID: 0x3434, productID: 0xD030)
    .ToList();

foreach (var device in devices)
{
    if (device.TryOpen(out HidStream stream))
    {
        using (stream)
        {
            stream.ReadTimeout = Timeout.Infinite;
            // blocking read loop here
        }
    }
}
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `HidDeviceLoader.GetDeviceOrDefault()` | `DeviceList.Local.GetHidDevices()` | HidSharp 2.0 | Old API deprecated; use DeviceList |
| `ShowBalloonTip()` | `ToastContentBuilder.Show()` | Windows 11 (2021) | Balloon tips don't persist in Win11 Action Center |
| `Application.Run(new Form())` for tray apps | `Application.Run(new CustomApplicationContext())` | .NET Framework era, still correct | Canonical formless pattern |
| Manual toast XML | `ToastContentBuilder` fluent API | toolkit 7.0 (2021) | Builder validates; raw XML is error-prone |

**Deprecated/outdated:**
- `HidDeviceLoader`: superseded by `DeviceList.Local` in HidSharp 2.x
- `NotifyIcon.ShowBalloonTip`: works on Windows 10 but broken for Action Center persistence on Windows 11

---

## Open Questions

1. **Which TLC index is the active battery device?**
   - What we know: Phase 1 identified path `9&3b58df5` as active; `9&248e17d0` is a ghost. However, device paths are not stable across reboots.
   - What's unclear: Whether the first result from `GetHidDevices(0x3434, 0xD030)` is always the active TLC, or if we need to try all and skip non-responding ones.
   - Recommendation: `TryOpen()` each candidate; track the first one that delivers a report within a timeout. Use `DeviceList.Changed` to re-enumerate on device connect/disconnect events.

2. **PID 0xD037 (wired/charging) behavior**
   - What we know: FINDINGS.md notes both PIDs should be monitored so percentage is accurate while charging. PID 0xD037 was not empirically confirmed.
   - What's unclear: Whether 0xD037 uses the same byte offset (5) and report structure.
   - Recommendation: Implement PID 0xD030 first. Add 0xD037 enumeration as a parallel candidate; if it produces reports with offset 5 in the valid range, accept them. Flag in code as "unverified protocol".

3. **Toast AUMID registration for unpackaged apps**
   - What we know: `Microsoft.Toolkit.Uwp.Notifications` 7.x handles AUMID registration automatically for unpackaged apps on Windows 10 1903+ (build 18362+). Our TFM pins to 19041.
   - What's unclear: Whether any manual COM registration step is needed.
   - Recommendation: Call `ToastContentBuilder().Show()` in the first dev run. If notifications don't appear, check Windows Settings > Notifications; the app may need to be run once to register. No manual COM registration needed per official docs for unpackaged desktop apps.

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.x (recommended) — not yet installed |
| Config file | None yet — Wave 0 gap |
| Quick run command | `dotnet test --filter Category=Unit` |
| Full suite command | `dotnet test` |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| HID-01 | `ParseBattery(report)` returns correct level at offset 5 | unit | `dotnet test --filter "FullyQualifiedName~HidReaderTests"` | ❌ Wave 0 |
| HID-01 | Returns null for reports shorter than 6 bytes | unit | `dotnet test --filter "FullyQualifiedName~HidReaderTests"` | ❌ Wave 0 |
| TRAY-01 | NotifyIcon is Visible=true after AppContext constructed | manual/smoke | Run app, inspect tray | — |
| NOTF-01 | BatteryMonitor fires notification at exactly level=20 | unit | `dotnet test --filter "FullyQualifiedName~BatteryMonitorTests"` | ❌ Wave 0 |
| NOTF-01 | Does NOT re-fire while still below threshold | unit | `dotnet test --filter "FullyQualifiedName~BatteryMonitorTests"` | ❌ Wave 0 |
| NOTF-01 | Re-fires after recovery above threshold | unit | `dotnet test --filter "FullyQualifiedName~BatteryMonitorTests"` | ❌ Wave 0 |
| STARTUP-01 | Registry key written/deleted correctly | unit | `dotnet test --filter "FullyQualifiedName~StartupManagerTests"` | ❌ Wave 0 |
| BG-01 | No Form is shown on startup | manual/smoke | Run app, confirm no window appears | — |

### Sampling Rate
- **Per task commit:** `dotnet test --filter Category=Unit`
- **Per wave merge:** `dotnet test`
- **Phase gate:** Full suite green before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `BatterMouse.Tests/BatterMouse.Tests.csproj` — new test project, xUnit
- [ ] `BatterMouse.Tests/HidReaderTests.cs` — covers HID-01 (unit tests for ParseBattery, boundary cases)
- [ ] `BatterMouse.Tests/BatteryMonitorTests.cs` — covers NOTF-01 (threshold, de-duplication, reset)
- [ ] `BatterMouse.Tests/StartupManagerTests.cs` — covers STARTUP-01 (registry write/delete/check)
- [ ] Framework install: `dotnet new xunit -n BatterMouse.Tests` + add project reference

Note: TRAY-01 and BG-01 require manual smoke testing (process must be running; no practical way to automate tray icon assertion without UI automation framework out of scope for v1).

---

## Sources

### Primary (HIGH confidence)
- [HidSharp NuGet 2.6.4](https://www.nuget.org/packages/HidSharp) — version, release date
- [HidSharp DeviceList.GetHidDevices documentation](https://docs.zer7.com/hidsharp/html/977bef7f-2064-25ec-5101-4c49063d26b0.htm) — confirmed no UsagePage filter parameter
- [Microsoft Learn: Send a local app notification from a C# app](https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/send-local-toast) — ToastContentBuilder, TFM requirement, unpackaged app pattern
- [Microsoft Learn: HKCU registry startup](https://learn.microsoft.com/en-us/answers/questions/1363124/add-application-to-windows-start-up-registry-for-c) — C# registry pattern with quoted paths
- Phase 1 FINDINGS.md + 01-02-SUMMARY.md — all HID protocol facts

### Secondary (MEDIUM confidence)
- [Red Gate Simple Talk: Tray Applications in .NET](https://www.red-gate.com/simple-talk/development/dotnet-development/creating-tray-applications-in-net-a-practical-guide/) — ApplicationContext pattern, Mutex single-instance, NotifyIcon disposal
- [Microsoft Q&A: ShowBalloonTip in Windows 11](https://learn.microsoft.com/en-us/answers/questions/1362368/how-to-show-balloon-tip-message-in-windows-notific) — confirmed BalloonTip does not persist in Win11 Action Center
- [Arendi DeviceList documentation](https://code.arendi.ch/Arendi.DotNETLibrary/7.2.0/Documentation/api/HidSharp.DeviceList.html) — GetHidDevices overload signatures confirmed

### Tertiary (LOW confidence)
- WebSearch results on PublishSingleFile + IncludeNativeLibrariesForSelfExtract for Phase 3 publish — needs empirical validation during Phase 3

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — HidSharp 2.6.4 verified on NuGet; toolkit 7.1.3 is current stable; all decided in Phase 1
- Architecture: HIGH — ApplicationContext pattern is canonical WinForms; verified against official Microsoft docs and Red Gate guide
- HID filtering: MEDIUM — GetHidDevices VID/PID filter confirmed from docs; UsagePage NOT a native filter (confirmed). LINQ+TryOpen workaround is logical but not found as verbatim HidSharp sample
- Pitfalls: HIGH — BalloonTip/Win11 confirmed via Microsoft Q&A; NotifyIcon disposal is a known dotnet/winforms bug; others derived from Phase 1 empirical data

**Research date:** 2026-03-13
**Valid until:** 2026-04-13 (HidSharp and toolkit are stable; Windows behavior is unlikely to change)
