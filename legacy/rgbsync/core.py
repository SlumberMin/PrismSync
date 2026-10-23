# -*- coding: utf-8 -*-
"""
Core of the RGB sync engine.

Design
------
One effect engine produces a color frame on one clock. Every device adapter
consumes that same frame and re-encodes it in its own native protocol
(Clevo WMI for the laptop, HID output reports for the keyboard and the mouse).
Because all three are fed from the same frame at the same instant, they move
together - that is the whole point.

Position axis
-------------
LED layouts differ wildly between the three devices, so the engine reasons
about a *position* (0.0 = leftmost LED of a device, 1.0 = rightmost) instead
of raw zone names. A wave therefore sweeps left to right inside every device
with the same shape and the same timing, which is what reads as "synced".
"""
import colorsys
import math
import threading
import time
from dataclasses import dataclass
from typing import Dict, List, Tuple

RGB = Tuple[int, int, int]


@dataclass
class Frame:
    """One instant of lighting state: zone key -> RGB."""

    colors: Dict[str, RGB]
    t: float

    def get(self, zone: str, default: RGB = (0, 0, 0)) -> RGB:
        return self.colors.get(zone, default)


def hsv(h: float, s: float = 1.0, v: float = 1.0) -> RGB:
    """HSV (0..1) -> RGB (0..255)."""
    r, g, b = colorsys.hsv_to_rgb(h % 1.0, max(0.0, min(1.0, s)), max(0.0, min(1.0, v)))
    return (int(r * 255), int(g * 255), int(b * 255))


def mix(a: RGB, b: RGB, f: float) -> RGB:
    f = max(0.0, min(1.0, f))
    return (int(a[0] + (b[0] - a[0]) * f),
            int(a[1] + (b[1] - a[1]) * f),
            int(a[2] + (b[2] - a[2]) * f))


def scale(c: RGB, f: float) -> RGB:
    f = max(0.0, min(1.0, f))
    return (int(c[0] * f), int(c[1] * f), int(c[2] * f))


class Effect:
    """Produces the color of a single LED given its position and the clock."""

    name = "effect"

    def color_at(self, pos: float, t: float) -> RGB:
        raise NotImplementedError

    def global_color(self, t: float) -> RGB:
        """
        Colour for a device that has only ONE zone (the laptop backlight).

        Such a device cannot show the spatial falloff of a wave, so it must not
        be sampled at a fixed position - it would sit at the dark floor of the
        wave most of the time and read as "switched off". Effects override this
        to return their headline colour at full brightness.
        """
        return self.color_at(0.5, t)

    def zone_color(self, center: float, t: float) -> RGB:
        """
        Colour a single-zone device should show, given where it sits on the desk
        axis. Defaults to the headline colour; effects that paint a colour band
        across the desk override this so the laptop picks up its own slice.
        """
        return self.global_color(t)

    def frame(self, positions: Dict[str, float], t: float) -> Frame:
        return Frame({z: self.color_at(p, t) for z, p in positions.items()}, t)


class Static(Effect):
    """Constant color everywhere."""

    name = "static"

    def __init__(self, color: RGB = (255, 0, 0)):
        self.base = color

    def color_at(self, pos: float, t: float) -> RGB:
        return self.base


class Breathe(Effect):
    """Smooth fade in/out, identical phase on every device."""

    name = "breathe"

    def __init__(self, color: RGB = (0, 160, 255), period: float = 2.0, depth: float = 0.35):
        self.base = color
        self.period = period
        self.depth = depth

    def color_at(self, pos: float, t: float) -> RGB:
        phase = 0.5 - 0.5 * math.cos(2 * math.pi * (t / self.period))
        return scale(self.base, 1.0 - self.depth * (1.0 - phase))


