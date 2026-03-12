# Protocol Discovery Findings — Keychron M3 / Keychron Link

**Date:** 2026-03-13 (empirically confirmed)
**Device:** Keychron M3 2.4GHz Dongle ("Keychron Link")
**VID:** 0x3434 (13364 decimal)
**PID (dongle):** 0xD030 (53296 decimal) — "Keychron Link" universal receiver
**PID (wired/charging):** 0xD037 (53299 decimal) — unconfirmed empirically, retained from reference

> **Note:** The research assumed PID 0xD034 (from keychron-m3-linux reference). The actual dongle
> on this system is PID 0xD030 ("Keychron Link"), a universal multi-device Keychron receiver.
> The battery protocol is otherwise consistent with the reference.

---

## HID Interface (TLC)

- **Usage Page:** 0x008C (140 decimal) — Battery System page (confirmed empirically)
- **Usage ID:** 0x0001 (confirmed from enumeration)
- **Interface Number:** 1 (MI_01, Col01)
- **Report Type:** [ ] Feature Report  [x] Input Report  [x] Both
  - Input reports are the primary source (continuous, reliable)
  - Feature report 0x51 also returns non-zero data (see below)

> **Confirmed:** usage_page=0x008C is NOT claimed by mouhid.sys on Windows.
> The device was opened and read successfully with no PermissionError.

---

## Battery Report

- **Report ID:** 0x54 (byte 0 of every input report — confirmed from session data)
- **Report Size:** 32 bytes
- **Battery Byte Offset:** **5** (fixed, empirically confirmed)
- **Value Range:** 0–100 (percent)
- **Value at test time:** 96% (`0x60`) — confirmed with cable connected after charging
  - Earlier reading of 70% (`0x46`) was accurate (mouse battery was at 70% before charging)
  - Both readings from TLC [0] path `9&3b58df5`; TLC [1] path `9&248e17d0` never responds
    (likely a ghost entry for a second paired device that is off/unpaired)
- **Confirmation method:** Byte 5 stable across all consecutive input reports while bytes 3
  and 4 vary. Value changes with charge state (70% → 96% after charging). No other byte
  in the 0–100 range is stable.

### Confirmed Report Structure

```
Offset  Value (example)  Notes
------  ---------------  -----
  0     0x54             Report ID (stable)
  1     0xe2 = 226       Unknown — stable
  2     0x01             Unknown — stable
  3     0x01 / 0x02      Likely charging state (1=discharging, 2=charging)
  4     0x00–0x02        Varies — likely event counter or button state
  5     0x46 = 70        *** BATTERY PERCENTAGE *** (stable, 0–100 range)
  6     0x04             Unknown — stable
  7     0x02             Unknown — stable
  8–31  0x00             Unused / padding
```

### Raw session data (from session-1.log)

```
hex[0001]: 54 e2 01 01 01 46 04 02 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
hex[0002]: 54 e2 01 01 01 46 04 02 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
hex[0004]: 54 e2 01 01 02 46 04 02 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
hex[0006]: 54 e2 01 02 00 46 04 02 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
```

---

## Feature Report 0x51

Feature report ID 0x51 returns non-zero data on the Battery TLC:

```
52 62 09 00 02 00 4f 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00 00
```

Byte 6 = 0x4f = 79. This value differs from the input report battery (70%), so it is likely
not a simple battery percentage. Could be raw voltage, charge capacity, or a different metric.
**Phase 2 should use input reports (offset 5), not feature reports.**

---

## Critical Deviation from Reference Implementation

**The keychron-m3-linux pattern scan does NOT match this dongle's report format.**

Reference pattern: `[0x00|0x01, pct, 0x02, 0x02]`
Actual report:     `54 e2 01 01 01 46 04 02 00...`

The pattern would look for byte 3 or 4 as `0x00|0x01`, then check `data[i+2]==0x02` and
`data[i+3]==0x02`. This doesn't match because byte 5 (the battery) is followed by 0x04, not 0x02.

**Conclusion:** Use **fixed offset 5** for the "Keychron Link" dongle (PID=0xD030).
The pattern-based approach was valid for the M3's dedicated dongle (PID=0xD034) but not here.

---

## How to Trigger Battery Input Reports

The battery TLC (0x008C) does NOT respond to mouse movement. Reports are emitted:
- When USB charging cable is connected/disconnected (reliably triggers immediate reports)
- Periodically in wireless mode — but interval is long (>20s, possibly 1–2 minutes)
- On significant battery state change

**For the C# app:** use a persistent background thread with an indefinite blocking read (no
timeout). The first report will arrive within a minute of startup. There is no need to poll.

**For the diagnostic --read-battery script:** works reliably with USB cable connected.
In wireless-only mode, may time out before the device sends its next periodic report.

---

## C# Translation (for Phase 2)

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
// Reading: blocking reads; battery reports arrive periodically (~every few seconds)
```

---

## Notes

1. **Two PIDs to monitor:** 0xD030 (wireless, confirmed) and 0xD037 (wired/charging, from reference).
   Phase 2 should enumerate both so percentage is accurate while charging.

2. **"Keychron Link" is a universal receiver** — it appears as 16 TLCs covering mouse, keyboard,
   and battery interfaces. The Battery TLC is consistently at Interface 1 (MI_01, Col01).

3. **Usage page 0x008C is "Battery System"** per USB HID Usage Tables. Not claimed by
   mouhid.sys or any Windows system driver on Windows 11.

4. **keychron-m3-linux vendor_id typo:** The source comment says `0x3414` but the correct value
   is 0x3434 (confirmed by `hid.enumerate()` output showing `0x3434`).
