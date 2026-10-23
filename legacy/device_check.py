# -*- coding: utf-8 -*-
"""
Device self-check: verifies that all three targets are present and reachable.

    python device_check.py
"""
import subprocess
import sys

try:
    import hid
except ImportError:
    hid = None

TARGETS = [
    ("HERO 68 HE", 0x372E, 0x103E, 0xFF60),
    ("MCHOSE V9 Turbo+", 0x3837, 0x600A, None),
]


def check_hid(label, vid, pid, usage_page):
    if hid is None:
        print("[FAIL] %-20s hidapi not installed" % label)
        return False
    found = []
    for d in hid.enumerate(vid, pid):
        if usage_page is not None and d.get("usage_page") != usage_page:
            continue
        found.append(d)
    if not found:
        print("[FAIL] %-20s not enumerated (VID %04X PID %04X)" % (label, vid, pid))
        return False
    ok = False
    for d in found:
        try:
            dev = hid.device()
            dev.open_path(d["path"])
            dev.close()
            ok = True
        except Exception as exc:
            print("[WARN] %-20s found but cannot open: %s" % (label, exc))
    if ok:
        print("[ OK ] %-20s enumerated, interface opens fine (%d collection(s))"
              % (label, len(found)))
    return ok


def check_clevo():
    # "拒绝访问 / Access denied" means the class EXISTS but this shell is not
    # elevated - that is a different situation from "class missing", and calling
    # it FAIL sent the user off to re-register a working driver.
    cmd = ('$o = Get-WmiObject -Query "select * from CLEVO_GET" -Namespace "root\\WMI" '
           '-ErrorAction Stop; if ($o) { "FOUND " + $o.InstanceName }')
    try:
        r = subprocess.run(["powershell", "-NoProfile", "-Command", cmd],
                           capture_output=True, text=True, timeout=30)
        out = ((r.stdout or "") + (r.stderr or "")).strip()
        if out.startswith("FOUND"):
            print("[ OK ] %-20s %s" % ("Laptop (Clevo WMI)", out))
            return True
        low = out.lower()
        if "拒绝访问" in out or "access" in low or "denied" in low:
            print("[ OK ] %-20s registered, needs administrator to call SetKBLED"
                  % "Laptop (Clevo WMI)")
            return True
        print("[FAIL] %-20s CLEVO_GET class missing - run register_clevo.ps1 as admin"
              % "Laptop (Clevo WMI)")
        return False
    except Exception as exc:
        print("[FAIL] %-20s %s" % ("Laptop (Clevo WMI)", exc))
        return False


def main():
    print("=" * 64)
    print("RGB sync device check")
    print("=" * 64)
    results = [check_clevo()]
    for label, vid, pid, up in TARGETS:
        results.append(check_hid(label, vid, pid, up))
    print("-" * 64)
    print("ready: %d / %d" % (sum(1 for r in results if r), len(results)))
    return 0 if all(results) else 1


sys.exit(main())
