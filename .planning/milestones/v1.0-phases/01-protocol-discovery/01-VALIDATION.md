---
phase: 1
slug: protocol-discovery
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-03-12
---

# Phase 1 — Validation Strategy

> Per-phase validation contract for feedback sampling during execution.

---

## Test Infrastructure

| Property | Value |
|----------|-------|
| **Framework** | Python `hid` + manual inspection (hardware-bound phase) |
| **Config file** | none |
| **Quick run command** | `python discover.py --enumerate` |
| **Full suite command** | `python discover.py` |
| **Estimated runtime** | ~5 seconds (enumeration only; full run requires physical device) |

---

## Sampling Rate

- **After every task commit:** Run `python discover.py --enumerate` (verifies device is found)
- **After every plan wave:** Run `python discover.py` (full discovery against physical device)
- **Before `/gsd:verify-work`:** FINDINGS.md populated with confirmed values
- **Max feedback latency:** 5 seconds (enumeration); manual inspection for byte offset confirmation

---

## Per-Task Verification Map

| Task ID | Plan | Wave | Requirement | Test Type | Automated Command | File Exists | Status |
|---------|------|------|-------------|-----------|-------------------|-------------|--------|
| 1-01-01 | 01 | 1 | HID-prereq | smoke | `python discover.py --enumerate` | ❌ W0 | ⬜ pending |
| 1-01-02 | 01 | 1 | HID-prereq | manual | visual inspection of hex dump | n/a | ⬜ pending |
| 1-01-03 | 01 | 2 | HID-prereq | manual | compare byte across two battery states | n/a | ⬜ pending |

*Status: ⬜ pending · ✅ green · ❌ red · ⚠️ flaky*

---

## Wave 0 Requirements

- [ ] `discover.py` — script skeleton with `--enumerate` flag that exits 0 if device found, 1 if not
- [ ] `requirements.txt` — pins `hid` PyPI package version

*Wave 0 is minimal: the phase is hardware-dependent; most verification is manual.*

---

## Manual-Only Verifications

| Behavior | Requirement | Why Manual | Test Instructions |
|----------|-------------|------------|-------------------|
| Battery byte identified | HID-prereq | Requires physical device at known charge state | Run script, note all 32 bytes; compare across two charge states; identify changing byte |
| Value confirmed as percentage | HID-prereq | Requires cross-referencing with OS battery indicator | Compare discovered byte value to Windows Bluetooth/HID battery indicator |
| FINDINGS.md accurate | HID-prereq | Human judgment on correctness | Review FINDINGS.md; confirm Usage Page, report ID, byte offset, value range are all populated |

---

## Validation Sign-Off

- [ ] All tasks have `<automated>` verify or Wave 0 dependencies
- [ ] Sampling continuity: no 3 consecutive tasks without automated verify
- [ ] Wave 0 covers all MISSING references
- [ ] No watch-mode flags
- [ ] Feedback latency < 30s
- [ ] `nyquist_compliant: true` set in frontmatter

**Approval:** pending
