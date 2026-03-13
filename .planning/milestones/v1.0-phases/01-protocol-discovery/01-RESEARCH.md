# Phase 1: Protocol Discovery - Research

**Researched:** 2026-03-12
**Domain:** Windows HID enumeration + empirical HID report reverse engineering
**Confidence:** MEDIUM (Windows HID API: HIGH; Python hid library API: HIGH; Keychron M3 exact byte offset: LOW — requires physical device probing)

---

## Summary

Phase 1 is a pure reverse-engineering exercise, not a software-build phase. The deliverable is a document (not an app): the exact Usage Page, report ID, and byte offset that carries battery percentage in the Keychron M3's HID reports. The primary risk for the entire BatterMouse project is that this offset is unknown and Keychron publishes no protocol documentation.

The approach is settled: use Python + the `hid` PyPI package to enumerate all TLCs exposed by the 2.4GHz dongle (VID 0x3434 PID 0xD034), open the vendor-specific TLC (Usage Page 0xFF__ — not claimed by mouhid.sys), then run two parallel discovery strategies: (1) brute-force scan feature report IDs 0x01–0x1F watching for a byte value 0–100 that matches known battery state, and (2) read unsolicited 32-byte input reports while moving the mouse and watching for a stable byte in the 0–100 range that changes with battery level. The reference implementation `byte-bandit/keychron-m3-linux` confirms this 32-byte input report model works for the M3 on Linux; the Windows port uses identical HID primitives.

The keychron-m3-linux source file `main.py` is the single best reference for the M3's protocol — it contains the battery byte offset — but the source could not be directly fetched during this research session (GitHub rate limiting). Reading that file on the physical machine (or via browser) must be the first step in execution. If the offset is found there, empirical discovery is still required to verify it produces a live-changing value on Windows.

**Primary recommendation:** Write a Python discovery script that enumerates, filters on Usage Page >= 0xFF00, opens via `open_path`, reads 32-byte input reports while moving the mouse, and prints a hex table of all 32 bytes every 0.5 s. Overlay two readings taken at different battery states to locate which byte changes monotonically — that is the battery byte.

---

## Standard Stack

### Core (discovery phase only)

| Library | Version | Purpose | Why Standard |
|---------|---------|---------|--------------|
| Python | 3.11+ | Discovery scripting | Fast REPL iteration; no compile cycle |
| `hid` (cython-hidapi) | 1.0.6+ | HID device access on Windows | Wraps hidapi C library; exposes enumerate/open_path/read/get_feature_report; works on Windows without elevated rights for vendor TLCs |

### Supporting

| Library | Version | Purpose | When to Use |
|---------|---------|---------|-------------|
| `hid` built-in `enumerate` | same | TLC listing + UsagePage inspection | Step 1 of every discovery session |
| Python `time` (stdlib) | stdlib | Rate-limiting report reads | Always needed — device is not talkative when idle |
| USBPcap + Wireshark | latest | USB traffic capture | If Python alone cannot find the report; captures raw URBs |
| USB-IF HID Descriptor Tool | v2.7 | Parse raw report descriptor bytes | After capturing descriptor via USBPcap |

### Alternatives Considered

| Instead of | Could Use | Tradeoff |
|------------|-----------|----------|
| Python `hid` | C# HidSharp discovery script | HidSharp is the Phase 2 production library; using it here is fine but slower iteration than Python REPL |
| Python `hid` | `pywinusb` | pywinusb is Windows-only and less maintained; `hid` is cross-platform and actively maintained |
| Python discovery | Wireshark-only | Wireshark captures everything but finding the battery byte requires correlating packets with battery state changes; Python is faster for iterative scanning |

**Installation:**

```bash
pip install hid
```

On Windows, `hid` requires the hidapi DLL. The PyPI wheel bundles it for win-x64 — no separate install needed.

---

## Architecture Patterns

### Discovery Script Structure