class Wave(Effect):
    """
    A narrow bright head travelling through the zones.

    ``floor`` is the brightness outside the head. It must not be 0: a literal
    black background makes whole keyboard zones switch off (the laptop
    backlight really goes dark), which reads as a glitch rather than an effect.
    It also must not be too low - at 0.08 three quarters of the keyboard looks
    switched off, which is what made the first demo read as "too dim".
    """

    name = "wave"

    def __init__(self, speed: float = 0.35, width: float = 0.3, sat: float = 1.0,
                 val: float = 1.0, floor: float = 0.32):
        self.speed = speed
        self.width = width
        self.sat = sat
        self.val = val
        self.floor = floor

    def color_at(self, pos: float, t: float) -> RGB:
        head = (t * self.speed) % 1.0
        d = abs(pos - head)
        d = min(d, 1.0 - d)
        if d > self.width:
            return hsv(head, self.sat, self.val * self.floor)
        falloff = 1.0 - (d / self.width)
        return hsv(head, self.sat, self.val * (self.floor +
                                              (1.0 - self.floor) * falloff))

    def global_color(self, t: float) -> RGB:
        # a single-zone device follows the wave's hue at full brightness
        head = (t * self.speed) % 1.0
        return hsv(head, self.sat, self.val)


class Flow(Effect):
    """
    A scrolling hue gradient where **every LED stays at full brightness**.

    This is the effect that reads as "synced" on a mixed setup: there is no dark
    part of the board, so nothing looks switched off, and the whole spectrum
    slides sideways in one direction. The laptop, being single-zone, shows the
    hue that is passing through the middle of the keyboard.
    """

    name = "flow"

    def __init__(self, speed: float = 0.12, span: float = 1.0, sat: float = 1.0,
                 val: float = 1.0, reverse: bool = False):
        self.speed = speed
        self.span = span
        self.sat = sat
        self.val = val
        self.dir = -1.0 if reverse else 1.0

    def color_at(self, pos: float, t: float) -> RGB:
        return hsv(pos * self.span + self.dir * t * self.speed, self.sat, self.val)

    def global_color(self, t: float) -> RGB:
        return hsv(0.5 * self.span + self.dir * t * self.speed, self.sat, self.val)


class Aurora(Effect):
    """
    极光：两条不同频率的正弦波叠加，色相被限制在一条窄色带里，亮度也随波
    起伏。全彩虹看久了会花，这种限定色域的光带更像"桌面的一部分"——颜色之
    间是互相推挤着流动的，而不是依次轮播。

    ``base_hue`` 是色带中心，``spread`` 是摆动幅度：0.2 大约是 72° 的色相范围。
    """

    name = "aurora"

    def __init__(self, speed: float = 0.055, sat: float = 0.82,
                 val: float = 1.0, base_hue: float = 0.53,
                 spread: float = 0.2):
        self.speed = speed
        self.sat = sat
        self.val = val
        self.base_hue = base_hue
        self.spread = spread

    def color_at(self, pos: float, t: float) -> RGB:
        tt = t * self.speed
        w1 = math.sin(pos * 5.2 + tt * 9.0)
        w2 = math.sin(pos * 11.5 - tt * 6.0 + 1.7)
        h = self.base_hue + self.spread * (0.66 * w1 + 0.34 * w2) + tt * 0.25
        v = 0.56 + 0.44 * (0.5 + 0.5 * math.sin(pos * 7.0 - tt * 11.0 + 0.6))
        return hsv(h, self.sat, self.val * v)

    def zone_color(self, center: float, t: float) -> RGB:
        return self.color_at(center, t)


