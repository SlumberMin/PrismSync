# -*- coding: utf-8 -*-
"""
Clevo WMI adapter - the laptop keyboard (Colorful X15 AT23 = Clevo NP50SNE).

The keyboard is driven through the Clevo WMI class CLEVO_GET, method
SetKBLED(uint32). Python WMI bindings (pymi / pywin32) cannot be installed in
this environment, so a small PowerShell bridge process is kept alive and fed
hex values on stdin instead.

Protocol values (reverse engineered from Clevo Control Center / clevomanager):
    light on        0xE007F007
    light off       0xE0003000
    part left       0xF0000000
    part mid        0xF1000000
    part right      0xF2000000
    part lightbar   0xF3000000
    brightness      0xF4000000 | level   (off 0 / low 63 / mid 127 / high 255)
    color packing   (B << 16) + (R << 8) + G   ->  0x00BBRRGG

Latency
-------
A single SetKBLED round trip through WMI costs milliseconds, far more than a
frame at 30 fps. Blocking the effect engine on it would drag every device down
to the WMI rate, so the adapter keeps a *single slot* holding the newest colour
and a background thread drains it. Intermediate frames are dropped on purpose:
the eye cannot see a 5 ms colour anyway, and the other devices keep their frame
rate.
"""
import os
import subprocess
import threading
import time
from typing import List, Optional

from .base import Adapter

PARTS = {
    "left": 0xF0000000,
    "mid": 0xF1000000,
    "right": 0xF2000000,
    "lightbar": 0xF3000000,
}
BRIGHTNESS = 0xF4000000
LEVELS = {"off": 0, "low": 63, "mid": 127, "high": 255}

BRIDGE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "clevo_bridge.ps1")


def pack_color(r: int, g: int, b: int) -> int:
    """Clevo packs a color as (B << 16) + (R << 8) + G."""
    return (b << 16) + (r << 8) + g


class ClevoAdapter(Adapter):
    """Single-zone control of the laptop keyboard backlight."""

    name = "laptop"
    # one global colour for the whole backlight, so the engine hands us the
    # effect's headline colour instead of a spatially sampled one
    single_zone = True
    # desk layout: headset far left, laptop in the middle, keyboard on the right
    span = (0.21, 0.53)

    def __init__(self, parts=("left",), brightness: str = "high",
                 ps: str = "powershell"):
        super().__init__()
        self.parts: List[str] = list(parts)
        self.brightness = brightness
        self.ps = ps
        self._proc = None
        self._thread: Optional[threading.Thread] = None
        self._stop = threading.Event()
        self._lock = threading.Lock()
        self._pending: Optional[int] = None
        self._shown: Optional[int] = None
        self.sent = 0            # WMI writes actually performed
        self.dropped = 0         # frames superseded before they were sent
        # The Colorful X15 AT23 (Clevo NP50SNE) backlight is one global colour:
        # the left/mid/right part values are accepted, but every key lights up
        # together, so the laptop cannot show a spatial gradient on its own.
        # It therefore exposes a single zone placed in the middle of the shared
        # axis and follows the centre of the wave - visually the same colour
        # the external keyboard shows in its middle.
        self.set_layout({p: 0.5 for p in self.parts})

    # -- bridge process --------------------------------------------------
    def open(self) -> None:
        cmd = [self.ps, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", BRIDGE]
        self._proc = subprocess.Popen(
            cmd, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
            stderr=subprocess.DEVNULL, text=True, bufsize=1,
            encoding="utf-8", errors="replace",
        )
        line = self._proc.stdout.readline().strip()
        if line != "READY":
            raise RuntimeError("Clevo WMI bridge not ready: %r "
                               "(run as administrator?)" % line)
        self._write_now(BRIGHTNESS | LEVELS[self.brightness])
        self._stop.clear()
        self._thread = threading.Thread(target=self._writer_loop,
                                        name="clevo-writer", daemon=True)
        self._thread.start()
        print("[laptop] Clevo WMI bridge ready")

    def _write_now(self, value: int) -> None:
        if self._proc is None or self._proc.poll() is not None:
            return
        try:
            self._proc.stdin.write("%08X\n" % (value & 0xFFFFFFFF))
            self._proc.stdin.flush()
            self.sent += 1
        except (BrokenPipeError, ValueError):
            pass

    def _writer_loop(self) -> None:
        while not self._stop.is_set():
            with self._lock:
                value = self._pending
                self._pending = None
            if value is None:
                time.sleep(0.001)
                continue
            if value == self._shown:
                continue
            self._write_now(value)
            self._shown = value
        # drain one last value so the final colour sticks
        with self._lock:
            value = self._pending
        if value is not None and value != self._shown:
            self._write_now(value)

    def close(self) -> None:
        self._stop.set()
        if self._thread is not None:
            self._thread.join(timeout=3)
            self._thread = None
        if self._proc is None:
            return
        try:
            self._proc.stdin.write("QUIT\n")
            self._proc.stdin.flush()
        except Exception:
            pass
        try:
            self._proc.wait(timeout=3)
        except Exception:
            self._proc.kill()
        self._proc = None

    # -- lighting --------------------------------------------------------
    def apply(self, frame) -> None:
        r, g, b = frame.get(self.parts[0])
        value = PARTS[self.parts[0]] | pack_color(r, g, b)
        with self._lock:
            if self._pending is not None:
                self.dropped += 1
            self._pending = value