```
discover/
├── discover.py          # Main discovery script — enumerate, open, read loop
├── scan_features.py     # Feature report brute-force scanner (report IDs 0x01–0x1F)
└── FINDINGS.md          # Output: documented byte offset, report ID, value range
```

### Pattern 1: Enumerate and Filter TLCs

**What:** List all HID TLCs exposed by the dongle, identify the vendor-specific one.
**When to use:** First step before opening any device.

```python
import hid

VID = 0x3434
PID_DONGLE = 0xD034

# Enumerate ALL interfaces for this VID/PID (dongle exposes multiple TLCs)
all_tlcs = hid.enumerate(VID, PID_DONGLE)

for d in all_tlcs:
    print(f"PID: {d['product_id']:#06x}  "
          f"UsagePage: {d['usage_page']:#06x}  "
          f"Usage: {d['usage']:#06x}  "
          f"Interface: {d['interface_number']}")
    print(f"  Path: {d['path']}")
    print()

# Filter: vendor-specific TLCs have usage_page >= 0xFF00
vendor_tlcs = [d for d in all_tlcs if d['usage_page'] >= 0xFF00]
print(f"\nVendor-specific TLCs: {len(vendor_tlcs)}")
```

**Expected output:** At least two TLCs — one with Usage Page 0x0001 (Generic Desktop, claimed by mouhid.sys) and one or more with Usage Page 0xFF__ (vendor-specific, accessible).

### Pattern 2: Read Input Reports in a Loop

**What:** Open a vendor TLC and continuously print all 32 bytes as hex. Visually identify which byte tracks battery level.
**When to use:** Primary discovery method for input-report-based battery data (which is what keychron-m3-linux uses for the M3).

```python
import hid, time

target = vendor_tlcs[0]  # First vendor-specific TLC from enumerate step

dev = hid.device()
dev.open_path(target['path'])  # Use path, NOT open(VID, PID) — avoids grabbing wrong TLC
dev.set_nonblocking(1)

print("Move mouse to trigger input reports. Ctrl+C to stop.")
print("Byte: 00 01 02 03 04 05 06 07 08 09 10 11 12 13 14 15 16 17 18 19 20 21 22 23 24 25 26 27 28 29 30 31")
print("-" * 100)

try:
    while True:
        data = dev.read(32, timeout_ms=500)
        if data:
            hex_row = ' '.join(f'{b:02x}' for b in data)
            dec_row = ' '.join(f'{b:3d}' for b in data)
            print(f"hex: {hex_row}")
            print(f"dec: {dec_row}")
            print()
        time.sleep(0.1)
except KeyboardInterrupt:
    pass
finally:
    dev.close()
```

**What to look for:** A byte that is stable between readings, in the range 0–100, and that decreases over a long session or changes when you charge/discharge the mouse.

### Pattern 3: Feature Report Brute-Force Scan

**What:** Request every feature report ID 0x00–0x1F; log all non-error responses.
**When to use:** If input reports do not contain a battery byte, battery may come via a feature report instead.

```python
import hid

dev = hid.device()
dev.open_path(target['path'])
dev.set_nonblocking(0)

print("Scanning feature report IDs 0x00 – 0x1F ...")
for report_id in range(0x00, 0x20):
    try:
        resp = dev.get_feature_report(report_id, 33)  # 33 = 1 byte report ID + 32 bytes data
        if resp and any(b != 0 for b in resp[1:]):  # Skip all-zero responses
            hex_str = ' '.join(f'{b:02x}' for b in resp)
            print(f"  Report ID {report_id:#04x}: {hex_str}")
    except Exception:
        pass  # Report ID not supported — expected for most IDs

dev.close()
```

**Note on get_feature_report:** The first argument is the report ID (integer). The second is the max_length including the report ID byte itself, so pass `report_size + 1`. The returned list has the report ID in position 0, data in positions 1+.

### Pattern 4: Confirming the Battery Byte is Live

**What:** Take two readings separated by a real battery state change and confirm the identified byte changes.
**When to use:** After identifying a candidate byte via patterns 2 or 3.

