# Roadmap: BatterMouse

## Overview

Three coarse phases take BatterMouse from unknown HID protocol to a daily-use background app. Phase 1 de-risks the only hard unknown — the exact byte in the Keychron HID report that carries battery percentage. Phase 2 builds the complete app against that discovered protocol. Phase 3 ships it as a single portable EXE ready for real use.

## Phases

- [ ] **Phase 1: Protocol Discovery** - Identify the exact HID report byte that carries battery level for this Keychron mouse
- [ ] **Phase 2: Core App** - Build the full BatterMouse app satisfying all five active requirements
- [ ] **Phase 3: Ship** - Publish a self-contained EXE polished for daily use

## Phase Details

### Phase 1: Protocol Discovery
**Goal**: The HID report ID and byte offset for battery percentage are known and documented for the user's specific Keychron mouse
**Depends on**: Nothing (first phase)
**Requirements**: Prerequisite for HID-01 (App reads Keychron mouse battery level from HID device)
**Success Criteria** (what must be TRUE):
  1. A diagnostic script (Python or C#) runs against the connected dongle and prints a human-readable battery percentage
  2. The correct HID Usage Page, report ID, and byte offset are identified and written down
  3. Changing the mouse battery state (discharge or charge) causes the printed value to change, confirming the field is live
**Plans**: 2 plans

Plans:
- [ ] 01-01-PLAN.md — Scaffold environment: discover.py skeleton + requirements.txt + FINDINGS.md template
- [ ] 01-02-PLAN.md — Full discovery run: capture session log, identify battery byte, populate FINDINGS.md

### Phase 2: Core App
**Goal**: Users have a running BatterMouse app that monitors battery, sits in the tray, and fires a toast when battery hits 20%
**Depends on**: Phase 1
**Requirements**: HID-01, TRAY-01, NOTF-01, STARTUP-01, BG-01
  - HID-01: App reads Keychron mouse battery level from HID device
  - TRAY-01: System tray icon shows app is running
  - NOTF-01: Windows toast notification fires when battery drops to or below 20%
  - STARTUP-01: App launches automatically on Windows startup
  - BG-01: App runs silently in the background (no main window required)
**Success Criteria** (what must be TRUE):
  1. The app starts and a tray icon appears — no window opens
  2. With the mouse connected, the app reads battery level from the HID device without error
  3. When battery level is at or below 20%, a Windows toast notification appears; it does not re-fire every poll cycle
  4. After rebooting Windows, the app is running in the tray without any manual launch
**Plans**: TBD

### Phase 3: Ship
**Goal**: BatterMouse.exe is a single self-contained file that installs by copy and runs reliably day-to-day
**Depends on**: Phase 2
**Requirements**: (all v1 requirements delivered as a polished, distributable artifact)
**Success Criteria** (what must be TRUE):
  1. A single `BatterMouse.exe` runs on a clean Windows 11 machine with no .NET runtime pre-installed
  2. The tray icon context menu lets the user see current battery percentage, toggle auto-start, and exit cleanly
  3. Closing the app via the tray menu exits the process completely — no ghost process remains
**Plans**: TBD

## Progress

**Execution Order:** Phase 1 → Phase 2 → Phase 3

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
| 1. Protocol Discovery | 0/2 | Planning done | - |
| 2. Core App | 0/? | Not started | - |
| 3. Ship | 0/? | Not started | - |
