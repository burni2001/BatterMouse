---
gsd_state_version: 1.0
milestone: v1.0
milestone_name: milestone
status: planning
stopped_at: Completed 01-protocol-discovery/01-01-PLAN.md
last_updated: "2026-03-12T16:38:46.305Z"
last_activity: 2026-03-12 — Roadmap created
progress:
  total_phases: 3
  completed_phases: 0
  total_plans: 2
  completed_plans: 1
  percent: 50
---

# STATE

## Project Reference

**BatterMouse** — A lightweight Windows 11 tray app that monitors a Keychron mouse battery via HID and fires a toast notification at 20%.

See: .planning/PROJECT.md (updated 2026-03-12)

**Core value:** Never be caught with a dead mouse
**Current focus:** Phase 1 — Protocol Discovery

## Current Position

Phase: 1 of 3 (Protocol Discovery)
Plan: 0 of ? in current phase
Status: Ready to plan
Last activity: 2026-03-12 — Roadmap created

Progress: [█████░░░░░] 50%

## Performance Metrics

**Velocity:**
- Total plans completed: 0
- Average duration: —
- Total execution time: —

**By Phase:**

| Phase | Plans | Total | Avg/Plan |
|-------|-------|-------|----------|
| - | - | - | - |

*Updated after each plan completion*
| Phase 01-protocol-discovery P01 | 9 | 2 tasks | 3 files |

## Accumulated Context

### Decisions

- Single 20% threshold (simple, sufficient for v1)
- Auto-start with Windows (user requirement)
- Stack: C# .NET 8 + WinForms + HidSharp; TFM net8.0-windows10.0.19041.0
- Keychron VID 0x3434, M3 dongle PID 0xD034; vendor-specific TLC (UP 0xFF__) is user-mode accessible
- Deploy as single self-contained EXE (PublishSingleFile + SelfContained + win-x64)
- [Phase 01-protocol-discovery]: Battery TLC uses usage_page=0x008C (140), not 0xFF00 as research assumed — confirmed from keychron-m3-linux main.py
- [Phase 01-protocol-discovery]: Battery value is pattern-matched (scan for 0x00/0x01,pct,0x02,0x02 sequence) not at a fixed byte offset
- [Phase 01-protocol-discovery]: Use hidapi PyPI package (cython-hidapi) not hid (pyhidapi) — hid lacks bundled hidapi.dll on Windows

### Pending Todos

None.

### Blockers/Concerns

- Phase 1: Exact byte offset in HID report is unknown — requires empirical discovery against the physical device. This is the project's primary risk.

## Session Continuity

Last session: 2026-03-12T16:38:46.303Z
Stopped at: Completed 01-protocol-discovery/01-01-PLAN.md
Resume file: None