Confirmation strategies (in order of practicality):

1. **Discharge method:** Run the mouse on battery for several hours. Record byte value at start and end. If the byte decreased, it is the battery indicator.
2. **Charge observation:** Plug mouse in to charge while monitoring. If the byte increases over time, confirmed.
3. **Comparison with known tool:** Run a known-good battery indicator (e.g., keychron-m3-linux on WSL or a Linux VM) simultaneously. Cross-check the value it reports against the byte you found.
4. **Value sanity:** A fresh-charge mouse should show a value of 100 (or close). A nearly-dead mouse should show a value near 0 or a low number. If the current value matches your physical knowledge of the battery state, that is a strong indicator.

### Anti-Patterns to Avoid

- **`hid.device().open(VID, PID)` without path filtering:** This opens the first matching interface, which may be the Generic Desktop TLC. That TLC is exclusive to mouhid.sys — you will get `OSError: open failed`. Always use `open_path()` with the path from `enumerate()`, filtered by usage_page.
- **Treating silence as zero battery:** The M3 dongle does not send reports when the mouse is idle. A timeout from `read()` means "no motion," not "battery 0%." Set `set_nonblocking(1)` and move the mouse to trigger reports.
- **Assuming a single report model:** The M3 may have different report IDs for different report types. Check that `data[0]` (the report ID byte, when using hidapi) corresponds to the same report ID across all readings.
- **Reading from the wrong PID:** PID 0xD037 is the wired/charging interface. PID 0xD034 is the 2.4GHz dongle. Connect via dongle for wireless battery readings.

---

## Don't Hand-Roll

| Problem | Don't Build | Use Instead | Why |
|---------|-------------|-------------|-----|
| HID device enumeration on Windows | Custom SetupDi + HidD P/Invoke | `hid.enumerate()` | Handles GUID lookup, interface detail allocation, error cases |
| Vendor TLC filtering | Manual Usage Page parsing from descriptor | Filter `d['usage_page'] >= 0xFF00` from enumerate results | `hid.enumerate()` returns usage_page per TLC already parsed |
| Feature report buffer sizing | Computing `FeatureReportByteLength` via HidP_GetCaps | Pass `max_length=33` (or 65 for larger reports) | Slightly oversized buffers are safe; hidapi clips to actual size |
| USB traffic capture | Custom WinUSB filter driver | USBPcap + Wireshark | Full USB capture stack, free, battle-tested for HID debugging |

**Key insight:** The `hid` PyPI package wraps all the Windows `SetupDi*` + `HidD_*` complexity in three lines of Python. The only thing that cannot be abstracted away is knowing which TLC to open — that requires usage_page filtering.

---

## Common Pitfalls

### Pitfall 1: mouhid.sys Exclusive Access

**What goes wrong:** `OSError: open failed` or `OSError: [Errno 13] Permission denied` when calling `open_path()` on the Generic Desktop TLC.
**Why it happens:** The Generic Desktop TLC (Usage Page 0x0001, Usage 0x0002 = Mouse) is opened exclusively by mouhid.sys the moment the dongle is inserted. User-mode processes cannot acquire it with read/write access.
**How to avoid:** Filter `usage_page` from `hid.enumerate()` results. Only open TLCs where `usage_page >= 0xFF00`. These vendor-specific TLCs are never claimed by system drivers.
**Warning signs:** The error occurs immediately on `open_path()`. Check the usage_page of the path you are opening.

### Pitfall 2: Mouse Idle = No Input Reports

**What goes wrong:** Script reads nothing for minutes; developer concludes the device doesn't work.
**Why it happens:** The M3 dongle only sends input reports when the mouse is moving or a button is pressed. A motionless mouse is silent.
**How to avoid:** Physically move the mouse during discovery reads. Use `timeout_ms=500` or `1000` in `dev.read()` — a timeout is not an error, it is normal.
**Warning signs:** `data = dev.read(32, timeout_ms=500)` returns an empty list `[]` — this is expected when the mouse has not moved. Do not interpret as device failure.

