---
phase: 02-core-app
verified: 2026-03-13T00:00:00Z
status: human_needed
score: 10/10 automated must-haves verified
re_verification: false
human_verification:
  - test: "Launch app with `dotnet run --project BatterMouse/BatterMouse.csproj` and confirm tray icon appears, no window opens, and right-click shows Exit menu item"
    expected: "Tray icon visible in system notification area; zero application windows; context menu with single Exit item"
    why_human: "WinForms tray icon visibility and formless process require a running GUI environment to observe"
  - test: "Run the app and open PowerShell; execute: Get-ItemProperty 'HKCU:\\SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run' -Name BatterMouse"
    expected: "Returns a value containing a quoted path to the BatterMouse executable"
    why_human: "Registry state at runtime depends on process identity path — cannot verify live value from grep alone"
  - test: "Click Exit in the tray context menu and confirm the tray icon disappears immediately without leaving a ghost icon"
    expected: "Icon gone immediately after click; `tasklist | findstr BatterMouse` returns nothing"
    why_human: "Ghost tray icon behaviour requires visual inspection and live process table check"
  - test: "Connect the Keychron mouse dongle and observe the app does not crash over a 2-minute window"
    expected: "App remains in tray, no exception dialog, Debug output shows battery reads"
    why_human: "HID read loop depends on physical hardware; cannot verify device enumeration and read success programmatically"
  - test: "Reboot Windows and confirm BatterMouse is running in the tray without a manual launch"
    expected: "Tray icon present after login, no manual run needed"
    why_human: "Windows startup registration can only be confirmed after an actual reboot"
---

# Phase 2: Core App Verification Report

**Phase Goal:** Users have a running BatterMouse app that monitors battery, sits in the tray, and fires a toast when battery hits 20%
**Verified:** 2026-03-13
**Status:** human_needed — all automated checks pass; 5 items require live runtime/hardware confirmation
**Re-verification:** No — initial verification

---

## Goal Achievement

### Observable Truths (from ROADMAP.md Success Criteria)

| # | Truth | Status | Evidence |
|---|-------|--------|----------|
| 1 | App starts and a tray icon appears; no window opens | ? HUMAN | AppContext.cs: NotifyIcon created with Visible=true, no Form constructed; confirmed by 02-04 smoke test |
| 2 | With mouse connected, app reads battery level from HID device without error | ? HUMAN | HidReader.cs: blocking read loop with IOException recovery, CancellationToken stop; confirmed by 02-04 smoke test |
| 3 | When battery is at or below 20%, toast fires; does not re-fire every cycle | ✓ VERIFIED | 5/5 BatteryMonitorTests pass: threshold=20, no-refire guard, recovery reset all confirmed by unit tests |
| 4 | After rebooting Windows, app is running in tray without manual launch | ? HUMAN | StartupManager.cs writes HKCU Run key; 4/4 StartupManagerTests pass; live reboot cannot be verified programmatically |

**Score:** 10/10 automated must-haves verified (truths 1, 2, 4 confirmed by prior smoke test per 02-04-SUMMARY; truth 3 fully verified by passing unit tests)

---

## Required Artifacts

All artifacts from plans 02-01 through 02-04:

| Artifact | Provided By | Status | Details |
|----------|-------------|--------|---------|
| `BatterMouse/BatterMouse.csproj` | 02-01 | ✓ VERIFIED | TFM net8.0-windows10.0.19041.0, OutputType WinExe, UseWindowsForms true, HidSharp 2.6.4, Microsoft.Toolkit.Uwp.Notifications 7.1.3 — exact match to plan spec |
| `BatterMouse/Program.cs` | 02-01 | ✓ VERIFIED | Mutex "BatterMouse_SingleInstance" guard present; Application.Run(new AppContext()) present |
| `BatterMouse/Resources/tray.ico` | 02-01 | ✓ VERIFIED | File exists (replaced invalid placeholder with valid 16x16 1-bit ICO in 02-02) |
| `BatterMouse.Tests/BatterMouse.Tests.csproj` | 02-01 | ✓ VERIFIED | TFM net8.0-windows10.0.19041.0, UseWindowsForms true, ProjectReference to main project confirmed |
| `BatterMouse.Tests/HidReaderTests.cs` | 02-01 | ✓ VERIFIED | 4 test methods present; all pass (13/13 suite result) |
| `BatterMouse.Tests/BatteryMonitorTests.cs` | 02-01 | ✓ VERIFIED | 5 test methods present; all pass |
| `BatterMouse.Tests/StartupManagerTests.cs` | 02-01 | ✓ VERIFIED | 4 test methods with IDisposable teardown present; all pass |
| `BatterMouse/HidReader.cs` | 02-02 | ✓ VERIFIED | ParseBattery static, BatteryLevelReceived event, Start/Stop, IsBackground=true thread, CancellationToken, ReadTimeout=Infinite, IOException recovery, PID_WIRELESS+PID_WIRED enumeration |
| `BatterMouse/BatteryMonitor.cs` | 02-02 | ✓ VERIFIED | Threshold=20, injectable Action<int> callback, _notified de-duplication, reset on recovery |
| `BatterMouse/ToastHelper.cs` | 02-02 | ✓ VERIFIED | ShowLowBattery via ToastContentBuilder chain; Cleanup() via ToastNotificationManagerCompat.Uninstall(); no ShowBalloonTip |
| `BatterMouse/StartupManager.cs` | 02-03 | ✓ VERIFIED | SetStartup(bool) and IsStartupEnabled() present; HKCU Run key path; quoted path value; no elevation |
| `BatterMouse/AppContext.cs` | 02-03 | ✓ VERIFIED | ApplicationContext subclass; NotifyIcon with Visible=true; Exit context menu; HidReader + BatteryMonitor + StartupManager wired in constructor; ExitThreadCore teardown order correct; no Form |

---

## Key Link Verification

| From | To | Via | Status | Evidence |
|------|----|-----|--------|---------|
| `HidReader.cs` | `BatteryMonitor.cs` | `BatteryLevelReceived` event subscription | ✓ WIRED | `AppContext.cs:34` — `_hidReader.BatteryLevelReceived += _batteryMonitor.OnBatteryLevel` |
| `BatteryMonitor.cs` | `ToastHelper.cs` | `ToastHelper.ShowLowBattery` callback | ✓ WIRED | `AppContext.cs:30` — `new BatteryMonitor(ToastHelper.ShowLowBattery)` |
| `AppContext.cs` | `StartupManager.cs` | `StartupManager.SetStartup` call | ✓ WIRED | `AppContext.cs:27` — `StartupManager.SetStartup(true)` in constructor |
| `AppContext.cs` | `HidReader.cs` | `BatteryLevelReceived` event subscription | ✓ WIRED | `AppContext.cs:33-35` — instance created, event subscribed, Start() called |
| `HidReader.BatteryLevelReceived` | `BatteryMonitor.OnBatteryLevel` | delegate chain | ✓ WIRED | `AppContext.cs:34` — delegate directly assigned |
| `BatterMouse.Tests.csproj` | `BatterMouse.csproj` | ProjectReference | ✓ WIRED | `BatterMouse.Tests.csproj:24` — `<ProjectReference Include="..\BatterMouse\BatterMouse.csproj" />` |

---

## Requirements Coverage

Requirements declared across all phase plans: HID-01, TRAY-01, NOTF-01, STARTUP-01, BG-01
(Note: No separate REQUIREMENTS.md file exists in .planning/ — requirements are fully defined inline in ROADMAP.md)

| Requirement | Source Plan(s) | Description | Status | Evidence |
|-------------|----------------|-------------|--------|---------|
| HID-01 | 02-01, 02-02, 02-04 | App reads Keychron mouse battery level from HID device | ✓ SATISFIED | HidReader.cs: DeviceList.Local.GetHidDevices(VID=0x3434, PID=0xD030), ParseBattery(offset 5), blocking read loop; 4/4 HidReaderTests pass |
| TRAY-01 | 02-01, 02-03, 02-04 | System tray icon shows app is running | ✓ SATISFIED | AppContext.cs: NotifyIcon with Visible=true, ContextMenuStrip with Exit item; human smoke test confirmed (02-04-SUMMARY) |
| NOTF-01 | 02-01, 02-02, 02-04 | Windows toast notification fires when battery drops to or below 20% | ✓ SATISFIED | BatteryMonitor threshold=20 with de-duplication; ToastHelper.ShowLowBattery via ToastContentBuilder; 5/5 BatteryMonitorTests pass |
| STARTUP-01 | 02-01, 02-03, 02-04 | App launches automatically on Windows startup | ✓ SATISFIED | StartupManager writes quoted path to HKCU\SOFTWARE\Microsoft\Windows\CurrentVersion\Run; 4/4 StartupManagerTests pass; human smoke test confirmed (02-04-SUMMARY) |
| BG-01 | 02-01, 02-03, 02-04 | App runs silently in the background (no main window required) | ✓ SATISFIED | No Form constructed anywhere (grep confirmed); ApplicationContext pattern with no ShowDialog/Show calls; human smoke test confirmed (02-04-SUMMARY) |

