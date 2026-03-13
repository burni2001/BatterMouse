---
phase: 03-ship
plan: 01
subsystem: infra
tags: [ico, icon, embedded-resource, single-file, gdi-plus, csproj]

# Dependency graph
requires:
  - phase: 02-core-app
    provides: BatterMouse.csproj with Content/CopyToOutputDirectory for tray.ico
provides:
  - Multi-size ICO (16/32/48px) with mouse+battery silhouette embedded in EXE
  - EmbeddedResource build action for tray.ico enabling single-file publish
affects: [03-02]

# Tech tracking
tech-stack:
  added: []
  patterns:
    - "EmbeddedResource for binary assets that must travel inside single-file EXE"
    - "GetManifestResourceStream('BatterMouse.Resources.tray.ico') to load at runtime"

key-files:
  created: []
  modified:
    - BatterMouse/Resources/tray.ico
    - BatterMouse/BatterMouse.csproj

key-decisions:
  - "Used C# GDI+ console project (not ImageMagick) to generate ICO — ImageMagick unavailable in PATH"
  - "ICO uses PNG sub-format (modern, supported by Windows Vista+) rather than BMP sub-format"
  - "EmbeddedResource replaces Content/CopyToOutputDirectory; ApplicationIcon kept separate (controls EXE shell icon)"

patterns-established:
  - "EmbeddedResource for tray.ico: load via Assembly.GetManifestResourceStream at runtime"

requirements-completed:
  - "(all v1 requirements delivered as a polished, distributable artifact)"

# Metrics
duration: 3min
completed: 2026-03-13
---

# Phase 3 Plan 01: Tray Icon Asset and EmbeddedResource Wiring Summary

**Multi-size tray.ico (16/32/48px white mouse+battery silhouette on transparent) generated via C# GDI+ and declared as EmbeddedResource, eliminating loose-file from single-file publish output**

## Performance

- **Duration:** ~3 min
- **Started:** 2026-03-13T11:47:32Z
- **Completed:** 2026-03-13T11:51:00Z
- **Tasks:** 2
- **Files modified:** 2

## Accomplishments

- Created a proper multi-size ICO replacing the placeholder 16x16 2-color 134-byte file with a 1100-byte 3-size (16/32/48px) RGBA asset
- White mouse body (rounded rectangle with transparent background) plus battery indicator in the lower-right quadrant, recognizable at 16x16
- Changed BatterMouse.csproj from `Content/CopyToOutputDirectory` to `EmbeddedResource` — `dotnet publish` now produces only `BatterMouse.exe` with no loose `tray.ico`

## Task Commits

Each task was committed atomically:

1. **Task 1: Draw tray.ico (16/32/48px, white mouse+battery silhouette)** - `a0358ce` (feat)
2. **Task 2: Change csproj build action from Content to EmbeddedResource** - `fa0af53` (feat)

**Plan metadata:** committed with SUMMARY/STATE/ROADMAP docs commit

## Files Created/Modified

- `BatterMouse/Resources/tray.ico` - Multi-size ICO (16/32/48px), 32bpp RGBA, white mouse+battery silhouette on transparent
- `BatterMouse/BatterMouse.csproj` - Replaced Content/CopyToOutputDirectory with EmbeddedResource for tray.ico

## Decisions Made

- **ImageMagick unavailable:** Used a temporary C# GDI+ console project (`net8.0-windows`) to programmatically render the silhouette using `System.Drawing` and encode a standards-compliant ICO with PNG sub-images.
- **PNG-in-ICO format:** Modern ICO format (PNG sub-images) chosen over legacy BMP sub-images. Windows Vista+ supports this natively, and it's what ImageMagick's `icon:auto-resize` would have produced.
- **ApplicationIcon preserved:** The `<ApplicationIcon>Resources\tray.ico</ApplicationIcon>` element was kept in the csproj. This is a build-time-only link that embeds the icon as the PE/EXE shell icon — it is orthogonal to the EmbeddedResource declaration and must remain.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 3 - Blocking] Created C# GDI+ project instead of using ImageMagick**
- **Found during:** Task 1 (Draw tray.ico)
- **Issue:** `magick` and `magick.exe` not found in PATH; plan specified ImageMagick as primary approach
- **Fix:** Implemented a temporary `net8.0-windows` console project using `System.Drawing` GDI+ APIs to render each size and write a standards-compliant ICO. Cleaned up afterwards.
- **Files modified:** BatterMouse/Resources/tray.ico
- **Verification:** `file tray.ico` reports "MS Windows icon resource - 3 icons"; `dotnet build` exits 0 with no warnings
- **Committed in:** a0358ce (Task 1 commit)

---

**Total deviations:** 1 auto-fixed (Rule 3 - blocking tooling gap)
**Impact on plan:** Functionally equivalent outcome. ICO format is identical to what ImageMagick would have produced. No scope creep.

## Issues Encountered

None beyond the ImageMagick tooling gap handled above.

## User Setup Required

None - no external service configuration required.

## Next Phase Readiness

- `BatterMouse/Resources/tray.ico` is now an EmbeddedResource bundled inside the EXE
- Plan 02 can load it via `Assembly.GetExecutingAssembly().GetManifestResourceStream("BatterMouse.Resources.tray.ico")` — this is the only supported loading path now that Content/CopyToOutputDirectory is removed
- `dotnet publish` produces a single `BatterMouse.exe` — no loose files alongside it

## Self-Check: PASSED

- FOUND: BatterMouse/Resources/tray.ico (1100 bytes, MS Windows icon resource - 3 icons)
- FOUND: .planning/phases/03-ship/03-01-SUMMARY.md
- FOUND: commit a0358ce (feat: create multi-size tray.ico)
- FOUND: commit fa0af53 (feat: switch tray.ico to EmbeddedResource)

---
*Phase: 03-ship*
*Completed: 2026-03-13*