### Pitfall 3: Report ID Confusion in get_feature_report

**What goes wrong:** Feature report scan returns garbage or all-zero data.
**Why it happens:** hidapi's `get_feature_report(report_id, max_length)` sends a GET_REPORT control transfer with the given report ID. If the device does not implement that report ID, some devices return zeros rather than an error. Scanning 0x00–0x1F may yield 32 all-zero responses.
**How to avoid:** Filter out all-zero responses in the scan output. Look only for responses with at least one non-zero byte beyond the report ID position. Additionally, cross-reference with the report descriptor (captured via USBPcap) to know which report IDs are declared.
**Warning signs:** All scan results look identical (all zeros).

### Pitfall 4: Battery Value Range is Not 0–100

**What goes wrong:** Developer identifies a byte that changes, but the value doesn't match expected percentage.
**Why it happens:** Some devices report battery in discrete steps (e.g., 4 levels: 0/1/2/3 where 3=full, as the SteelSeries Arctis does). Others report voltage (millivolts). Others use 0–255.
**How to avoid:** At the time of discovery, note the actual byte value alongside the known physical battery state. If you know the mouse is "fully charged" and the byte shows 100, that confirms 0–100 percent. If it shows 4 or 255, document the raw scale and the conversion formula.
**Warning signs:** Byte value does not match intuition (e.g., shows 64 on a fully charged mouse — could be 0x64 = 100 decimal; check both hex and decimal interpretations).

### Pitfall 5: Multiple Vendor TLCs

**What goes wrong:** There are two vendor TLCs, and the wrong one is opened.
**Why it happens:** Some Keychron dongles expose more than one vendor-specific interface (e.g., one for battery, one for firmware updates or LED control).
**How to avoid:** Run the enumerate script on all vendor TLCs and try each one systematically. Log the Usage and Usage ID of each alongside the path.
**Warning signs:** `open_path()` succeeds but `read()` always returns the same static bytes regardless of mouse state.

---

## Code Examples

Verified patterns from official sources:

### Complete Discovery Script Skeleton

```python
#!/usr/bin/env python3
"""
BatterMouse Phase 1 — Protocol Discovery Script
Usage: python discover.py
Move the mouse to trigger HID input reports.
"""
import hid
import time
import sys

VID = 0x3434
PID_DONGLE = 0xD034

def enumerate_tlcs():
    """List all HID TLCs for the Keychron dongle."""
    devices = hid.enumerate(VID, PID_DONGLE)
    if not devices:
        print(f"ERROR: No device found with VID={VID:#06x} PID={PID_DONGLE:#06x}")
        print("Ensure the 2.4GHz dongle is connected and the mouse is on.")
        sys.exit(1)

    print(f"Found {len(devices)} TLC(s) for Keychron M3 dongle:\n")
    for i, d in enumerate(devices):
        marker = "<-- VENDOR (candidate)" if d['usage_page'] >= 0xFF00 else "    (system-claimed)"
        print(f"  [{i}] UsagePage={d['usage_page']:#06x}  Usage={d['usage']:#06x}  "
              f"Interface={d['interface_number']}  {marker}")
        print(f"      Path: {d['path']}")
    print()
    return devices

def read_input_reports(path: bytes, duration_s: int = 60):
    """Read input reports for `duration_s` seconds and print as hex+decimal."""
    dev = hid.device()
    dev.open_path(path)
    dev.set_nonblocking(1)

    print(f"Reading input reports for {duration_s}s. Move mouse to trigger reports.")
    print("Byte index: " + " ".join(f"{i:02d}" for i in range(32)))
    print("-" * 100)

    deadline = time.time() + duration_s
    seen_count = 0
    try:
        while time.time() < deadline:
            data = dev.read(32, timeout_ms=200)
            if data:
                seen_count += 1
                hex_row = " ".join(f"{b:02x}" for b in data)
                dec_row = " ".join(f"{b:3d}" for b in data)
                print(f"hex[{seen_count:04d}]: {hex_row}")
                print(f"dec[{seen_count:04d}]: {dec_row}")
                print()
            time.sleep(0.05)
    except KeyboardInterrupt:
        pass
    finally:
        dev.close()

    print(f"\nCapture complete. {seen_count} reports received.")

def scan_feature_reports(path: bytes):
    """Try all feature report IDs 0x00–0x1F and print non-zero responses."""
    dev = hid.device()
    dev.open_path(path)
    dev.set_nonblocking(0)

    print("Scanning feature report IDs 0x00 – 0x1F ...")
    found_any = False
    for report_id in range(0x00, 0x20):
        try:
            resp = dev.get_feature_report(report_id, 33)
            if resp and any(b != 0 for b in resp[1:]):
                hex_str = " ".join(f"{b:02x}" for b in resp)
                print(f"  Report ID {report_id:#04x}: {hex_str}")
                found_any = True
        except Exception:
            pass
    if not found_any:
        print("  No non-zero feature reports found.")
    dev.close()

if __name__ == "__main__":
    devices = enumerate_tlcs()

    # Select vendor-specific TLCs only
    vendor_tlcs = [d for d in devices if d['usage_page'] >= 0xFF00]
    if not vendor_tlcs:
        print("ERROR: No vendor-specific TLCs found. Cannot read battery without them.")
        sys.exit(1)

    for tlc in vendor_tlcs:
        print(f"\n=== Trying TLC: UsagePage={tlc['usage_page']:#06x} ===")
        print("--- Feature Report Scan ---")
        scan_feature_reports(tlc['path'])
        print("\n--- Input Report Stream (60s) ---")
        read_input_reports(tlc['path'], duration_s=60)
```

