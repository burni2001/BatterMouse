# Milestones

## v1.0 MVP (Shipped: 2026-03-13)

**Phases completed:** 3 phases, 9 plans
**Timeline:** 2026-03-12 → 2026-03-13 (2 days)
**Git range:** feat(01-01) → feat(03-03)

**Key accomplishments:**
- Empirically confirmed Keychron Link dongle HID protocol: VID=0x3434, PID=0xD030, battery at fixed byte offset 5
- Built C# .NET 8 HID reader using persistent blocking thread (ReadTimeout=Infinite) for infrequent wireless reports
- 20% battery threshold notification with de-duplication — fires once, resets on recovery
- Formless tray-only WinForms app: no window, HKCU auto-start, IContainer-owned NotifyIcon, clean process exit
- Multi-size tray icon (16/32/48px) embedded in single-file self-contained EXE via EmbeddedResource
- Full context menu (battery label, separator, Start with Windows toggle, Exit) with thread-safe HID updates; 20/20 tests green
- Tag-triggered GitHub Actions release pipeline (v*.*.*) on windows-latest; smoke test verified

---

