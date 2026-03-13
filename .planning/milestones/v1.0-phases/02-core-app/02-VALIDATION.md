---
phase: 2
slug: core-app
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-13
---

# Phase 2 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit 2.x |
| **Config file** | `BatterMouse.Tests/BatterMouse.Tests.csproj` — Wave 0 creates this |
| **Quick run command** | `dotnet test --filter Category=Unit` |
| **Full suite command** | `dotnet test` |
| **Estimated runtime** | ~5 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test --filter Category=Unit`
- **After every plan wave:** Run `dotnet test`
- **Before `/gsd:verify-work`:** Full suite must be green
- **Max feedback latency:** ~5 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| ParseBattery unit tests | 02 | 1 | HID-01 | unit | `dotnet test --filter "FullyQualifiedName~HidReaderTests"` | ❌ Wave 0 | ⬜ pending |
| ParseBattery boundary | 02 | 1 | HID-01 | unit | `dotnet test --filter "FullyQualifiedName~HidReaderTests"` | ❌ Wave 0 | ⬜ pending |
| Threshold fires at 20 | 03 | 1 | NOTF-01 | unit | `dotnet test --filter "FullyQualifiedName~BatteryMonitorTests"` | ❌ Wave 0 | ⬜ pending |
| No re-fire below threshold | 03 | 1 | NOTF-01 | unit | `dotnet test --filter "FullyQualifiedName~BatteryMonitorTests"` | ❌ Wave 0 | ⬜ pending |
| Re-fires after recovery | 03 | 1 | NOTF-01 | unit | `dotnet test --filter "FullyQualifiedName~BatteryMonitorTests"` | ❌ Wave 0 | ⬜ pending |
| Registry write/delete | 04 | 2 | STARTUP-01 | unit | `dotnet test --filter "FullyQualifiedName~StartupManagerTests"` | ❌ Wave 0 | ⬜ pending |
| Tray icon visible | 01 | 1 | TRAY-01 | manual | Run app, inspect tray | — | ⬜ pending |
| No window on startup | 01 | 1 | BG-01 | manual | Run app, confirm no window | — | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `BatterMouse.Tests/BatterMouse.Tests.csproj` — xUnit test project (`dotnet new xunit -n BatterMouse.Tests`)
- [ ] `BatterMouse.Tests/HidReaderTests.cs` — stubs for HID-01 (ParseBattery offset 5, boundary cases)
- [ ] `BatterMouse.Tests/BatteryMonitorTests.cs` — stubs for NOTF-01 (threshold=20, de-duplication, reset)
- [ ] `BatterMouse.Tests/StartupManagerTests.cs` — stubs for STARTUP-01 (registry key CRUD)
- [ ] Project reference: `BatterMouse.Tests` references `BatterMouse` main project

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Tray icon appears on launch | TRAY-01 | No UI automation framework in scope for v1 | Launch app; confirm icon in system tray area (bottom-right) |
| No window opens on launch | BG-01 | Requires visual/process inspection | Launch app; confirm no window appears, only tray icon |
| Toast fires at ≤20% | NOTF-01 (integration) | Requires physical mouse at low battery | Let battery drain to ≤20% or test via manual battery level override |
| App in tray after reboot | STARTUP-01 (integration) | Requires OS reboot | Reboot Windows; confirm app running in tray without manual launch |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 10s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