### get_feature_report API Note

```python
# Source: trezor.github.io/cython-hidapi/api.html
# Signature: get_feature_report(report_num: int, max_length: int) -> List[int]
#
# report_num: the report ID to request (integer, 0–255)
# max_length: total buffer size INCLUDING the report ID byte
#   For a 32-byte report: pass 33
# Returns: list of integers [report_id, byte1, byte2, ..., byteN]
#   resp[0] == report_num (echoed back)
#   resp[1:] == actual report data

resp = dev.get_feature_report(0x05, 33)
battery_candidate = resp[3]  # hypothetical — must be verified empirically
```

### hid.enumerate Return Fields

```python
# Source: trezor.github.io/cython-hidapi/api.html
# Each dict from hid.enumerate() contains:
{
    'path': b'/path/to/device',          # bytes — use with open_path()
    'vendor_id': 0x3434,                 # int
    'product_id': 0xD034,                # int
    'serial_number': '',                 # str
    'release_number': 256,               # int
    'manufacturer_string': 'Keychron',   # str
    'product_string': '...',             # str
    'usage_page': 0xFF00,                # int — KEY for TLC filtering
    'usage': 0x0001,                     # int
    'interface_number': 2,               # int
}
```

---

## Keychron M3 Community Findings

**Confidence: LOW** — These are inferred from the keychron-m3-linux project README and repository structure, not from direct source code reading. The `main.py` source was not retrievable during this research session.

| Finding | Source | Confidence |
|---------|--------|------------|
| M3 sends 32-byte input reports via 2.4GHz dongle | keychron-m3-linux README | MEDIUM |
| Battery percentage IS in those input reports (not feature reports) | keychron-m3-linux behavior described | MEDIUM |
| M3 is "not very talkative when idle" — reports trigger on motion | keychron-m3-linux hid-battery.md note | MEDIUM |
| Exact byte offset in `main.py` | Source file not fetched — MUST be read before implementation | LOW |
| Two PIDs monitored: 0xD034 (wireless) + 0xD037 (wired/charging) | keychron-m3-linux udev rules | MEDIUM |
| Value range is 0–100 (percentage, not discrete steps) | Inferred from "battery percentage" language | LOW |