No orphaned requirements — all 5 IDs (HID-01, TRAY-01, NOTF-01, STARTUP-01, BG-01) are claimed by plans and verified.

---

## Anti-Patterns Found

| File | Line | Pattern | Severity | Impact |
|------|------|---------|----------|--------|
| `BatterMouse/AppContext.cs` | 37-38 | `// TODO Phase 3: replace tray icon with branded tray.ico` | ℹ️ Info | App uses SystemIcons.Application as placeholder icon. Functional — app runs and shows a tray icon. Phase 3 polish item only; does not affect Phase 2 goal |
| `BatterMouse/ToastHelper.cs` | 7-8 | Comment mentions "ShowBalloonTip" in a prohibition note | ℹ️ Info | Comment only; no actual ShowBalloonTip call exists anywhere in source (verified by grep) |

No blockers. No warnings. The Phase 3 icon TODO is correctly scoped — SystemIcons.Application is a valid working icon, not a null or broken reference.

---

## Build and Test Results

**`dotnet build BatterMouse/BatterMouse.csproj`**: Succeeded — 0 errors, 0 warnings
**`dotnet test BatterMouse.Tests/BatterMouse.Tests.csproj --filter Category=Unit`**: 13/13 passed
- HidReaderTests: 4/4 (ParseBattery offset, boundary 20%, null-short, null-empty)
- BatteryMonitorTests: 5/5 (fires@20, fires@15, no-refire below, refire after recovery, no-fire above)
- StartupManagerTests: 4/4 (enable writes, disable removes, false-when-absent, true-when-present)

**ShowBalloonTip in source code**: None (comment-only reference in ToastHelper.cs)
**Form construction in source code**: None

---

## Human Verification Required

All five items below have implementation evidence in the codebase confirming the code path exists and is wired correctly. The human checks confirm live runtime behaviour on actual hardware that cannot be exercised programmatically in this environment.

### 1. Tray Icon Visible — No Window

**Test:** `dotnet run --project BatterMouse/BatterMouse.csproj`
**Expected:** Tray icon appears in Windows notification area (bottom-right taskbar); no application window opens
**Why human:** WinForms NotifyIcon visibility requires a running GUI message loop and a physical display

### 2. Registry Auto-Start Written

**Test:** After launching the app, run in PowerShell: `Get-ItemProperty "HKCU:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" -Name BatterMouse`
**Expected:** Returns a quoted path to the BatterMouse exe (e.g., `"C:\path\to\BatterMouse.exe"`)
**Why human:** Registry key value includes the runtime process exe path; only verifiable at runtime

### 3. Clean Exit — No Ghost Tray Icon

**Test:** Right-click tray icon, click Exit; then run `tasklist | findstr BatterMouse`
**Expected:** Icon disappears immediately; process not found in tasklist
**Why human:** Ghost icon detection requires visual observation; process-gone check needs a live terminal

### 4. HID Read Without Crash

**Test:** With Keychron mouse dongle connected, leave app running for 2+ minutes
**Expected:** App remains in tray; no exception dialog; battery reads visible in Debug output if debugger attached
**Why human:** Requires physical Keychron dongle (VID 0x3434 / PID 0xD030); cannot enumerate hardware programmatically in this context

### 5. Windows Startup Launch After Reboot

**Test:** Reboot the machine; log in; check if BatterMouse tray icon appears without manually launching it
**Expected:** Tray icon present without manual action
**Why human:** Startup registry confirmation requires an actual reboot cycle

---

## Gaps Summary

No gaps. All ten automated must-haves are verified. The five human-verification items above were previously confirmed by the user during the plan 02-04 smoke test (recorded in 02-04-SUMMARY.md — Human Smoke Test Results table shows all items Passed). They are listed here as human-verification items because they cannot be re-confirmed programmatically by this verifier, not because there is any evidence they are broken.

The phase goal — "Users have a running BatterMouse app that monitors battery, sits in the tray, and fires a toast when battery hits 20%" — is achieved by the implementation evidence in the codebase, confirmed by a passing 13-test suite and documented smoke test results.

---

_Verified: 2026-03-13_
_Verifier: Claude (gsd-verifier)_
