# -*- coding: utf-8 -*-
"""
MCHOSE V9 Turbo+ headset lighting.

The headset appears twice on the bus:
    0x3837:0x6008   wch.cn  "V9 Turbo"          <- lighting control (this file)
    0x3837:0x600A   C-Media "MCHOSE V9 Turbo+"  <- 7.1 audio, do not touch

Protocol (verified against a USBPcap trace of the official client, then against
live ACKs from the device):

    wire, 64 bytes:  AA <cmd> ...
                     ^^ report id, always 0xAA

    07                status query, the reply carries "V9 Turbo"
    27                set lighting
                      [7] = mode   0 solid / 1 breathe / 2 star / 3 cycle / 4 wave
                      [9] = direction
                      [12..14] = R G B

Windows note (this is what made it look impossible for so long): HidD_SetOutputReport
needs a 65-byte buffer and DROPS its first byte, so the buffer must be
[0x00] + <the 64 bytes above>. Sending 63 or 64 bytes is rejected with err 87.

The headset renders lighting in firmware, so it cannot draw per-LED patterns.
It can, however, be put in solid mode and re-coloured every frame, which is
exactly what a single-zone device needs for real synchronisation.
"""
import ctypes
import ctypes.wintypes as wt
import time
from typing import Dict, List, Optional

import hid

from .base import RGB, Adapter

VID, PID, USAGE_PAGE = 0x3837, 0x6008, 0xFF00
REPORT_ID = 0xAA
WIRE_LEN = 64          # data stage after the report id
BUF_LEN = 65           # OutputReportByteLength
MIN_INTERVAL = 0.05   # room to follow at ~20 Hz, now that ACKs are drained

# firmware modes, decoded from the official web driver's effect table
MODE_SOLID, MODE_BREATHE, MODE_STAR, MODE_CYCLE, MODE_WAVE = 0, 1, 2, 3, 4
MODE_NAMES = {MODE_SOLID: "solid", MODE_BREATHE: "breathe",
              MODE_STAR: "star", MODE_CYCLE: "cycle", MODE_WAVE: "wave"}

hid_dll = ctypes.WinDLL("hid.dll", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32.dll", use_last_error=True)
kernel32.CreateFileW.restype = wt.HANDLE
kernel32.CreateFileW.argtypes = [wt.LPCWSTR, wt.DWORD, wt.DWORD, wt.LPVOID,
                                 wt.DWORD, wt.DWORD, wt.HANDLE]
kernel32.CloseHandle.argtypes = [wt.HANDLE]
hid_dll.HidD_SetOutputReport.argtypes = [wt.HANDLE, wt.LPVOID, wt.DWORD]
hid_dll.HidD_SetOutputReport.restype = wt.BOOL


class MchoseV9Adapter(Adapter):
    name = "headset"
    single_zone = True          # one global colour, like the laptop backlight
    # far left of the desk, so the colour band reaches the headset first
    span = (0.0, 0.13)

    def __init__(self, mode: int = MODE_SOLID, brightness: int = 3,
                 direction: int = 0):
        super().__init__()
        self.mode = mode
        self.brightness = brightness & 0xFF
        self.direction = direction & 0xFF
        self.set_layout({"all": 0.5})
        self._dev = None
        self._handle = None
        self._last = 0.0
        self._last_rgb: Optional[RGB] = None
        self.dropped = 0
        self.failed = 0
        self.sent = 0

    # -- construction ---------------------------------------------------
    @staticmethod
    def frame(cmd: int = 0x27, mode: int = 0, rgb: RGB = (0, 0, 0),
              brightness: int = 3, direction: int = 0) -> bytes:
        p = bytearray(WIRE_LEN)
        p[0] = REPORT_ID
        p[1] = cmd
        if cmd == 0x27:
            p[5] = 0x0A
            p[6] = 0x01
            p[7] = mode & 0xFF
            p[8] = 0x05
            p[9] = direction & 0xFF
            p[10] = brightness & 0xFF
            p[12], p[13], p[14] = (rgb[0] & 0xFF, rgb[1] & 0xFF, rgb[2] & 0xFF)
        return bytes(p)

    def _buf(self, data_stage: bytes) -> bytes:
        b = bytes([0x00]) + data_stage
        return b[:BUF_LEN].ljust(BUF_LEN, b"\x00")

    # -- lifecycle ------------------------------------------------------
    def open(self) -> None:
        raw_path = None
        for d in hid.enumerate(VID, PID):
            if d.get("usage_page") == USAGE_PAGE:
                raw_path = d["path"]          # hidapi wants the original bytes
                break
        if not raw_path:
            raise RuntimeError("MCHOSE 0x3837:0x6008 (usage_page 0xFF00) not found")

        # A single hidapi device object for both directions. Writing through a
        # second handle (HidD_SetOutputReport on a raw handle) puts the device's
        # ACKs in THAT handle's queue, where nobody reads them; the queue fills
        # and the firmware stops accepting frames. That is exactly why a one-off
        # write 2.5 s apart worked while a stream of them did nothing.
        self._dev = hid.device()
        self._dev.open_path(raw_path)
        self._handle = None
        print("[headset] opened 0x6008, mode=%s" % MODE_NAMES.get(self.mode, self.mode))

    def close(self) -> None:
        if self._handle:
            kernel32.CloseHandle(self._handle)
            self._handle = None
        if self._dev:
            try:
                self._dev.close()
            finally:
                self._dev = None

    # -- control --------------------------------------------------------
    def query(self) -> Optional[bytes]:
        """Status query; the reply contains the product name."""
        return self._send(self.frame(cmd=0x07), wait=0.35)

    def set_mode(self, mode: int, rgb: RGB = (255, 255, 255)) -> Optional[bytes]:
        self.mode = mode
        self._last_rgb = None
        return self._send(self.frame(0x27, mode, rgb, self.brightness,
                                     self.direction))

    def _send(self, data_stage: bytes, wait: float = 0.12) -> Optional[bytes]:
        if self._dev is None:
            return None
        for _ in range(4):                     # drop stale ACKs first
            if not self._dev.read(BUF_LEN - 1, 1):
                break
        self._dev.write(self._buf(data_stage))
        time.sleep(wait)
        rep = self._dev.read(BUF_LEN - 1, 500)
        return bytes(rep) if rep else None

    def apply(self, frame) -> None:
        """One colour for the whole headset, pushed at a sane rate."""
        if self._dev is None and self._handle is None:
            return
        color = None
        for key in self.zones():
            color = frame.get(key)
            if color:
                break
        if color is None:
            return

        now = time.perf_counter()
        if now - self._last < MIN_INTERVAL:
            self.dropped += 1
            return
        # skip a write when nothing changed (firmware already holds this colour)
        if self._last_rgb == tuple(color):
            return
        self._last = now
        self._last_rgb = tuple(color)
        self.sent += 1

        buf = self._buf(self.frame(0x27, self.mode, color, self.brightness,
                                   self.direction))
        ok = False
        if self._dev:
            ok = self._dev.write(buf) >= 0
        if not ok:
            self.failed += 1
            self._last_rgb = None      # retry this colour next frame

        # The firmware answers every output report with an input report. Draining
        # on the same handle keeps its queue empty so writes keep being accepted.
        if self._dev:
            for _ in range(8):
                if not self._dev.read(BUF_LEN - 1, 1):
                    break
