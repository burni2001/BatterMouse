# BatterMouse

## What This Is

A lightweight Windows 11 background app that monitors the battery level of a Keychron mouse connected via 2.4GHz USB dongle (HID device). It sits in the system tray and fires a Windows toast notification when the battery drops to 20% or below. Ships as a single self-contained EXE — install by copy, no runtime required.

## Core Value

Never be caught with a dead mouse — warn the user before the battery dies so they can charge it in time.

## Requirements

### Validated

- ✓ App reads Keychron mouse battery level from HID device — v1.0 (VID=0x3434, PID=0xD030, offset 5, blocking thread)
- ✓ System tray icon shows app is running — v1.0 (real embedded multi-size ICO, 4-item context menu)
- ✓ Windows toast notification fires when battery drops to or below 20% — v1.0 (de-duplicated, resets on recovery)
- ✓ App launches automatically on Windows startup — v1.0 (HKCU Run registry, toggle in tray menu)
- ✓ App runs silently in the background (no main window required) — v1.0 (formless WinForms ApplicationContext)

### Active

(None — all v1 requirements shipped. Add v1.1 requirements here when planning next milestone.)

### Out of Scope

- Multiple battery thresholds — single 20% warning is sufficient for v1
- Configurable threshold — hardcoded 20% for v1; keep it simple
- Support for other mice — Keychron-specific HID protocol only for v1
- Mobile / cross-platform — Windows 11 only
- Live battery percentage in tooltip — tooltip shows "BatterMouse — reading..." (tray label updates on each HID event)

## Context

- v1.0 shipped 2026-03-13. Codebase: ~600 LOC C# across 6 source files + 7 test files.
- Tech stack: C# .NET 8, WinForms, HidSharp 2.6.4, Microsoft.Toolkit.Uwp.Notifications 7.1.3, xUnit
- 20/20 unit tests green. NOTF-01 (20% toast) verified by unit test; live integration requires mouse battery ≤20%.
- Dongle PID is 0xD030 ("Keychron Link") — NOT 0xD034 as pre-ship research assumed. Confirmed empirically.
- Battery reports arrive infrequently wirelessly (>20s gap); immediate when USB cable connects.

## Constraints

- **Platform**: Windows 11 only — leverages WinRT/Win32 APIs directly
- **Scope**: Small/lightweight — no unnecessary UI, no heavy frameworks
- **HID Access**: Reads from VID=0x3434, PID=0xD030 (Keychron Link dongle), usage_page=0x008C, Interface 1, byte offset 5

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Single 20% threshold | Simple and sufficient for v1 | ✓ Good — shipped, no user friction |
| Auto-start with Windows | User requirement — runs silently | ✓ Good — HKCU registry, no elevation needed |
| C# .NET 8 + WinForms + HidSharp | Native Windows, single-file publish, good HID library | ✓ Good — clean single EXE, 20/20 tests |
| Persistent blocking HID thread | Wireless reports arrive >20s apart; poll loop causes spurious IOExceptions | ✓ Good — stable reads |
| EmbeddedResource for tray.ico | Single-file publish requires assets bundled in EXE | ✓ Good — no loose files |
| softprops/action-gh-release@v2 | actions/create-release is archived/deprecated | ✓ Good — smooth CI release |
| windows-latest CI runner | ubuntu-latest produces link errors for win-x64 WinExe | ✓ Good — required |

---
*Last updated: 2026-03-13 after v1.0 milestone*
