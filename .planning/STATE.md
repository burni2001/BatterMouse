---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
status: in_progress
stopped_at: Completed 01-protocol-discovery/01-02-PLAN.md — Phase 1 complete
last_updated: "2026-03-13T00:00:00.000Z"
last_activity: 2026-03-13 — Phase 1 complete, battery byte offset confirmed
progress:
  total_phases: 3
  completed_phases: 1
  total_plans: 2
  completed_plans: 2
  percent: 33
---

# STATE

## Project Reference

**BatterMouse** — A lightweight Windows 11 tray app that monitors a Keychron mouse battery via HID and fires a toast notification at 20%.

See: .planning/PROJECT.md (updated 2026-03-12)

**Core value:** Never be caught with a dead mouse
**Current focus:** Phase 2 — C# app implementation

## Current Position

Phase: 2 of 3 (C# App)
Plan: 0 of ? in current phase
Status: Ready to plan
Last activity: 2026-03-13 — Phase 1 complete

Progress: [███░░░░░░░] 33%

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

### Pending Todos

None.

### Blockers/Concerns

None. Phase 1 primary risk (unknown byte offset) is resolved.

## Session Continuity

Last session: 2026-03-13
Stopped at: Phase 1 complete. Next: plan Phase 2.
Resume file: None
