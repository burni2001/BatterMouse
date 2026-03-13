---
phase: 3
slug: ship
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-13
---

# Phase 3 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | xUnit (BatterMouse.Tests project) |
| **Config file** | BatterMouse.Tests/BatterMouse.Tests.csproj |
| **Quick run command** | `dotnet test BatterMouse.Tests` |
| **Full suite command** | `dotnet test BatterMouse.Tests && dotnet publish BatterMouse -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true` |
| **Estimated runtime** | ~15 seconds |

---

## Sampling Rate

- **After every task commit:** Run `dotnet test BatterMouse.Tests`
- **After every plan wave:** Run full suite command above
- **Before `/gsd:verify-work`:** Full suite must be green and EXE must publish successfully
- **Max feedback latency:** 15 seconds

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 3-01-01 | 01 | 1 | ICO embed | build | `dotnet build BatterMouse` | ❌ W0 | ⬜ pending |
| 3-01-02 | 01 | 1 | Single-file publish | build | `dotnet publish BatterMouse -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true` | ❌ W0 | ⬜ pending |
| 3-02-01 | 02 | 1 | Tray menu items | manual | see manual verifications | N/A | ⬜ pending |
| 3-02-02 | 02 | 1 | Auto-start toggle | manual | see manual verifications | N/A | ⬜ pending |
| 3-02-03 | 02 | 1 | Thread marshaling | unit | `dotnet test BatterMouse.Tests` | ❌ W0 | ⬜ pending |
| 3-03-01 | 03 | 2 | GitHub Actions CI | build | via GH Actions on push | N/A | ⬜ pending |
| 3-03-02 | 03 | 2 | Release artifact | manual | see manual verifications | N/A | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] Verify `dotnet test` passes for existing test suite (no regressions from phase changes)
- [ ] Confirm publish toolchain works locally before CI setup

*Existing infrastructure (xUnit + BatterMouse.Tests) covers most automated checks.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Tray icon context menu shows battery % | SC-2 | Requires running EXE on Windows | Launch EXE, right-click tray icon, verify battery % label is visible and disabled |
| Auto-start toggle persists across reboot | SC-2 | Registry write requires live system | Toggle auto-start on, reboot, verify app launches on login |
| Exit from tray menu kills process | SC-3 | Process lifecycle requires live system | Click Exit in tray menu, open Task Manager, confirm BatterMouse.exe gone |
| EXE runs on clean Win11 without .NET | SC-1 | Requires clean VM | Copy EXE to clean Windows 11 VM, run without installing .NET runtime |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 15s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
