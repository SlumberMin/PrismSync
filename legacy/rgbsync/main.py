# -*- coding: utf-8 -*-
"""
Entry point of the RGB sync engine.

Drives the three devices from a single clock and a single effect:

    laptop    Colorful X15 AT23 (Clevo NP50SNE) - via Clevo WMI
    keyboard  AULA HERO 68 HE                   - via HID output reports
    mouse     MCHOSE V9 Turbo+                 - via HID output reports

The laptop path talks to WMI, so the whole process must run elevated
("Run as administrator"). Use --no-laptop to run the peripherals only.

Examples:
    python rgbsync/main.py --effect wave
    python rgbsync/main.py --effect rainbow --fps 60
    python rgbsync/main.py --effect breathe --no-laptop
"""
import argparse
import os
import sys
import time

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from rgbsync.core import SyncEngine
from rgbsync import effects
from rgbsync.adapters.hero68 import Hero68Adapter

EFFECTS = {spec["id"]: spec for spec in effects.SPECS}


def build_engine(args):
    effect = effects.build(args.effect, speed=args.speed,
                           brightness=args.power, color=tuple(args.color))
    engine = SyncEngine(effect, fps=args.fps)

    if not args.no_laptop:
        from rgbsync.adapters.clevo import ClevoAdapter
        engine.add(ClevoAdapter(brightness=args.brightness))
    if not args.no_keyboard:
        engine.add(Hero68Adapter(bands=args.bands))
    if not args.no_mouse:
        try:
            from rgbsync.adapters.mchose import MchoseV9Adapter
            engine.add(MchoseV9Adapter())
        except ImportError:
            pass
    return engine


def _tee(path):
    """Duplicate everything printed to stdout into ``path`` (line buffered)."""
    class Tee(object):
        def __init__(self, stream, handle):
            self.stream = stream
            self.handle = handle

        def write(self, data):
            self.stream.write(data)
            try:
                self.handle.write(data)
                self.handle.flush()
            except Exception:
                pass
            return len(data)

        def flush(self):
            try:
                self.stream.flush()
                self.handle.flush()
            except Exception:
                pass

    sys.stdout = Tee(sys.stdout, open(path, "w", encoding="utf-8", buffering=1))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--effect", choices=sorted(EFFECTS), default="trio")
    ap.add_argument("--fps", type=int, default=30)
    ap.add_argument("--speed", type=float, default=1.0,
                    help="effect speed multiplier (0.3 slow .. 2.5 fast)")
    ap.add_argument("--power", type=float, default=1.0,
                    help="effect brightness 0.1 .. 1.0")
    ap.add_argument("--color", type=lambda s: tuple(int(x) for x in s.split(",")),
                    default=(255, 0, 0), help="RGB for the static effect")
    ap.add_argument("--seconds", type=float, default=0.0, help="0 = run forever")
    ap.add_argument("--brightness", choices=["off", "low", "mid", "high"], default="high")
    ap.add_argument("--bands", type=int, default=0,
                    help="colour quantisation levels for the keyboard "
                         "(0 = none; a gradient costs one packet per colour, "
                         "~8ms for all 67 keys)")
    ap.add_argument("--no-laptop", action="store_true")
    ap.add_argument("--no-keyboard", action="store_true")
    ap.add_argument("--no-mouse", action="store_true")
    ap.add_argument("--log-file", default="",
                    help="tee stdout into this file (used when the process is "
                         "launched elevated and its console is not visible)")
    ap.add_argument("--uniform", action="store_true",
                    help="every device shows one identical colour")
    args = ap.parse_args()

    if args.log_file:
        _tee(args.log_file)

    engine = build_engine(args)
    engine.uniform = args.uniform
    print("effect=%s fps=%d bands=%d devices=%s"
          % (args.effect, args.fps, args.bands,
             [a.name for a in engine.adapters]))
    try:
        engine.run(args.seconds)
    except KeyboardInterrupt:
        print("\nstopped")


if __name__ == "__main__":
    main()
