# Phase 3: Ship - Research

**Researched:** 2026-03-13
**Domain:** WinForms tray icon polish + .NET single-file publish + GitHub Actions release pipeline
**Confidence:** HIGH

## Summary

Phase 3 has three parallel workstreams: (1) extend `AppContext.BuildMenu()` with a battery-label/auto-start context menu, (2) swap the placeholder tray icon for a real ICO file and wire tooltip updates, and (3) set up a GitHub Actions workflow that builds on Windows and publishes a GitHub Release on tag push.

The most important technical discovery is that `tray.ico` is currently declared as `<Content CopyToOutputDirectory="PreserveNewest">`, which means it lands as a *loose file* next to the EXE — not bundled into it. For a truly single-file distribution the icon must be embedded as a managed resource and loaded via `GetManifestResourceStream`. The `.csproj` already has `ApplicationIcon` pointing to `Resources\tray.ico`, which controls the EXE file icon; that is a separate mechanism (handled at link time) and is unaffected by publish mode.

The GitHub Actions workflow must run on `windows-latest` (not `ubuntu-latest`) because `dotnet publish -r win-x64` for a WinExe requires the Windows SDK toolchain. The `softprops/action-gh-release@v2` action is the ecosystem standard for attaching artifacts to a tag-triggered release.

**Primary recommendation:** Embed `tray.ico` as an embedded resource, load it with `Assembly.GetManifestResourceStream`, and run the GH Actions job on `windows-latest`.

---

<user_constraints>
## User Constraints (from CONTEXT.md)

### Locked Decisions
- Battery level shown as a **disabled (grayed-out, non-clickable) label** at the top of the menu
- Format: `Battery: 87%` — updates whenever a new HID reading arrives
- Before the first reading: `Battery: --`
- Auto-start toggle uses a **checkmark** pattern: label is always `Start with Windows`; a checkmark (✓) appears next to it when auto-start is enabled
- Menu order: Battery label → separator → Start with Windows → Exit
- Tray icon tooltip shows **just the battery percentage** once a reading is available (e.g., `87%`)
- Before the first reading: `BatterMouse — reading...`
- Tray icon: Mouse + small battery indicator silhouette; **white / light gray** — visible on both dark and light taskbars; same `Resources\tray.ico` for both tray icon and EXE file icon (`ApplicationIcon`)
- Build trigger: **push a git tag** (e.g., `v1.0.0`) → GitHub Actions builds and creates a GitHub Release with `BatterMouse.exe` as an attached artifact
- `dotnet publish` flags already configured in `.csproj` (PublishSingleFile, SelfContained, win-x64)
- No installer — single EXE that runs by copy

### Claude's Discretion
- Exact pixel layout of the tray icon within the 16×16 / 32×32 constraints
- ICO file format details (multi-size embedding: 16, 32, 48 px)
- GitHub Actions YAML workflow structure

### Deferred Ideas (OUT OF SCOPE)
None — discussion stayed within phase scope.
</user_constraints>

---

<phase_requirements>
## Phase Requirements

| ID | Description | Research Support |
|----|-------------|-----------------|
| TRAY-01 | System tray icon with context menu | BuildMenu() extension pattern documented; ToolStripMenuItem.Enabled/Checked APIs verified |
| NOTF-01 | Low battery toast notification | Already implemented in Phase 2; no changes needed |
| STARTUP-01 | Auto-start with Windows | StartupManager.IsStartupEnabled() + SetStartup() already exist; toggle wiring documented |
| BG-01 | No visible window | Already satisfied; no changes needed |
| HID-01 | HID battery reading | Already implemented; only change is wiring BatteryLevelReceived to update menu label + tooltip |
| (all v1) | Polished distributable artifact | ICO embedding strategy, GitHub Actions YAML, and publish command documented |
</phase_requirements>

---

## Standard Stack

