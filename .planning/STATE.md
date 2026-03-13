---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
status: completed
stopped_at: Completed 03-03-PLAN.md (GitHub Actions release workflow + smoke test)
last_updated: "2026-03-13T12:10:41.748Z"
last_activity: 2026-03-13 — Phase 3 plan 03 complete (GitHub Actions release workflow, smoke test approved)
progress:
  total_phases: 3
  completed_phases: 3
  total_plans: 9
  completed_plans: 9
  percent: 100
---

# STATE

## Project Reference

**BatterMouse** — A lightweight Windows 11 tray app that monitors a Keychron mouse battery via HID and fires a toast notification at 20%.

See: .planning/PROJECT.md (updated 2026-03-12)

**Core value:** Never be caught with a dead mouse
**Current focus:** COMPLETE — all phases shipped

## Current Position

Phase: 3 of 3 (Ship) — COMPLETE
Plan: 3 of 3 in current phase — COMPLETE
Status: All phases and plans complete; BatterMouse v1.0 ready to release
Last activity: 2026-03-13 — Phase 3 plan 03 complete (GitHub Actions release workflow, smoke test approved)

Progress: [██████████] 100% (9 of 9 plans complete)

## Accumulated Context

### Decisions

- Single 20% threshold (simple, sufficient for v1)
- Auto-start with Windows (user requirement)
- Stack: C# .NET 8 + WinForms + HidSharp; TFM net8.0-windows10.0.19041.0
- Deploy as single self-contained EXE (PublishSingleFile + SelfContained + win-x64)
- **[CONFIRMED empirically 2026-03-13]** Dongle PID is 0xD030 ("Keychron Link"), NOT 0xD034 as research assumed
- **[CONFIRMED empirically 2026-03-13]** Battery byte is at fixed offset 5 (NOT pattern-based)
- **[CONFIRMED empirically 2026-03-13]** Battery TLC: VID=0x3434, PID=0xD030, usage_page=0x008C, Interface 1
- **[CONFIRMED empirically 2026-03-13]** Battery reports arrive infrequently wirelessly (>20s); immediate when USB cable connects
- **[CONFIRMED empirically 2026-03-13]** C# reader should use persistent blocking thread (no timeout), not a poll loop
- Use hidapi PyPI package (cython-hidapi) not hid (pyhidapi) — hid lacks bundled hidapi.dll on Windows (Phase 1 tooling only)
- [Phase 02-core-app]: Wrote BatterMouse.csproj manually — dotnet template does not accept windows-versioned TFM in --framework flag
- [Phase 02-core-app]: Added UseWindowsForms=true to test project csproj to allow ProjectReference to WinForms main project
- [Phase 02-core-app]: AppContext.cs stub created to unblock build — plan 02-03 will replace with full implementation
- [Phase 02-core-app]: ReadTimeout=Timeout.Infinite in HidReader — wireless reports arrive >20s apart, finite timeout causes spurious IOExceptions
- [Phase 02-core-app]: AppContext uses SystemIcons.Application as placeholder tray icon for compile-time independence from tray.ico file
- [Phase 02-04 gate confirmed 2026-03-13]: All 5 requirements met — 13/13 unit tests green; human smoke-tested tray, HKCU registry, HID read, clean exit
- [Phase 02-04 gate confirmed 2026-03-13]: NOTF-01 (20% toast) verified by unit tests; live integration requires mouse battery <=20%
- [Phase 03-ship]: ICO generated via C# GDI+ (ImageMagick unavailable); EmbeddedResource replaces Content/CopyToOutputDirectory for single-file publish
- [Phase 03-02]: BuildMenuInternal exposed as internal static — tests build menu directly without constructing AppContext (which starts HID reader)
- [Phase 03-02]: CheckOnClick=false on startup toggle — state read explicitly from registry in ToggleStartup, not relying on WinForms auto-toggle
- [Phase 03-02]: Dual UI-thread dispatch: BeginInvoke when menu handle ready, SynchronizationContext.Post as construction-time fallback
- [Phase 03-03]: windows-latest runner required for win-x64 WinExe builds (ubuntu-latest produces link errors)
- [Phase 03-03]: softprops/action-gh-release@v2 chosen (actions/create-release is archived/deprecated)
- [Phase 03-03]: No --self-contained/-r flags on dotnet publish — already declared in BatterMouse.csproj
- [Phase 03-03]: Human smoke test approved — real icon, 4-item menu, registry toggle, clean exit, single-file output all verified

### Pending Todos

None.

### Blockers/Concerns

None. Phase 1 primary risk (unknown byte offset) is resolved.

## Session Continuity

Last session: 2026-03-13T12:30:00Z
Stopped at: Completed 03-03-PLAN.md (GitHub Actions release workflow + smoke test)
Resume file: None
