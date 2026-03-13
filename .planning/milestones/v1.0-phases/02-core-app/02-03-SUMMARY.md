---
phase: 02-core-app
plan: 03
subsystem: ui
tags: [winforms, registry, hkcu, tray-icon, applicationcontext, startup]

# Dependency graph
requires:
  - phase: 02-01
    provides: "RED stubs for StartupManagerTests; AppContext stub; solution scaffold"
  - phase: 02-02
    provides: "HidReader, BatteryMonitor, ToastHelper"
provides:
  - "StartupManager: HKCU registry auto-start (SetStartup, IsStartupEnabled)"
  - "AppContext: ApplicationContext subclass wiring all components; tray icon with Exit menu"
affects: [02-04, program-entry]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "ApplicationContext subclass pattern for formless tray-only WinForms app"
    - "HKCU Run registry for zero-elevation auto-start with quoted paths"
    - "IContainer-owned NotifyIcon for proper disposal on exit (prevents ghost icons)"
    - "ExitThreadCore override: Stop HID reader, hide tray icon, cleanup toasts, then call base"

key-files:
  created:
    - BatterMouse/StartupManager.cs
  modified:
    - BatterMouse/AppContext.cs

key-decisions:
  - "SystemIcons.Application used as placeholder tray icon — avoids file dependency on tray.ico during compilation; TODO Phase 3 replace"
  - "StartupManager.SetStartup(true) called in AppContext constructor (idempotent, runs on every launch)"
  - "AppContext sealed to prevent subclassing; uses IContainer pattern for reliable NotifyIcon disposal"

patterns-established:
  - "Tray app lifecycle: constructor wires all components; ExitThreadCore tears them down in order"
  - "StartupManager is purely static — no instance state, no DI needed"
  - "Ghost icon prevention: _trayIcon.Visible = false before base.ExitThreadCore()"

requirements-completed: [TRAY-01, STARTUP-01, BG-01]

# Metrics
duration: 2min
completed: 2026-03-13
---

# Phase 2 Plan 03: StartupManager and AppContext Summary

**HKCU registry auto-start (StartupManager) and ApplicationContext subclass (AppContext) wiring HidReader + BatteryMonitor + ToastHelper into a formless tray app**

## Performance

- **Duration:** 2 min
- **Started:** 2026-03-13T00:09:46Z
- **Completed:** 2026-03-13T00:12:07Z
- **Tasks:** 2 (StartupManager + AppContext)
- **Files modified:** 2

## Accomplishments

- StartupManager: HKCU registry read/write for auto-start; quoted paths guard against spaces in paths; all 4 unit tests passing
- AppContext: full ApplicationContext subclass replacing stub; wires HidReader, BatteryMonitor, ToastHelper; tray icon with Exit menu; clean exit teardown
- No Form constructed or shown; no ShowBalloonTip usage anywhere

## Task Commits

Each task was committed atomically:

1. **Task 1 + 2: StartupManager and AppContext implementation** - `d062a4d` (feat)

**Plan metadata:** (docs commit — see below)

## Files Created/Modified

- `BatterMouse/StartupManager.cs` — HKCU registry auto-start; SetStartup(bool), IsStartupEnabled()
- `BatterMouse/AppContext.cs` — Full ApplicationContext subclass; replaces stub; wires all components

## Decisions Made

- Used `SystemIcons.Application` as placeholder tray icon — avoids a file dependency on `tray.ico` at compile time; Phase 3 can switch to a real branded icon
- `StartupManager.SetStartup(true)` called in AppContext constructor (idempotent by registry semantics — re-writing the same value is harmless)
- `AppContext` marked `sealed` — no subclassing needed; `IContainer` owns both `NotifyIcon` and `ContextMenuStrip` for reliable disposal

## Deviations from Plan

None — plan executed exactly as written. All source files required by AppContext (HidReader, BatteryMonitor, ToastHelper) were already present from plan 02-02, which was executed before this plan.

## Issues Encountered

None. Build succeeded with 0 warnings on first attempt. All 13 unit tests (StartupManagerTests x4, HidReaderTests x4, BatteryMonitorTests x5) passed immediately.

## User Setup Required

None — no external service configuration required.

## Next Phase Readiness

- StartupManager and AppContext complete; plan 02-04 (entry point / Program.cs wire-up) can proceed
- All three components (HidReader, BatteryMonitor, ToastHelper) are wired and tested
- tray.ico placeholder (SystemIcons.Application) is in place; Phase 3 to replace with branded icon
- No blockers

---
*Phase: 02-core-app*
*Completed: 2026-03-13*

## Self-Check: PASSED

All 3 files confirmed on disk: StartupManager.cs, AppContext.cs, 02-03-SUMMARY.md.
Commit d062a4d confirmed in git log.