**Critical action for Wave 0:** Read `https://github.com/byte-bandit/keychron-m3-linux/blob/main/main.py` in a browser or via `git clone` and extract the exact battery byte offset and any report ID constants before writing the discovery script. This may make the "brute force" discovery unnecessary.

---

## What to Document as Output

When discovery is complete, write the following to `FINDINGS.md` in the phase directory:

```markdown
# Protocol Discovery Findings — Keychron M3

**Date:** YYYY-MM-DD
**Device:** Keychron M3 2.4GHz Dongle
**VID:** 0x3434
**PID:** 0xD034

## HID Interface (TLC)
- **Usage Page:** 0x____  (e.g., 0xFF00)
- **Usage ID:** 0x____
- **Interface Number:** N
- **Report Type:** [ ] Feature Report  [x] Input Report  [ ] Both

## Battery Report
- **Report ID:** 0x__ (or "none" if single-report device)
- **Report Size:** 32 bytes
- **Battery Byte Offset:** N  (0-indexed; byte N in the report payload)
- **Value Range:** 0–100 (percent) OR describe raw scale and conversion formula
- **Value at full charge:** ___
- **Value at ~20% charge:** ___

## Confirmation
- **Method used to confirm byte is live:** [discharge over N hours / charge observation / comparison with known tool]
- **Readings:** Start={value} at {battery state}, End={value} at {battery state}

## C# Translation (for Phase 2)
```csharp
// In HidSharp stream read loop:
const int BatteryByteOffset = N;  // fill in
int batteryPercent = report[BatteryByteOffset];
```

## Notes
- Any anomalies or edge cases found
```

---

## State of the Art

| Old Approach | Current Approach | When Changed | Impact |
|--------------|------------------|--------------|--------|
| Raw P/Invoke to SetupDi + HidD functions | `hid` PyPI package (cython-hidapi wrapper) | ~2015 | Reduces 80 lines of C# P/Invoke to 3 lines of Python for discovery |
| Opening device with `hid.open(VID, PID)` | `hid.open_path(path)` from `enumerate()` | hidapi v0.9+ | Required on Windows when VID/PID maps to multiple TLCs with different usage pages |
| Balloon tip notifications | WinRT `ToastNotification` | Windows 10 | Native toast with Action Center persistence (Phase 2 concern, not Phase 1) |

---

## Open Questions

1. **Exact battery byte offset in keychron-m3-linux `main.py`**
   - What we know: The file exists, the project works, the offset is hardcoded in it
   - What's unclear: The exact value (could not fetch during research — GitHub rate-limited)
   - Recommendation: Read the source before starting Plan 1; if offset is found there, verification on Windows is still required but brute-force scan becomes optional

2. **Does the M3 send battery data on unsolicited input reports or feature reports?**
   - What we know: keychron-m3-linux uses input reports (reads in a loop); the hid-battery.md research confirms this is the observed pattern
   - What's unclear: Whether there is also a feature report that returns battery (some Keychron keyboards expose battery via a dedicated feature report ID)
   - Recommendation: Run both scan_feature_reports and read_input_reports in the discovery script; feature report scan is cheap

3. **Battery value encoding (0–100 vs. raw scale)**
   - What we know: "Battery percentage" language in keychron-m3-linux implies 0–100
   - What's unclear: Whether the value is a raw percentage or requires a conversion (e.g., Arctis uses 0–4 discrete levels)
   - Recommendation: Cross-check the discovered byte value against physical knowledge of current charge state at time of discovery

4. **Multiple vendor TLCs on the dongle**
   - What we know: The dongle exposes at least one Generic Desktop and one vendor-specific TLC
   - What's unclear: How many vendor TLCs exist and which one carries battery
   - Recommendation: The discovery script tries all vendor TLCs sequentially

---

## Validation Architecture

`nyquist_validation` is enabled in `.planning/config.json`.

### Test Framework