### Core
| Library / Tool | Version | Purpose | Why Standard |
|----------------|---------|---------|--------------|
| System.Windows.Forms | .NET 8 built-in | ContextMenuStrip, NotifyIcon, ToolStripMenuItem | Already in use; no new dependency |
| dotnet publish | .NET 8 SDK | Self-contained single-file EXE | Already configured in .csproj |
| softprops/action-gh-release | v2 | Create GitHub Release + upload artifact on tag push | De-facto standard for .NET OSS releases; maintained, cross-platform |
| actions/setup-dotnet | v4 | Install .NET 8 SDK on runner | Official GitHub action from actions/ org |

### Supporting
| Library / Tool | Version | Purpose | When to Use |
|----------------|---------|---------|-------------|
| ImageMagick CLI | any | Convert PNG art to multi-size ICO offline (dev machine only) | If developer creates art as PNG first; command: `magick input.png -define icon:auto-resize=48,32,16 tray.ico` |

### Alternatives Considered
| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Embedded resource for ICO | Content + CopyToOutputDirectory | Content approach leaves a loose .ico file next to EXE — not single-file |
| windows-latest runner | ubuntu-latest | win-x64 WinExe publish requires Windows SDK toolchain; ubuntu produces link errors |
| softprops/action-gh-release | actions/create-release + actions/upload-release-asset | actions/create-release is archived/deprecated by GitHub |

**Installation (CI — no new packages needed locally):** The `.csproj` already references everything needed.

---

## Architecture Patterns

### Recommended Project Structure (additions only)
```
BatterMouse/
├── Resources/
│   └── tray.ico          # Already exists — change build action to EmbeddedResource
├── AppContext.cs          # Extend BuildMenu(), wire BatteryLevelReceived updates
└── BatterMouse.csproj     # Change Content → EmbeddedResource for tray.ico

.github/
└── workflows/
    └── release.yml        # New — tag-triggered build + publish
```

### Pattern 1: Disabled Menu Label (Battery Display)
**What:** A `ToolStripMenuItem` with `Enabled = false` renders grayed-out and is not clickable.
**When to use:** Any purely informational item in a context menu.
**Example:**
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.toolstripmenuitem
private ToolStripMenuItem _batteryLabel = null!;

private ContextMenuStrip BuildMenu()
{
    var menu = new ContextMenuStrip(_components);

    _batteryLabel = new ToolStripMenuItem("Battery: --") { Enabled = false };
    menu.Items.Add(_batteryLabel);
    menu.Items.Add(new ToolStripSeparator());

    var startupItem = new ToolStripMenuItem("Start with Windows")
    {
        Checked = StartupManager.IsStartupEnabled(),
        CheckOnClick = false   // we manage state manually
    };
    startupItem.Click += ToggleStartup;
    menu.Items.Add(startupItem);

    menu.Items.Add("Exit", null, (_, _) => ExitThread());
    return menu;
}
```

### Pattern 2: Checkmark Toggle (Auto-start)
**What:** `ToolStripMenuItem.Checked` shows a checkmark; toggled manually in the Click handler.
**When to use:** Binary on/off setting where the menu item is always visible but state changes.
**Example:**
```csharp
private void ToggleStartup(object? sender, EventArgs e)
{
    bool current = StartupManager.IsStartupEnabled();
    StartupManager.SetStartup(!current);
    if (sender is ToolStripMenuItem item)
        item.Checked = !current;
}
```

### Pattern 3: Live Menu + Tooltip Updates from Event
**What:** The `BatteryLevelReceived` event (background thread) must marshal to the UI thread before touching WinForms controls.
**When to use:** Any time a non-UI thread updates tray icon text or menu items.
**Example:**
```csharp
// Wire in AppContext constructor alongside _batteryMonitor:
_hidReader.BatteryLevelReceived += level =>
{
    // NotifyIcon.Text and ContextMenuStrip items must be set on the UI thread.
    // Application.OpenForms is empty (no visible Form), but the SynchronizationContext
    // set by Application.Run is available.
    _trayIcon.ContextMenuStrip?.BeginInvoke(() =>
    {
        _batteryLabel.Text = $"Battery: {level}%";
        _trayIcon.Text = $"{level}%";
    });
};
```

> Note: `BeginInvoke` on a `Control` (ContextMenuStrip is a Control subclass) marshals to the UI thread. If `ContextMenuStrip` is null or handle not yet created, use `SynchronizationContext.Current?.Post` captured at constructor time from the UI thread.

### Pattern 4: Embedded Resource ICO Loading
**What:** Embed `tray.ico` as a managed resource so it is bundled into the single EXE.
**When to use:** Any file that must ship inside the single-file publish output.
**csproj change:**
```xml
<!-- REMOVE this: -->
<Content Include="Resources\tray.ico">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</Content>

