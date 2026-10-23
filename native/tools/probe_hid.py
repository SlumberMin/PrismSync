# -*- coding: utf-8 -*-
"""Ground-truth probe for the three lighting HID devices (run before porting to C#).

Prints the exact bytes accepted by each device and how many bytes the Windows HID
driver expects for an output report, so the native port can replicate it 1:1.
"""
import ctypes
import ctypes.wintypes as wt
import time

import hid

hid_dll = ctypes.WinDLL("hid.dll", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32.dll", use_last_error=True)
kernel32.CreateFileW.restype = wt.HANDLE
kernel32.CreateFileW.argtypes = [wt.LPCWSTR, wt.DWORD, wt.DWORD, wt.LPVOID,
                                 wt.DWORD, wt.DWORD, wt.HANDLE]
kernel32.CloseHandle.argtypes = [wt.HANDLE]
kernel32.WriteFile.argtypes = [wt.HANDLE, wt.LPVOID, wt.DWORD,
                               ctypes.POINTER(wt.DWORD), wt.LPVOID]
kernel32.WriteFile.restype = wt.BOOL
hid_dll.HidD_SetOutputReport.argtypes = [wt.HANDLE, wt.LPVOID, wt.DWORD]
hid_dll.HidD_SetOutputReport.restype = wt.BOOL
hid_dll.HidD_GetAttributes.argtypes = [wt.HANDLE, wt.LPVOID]
hid_dll.HidD_GetAttributes.restype = wt.BOOL


class HIDD_ATTRIBUTES(ctypes.Structure):
    _fields_ = [("Size", wt.ULONG), ("VendorID", wt.USHORT),
                ("ProductID", wt.USHORT), ("VersionNumber", wt.USHORT)]


def find(vid, pid, usage_page):
    for d in hid.enumerate(vid, pid):
        if d.get("usage_page") == usage_page:
            return d
    return None


def path_text(d):
    p = d["path"]
    return p.decode("utf-8", "replace") if isinstance(p, bytes) else str(p)


def raw_handle(d):
    path = path_text(d)
    h = kernel32.CreateFileW(path, 0xC0000000, 0x3, None, 3, 0, None)
    if h in (wt.HANDLE(-1).value, 0xFFFFFFFFFFFFFFFF):
        raise ctypes.WinError(ctypes.get_last_error())
    return h


def probe_headset():
    print("\n=== HEADSET 0x3837:0x6008 usage_page 0xFF00 ===")
    d = find(0x3837, 0x6008, 0xFF00)
    print("enum:", {k: d[k] for k in ("usage_page", "usage", "interface_number",
                                     "product_string")})
    # -- hidapi stream, exactly like mchose.py
    dev = hid.device()
    dev.open_path(d["path"])
    wire = bytearray(64)
    wire[0] = 0xAA
    wire[1] = 0x27
    wire[5] = 0x0A
    wire[6] = 0x01
    wire[7] = 0x00
    wire[8] = 0x05
    wire[9] = 0x00
    wire[10] = 0x03
    wire[12], wire[13], wire[14] = 0x30, 0x00, 0xFF   # purple-ish
    buf = bytes([0x00]) + bytes(wire)
    print("hidapi write 65B ->", dev.write(buf))
    time.sleep(0.15)
    print("ack:", bytes(dev.read(64, 500)).hex())
    # status query 0x07
    q = bytearray(64)
    q[0] = 0xAA
    q[1] = 0x07
    print("query write ->", dev.write(bytes([0x00]) + bytes(q)))
    time.sleep(0.15)
    rep = bytes(dev.read(64, 500))
    print("query reply:", rep.hex())
    if len(rep) > 16:
        print("reply rgb @12..14:", rep[12], rep[13], rep[14])
    dev.close()

    # -- raw Win32: what length does the driver want?
    h = raw_handle(d)
    time.sleep(0.05)
    for n in (64, 65):
        payload = (bytes([0x00]) + bytes(wire) + b"\x00" * 8)[:n]
        buf2 = ctypes.create_string_buffer(payload, n)
        ok = hid_dll.HidD_SetOutputReport(h, buf2, n)
        print("HidD_SetOutputReport(%dB) -> %s err=%d"
              % (n, bool(ok), ctypes.get_last_error()))
        time.sleep(0.05)
    kernel32.CloseHandle(h)


def probe_keyboard():
    print("\n=== KEYBOARD 0x372E:0x103E usage_page 0xFF60 ===")
    d = find(0x372E, 0x103E, 0xFF60)
    print("enum:", {k: d[k] for k in ("usage_page", "usage", "interface_number",
                                     "product_string")})
    dev = hid.device()
    dev.open_path(d["path"])

    def pkt(cmd, param, payload=b"", total=1, idx=0):
        p = bytearray(64)
        p[0] = 0x09
        p[1] = cmd
        p[2] = param
        p[4] = total
        p[5] = idx
        if payload:
            p[6] = len(payload)
            p[7:7 + len(payload)] = payload
        p[63] = (0xFF - (sum(p[:63]) & 0xFF)) & 0xFF
        return bytes(p)

    dev.write(pkt(0x82, 0x01))
    r = bytes(dev.read(64, 1000))
    print("uid reply len=%d cmd=%02x payload=%s" % (len(r), r[1] if len(r) > 1 else -1,
                                                    r[7:13].hex()))
    print("hidapi write 64B ->", dev.write(pkt(0x01, 0x80, b"\x10")))
    time.sleep(0.02)
    dev.write(pkt(0x04, 0x02, b"\x13\x00"))
    dev.write(pkt(0x04, 0x04, b"\x64\x04"))
    time.sleep(0.05)
    # paint 67 keys green via cmd 0x08, one colour segment per packet
    keys = [1] + list(range(15, 76)) + [77, 98, 99, 102, 103]
    payload = bytes([0x00, 0xFF, 0x80, len(keys)]) + bytes(keys)
    print("paint 67 keys payload=%dB ->" % len(payload), dev.write(pkt(0x08, 0x01, payload)))
    time.sleep(0.03)
    print("commit ->", dev.write(pkt(0x01, 0x00)))
    dev.close()

    # raw handle: WriteFile vs SetOutputReport
    h = raw_handle(d)
    buf = ctypes.create_string_buffer(pkt(0x01, 0x80, b"\x10"), 64)
    written = wt.DWORD(0)
    ok = kernel32.WriteFile(h, buf, 64, ctypes.byref(written), None)
    print("WriteFile(64B) -> %s written=%d err=%d"
          % (bool(ok), written.value, ctypes.get_last_error()))
    buf65 = ctypes.create_string_buffer(b"\x00" + pkt(0x01, 0x80, b"\x10").ljust(64, b"\x00"), 65)
    ok = hid_dll.HidD_SetOutputReport(h, buf65, 65)
    print("HidD_SetOutputReport(65B) -> %s err=%d"
          % (bool(ok), ctypes.get_last_error()))
    kernel32.CloseHandle(h)


if __name__ == "__main__":
    probe_headset()
    probe_keyboard()
