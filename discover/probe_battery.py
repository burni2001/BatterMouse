import hid, sys

VID = 0x3434
PIDS = [
    (0xD030, "Keychron Link (wireless dongle)"),
    (0xD037, "Wired/charging"),
]
BP = 0x008C

for pid, pid_label in PIDS:
    devs = [d for d in hid.enumerate(VID, pid) if d['usage_page'] == BP]
    if not devs:
        print(f"PID {pid:#06x} ({pid_label}): not found")
        continue

    print(f"\nPID {pid:#06x} ({pid_label}): {len(devs)} Battery TLC(s)")
    for i, d in enumerate(devs):
        print(f"  [{i}] {d['path']}")
        dev = hid.device()
        try:
            dev.open_path(d['path'])
            dev.set_nonblocking(0)
            data = dev.read(32, timeout_ms=20000)
            dev.close()
            if data:
                print(f"      hex: {' '.join(f'{b:02x}' for b in data)}")
                print(f"      dec: {' '.join(f'{b:3d}' for b in data)}")
            else:
                print("      (no data within 20s)")
        except Exception as e:
            print(f"      ERROR: {e}")