class TrioFlow(Effect):
    """
    One colour band wrapped across the whole desk, sliding left to right.

    The three devices are laid out side by side (headset on the left, laptop in
    the middle, keyboard on the right), so the desk axis is wider than a single
    keyboard. ``span`` controls how many hue cycles fit across that axis:

        span 2.2  ->  headset sees one hue, the laptop another, and the keyboard
                      covers ~0.9 of a full rainbow, i.e. several colours at
                      once, which is what makes the right-hand side look rich.

    Because the band moves over time, a colour passes the headset first, then
    the laptop, then sweeps through the keyboard: one colour chasing the next.
    Every LED stays at full brightness, so nothing ever looks switched off.
    """

    name = "trio"

    def __init__(self, speed: float = 0.085, span: float = 2.2,
                 sat: float = 1.0, val: float = 1.0, reverse: bool = False):
        self.speed = speed
        self.span = span
        self.sat = sat
        self.val = val
        self.dir = -1.0 if reverse else 1.0

    def color_at(self, pos: float, t: float) -> RGB:
        return hsv(pos * self.span + self.dir * t * self.speed,
                   self.sat, self.val)

    def global_color(self, t: float) -> RGB:
        return hsv(0.5 * self.span + self.dir * t * self.speed,
                   self.sat, self.val)

    def zone_color(self, center: float, t: float) -> RGB:
        # a single-zone device shows the slice of the band that sits on it
        return self.color_at(center, t)


class Duo(Effect):
    """
    双色流转：两种颜色横跨桌面交替推进，交界是一条软过渡带。

    比彩虹克制得多——只有两个色相，整条桌面像一块会呼吸的渐层玻璃，最接近
    "配色统一"的桌搭观感。
    """

    name = "duo"

    def __init__(self, speed: float = 0.075, sat: float = 1.0, val: float = 1.0,
                 c1: RGB = (0, 208, 255), c2: RGB = (255, 0, 150),
                 cycles: float = 1.4):
        self.speed = speed
        self.sat = sat
        self.val = val
        self.c1 = c1
        self.c2 = c2
        self.cycles = cycles

    def color_at(self, pos: float, t: float) -> RGB:
        phase = (pos * self.cycles - t * self.speed) * 2.0 * math.pi
        f = 0.5 + 0.5 * math.sin(phase)
        f = f * f * (3.0 - 2.0 * f)          # smoothstep: softer band edges
        return scale(mix(self.c1, self.c2, f), self.val)

    def zone_color(self, center: float, t: float) -> RGB:
        return self.color_at(center, t)


class Comet(Effect):
    """Bright head with a long fading tail - always lit, reads as a wave."""

    name = "comet"

    def __init__(self, speed: float = 0.3, tail: float = 0.7,
                 sat: float = 1.0, val: float = 1.0, floor: float = 0.18):
        self.speed = speed
        self.tail = tail
        self.sat = sat
        self.val = val
        self.floor = floor

    def color_at(self, pos: float, t: float) -> RGB:
        head = (t * self.speed) % 1.0
        behind = (head - pos) % 1.0      # 0 at the head, grows towards the tail
        if behind > self.tail:
            return hsv(head, self.sat, self.val * self.floor)
        fade = 1.0 - (behind / self.tail) * (1.0 - self.floor)
        return hsv(head, self.sat, self.val * fade)

    def global_color(self, t: float) -> RGB:
        head = (t * self.speed) % 1.0
        return hsv(head, self.sat, self.val)


class Rainbow(Effect):
    """Same hue everywhere, cycling over time (all devices in lockstep)."""

    name = "rainbow"

    def __init__(self, speed: float = 0.12, sat: float = 1.0, val: float = 1.0):
        self.speed = speed
        self.sat = sat
        self.val = val

    def color_at(self, pos: float, t: float) -> RGB:
        return hsv(t * self.speed, self.sat, self.val)


class HueSweep(Effect):
    """Each device shows the same gradient; hue depends on position."""

    name = "hue_sweep"

    def __init__(self, speed: float = 0.2, span: float = 0.5):
        self.speed = speed
        self.span = span

    def color_at(self, pos: float, t: float) -> RGB:
        return hsv(t * self.speed + pos * self.span, 1.0, 1.0)


class AudioReactive(Effect):
    """Beat driven. ``level`` (0..1) and ``pulse`` come from the audio feed."""

    name = "audio"

    def __init__(self, decay: float = 0.86):
        self.level = 0.0
        self.pulse = 0.0
        self.decay = decay

    def update(self, level: float, dt: float) -> None:
        if level > self.level + 0.08:
            self.pulse = 1.0
        self.level = level
        self.pulse *= self.decay ** max(dt, 1e-3)

    def color_at(self, pos: float, t: float) -> RGB:
        base = hsv(t * 0.08, 1.0, 0.25 + 0.75 * self.pulse)
        return scale(base, 0.35 + 0.65 * self.pulse)


