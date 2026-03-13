---
phase: 03-ship
plan: 02
subsystem: ui
tags: [winforms, tray-icon, context-menu, threading, embedded-resource, xunit, tdd]

# Dependency graph
requires:
  - phase: 03-01
    provides: tray.ico embedded as EmbeddedResource in BatterMouse assembly
  - phase: 02-core-app
    provides: AppContext stub, HidReader.BatteryLevelReceived, StartupManager, BatteryMonitor
provides:
  - Full AppContext with real embedded icon, 4-item context menu, and thread-safe HID tooltip/label updates
  - AppContextMenuTests — 5 unit tests for menu structure, battery label, startup toggle checked state
  - IconResourceTests — 2 unit tests confirming tray.ico embedded resource is present and non-empty
  - InternalsVisibleTo wiring (AssemblyInfo.cs) enabling test project to access internal AppContext.BuildMenuInternal
affects: [03-03-publish]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "InternalsVisibleTo + internal static helper to expose WinForms construction for unit tests"
    - "STA thread wrapper in tests to safely construct ContextMenuStrip without message loop"
    - "Dual UI-thread dispatch: BeginInvoke when handle created, SynchronizationContext.Post as fallback"
    - "Return container alongside menu from STA helper to prevent premature disposal"

key-files:
  created:
    - BatterMouse/AssemblyInfo.cs
    - BatterMouse.Tests/AppContextMenuTests.cs
    - BatterMouse.Tests/IconResourceTests.cs
  modified:
    - BatterMouse/AppContext.cs

key-decisions:
  - "Expose BuildMenuInternal as internal static method taking IContainer — lets tests build the menu without constructing AppContext (which needs HID hardware)"
  - "Return (ContextMenuStrip, Container) tuple from STA helper — container must outlive menu inspection to prevent premature disposal"
  - "CheckOnClick=false on startup toggle — state managed explicitly via ToggleStartup handler reading registry, not trusting WinForms auto-toggle"
  - "Capture SynchronizationContext at construction time as fallback for UI thread dispatch before ContextMenuStrip handle is created"

patterns-established:
  - "STA thread wrapper: Thread + SetApartmentState(STA) + Join for safe WinForms construction in xunit tests"
  - "BeginInvoke / SynchronizationContext dual-path for cross-thread WinForms updates"

requirements-completed:
  - "(all v1 requirements delivered as a polished, distributable artifact)"

# Metrics
duration: 4min
completed: 2026-03-13
---

# Phase 3 Plan 02: AppContext Polished Tray Experience Summary

**WinForms AppContext rewritten with real embedded tray.ico, 4-item context menu (battery label + auto-start toggle + exit), and thread-safe BatteryLevelReceived dispatch — 20 tests green**

## Performance

- **Duration:** ~4 min
- **Started:** 2026-03-13T11:53:33Z
- **Completed:** 2026-03-13T11:56:53Z
- **Tasks:** 2 (TDD: 1 RED commit + 1 GREEN commit)
- **Files modified:** 4

## Accomplishments
- Real tray icon loaded via `Assembly.GetManifestResourceStream("BatterMouse.Resources.tray.ico")` — no more SystemIcons.Application placeholder
- Full context menu: Battery label (disabled, "Battery: --"), separator, Start with Windows toggle (Checked mirrors registry), Exit
- Thread-safe HID event handling: BeginInvoke when menu handle is ready, SynchronizationContext.Post as construction-time fallback
- 7 new unit tests (5 menu structure + 2 icon resource) — total suite: 20 green

## Task Commits

Each task was committed atomically:

1. **Task 1: Add unit tests for menu structure and icon resource** - `21c744e` (test — RED)
2. **Task 2: Rewrite AppContext with real icon, full menu, thread-safe HID** - `ec18269` (feat — GREEN)

_TDD: RED commit first, GREEN commit after implementation._

## Files Created/Modified
- `BatterMouse/AppContext.cs` - Full Phase 3 rewrite: embedded icon, 4-item menu, thread-safe dispatch
- `BatterMouse/AssemblyInfo.cs` - [assembly: InternalsVisibleTo("BatterMouse.Tests")]
- `BatterMouse.Tests/AppContextMenuTests.cs` - 5 tests for menu structure via STA thread wrapper
- `BatterMouse.Tests/IconResourceTests.cs` - 2 tests confirming embedded tray.ico present and non-empty

## Decisions Made
- Exposed `BuildMenuInternal` as `internal static` taking `IContainer` — tests can build the menu directly without constructing `AppContext` (which starts HID reader and requires hardware)
- STA helper returns `(ContextMenuStrip, Container)` tuple — disposing the container before reading items caused `Items.Count == 0` (found and fixed during GREEN phase as Rule 1 auto-fix)
- `CheckOnClick = false` on startup toggle — state read explicitly from registry in `ToggleStartup`, not relying on WinForms auto-toggle which could diverge from actual registry state

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] STA helper disposed Container before menu items were readable**
- **Found during:** Task 2 (GREEN — running tests after first implementation)
- **Issue:** `using var container = new Container()` in STA thread helper disposed the container (and registered ContextMenuStrip) before calling thread rejoined — `menu.Items.Count` returned 0 on all tests
- **Fix:** Changed helper return type to `(ContextMenuStrip, Container)` tuple; caller owns disposal. Updated all 5 test methods to use `using (container) using (menu)` pattern.
- **Files modified:** BatterMouse.Tests/AppContextMenuTests.cs
- **Verification:** All 5 menu tests pass with correct item counts and values
- **Committed in:** ec18269 (Task 2 commit)

---

**Total deviations:** 1 auto-fixed (Rule 1 — bug)
**Impact on plan:** Essential fix for test correctness. No scope creep.

## Issues Encountered
- WinForms `ContextMenuStrip` registered with a `Container` is disposed when the container is disposed — standard WinForms component ownership. Fixed by returning both from the STA helper and deferring disposal to the test method.

## User Setup Required
None - no external service configuration required.

## Next Phase Readiness
- AppContext is complete for Phase 3 final publish step
- All 20 unit tests green; build clean with 0 warnings
- Plan 03-03 (dotnet publish / single-file EXE distribution) can proceed immediately

---
*Phase: 03-ship*
*Completed: 2026-03-13*