<!-- REPLACE with: -->
<EmbeddedResource Include="Resources\tray.ico" />
```
**Loading code:**
```csharp
// In AppContext constructor, replacing SystemIcons.Application:
var asm = typeof(AppContext).Assembly;
using var stream = asm.GetManifestResourceStream("BatterMouse.Resources.tray.ico")!;
var icon = new Icon(stream);
```
> The resource name follows the pattern: `{AssemblyName}.{folder}.{filename}` — with slashes replaced by dots. For this project: `BatterMouse.Resources.tray.ico`.

### Pattern 5: GitHub Actions Tag-Triggered Release
**What:** Publish on `git push v1.0.0` (or any `v*` tag), create a GitHub Release, attach `BatterMouse.exe`.
**When to use:** Every public release.
**Example (`.github/workflows/release.yml`):**
```yaml
name: release

on:
  push:
    tags:
      - "v*.*.*"

permissions:
  contents: write

jobs:
  build-and-release:
    runs-on: windows-latest   # MUST be Windows for win-x64 WinExe publish

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET 8
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Restore
        run: dotnet restore BatterMouse/BatterMouse.csproj

      - name: Publish
        run: >
          dotnet publish BatterMouse/BatterMouse.csproj
          -c Release
          --no-restore
          -o publish/

      - name: Create GitHub Release
        uses: softprops/action-gh-release@v2
        with:
          files: publish/BatterMouse.exe
