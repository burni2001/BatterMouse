#!/usr/bin/env python3
"""
BatterMouse Phase 1 -- Protocol Discovery Script

Usage:
  python discover.py --enumerate    # Quick check: is device present? (exits 0=found, 1=not found)
  python discover.py                # Full discovery: enumerate + feature scan + 60s input stream

Device: Keychron M3 2.4GHz Dongle
  VID: 0x3434 (13364)
  PID dongle (wireless): 0xD034 (53296)
  PID device (wired/charging): 0xD037 (53299)

Key finding from keychron-m3-linux reference implementation:
  The battery TLC uses usage_page=0x008C (140), NOT 0xFF00 as the generic vendor-page
  assumption would suggest. The battery value is found by pattern-matching within the
  32-byte input report, not at a fixed byte offset.

Anti-patterns this script avoids (from Phase 1 research):
  - Do NOT use hid.device().open(VID, PID) -- always use open_path() with path from enumerate()
  - Do NOT open Usage Page 0x0001 (Generic Desktop) -- claimed exclusively by mouhid.sys
  - Do NOT interpret an empty read as an error -- use set_nonblocking(1); empty list = mouse idle
"""
import hid
import time
import sys
import argparse

VID = 0x3434
PID_DONGLE = 0xD034   # 2.4GHz wireless dongle
PID_DEVICE = 0xD037   # wired / charging connection

# Usage page confirmed from keychron-m3-linux main.py reference implementation
BATTERY_USAGE_PAGE = 0x008C   # 140 decimal -- "Battery System" page
VENDOR_USAGE_PAGE_MIN = 0xFF00  # Fallback: generic vendor-specific pages


def _is_target_tlc(d: dict) -> bool:
    """Return True if this TLC is a candidate for battery data."""
    # Primary: usage_page=0x008C as seen in the reference implementation
    if d['usage_page'] == BATTERY_USAGE_PAGE:
        return True
    # Fallback: standard vendor-specific range (>= 0xFF00)
    if d['usage_page'] >= VENDOR_USAGE_PAGE_MIN:
        return True
    return False


def enumerate_tlcs(pid: int = PID_DONGLE) -> list:
    """
    List all HID TLCs for the specified Keychron PID.

    Returns the full list of device dicts from hid.enumerate().
    Prints a formatted table of all TLCs found.
    Does NOT exit -- callers decide what to do with the result.
    """
    devices = hid.enumerate(VID, pid)
    if not devices:
        return []

    print(f"Found {len(devices)} TLC(s) for VID={VID:#06x} PID={pid:#06x}:\n")
    for i, d in enumerate(devices):
        if d['usage_page'] == BATTERY_USAGE_PAGE:
            marker = "<-- BATTERY TLC (usage_page=0x008C, confirmed pattern)"
        elif d['usage_page'] >= VENDOR_USAGE_PAGE_MIN:
            marker = "<-- VENDOR (candidate, usage_page>=0xFF00)"
        else:
            marker = "    (system-claimed, skip)"
        print(f"  [{i}] UsagePage={d['usage_page']:#06x}  Usage={d['usage']:#06x}  "
              f"Interface={d['interface_number']}  {marker}")
        print(f"      Path: {d['path']}")
    print()
    return devices


def scan_feature_reports(path: bytes) -> None:
    """
    Try all feature report IDs 0x00-0x1F and print non-zero responses.

    Used to check if battery data is also available via feature reports (likely not
    for the M3 based on reference implementation, but cheap to verify).
    """
    dev = hid.device()
    dev.open_path(path)
    dev.set_nonblocking(0)

    print("Scanning feature report IDs 0x00 - 0x1F ...")
    found_any = False
    for report_id in range(0x00, 0x20):
        try:
            resp = dev.get_feature_report(report_id, 33)  # 33 = 1 report ID byte + 32 data bytes
            if resp and any(b != 0 for b in resp[1:]):    # skip all-zero responses
                hex_str = " ".join(f"{b:02x}" for b in resp)
                print(f"  Report ID {report_id:#04x}: {hex_str}")
                found_any = True
        except Exception:
            pass  # report ID not supported -- expected for most IDs
    if not found_any:
        print("  No non-zero feature reports found.")
    dev.close()


def read_input_reports(path: bytes, duration_s: int = 60) -> None:
    """
    Read input reports for duration_s seconds, printing each as hex and decimal.

    Move the mouse to trigger reports -- the M3 is silent when idle.
    Scans each report with parse_battery_data() and highlights the battery byte if found.
    """
    dev = hid.device()
    dev.open_path(path)
    dev.set_nonblocking(1)  # empty read = mouse idle, NOT an error

    print(f"Reading input reports for {duration_s}s. Move mouse to trigger reports.")
    print("Byte index: " + " ".join(f"{i:02d}" for i in range(32)))
    print("-" * 110)

    deadline = time.time() + duration_s
    seen_count = 0
    try:
        while time.time() < deadline:
            data = dev.read(32, timeout_ms=200)
            if data:
                seen_count += 1
                hex_row = " ".join(f"{b:02x}" for b in data)
                dec_row = " ".join(f"{b:3d}" for b in data)
                battery = parse_battery_data(data)
                batt_note = f"  <-- BATTERY={battery}%" if battery is not None else ""
                print(f"hex[{seen_count:04d}]: {hex_row}{batt_note}")
                print(f"dec[{seen_count:04d}]: {dec_row}")
                print()
            time.sleep(0.05)
    except KeyboardInterrupt:
        pass
    finally:
        dev.close()

    print(f"\nCapture complete. {seen_count} reports received.")


