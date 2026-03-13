---
phase: 02-core-app
plan: 01
subsystem: infra
tags: [csharp, dotnet, xunit, hidsharp, winforms, winexe]

# Dependency graph
requires:
  - phase: 01-protocol-discovery
    provides: Confirmed VID=0x3434, PID=0xD030, battery byte offset 5, report structure
provides:
  - BatterMouse/BatterMouse.csproj with TFM net8.0-windows10.0.19041.0, HidSharp 2.6.4, Microsoft.Toolkit.Uwp.Notifications 7.1.3
  - BatterMouse/Program.cs with single-instance Mutex guard and Application.Run(AppContext)
  - BatterMouse/Resources/tray.ico placeholder
  - BatterMouse.Tests/BatterMouse.Tests.csproj targeting same TFM with ProjectReference to main
  - HidReaderTests.cs RED stubs defining ParseBattery contract (offset 5, boundary, null cases)
  - BatteryMonitorTests.cs RED stubs defining threshold logic contract (20%, no-refire, recovery)
  - StartupManagerTests.cs RED stubs defining registry startup contract (write/delete/check)
affects: [02-02, 02-03, 02-04]

# Tech tracking
tech-stack:
  added:
    - HidSharp 2.6.4 (HID device I/O)
    - Microsoft.Toolkit.Uwp.Notifications 7.1.3 (Windows toast notifications)
    - xunit 2.9.3 (test framework)
    - Microsoft.NET.Test.Sdk 17.14.1
    - xunit.runner.visualstudio 3.1.4
    - coverlet.collector 6.0.4
  patterns:
    - WinExe formless app with ApplicationContext (no Form1)
    - Single-instance Mutex guard in Program.Main
    - TFM net8.0-windows10.0.19041.0 for Windows API access
    - PublishSingleFile + SelfContained + win-x64 for Phase 3 publish
    - RED stub tests referencing not-yet-existing classes (Wave 1 contract definition)

key-files:
  created:
    - BatterMouse/BatterMouse.csproj
    - BatterMouse/Program.cs
    - BatterMouse/Resources/tray.ico
    - BatterMouse.Tests/BatterMouse.Tests.csproj
    - BatterMouse.Tests/HidReaderTests.cs
    - BatterMouse.Tests/BatteryMonitorTests.cs
    - BatterMouse.Tests/StartupManagerTests.cs
  modified: []

key-decisions:
  - "Used dotnet new winforms then overwrote csproj — template does not accept windows-versioned TFM flag"
  - "Added UseWindowsForms=true to test project to allow referencing the WinForms main project"
  - "Removed auto-generated Form1.cs and Form1.Designer.cs — app is formless (tray-only)"

patterns-established:
  - "RED stubs: test files compile only after Wave 2 plans create their target classes"
  - "Wave structure: Wave 1 scaffolds contracts; Wave 2 implements them in parallel"

requirements-completed: [HID-01, TRAY-01, NOTF-01, STARTUP-01, BG-01]

# Metrics
duration: 2min
completed: 2026-03-13
---

# Phase 2 Plan 01: Solution Scaffold Summary

**C# .NET 8 WinExe solution with HidSharp + toast notification packages and three RED xUnit test stubs defining HidReader, BatteryMonitor, and StartupManager contracts**

## Performance

- **Duration:** ~2 min
- **Started:** 2026-03-13T00:05:00Z
- **Completed:** 2026-03-13T00:07:27Z
- **Tasks:** 2
- **Files modified:** 7

## Accomplishments
- Main app project with correct TFM net8.0-windows10.0.19041.0, WinExe output, HidSharp 2.6.4, and Microsoft.Toolkit.Uwp.Notifications 7.1.3
- Program.cs with BatterMouse_SingleInstance Mutex guard — prevents duplicate instances
- xUnit test project referencing main project with matching TFM, ready for Wave 2 implementations
- Three RED stub test files establishing exact method contracts for HidReader, BatteryMonitor, and StartupManager

## Task Commits

Each task was committed atomically:

1. **Task 1: Create C# solution and main app project** - `e181715` (feat)
2. **Task 2: Create xUnit test project with failing stubs** - `60404f9` (feat)

**Plan metadata:** (docs commit — pending)

## Files Created/Modified
- `BatterMouse/BatterMouse.csproj` - Main WinExe project, TFM net8.0-windows10.0.19041.0, HidSharp + notifications packages, Phase 3 publish flags
- `BatterMouse/Program.cs` - Entry point, single-instance Mutex, Application.Run(AppContext)
- `BatterMouse/Resources/tray.ico` - Placeholder ICO (Phase 3 polish concern)
- `BatterMouse.Tests/BatterMouse.Tests.csproj` - xUnit project targeting same TFM, ProjectReference to main
- `BatterMouse.Tests/HidReaderTests.cs` - RED stubs: ParseBattery(offset 5, boundary, null/short)
- `BatterMouse.Tests/BatteryMonitorTests.cs` - RED stubs: threshold=20, no-refire, reset after recovery
- `BatterMouse.Tests/StartupManagerTests.cs` - RED stubs: HKCU registry write/delete/check with teardown

## Decisions Made
- Used `dotnet new winforms` then overwrote csproj manually — `--framework` flag does not accept the windows-versioned TFM suffix
- Added `<UseWindowsForms>true</UseWindowsForms>` to test project csproj to allow building against the WinForms-dependent main project
- Deleted auto-generated Form1.cs/Form1.Designer.cs — app is formless (tray-only, no main window)

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Added UseWindowsForms to test project csproj**
- **Found during:** Task 2 (xUnit test project setup)
- **Issue:** `dotnet add reference` failed with incompatible TFM — scaffolded test project used net10.0, and after fixing TFM the WinForms dependency still required UseWindowsForms to allow a valid ProjectReference
- **Fix:** Set TFM to net8.0-windows10.0.19041.0 and added UseWindowsForms=true in BatterMouse.Tests.csproj; added ProjectReference directly in csproj instead of via CLI
- **Files modified:** BatterMouse.Tests/BatterMouse.Tests.csproj
- **Verification:** `dotnet restore BatterMouse.Tests/BatterMouse.Tests.csproj` exits 0
- **Committed in:** 60404f9 (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (1 blocking)
**Impact on plan:** Required fix — without UseWindowsForms the ProjectReference would fail. No scope creep.

## Issues Encountered
- `dotnet new xunit` scaffolded with net10.0 TFM by default (newer SDK installed). Fixed by rewriting csproj before adding the project reference.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- Both projects restore cleanly — Wave 2 plans (02-02, 02-03) can proceed in parallel
- Test stubs define exact contracts: HidReader.ParseBattery, BatteryMonitor.OnBatteryLevel, StartupManager.SetStartup/IsStartupEnabled
- Program.cs references AppContext which does not exist yet (created in 02-03) — build will fail until 02-03 completes (expected)
- tray.ico is a placeholder binary (notepad.exe copy) — replace with real icon in Phase 3

## Self-Check: PASSED

All 7 files confirmed on disk. Both commits (e181715, 60404f9) confirmed in git log.

---
*Phase: 02-core-app*
*Completed: 2026-03-13*