```

### Anti-Patterns to Avoid
- **`ubuntu-latest` for win-x64 WinExe publish:** Produces link errors; `win-x64` publish of a `WinExe` requires the Windows SDK. Always use `windows-latest`.
- **`Content CopyToOutputDirectory` for ICO in single-file mode:** Leaves a loose `.ico` next to the EXE — the published "single file" is actually two files. Use `EmbeddedResource` instead.
- **`AppDomain.CurrentDomain.BaseDirectory` for file paths in single-file apps:** The docs flag this as unreliable; use `AppContext.BaseDirectory` or, better, `GetManifestResourceStream` to avoid file path dependency entirely.
- **`CheckOnClick = true` on auto-start item:** Allows the checkmark to flip without actually toggling the registry. Manage `Checked` manually after the `SetStartup` call.
- **Updating NotifyIcon.Text from a non-UI thread without marshaling:** WinForms controls are not thread-safe; setting `_trayIcon.Text` from the HID reader thread without `BeginInvoke` causes intermittent exceptions.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| GitHub Release creation + asset upload | Custom `gh release create` shell steps | `softprops/action-gh-release@v2` | Handles draft releases, update-if-exists, multi-file globs; `actions/create-release` is archived |
| Multi-size ICO generation | Binary ICO writer in C# | ImageMagick CLI (offline, one-time) | ICO format is fiddly; magick handles palette/bit-depth correctly |
| Thread marshaling to UI | Custom lock/queue | `Control.BeginInvoke` | Already provided by WinForms message loop; zero new dependencies |

**Key insight:** The publish pipeline is almost entirely configured — `PublishSingleFile`, `SelfContained`, `RuntimeIdentifier`, and `IncludeNativeLibrariesForSelfExtract` are already in the `.csproj`. The GH Actions YAML just needs to call `dotnet publish` and hand the output to `action-gh-release`.

---

## Common Pitfalls

### Pitfall 1: ICO file not found at runtime after publish
**What goes wrong:** App crashes or falls back to a generic icon because `new Icon(Path.Combine(baseDir, "Resources", "tray.ico"))` cannot find the file when there is no loose file next to the EXE.
**Why it happens:** `PublishSingleFile` bundles managed assemblies but does NOT bundle `<Content>` files by default. The ICO ends up as a separate file that is not included in the release ZIP or copied to the user's machine.
**How to avoid:** Change build action to `EmbeddedResource` and load via `GetManifestResourceStream`.
**Warning signs:** After `dotnet publish`, the `publish/` folder contains both `BatterMouse.exe` and `Resources/tray.ico`.

### Pitfall 2: Tray icon tooltip updated from wrong thread
**What goes wrong:** `InvalidOperationException: Cross-thread operation not valid` or silent corruption of tray state.
**Why it happens:** `HidReader` fires `BatteryLevelReceived` from a background thread. `NotifyIcon.Text` and `ContextMenuStrip.Items[x].Text` are WinForms properties that must be touched on the UI thread.
**How to avoid:** Always marshal updates through `_trayIcon.ContextMenuStrip.BeginInvoke(...)` or capture the `SynchronizationContext` at construction time.
**Warning signs:** Tooltip or menu label updates work most of the time but occasionally fail or display stale data.

### Pitfall 3: Disabled ToolStripMenuItem not visually grayed out (rendering quirk)
**What goes wrong:** The battery label item appears identical to enabled items in some Windows themes.
**Why it happens:** A known WinForms rendering issue documented in dotnet/winforms#5493 — `Enabled = false` items may not gray in all visual styles.
**How to avoid:** Setting `Enabled = false` is still correct (it prevents clicks and keyboard navigation). Accept that visual graying depends on the active Windows theme. No workaround needed for this app.
**Warning signs:** On high-contrast themes, the disabled checkmark state may also not render as expected (dotnet/winforms#7980).

### Pitfall 4: `runs-on: ubuntu-latest` fails for win-x64 WinExe
**What goes wrong:** `dotnet publish` for `win-x64` with `WinExe` output type fails on Linux runners with a link error or produces no output.
**Why it happens:** WinExe publish for win-x64 requires the Windows SDK/linker. Cross-compilation for GUI apps is not supported on non-Windows runners.
**How to avoid:** Use `runs-on: windows-latest` in the GitHub Actions job.
**Warning signs:** CI error mentioning `link.exe` not found, or the publish output directory is empty.

### Pitfall 5: `NotifyIcon.Text` length limit
**What goes wrong:** ArgumentException thrown when setting the tooltip.
**Why it happens:** In .NET 6+ the limit is 127 characters (up from 63). Simple strings like `"87%"` are far below this.
**How to avoid:** Keep tooltip to just the percentage or `"BatterMouse — reading..."` — both are well within limit.
**Warning signs:** N/A for this app (the strings are short).

### Pitfall 6: Tag pattern mismatch in workflow trigger
**What goes wrong:** Pushing `v1.0.0` does not trigger the release workflow.
**Why it happens:** If the YAML uses `branches:` instead of `tags:`, or the glob pattern does not match the tag format.
**How to avoid:** Use `on: push: tags: ["v*.*.*"]` and verify with `git push origin v1.0.0`.
**Warning signs:** No Actions run appears in the GitHub repository's Actions tab after the tag push.

---

## Code Examples

Verified patterns from official sources:

### Disabled menu item (battery label)
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.toolstripmenuitem.enabled
var batteryLabel = new ToolStripMenuItem("Battery: --") { Enabled = false };
menu.Items.Add(batteryLabel);
```

### Checkmark auto-start toggle
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.toolstripmenuitem.checked
var startupItem = new ToolStripMenuItem("Start with Windows")
{
    Checked = StartupManager.IsStartupEnabled()
};
startupItem.Click += (_, _) =>
{
    bool nowEnabled = !StartupManager.IsStartupEnabled();
    StartupManager.SetStartup(nowEnabled);
    startupItem.Checked = nowEnabled;
};
```

### Embedded resource ICO load
```csharp
// Source: https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
using var stream = typeof(AppContext).Assembly
    .GetManifestResourceStream("BatterMouse.Resources.tray.ico")
    ?? throw new InvalidOperationException("tray.ico not found as embedded resource");
