# Phase 3: Ship - Context

**Gathered:** 2026-03-13
**Status:** Ready for planning

<domain>
## Phase Boundary

Polish and distribute BatterMouse as a single self-contained EXE. The app already satisfies all five functional requirements (HID-01, TRAY-01, NOTF-01, STARTUP-01, BG-01) — Phase 3 adds a real tray icon, a complete context menu, and a GitHub Actions release pipeline.

</domain>

<decisions>
## Implementation Decisions

### Tray context menu
- Battery level shown as a **disabled (grayed-out, non-clickable) label** at the top of the menu
- Format: `Battery: 87%` — updates whenever a new HID reading arrives
- Before the first reading: `Battery: --`
- Auto-start toggle uses a **checkmark** pattern: label is always `Start with Windows`; a checkmark (✓) appears next to it when auto-start is enabled
- Menu order: Battery label → separator → Start with Windows → Exit

### Tooltip
- Tray icon tooltip shows **just the battery percentage** once a reading is available (e.g., `87%`)
- Before the first reading: `BatterMouse — reading...`

### Tray icon
- Mouse + small battery indicator silhouette
- **White / light gray** — visible on both dark and light taskbars
- Same `Resources\tray.ico` file is used for both the tray icon and the EXE file icon (`ApplicationIcon` in .csproj already points there)
- Claude's discretion on exact pixel layout — must be legible at 16×16

### Build & distribution
- Trigger: **push a git tag** (e.g., `v1.0.0`) → GitHub Actions builds and creates a GitHub Release with `BatterMouse.exe` as an attached artifact
- `dotnet publish` already configured in `.csproj` (PublishSingleFile, SelfContained, win-x64)
- No installer — single EXE that runs by copy

### Claude's Discretion
- Exact pixel layout of the tray icon within the 16×16 / 32×32 constraints
- ICO file format details (multi-size embedding: 16, 32, 48 px)
- GitHub Actions YAML workflow structure

</decisions>

<code_context>
## Existing Code Insights

### Reusable Assets
- `AppContext.cs:BuildMenu()` — current method returns a `ContextMenuStrip` with only "Exit"; extend here to add battery label and Start with Windows toggle
- `StartupManager` — already implements `SetStartup(bool)` and reads current registry state; the toggle can call `StartupManager.IsStartupEnabled()` + `SetStartup(!current)`
- `BatteryMonitor` — already tracks last known battery level; expose a property so `AppContext` can read it for the menu label and tooltip
- `Program.cs` — single-instance mutex already in place

### Established Patterns
- `_components` (IContainer) owns all disposable UI objects — add new menu items through it
- `ExitThreadCore()` already handles clean exit (tray icon hidden, toast cleanup)
- `SystemIcons.Application` placeholder is in the constructor comment with a `// TODO Phase 3:` annotation pointing to the real icon path

### Integration Points
- `AppContext` constructor: replace `SystemIcons.Application` with `new Icon(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "tray.ico"))`
- `BuildMenu()`: add disabled battery label + separator + checkmark auto-start item
- `HidReader.BatteryLevelReceived` event: also update `_trayIcon.Text` (tooltip) and the battery menu label on each reading

</code_context>

<specifics>
## Specific Ideas

- Auto-start label must be exactly **"Start with Windows"** (not "Enable/Disable auto-start" or "Toggle auto-start")
- Tooltip shows **only the percentage** when known (e.g., `87%`) — not the app name

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.

</deferred>

---

*Phase: 03-ship*
*Context gathered: 2026-03-13*
