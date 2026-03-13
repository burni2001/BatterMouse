---
phase: 03-ship
plan: 03
subsystem: infra
tags: [github-actions, dotnet-publish, self-contained, single-file, smoke-test]

# Dependency graph
requires:
  - phase: 03-ship/03-01
    provides: tray.ico embedded as EmbeddedResource; single-file publish verified
  - phase: 03-ship/03-02
    provides: AppContext with full tray menu, registry auto-start, thread-safe HID updates
provides:
  - Tag-triggered GitHub Actions release workflow (push v*.*.* -> GitHub Release with BatterMouse.exe)
  - Human smoke-test gate confirming complete shipped tray experience on Windows 11
affects: []

# Tech tracking
tech-stack:
  added: [github-actions, softprops/action-gh-release@v2, actions/checkout@v4, actions/setup-dotnet@v4]
  patterns: [tag-triggered release (v*.*.*), windows-latest runner for win-x64 WinExe publish, permissions:contents:write for release creation]

key-files:
  created: [.github/workflows/release.yml]
  modified: []

key-decisions:
  - "windows-latest runner required — ubuntu-latest produces link errors for win-x64 WinExe builds"
  - "softprops/action-gh-release@v2 used — actions/create-release is archived and deprecated"
  - "No --self-contained or -r flags passed to dotnet publish — declared in BatterMouse.csproj (PublishSingleFile=true, SelfContained=true, RuntimeIdentifier=win-x64)"
  - "permissions: contents: write set at workflow level — required for softprops/action-gh-release to create releases"

patterns-established:
  - "GitHub Actions release: trigger on tags/v*.*.*, restore then publish then create-release"

requirements-completed: []

# Metrics
duration: ~15min
completed: 2026-03-13
---

# Phase 3 Plan 03: Release Pipeline and Smoke Test Summary

**Tag-triggered GitHub Actions workflow (windows-latest -> dotnet publish -> softprops/action-gh-release) and human smoke test confirming real icon, full 4-item menu, registry toggle, clean exit, and single-file output**

## Performance

- **Duration:** ~15 min
- **Started:** 2026-03-13
- **Completed:** 2026-03-13
- **Tasks:** 2 (1 auto + 1 checkpoint:human-verify)
- **Files modified:** 1

## Accomplishments

- GitHub Actions release workflow created: pushing a v*.*.* tag now triggers windows-latest build, dotnet publish, and attaches BatterMouse.exe to a GitHub Release automatically
- Human smoke test fully approved: tray icon (mouse+battery silhouette), correct 4-item context menu order, Start with Windows registry toggle working, clean Exit (no ghost process), single-file output (no loose assets)
- All Phase 3 success criteria confirmed: self-contained EXE, complete tray experience, automated release pipeline ready

## Task Commits

Each task was committed atomically:

1. **Task 1: Create GitHub Actions release workflow** - `0dccd04` (feat)
2. **Task 2: Human smoke test** - verification only; no code changes (checkpoint gate passed)

**Plan metadata:** (this commit — docs: complete 03-03 plan)

## Files Created/Modified

- `.github/workflows/release.yml` - Tag-triggered release workflow (windows-latest, dotnet restore/publish, softprops/action-gh-release@v2)

## Decisions Made

- windows-latest runner is required for win-x64 WinExe builds — ubuntu-latest produces native link errors
- softprops/action-gh-release@v2 chosen over archived actions/create-release
- No publish flags override csproj settings (PublishSingleFile, SelfContained, RuntimeIdentifier already declared in BatterMouse.csproj)
- permissions: contents: write set at workflow level (not job level) for release creation

## Deviations from Plan

None - plan executed exactly as written. Task 1 workflow created per spec; Task 2 smoke test approved by user with all five checks passing.

## Issues Encountered

None.

## User Setup Required

None - no external service configuration required beyond GitHub repository permissions (GITHUB_TOKEN with contents:write is automatically provided in Actions runs).

## Next Phase Readiness

Phase 3 is fully complete. BatterMouse v1.0 is ready to ship:
- To release: `git tag v1.0.0 && git push origin v1.0.0`
- GitHub Actions will build and publish BatterMouse.exe as a release artifact automatically

---
*Phase: 03-ship*
*Completed: 2026-03-13*

## Self-Check: PASSED

- FOUND: .planning/phases/03-ship/03-03-SUMMARY.md
- FOUND: commit 0dccd04 (feat(03-03): add GitHub Actions release workflow)