_trayIcon.Icon = new Icon(stream);
```

### UI-thread marshal for HID event
```csharp
// Standard WinForms cross-thread pattern (verified behavior)
_hidReader.BatteryLevelReceived += level =>
{
    if (_trayIcon.ContextMenuStrip?.IsHandleCreated == true)
    {
        _trayIcon.ContextMenuStrip.BeginInvoke(() =>
        {
            _batteryLabel.Text = $"Battery: {level}%";
            _trayIcon.Text = $"{level}%";
        });
    }
};
```

### GitHub Actions release workflow (complete)
```yaml
# .github/workflows/release.yml
name: release

on:
  push:
    tags:
      - "v*.*.*"

permissions:
  contents: write

jobs:
  build-and-release:
    runs-on: windows-latest

    steps:
      - uses: actions/checkout@v4

      - name: Setup .NET 8
        uses: actions/setup-dotnet@v4
        with:
          dotnet-version: '8.0.x'

      - name: Restore
        run: dotnet restore BatterMouse/BatterMouse.csproj

      - name: Publish
        run: dotnet publish BatterMouse/BatterMouse.csproj -c Release --no-restore -o publish/

      - name: Create GitHub Release
        uses: softprops/action-gh-release@v2
        with:
          files: publish/BatterMouse.exe
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| `actions/create-release` + `actions/upload-release-asset` | `softprops/action-gh-release@v2` | 2022 (actions/create-release archived) | Simpler YAML; single step |
| `NotifyIcon.Text` limit 63 chars | Limit 127 chars (.NET 6+) | .NET 6 | Irrelevant for short strings; no action needed |
| `AppDomain.CurrentDomain.BaseDirectory` for file paths | `AppContext.BaseDirectory` or embedded resources | .NET 5 single-file | Avoids file-not-found in single-file publish |
| `Content CopyToOutputDirectory` for resources | `EmbeddedResource` + `GetManifestResourceStream` | .NET 5 single-file | Required for true single-file distribution |

**Deprecated/outdated:**
- `actions/create-release`: Archived by GitHub; do not use.
- `Assembly.Location` in single-file apps: Returns empty string — do not use to build file paths.

---

## Open Questions

1. **Does the existing `tray.ico` contain 16x16, 32x32, and 48x48 frames?**
   - What we know: The file exists at `BatterMouse/Resources/tray.ico` but was created as a placeholder (Phase 2 used `SystemIcons.Application`).
   - What's unclear: Whether the current ICO has actual artwork or is an empty/placeholder file.
   - Recommendation: Wave 0 task should inspect/replace the file. If it is a placeholder, draw the icon using ImageMagick or a pixel editor and regenerate with `magick art.png -define icon:auto-resize=48,32,16 tray.ico`.

2. **SynchronizationContext availability before first HID event**
   - What we know: `Application.Run(new AppContext())` sets the WinForms `SynchronizationContext` on the main thread before `AppContext`'s constructor returns.
   - What's unclear: Whether `ContextMenuStrip.IsHandleCreated` is true before the menu is first opened (handle may be lazy-created).
   - Recommendation: Capture `SynchronizationContext.Current` at construction time as a fallback: `_uiContext = SynchronizationContext.Current;` and use `_uiContext?.Post(...)` if `BeginInvoke` is unavailable.

---

## Validation Architecture

### Test Framework
| Property | Value |
|----------|-------|
| Framework | xUnit 2.9.3 |
| Config file | `BatterMouse.Tests/BatterMouse.Tests.csproj` |
| Quick run command | `dotnet test BatterMouse.Tests/BatterMouse.Tests.csproj --no-build -v q` |
| Full suite command | `dotnet test BatterMouse.Tests/BatterMouse.Tests.csproj -v q` |

