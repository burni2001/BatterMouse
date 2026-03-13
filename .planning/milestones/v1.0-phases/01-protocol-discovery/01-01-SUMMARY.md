---
phase: 01-protocol-discovery
plan: 01
subsystem: discovery
tags: [python, hid, hidapi, keychron, m3, hid-enumeration, battery-protocol]

# Dependency graph
requires: []
provides:
  - "discover/discover.py — Python HID discovery script with --enumerate, enumerate_tlcs, scan_feature_reports, read_input_reports"
  - "discover/requirements.txt — pinned hidapi>=0.14.0 dependency"
  - "discover/FINDINGS.md — protocol findings template with confirmed battery parse algorithm"
affects: [01-02, 02-hidsharp-integration, phase-2]

# Tech tracking
tech-stack:
  added: ["Python 3.14 (C:\\Python314 + C:\\Users\\Bernhard\\AppData\\Local\\Programs\\Python\\Python314\\Lib)", "hidapi 0.15.0 (cython-hidapi, bundles hidapi.dll for win-x64)"]
  patterns: ["open_path() with enumerate() filter — never open(VID, PID) directly", "usage_page pattern matching for TLC selection", "pattern-based battery byte extraction (not fixed offset)"]

key-files:
  created:
    - "discover/discover.py"
    - "discover/requirements.txt"
    - "discover/FINDINGS.md"
  modified: []

key-decisions:
  - "Battery TLC uses usage_page=0x008C (140), NOT usage_page>=0xFF00 as research assumed — confirmed from keychron-m3-linux main.py"
  - "Battery value is pattern-matched (scan for 0x00/0x01, _, 0x02, 0x02 sequence) — not at a fixed byte offset"
  - "Use hidapi PyPI package (cython-hidapi), NOT hid (pyhidapi) — hid lacks bundled hidapi.dll on Windows and conflicts with hidapi"
  - "discover.py filters on usage_page==0x008C primary + >=0xFF00 fallback for robustness"

patterns-established:
  - "HID TLC filter: usage_page==0x008C (battery) primary, >=0xFF00 (vendor-generic) fallback"
  - "Battery parse: scan 32-byte report for [0x00|0x01, pct, 0x02, 0x02] pattern — data[i+1] is percentage"
  - "Always open TLC via open_path(path) from enumerate() result — never open(VID, PID)"
  - "set_nonblocking(1) for input reports — empty read = mouse idle, not an error"

requirements-completed: []

# Metrics
duration: 9min
completed: 2026-03-12
---

# Phase 1 Plan 01: Discovery Environment Scaffold Summary

**Python HID discovery script with --enumerate flag, pattern-based battery parser from keychron-m3-linux, and FINDINGS.md documenting usage_page=0x008C (not 0xFF00) and battery pattern algorithm**

## Performance

- **Duration:** ~9 min
- **Started:** 2026-03-12T16:28:48Z
- **Completed:** 2026-03-12T16:37:19Z
- **Tasks:** 2
- **Files modified:** 3 created

## Accomplishments

- Fetched keychron-m3-linux main.py from GitHub (master branch) and extracted the battery parse algorithm
- Discovered critical deviation: usage_page is 0x008C (Battery System), NOT 0xFF00 as research assumed
- Wrote discover.py (245 lines) with all three discovery functions and --enumerate flag
- Verified script exits 0/1 correctly and imports hid successfully on this machine
- Resolved Python environment path issue (C:\Python314 launcher + AppData Lib split install)

## Task Commits

Each task was committed atomically:

1. **Task 1: Check keychron-m3-linux main.py for known byte offset** - `a089162` (feat)
2. **Task 2: Write discover.py skeleton and requirements.txt** - `33f9c52` (feat)

**Plan metadata:** (pending final commit)

## Files Created/Modified

- `discover/discover.py` — 245-line discovery script: enumerate_tlcs, scan_feature_reports, read_input_reports, parse_battery_data, --enumerate flag
- `discover/requirements.txt` — pins hidapi>=0.14.0 (cython-hidapi, not pyhidapi)
- `discover/FINDINGS.md` — protocol findings with confirmed usage_page, parse algorithm, and C# translation guidance

## Decisions Made

1. **usage_page=0x008C not 0xFF00:** The reference implementation uses `usage_page = 140` (0x008C). The research assumed vendor TLCs use 0xFF00+. The script uses 0x008C as primary filter and 0xFF00+ as fallback.

2. **Pattern-based battery extraction:** `parse_battery_data` scans for the sequence `[0x00|0x01, pct, 0x02, 0x02]` rather than reading a fixed byte offset. This matches the keychron-m3-linux reference exactly.

