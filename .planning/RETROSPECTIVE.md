# Retrospective: BatterMouse

---

## Milestone: v1.0 — MVP

**Shipped:** 2026-03-13
**Phases:** 3 | **Plans:** 9

### What Was Built

- Phase 1: Python HID discovery script confirmed Keychron Link dongle protocol (VID=0x3434, PID=0xD030, battery at offset 5)
- Phase 2: Full C# .NET 8 app — HidReader, BatteryMonitor (20% threshold + de-dup), ToastHelper, StartupManager, AppContext wiring
- Phase 3: Real tray icon embedded in EXE, polished 4-item context menu, GitHub Actions release pipeline

### What Worked

- **TDD RED→GREEN pattern** — Writing failing stubs first in 02-01 kept Phase 2 plans focused and verified
- **Phase gate with human smoke test** — 02-04 checkpoint caught nothing (clean pass), confirming prior phases were solid
- **Empirical protocol discovery first** — Phase 1 de-risked the only hard unknown; Phase 2 had zero HID surprises
- **Single blocking HID thread** — Correct approach for infrequent wireless reports; no poll loop timeouts
- **EmbeddedResource for binary assets** — Clean single-file publish with no loose files

### What Was Inefficient

- **Multiple tray.ico iterations** — Placeholder ICO in 02-01/02-02, replaced in 02-02, then fully replaced in 03-01. Could have been done once in Phase 3 from the start.
- **ImageMagick unavailable** — Had to create a temporary C# GDI+ console project to generate the ICO. A pre-existing icon tool or a checked-in source SVG would save time.
- **AppContext stub created in 02-02 to unblock build** — This was necessary but created a known throwaway. Acceptable tradeoff.

### Patterns Established

- `InternalsVisibleTo` + `internal static BuildMenuInternal` — enables WinForms unit tests without starting the app
- STA thread wrapper for ContextMenuStrip tests — required for WinForms UI component construction in xUnit
- Dual UI-thread dispatch: `BeginInvoke` when handle ready, `SynchronizationContext.Post` as construction-time fallback
- `windows-latest` CI runner required for `win-x64 WinExe` builds

### Key Lessons

- Research assumed PID=0xD034; empirical discovery found PID=0xD030. Always discover empirically first for hardware protocols.
- Battery reports arrive >20s apart wirelessly — `ReadTimeout=Timeout.Infinite` is non-negotiable.
- `dotnet` template does not accept windows-versioned TFM in `--framework` flag — write `.csproj` manually for `net8.0-windows10.0.19041.0`.
- `actions/create-release` is archived — use `softprops/action-gh-release@v2`.

### Cost Observations

- Model mix: 100% Sonnet (all agents)
- Sessions: ~3 (Phase 1 human discovery, Phase 2 execution, Phase 3 execution)
- Notable: 2-day wall-clock for a complete, tested, CI-released Windows app

---

## Cross-Milestone Trends

| Milestone | Phases | Plans | Timeline | Verification |
|-----------|--------|-------|----------|--------------|
| v1.0 MVP | 3 | 9 | 2 days | 8/8 ✓ |