### Phase Requirements → Test Map
| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| TRAY-01 menu label | Battery label starts as "Battery: --" | unit | `dotnet test --filter "FullyQualifiedName~AppContextMenuTests"` | ❌ Wave 0 |
| TRAY-01 checkmark | `Start with Windows` checked state reflects registry | unit | `dotnet test --filter "FullyQualifiedName~StartupToggleTests"` | ❌ Wave 0 |
| TRAY-01 menu order | Items in correct order: label, sep, startup, exit | unit | `dotnet test --filter "FullyQualifiedName~AppContextMenuTests"` | ❌ Wave 0 |
| tooltip update | Tooltip changes to `"87%"` on BatteryLevelReceived(87) | unit | `dotnet test --filter "FullyQualifiedName~TooltipTests"` | ❌ Wave 0 |
| ICO embedded resource | `GetManifestResourceStream("BatterMouse.Resources.tray.ico")` returns non-null | unit | `dotnet test --filter "FullyQualifiedName~IconResourceTests"` | ❌ Wave 0 |
| Clean exit | No NotifyIcon visible after ExitThread | manual-only | N/A — requires UI thread and WinForms message pump | N/A |
| Single-file publish | `dotnet publish` produces exactly one file in output dir | smoke | `dotnet publish ... && ls publish/` | ❌ Wave 0 (CI) |

> Note: `AppContext` creates a WinForms `NotifyIcon` which requires an STA thread and a real message loop. Unit tests for menu structure should construct `AppContext` pieces in isolation (e.g., test `BuildMenu()` extracted as an internal static helper, or test `StartupManager` logic independently). The full WinForms smoke test remains manual.

### Sampling Rate
- **Per task commit:** `dotnet test BatterMouse.Tests/BatterMouse.Tests.csproj -v q`
- **Per wave merge:** `dotnet test BatterMouse.Tests/BatterMouse.Tests.csproj -v q`
- **Phase gate:** Full suite green + manual smoke (tray visible, menu correct, exit clean) before `/gsd:verify-work`

### Wave 0 Gaps
- [ ] `BatterMouse.Tests/AppContextMenuTests.cs` — covers menu structure, label text, checkmark state
- [ ] `BatterMouse.Tests/IconResourceTests.cs` — covers embedded resource load
- [ ] CI smoke: publish step in `release.yml` verifies single-file output

*(Existing tests: HidReaderTests.cs, BatteryMonitorTests.cs, StartupManagerTests.cs — all pass from Phase 2. No new framework install needed.)*

---

## Sources

### Primary (HIGH confidence)
- [Microsoft Docs: Single-file deployment overview](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview) — content file handling, embedded resource recommendation, AppContext.BaseDirectory
- [Microsoft Docs: ToolStripMenuItem.Checked](https://learn.microsoft.com/en-us/dotnet/api/system.windows.forms.toolstripmenuitem.checked?view=windowsdesktop-8.0) — checkmark behavior
- [Microsoft Docs: NotifyIcon.Text breaking change](https://learn.microsoft.com/en-us/dotnet/core/compatibility/windows-forms/6.0/notifyicon-text-max-text-length-increased) — 127-char limit in .NET 6+
- [softprops/action-gh-release](https://github.com/softprops/action-gh-release) — tag trigger, permissions, files input

### Secondary (MEDIUM confidence)
- [dotnet/winforms#5493](https://github.com/dotnet/winforms/issues/5493) — disabled item visual rendering quirk
- [dotnet/winforms#7980](https://github.com/dotnet/winforms/issues/7980) — high-contrast checkmark accessibility issue
- [meziantou.net: Creating ICO files in .NET](https://www.meziantou.net/creating-ico-files-from-multiple-images-in-dotnet.htm) — ICO format structure (for reference only; we use ImageMagick)

### Tertiary (LOW confidence)
- WebSearch results confirming `windows-latest` requirement for win-x64 WinExe publish — not found as an explicit Microsoft doc statement, but consistent across multiple community sources and the dotnet/sdk#11162 issue.

---

## Metadata

**Confidence breakdown:**
- Standard stack: HIGH — all APIs from official .NET 8 docs; action-gh-release from official repo
- Architecture: HIGH — patterns verified against official docs; ICO embedding is documented single-file guidance
- Pitfalls: MEDIUM-HIGH — thread-safety and ICO pitfalls verified; `ubuntu-latest` limitation from community sources (issue tracker) rather than official docs
- GitHub Actions YAML: HIGH — verified from action-gh-release README + Microsoft docs structure

**Research date:** 2026-03-13
**Valid until:** 2026-09-13 (stable ecosystem; .NET 8 LTS, action-gh-release v2 stable)
