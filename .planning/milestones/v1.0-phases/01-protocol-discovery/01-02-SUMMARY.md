---
phase: 01-protocol-discovery
plan: 02
status: complete
completed: 2026-03-13
duration: ~2 sessions
---

# Plan 01-02 Summary — Live Discovery Session

## Primary Deliverable

**Battery byte offset: 5 (fixed)**

This is the single most important fact from Phase 1. Phase 2 reads `report[5]` as the
battery percentage (0–100 scale, integer percent).

---

## Confirmed Protocol Values

| Field | Value | Source |
|-------|-------|--------|
| VID | 0x3434 | hid.enumerate() |
| PID (wireless dongle) | **0xD030** ("Keychron Link") | empirical — NOT 0xD034 as research assumed |
| PID (wired/charging) | 0xD037 | reference (not found during testing) |
| Usage Page (Battery TLC) | 0x008C | empirical |
| Usage ID | 0x0001 | empirical |
| Interface Number | 1 (MI_01, Col01) | empirical |
| Report ID (byte 0) | 0x54 | empirical |
| Battery Byte Offset | **5** | empirical |
| Value range | 0–100 (integer %) | empirical (saw 70%, 96%) |

---

## Report Structure (empirically confirmed)

```
Offset  Example  Notes
   0    0x54     Report ID (stable)
   1    0xe2     Unknown — stable
   2    0x01     Unknown — stable
   3    0x01/02  Likely charging state: 1=discharging, 2=charging
   4    0x00–02  Likely event counter
   5    0x60     *** BATTERY PERCENTAGE *** (was 0x46=70% before charging, 0x60=96% after)
   6    0x04     Unknown — stable
   7    0x02     Unknown — stable
  8–31  0x00     Unused / padding
```

---

## Key Deviations from Research Assumptions

1. **PID is 0xD030, not 0xD034** — "Keychron Link" universal receiver, not the M3-specific dongle
2. **Fixed byte offset (5), not pattern-based** — the keychron-m3-linux pattern `[0x00|0x01, pct, 0x02, 0x02]` does not match this receiver's report format
3. **Battery TLC reports are infrequent wirelessly** — arrive periodically (>20s interval, possibly 1–2 min); triggered immediately when USB cable connects/disconnects. The C# app uses a persistent blocking read thread, so this is not a problem.
4. **Two Battery TLCs enumerated** — one (path `9&3b58df5`) is the active mouse TLC; the other (`9&248e17d0`) never responds (ghost entry for an unpaired/off second device)

---

## Artifacts Produced

- `discover/session-1.log` — raw HID output from the full discovery run
- `discover/FINDINGS.md` — complete protocol documentation with C# translation
- `discover/discover.py` — updated with `--read-battery` mode, PID=0xD030, byte offset 5
- `discover/probe_battery.py` — diagnostic script for reading both Battery TLCs

---

## C# Handoff (verbatim from FINDINGS.md)

```csharp
// HID device selection: filter by usage_page == 0x008C (Battery System page)
// VID = 0x3434, PID = 0xD030 (Keychron Link dongle)

const int BatteryByteOffset = 5;  // confirmed empirically 2026-03-13

// In HidSharp stream read loop:
static int? ParseBattery(byte[] report)
{
    if (report.Length > BatteryByteOffset)
        return report[BatteryByteOffset];
    return null;
}

// Device enumeration: filter usage_page == 0x008C (140)
// Interface: MI_01 (interface_number == 1)
// Reading: persistent background thread with blocking reads; no timeout needed
// Multiple Battery TLCs: open first that responds; others may be ghost entries
```

---

## Phase 1 Complete

Phase 2 has everything it needs:
- Which HID device to open (VID=0x3434, PID=0xD030, usage_page=0x008C)
- Which byte to read (offset 5)
- What the value means (0–100 integer percent)
- How to structure the reader (persistent blocking thread)
- When to notify (value ≤ 20)