def parse_battery_data(data: list) -> int | None:
    """
    Extract battery percentage from a 32-byte HID input report.

    Algorithm from keychron-m3-linux main.py:
      Scan for sequence: [0x00 or 0x01, <battery_pct>, 0x02, 0x02]
      Returns data[i+1] (the percentage) when the surrounding pattern matches.
      Returns None if the pattern is not found in this report.
    """
    for i in range(len(data) - 3):
        if (data[i] == 0x00 or data[i] == 0x01) and data[i + 2] == 0x02 and data[i + 3] == 0x02:
            return data[i + 1]
    return None


def _run_enumerate_check() -> None:
    """
    --enumerate mode: quick check that device is present and has battery TLC.

    Exit codes:
      0 = at least one battery/vendor TLC found
      1 = device not found, or device found but no suitable TLC
    """
    # Try dongle PID first, then device/charging PID
    for pid, label in [(PID_DONGLE, "dongle (wireless)"), (PID_DEVICE, "device (wired/charging)")]:
        devices = hid.enumerate(VID, pid)
        if devices:
            candidate_tlcs = [d for d in devices if _is_target_tlc(d)]
            if candidate_tlcs:
                print(f"OK: {len(candidate_tlcs)} vendor TLC(s) found for VID={VID:#06x} PID={pid:#06x} ({label})")
                for d in candidate_tlcs:
                    marker = "BATTERY_TLC" if d['usage_page'] == BATTERY_USAGE_PAGE else "VENDOR"
                    print(f"  [{marker}] UsagePage={d['usage_page']:#06x}  Usage={d['usage']:#06x}  "
                          f"Interface={d['interface_number']}")
                    print(f"  Path: {d['path']}")
                sys.exit(0)
            else:
                print(f"WARNING: device found (VID={VID:#06x} PID={pid:#06x}) but no vendor TLC "
                      f"-- check usage_page filtering")
                tlc_pages = [f"{d['usage_page']:#06x}" for d in devices]
                print(f"  TLCs present: {tlc_pages}")
                sys.exit(1)

    # Neither PID found
    print(f"ERROR: Keychron dongle not found (VID={VID:#06x} PID={PID_DONGLE:#06x})")
    print("Ensure the 2.4GHz USB dongle is plugged in and the mouse is switched on.")
    sys.exit(1)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(
        description="BatterMouse Phase 1 — Keychron M3 HID discovery script",
        formatter_class=argparse.RawDescriptionHelpFormatter,
        epilog=(
            "Examples:\n"
            "  python discover.py --enumerate    # Quick device check\n"
            "  python discover.py                # Full 60s discovery session\n"
        ),
    )
    parser.add_argument(
        "--enumerate",
        action="store_true",
        help="Quick check: is the device present? Exits 0=found, 1=not found.",
    )
    args = parser.parse_args()

    if args.enumerate:
        _run_enumerate_check()
        # _run_enumerate_check() always calls sys.exit(), so we never reach here

    # --- Full discovery mode ---
    print("=== BatterMouse Phase 1: Full Discovery ===\n")

    # Try both PIDs
    all_found = {}
    for pid, label in [(PID_DONGLE, "dongle (wireless)"), (PID_DEVICE, "device (wired/charging)")]:
        devices = enumerate_tlcs(pid)
        vendor_tlcs = [d for d in devices if _is_target_tlc(d)]
        if vendor_tlcs:
            all_found[pid] = (label, vendor_tlcs)

    if not all_found:
        print(f"ERROR: No Keychron device found (VID={VID:#06x}).")
        print("Ensure dongle is connected and mouse is on.")
        sys.exit(1)

    # Run discovery on the first PID that returned results (prefer dongle)
    pid = PID_DONGLE if PID_DONGLE in all_found else list(all_found.keys())[0]
    label, vendor_tlcs = all_found[pid]
    print(f"Running discovery against PID={pid:#06x} ({label})\n")

    for tlc in vendor_tlcs:
        up_label = "BATTERY_TLC" if tlc['usage_page'] == BATTERY_USAGE_PAGE else "VENDOR"
        print(f"\n=== Trying TLC: [{up_label}] UsagePage={tlc['usage_page']:#06x} ===")
        print("--- Feature Report Scan ---")
        try:
            scan_feature_reports(tlc['path'])
        except OSError as e:
            print(f"  Could not open for feature scan: {e}")

        print("\n--- Input Report Stream (60s) ---")
        try:
            read_input_reports(tlc['path'], duration_s=60)
        except OSError as e:
            print(f"  Could not open for input read: {e}")