3. **hidapi not hid:** The `hid` PyPI package (pyhidapi, ctypes bindings) requires a separately installed hidapi.dll. The `hidapi` PyPI package (cython-hidapi) bundles the DLL. Both install as `import hid` but they conflict. requirements.txt pins `hidapi` and documents the conflict.

4. **python314._pth workaround:** Python 3.14 on this machine has a split install (launcher at C:\Python314, Lib at AppData). Created `python314._pth` to point the launcher to the correct Lib directory. This is a one-time machine setup, not a project dependency.

## Deviations from Plan

### Auto-fixed Issues

**1. [Rule 1 - Bug] Corrected usage_page filter from >=0xFF00 to ==0x008C**
- **Found during:** Task 1 (fetching keychron-m3-linux main.py)
- **Issue:** RESEARCH.md skeleton and task description both specify `usage_page >= 0xFF00` for vendor TLC filtering. The reference implementation uses `usage_page = 140` (0x008C), which is below 0xFF00. A script using only >=0xFF00 would fail to find the battery TLC.
- **Fix:** Added primary filter `usage_page == 0x008C` with fallback `>= 0xFF00`. Documented in FINDINGS.md.
- **Files modified:** discover/discover.py, discover/FINDINGS.md
- **Verification:** Structural check confirmed filter in script; enumeration tested successfully
- **Committed in:** 33f9c52 (Task 2 commit)

**2. [Rule 3 - Blocking] Resolved Python split-install (python314._pth)**
- **Found during:** Task 2 (running pip install hid)
- **Issue:** C:\Python314\python.exe (the launcher exe) could not find stdlib due to split install. `py` and `python` both failed with "Could not find platform independent libraries <prefix>".
- **Fix:** Created C:\Python314\python314._pth pointing to AppData Python314 Lib and site-packages.
- **Files modified:** C:\Python314\python314._pth (machine config, not in repo)
- **Verification:** `C:\Python314\python.exe --version` works; `import hid; hid.enumerate()` returns 24 devices
- **Committed in:** N/A (machine config)

**3. [Rule 3 - Blocking] Replaced conflicting hid package with hidapi**
- **Found during:** Task 2 (running discover.py --enumerate)
- **Issue:** `pip install hid` installed pyhidapi (ctypes bindings) which lacks bundled hidapi.dll on Windows. Running discover.py failed with `ImportError: Unable to load any of the following libraries: ... hidapi.dll`.
- **Fix:** Uninstalled `hid`, installed `hidapi` (cython-hidapi 0.15.0 with bundled win_amd64 DLL). Updated requirements.txt accordingly.
- **Files modified:** discover/requirements.txt
- **Verification:** `import hid; hid.enumerate()` returns 24 devices; discover.py exits 1 with clear error
- **Committed in:** 33f9c52 (Task 2 commit)

---

**Total deviations:** 3 auto-fixed (1 bug, 2 blocking)
**Impact on plan:** All fixes essential. The usage_page correction is a significant research correction that directly affects Phase 2's C# implementation. No scope creep.

## Vendor TLC Usage Pages Seen on This Machine

The dongle was not connected during this execution, so live enumeration results are not available. The usage_page=0x008C finding comes from the reference implementation (keychron-m3-linux). This will be verified empirically when the dongle is connected in Plan 02.

## Issues Encountered

- Python 3.14 split install (C:\Python314 vs AppData Lib) required creating a `_pth` file — one-time machine setup
- `hid` vs `hidapi` package naming confusion — documented in requirements.txt with explanation
- keychron-m3-linux repo uses `master` branch, not `main` — first fetch attempt returned 404; API query revealed correct branch

## User Setup Required

**Python environment note:** On this machine, the `py` launcher resolves to C:\Python314\python.exe. If running discover.py from command prompt, use:
```
C:\Python314\python.exe discover\discover.py --enumerate
```
Or add `C:\Python314` to PATH and use `python discover\discover.py --enumerate`.

A `python314._pth` file was created at `C:\Python314\python314._pth` to configure the standard library path.

## Next Phase Readiness

- discover.py is ready for Plan 02 (empirical byte offset discovery with physical device)
- FINDINGS.md template is populated with confirmed usage_page and parse algorithm; only live measurement values are TBD
- requirements.txt is correct; `pip install -r discover/requirements.txt` will install hidapi
- The battery parse algorithm from keychron-m3-linux is likely correct — Plan 02 should verify against physical device

## Self-Check: PASSED

- FOUND: discover/FINDINGS.md
- FOUND: discover/discover.py
- FOUND: discover/requirements.txt
- FOUND: .planning/phases/01-protocol-discovery/01-01-SUMMARY.md
- FOUND commit a089162: feat(01-01): add protocol findings from keychron-m3-linux reference
- FOUND commit 33f9c52: feat(01-01): add discover.py skeleton and requirements.txt

---
*Phase: 01-protocol-discovery*
*Completed: 2026-03-12*