Phase 1 is a hardware reverse-engineering exercise, not a software-build phase. There is no automatable unit test for "did we find the correct byte offset" — that requires a physical Keychron M3 with a known battery state. However, the discovery script itself is testable, and the output (FINDINGS.md) can be validated structurally.

| Property | Value |
|----------|-------|
| Framework | Python (no formal framework — script output validation) |
| Config file | none |
| Quick run command | `python discover.py` (requires physical device) |
| Full suite command | `python discover.py` + manual FINDINGS.md review |

### Phase Requirements -> Test Map

| Req ID | Behavior | Test Type | Automated Command | File Exists? |
|--------|----------|-----------|-------------------|-------------|
| PRE-HID-01-a | Script runs and prints output without crashing | smoke | `python discover.py --enumerate-only` | Wave 0 |
| PRE-HID-01-b | Script identifies at least one vendor-specific TLC | smoke | `python discover.py --enumerate-only` (exit 0 = found) | Wave 0 |
| PRE-HID-01-c | Battery byte identified: FINDINGS.md exists and is populated | manual | human review of FINDINGS.md | Wave 0 |
| PRE-HID-01-d | Byte value changes between two readings at different battery levels | manual | read two captured logs, diff them at identified offset | manual only |

**Manual-only justification for PRE-HID-01-c and PRE-HID-01-d:** These require physical hardware state manipulation (charging/discharging a mouse) that cannot be automated in a script.

### Sampling Rate

- **Per task commit:** `python discover.py --enumerate-only` (verifies environment setup)
- **Per wave merge:** Full `python discover.py` run with human review of output
- **Phase gate:** FINDINGS.md completed and reviewed before Phase 2 planning begins

### Wave 0 Gaps

- [ ] `discover/discover.py` — the discovery script (the primary deliverable of Plan 1)
- [ ] `discover/FINDINGS.md` — output template to be filled in during execution
- [ ] Python `hid` package install: `pip install hid` — must be done in the execution environment

---

## Sources

### Primary (HIGH confidence)
- Microsoft Learn — Finding and Opening a HID Collection: https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/finding-and-opening-a-hid-collection
- Microsoft Learn — Obtaining HID Reports: https://learn.microsoft.com/en-us/windows-hardware/drivers/hid/obtaining-hid-reports
- cython-hidapi API reference (Trezor): https://trezor.github.io/cython-hidapi/api.html — enumerate, open_path, read, get_feature_report signatures
- hid-battery.md (project research file) — Windows HID TLC enumeration, mouhid.sys exclusivity, Python API patterns

### Secondary (MEDIUM confidence)
- keychron-m3-linux README (GitHub): https://github.com/byte-bandit/keychron-m3-linux — confirms 32-byte input report model for M3, dual PID monitoring
- aarol.dev — Creating a battery indicator app: https://aarol.dev/posts/arctis-hid/ — confirms USBPcap + Wireshark methodology, battery byte value encoding patterns
- MrAdrianPl/keychron_battery_widget: https://github.com/MrAdrianPl/keychron_battery_widget — confirms USBPcap approach for Keychron battery discovery

### Tertiary (LOW confidence — requires device verification)
- keychron-m3-linux `main.py` exact byte offset — NOT fetched; must be read from source before Plan 1 execution
- Battery value range (0–100 assumed) — inferred from language, not confirmed from source

---

## Metadata

**Confidence breakdown:**
- Windows HID TLC enumeration approach: HIGH — confirmed via Microsoft docs + hid-battery.md prior research
- Python `hid` library API: HIGH — confirmed via cython-hidapi official docs
- M3 uses 32-byte input reports: MEDIUM — confirmed from keychron-m3-linux README behavior description
- Exact byte offset in M3 input report: LOW — source not fetched; requires physical probing or reading main.py
- Battery value range (0–100): LOW — inferred, not confirmed from source

**Research date:** 2026-03-12
**Valid until:** 2026-06-12 (stable domain; HID protocol doesn't change; `hid` PyPI API is stable)
