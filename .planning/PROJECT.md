# BatterMouse

## What This Is

A lightweight Windows 11 background app that monitors the battery level of a Keychron mouse connected via 2.4GHz USB dongle (HID device). It sits in the system tray and fires a Windows toast notification when the battery drops to 20% or below.

## Core Value

Never be caught with a dead mouse — warn the user before the battery dies so they can charge it in time.

## Requirements

### Validated

(None yet — ship to validate)

### Active

- [ ] App reads Keychron mouse battery level from HID device
- [ ] System tray icon shows app is running
- [ ] Windows toast notification fires when battery drops to or below 20%
- [ ] App launches automatically on Windows startup
- [ ] App runs silently in the background (no main window required)

### Out of Scope

- Multiple battery thresholds — single 20% warning is sufficient for v1
- Configurable threshold — hardcoded 20% for v1; keep it simple
- Support for other mice — Keychron-specific HID protocol only
- Mobile / cross-platform — Windows 11 only

## Context

- The mouse connects via a 2.4GHz USB dongle and is recognized as an HID device by Windows
- Battery level is confirmed accessible: a web-based Chrome app reads it via WebHID, proving the HID device exposes this data
- Windows 11 is the target OS; modern WinRT toast notifications are preferred
- The project name "BatterMouse" is already established (working directory name)

## Constraints

- **Platform**: Windows 11 only — can leverage WinRT/Win32 APIs directly
- **Scope**: Small/lightweight — no unnecessary UI, no heavy frameworks
- **HID Access**: Must read battery from the specific Keychron HID device; requires identifying the correct HID report descriptor

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Single 20% threshold | Simple and sufficient for v1; user agreed | — Pending |
| Auto-start with Windows | User requirement — runs silently in background | — Pending |

---
*Last updated: 2026-03-12 after initialization*