class SyncEngine:
    """Runs the effect on a fixed cadence and feeds every adapter."""

    def __init__(self, effect: Effect, fps: int = 30, uniform: bool = False):
        self.effect = effect
        self.period = 1.0 / fps
        self.adapters: List = []
        self._next = time.perf_counter()
        self._t0 = self._next
        self._frames = 0
        self._fps_mark = self._next
        # uniform: every zone sits at the same point of the axis, so all
        # devices show one identical colour (the laptop backlight is a single
        # global colour, so this is what makes the two keyboards match).
        self.uniform = uniform
        self.last_frame: Frame = None
        self._stop = False
        self._lock = threading.Lock()

    def add(self, adapter) -> None:
        self.adapters.append(adapter)

    def set_effect(self, effect: Effect, reset_phase: bool = False) -> None:
        """
        Swap the effect live (called from the GUI thread).

        ``reset_phase`` restarts the clock at 0, which is what you want when the
        user picks a different effect (it should begin at its first frame) but
        NOT when they merely nudge a slider - that would make the animation jump.
        """
        with self._lock:
            self.effect = effect
            if reset_phase:
                self._t0 = time.perf_counter()

    def stop(self) -> None:
        self._stop = True

    def positions(self) -> Dict[str, float]:
        """Every LED of every device, projected onto the shared desk axis."""
        pos: Dict[str, float] = {}
        for ad in self.adapters:
            pos.update(ad.global_positions())
        if self.uniform:
            return {z: 0.5 for z in pos}
        return pos

    def tick(self) -> None:
        now = time.perf_counter()
        if now < self._next:
            time.sleep(self._next - now)
            now = time.perf_counter()
        self._next = now + self.period
        # effects get a time that starts at 0 for this run, so a fresh start
        # always begins at phase 0 instead of wherever perf_counter happens to be
        t = now - self._t0
        effect = self.effect
        frame = effect.frame(self.positions(), t)
        self.last_frame = frame
        for ad in self.adapters:
            try:
                if self.uniform:
                    color = effect.global_color(t)
                    ad.apply(Frame({z: color for z in ad.positions()}, t))
                elif getattr(ad, "single_zone", False):
                    color = effect.zone_color(ad.center(), t)
                    ad.apply(Frame({z: color for z in ad.positions()}, t))
                else:
                    ad.apply(frame)
            except Exception as exc:
                print("[%s] apply failed: %s" % (getattr(ad, "name", "?"), exc))
        self._frames += 1
        if now - self._fps_mark >= 2.0:
            fps = self._frames / (now - self._fps_mark)
            extra = []
            for ad in self.adapters:
                if hasattr(ad, "sent"):
                    s = "%s sent=%d thr=%d" % (getattr(ad, "name", "?"),
                                               ad.sent, ad.dropped)
                    if getattr(ad, "failed", 0):
                        s += " FAILED=%d" % ad.failed
                    extra.append(s)
            print("fps %.1f (target %.1f)%s"
                  % (fps, 1.0 / self.period,
                     ("  " + "  ".join(extra)) if extra else ""))
            self._frames = 0
            self._fps_mark = now

    def run(self, seconds: float = 0.0) -> None:
        end = (time.perf_counter() + seconds) if seconds else None
        alive = []
        for ad in self.adapters:
            try:
                ad.open()
                alive.append(ad)
            except Exception as exc:
                print("[%s] open failed, device skipped: %s"
                      % (getattr(ad, "name", "?"), exc))
        self.adapters = alive
        try:
            while not self._stop and (end is None or time.perf_counter() < end):
                self.tick()
        finally:
            self.close_adapters()

    def close_adapters(self) -> None:
        for ad in self.adapters:
            try:
                ad.close()
            except Exception:
                pass
        self.adapters = []
