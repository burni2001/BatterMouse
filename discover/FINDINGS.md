# Protocol Discovery Findings — Keychron M3

**Date:** 2026-03-12
**Device:** Keychron M3 2.4GHz Dongle
**VID:** 0x3434 (13364 decimal)
**PID (dongle):** 0xD034 (53296 decimal)
**PID (wired/charging):** 0xD037 (53299 decimal)

---

## HID Interface (TLC)

- **Usage Page:** 0x008C (140 decimal) — NOT 0xFF00 as initially assumed; this is a vendor-defined page below the 0xFF00 range
- **Usage ID:** TBD — to be confirmed empirically in Plan 02
- **Interface Number:** TBD — to be confirmed empirically in Plan 02
- **Report Type:** [ ] Feature Report  [x] Input Report  [ ] Both

> **Source:** keychron-m3-linux main.py (pre-confirmed)
> The reference implementation uses `usage_page = 140` (0x008C) to find the correct TLC.
> This contradicts the research assumption that vendor TLCs always use 0xFF__ pages.
> On Windows, mouhid.sys claims Usage Page 0x0001 (Generic Desktop) — 0x008C is NOT
> claimed by mouhid.sys and should be user-mode accessible.

---

## Battery Report

- **Report ID:** None declared (single-report device, or report ID is part of data pattern)
- **Report Size:** 32 bytes (confirmed from keychron-m3-linux source)
- **Battery Value Location:** Dynamic pattern match, NOT a fixed byte offset
- **Battery Byte Offset:** PATTERN-BASED — see Parse Algorithm below
- **Value Range:** 0–100 (percent) — confirmed from source: `wbprint(f"{percentage}%")`
- **Value at full charge:** TBD — to be measured empirically
- **Value at ~20% charge:** TBD — to be measured empirically

### Parse Algorithm (from keychron-m3-linux main.py)

```python
def parse_battery_data(data):
    for i in range(len(data) - 3):
        if (data[i] == 0x00 or data[i] == 0x01) and data[i+2] == 0x02 and data[i+3] == 0x02:
            return data[i+1]
    return None
```

**Pattern:** Scan the 32-byte report for a sequence where:
- `data[i]` is 0x00 or 0x01 (likely a report type/status byte)
- `data[i+2]` is 0x02
- `data[i+3]` is 0x02
- `data[i+1]` is the battery percentage (0–100)

**Key insight:** The battery value is NOT at a hardcoded offset — it is found by pattern matching
within the report. This approach handles variable report structures gracefully.

---

## Critical Deviation from Research Assumptions

The research (01-RESEARCH.md) assumed vendor TLCs use `usage_page >= 0xFF00`. This is **incorrect**
for the Keychron M3. The actual usage page is **0x008C** (140).

**Impact on discover.py:**
- Filter must use `d['usage_page'] == 0x008C` (or equivalently `== 140`)
- The `>= 0xFF00` filter in the RESEARCH.md skeleton will miss the correct TLC
- The `--enumerate` flag must check for UP 0x008C, not UP >= 0xFF00

**Impact on discover.py written for this plan:**
- The script will be written with BOTH filters: primary filter `usage_page == 0x008C`,
  fallback filter `usage_page >= 0xFF00` for any additional vendor TLCs
- This ensures the script finds the battery TLC while remaining flexible

---

## Confirmation

- **Method used to confirm byte is live:** Not yet confirmed — requires physical device testing in Plan 02
- **Readings:** TBD — to be recorded during Plan 02 execution

---

## C# Translation (for Phase 2)

```csharp
// In HidSharp stream read loop — DO NOT use fixed offset:
// Scan for the pattern instead:
static int? ParseBattery(byte[] report)
{
    for (int i = 0; i < report.Length - 3; i++)
    {
        if ((report[i] == 0x00 || report[i] == 0x01)
            && report[i + 2] == 0x02
            && report[i + 3] == 0x02)
        {
            return report[i + 1];
        }
    }
    return null;
}

// Device enumeration: filter usage_page == 0x008C (140)
// NOT usage_page >= 0xFF00
```

---

## Notes

1. **Usage page 0x008C is a "Battery System" page** per USB HID Usage Tables spec
   (Usage Page 0x85 = Battery System; 0x008C may be a different assignment — verify).
   Either way, it is NOT claimed by mouhid.sys on Windows.

2. **keychron-m3-linux vendor_id typo:** The source comment says `0x3414` but the actual
   value is `13364` decimal = `0x3434`. The comment is wrong; the decimal is right.

3. **Two PIDs monitored:** The reference implementation monitors both PID 0xD034 (dongle,
   wireless) and PID 0xD037 (wired/charging). The BatterMouse app should do the same to
   show accurate percentage while charging.

4. **No feature report scanning needed:** The reference implementation uses only input reports
   (32-byte reads in a loop). Feature report scanning can be attempted in Plan 02 but is
   likely unnecessary.
