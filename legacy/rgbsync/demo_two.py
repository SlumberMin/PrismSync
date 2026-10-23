# -*- coding: utf-8 -*-
"""
Two-device sync check: the laptop (Clevo WMI) and the HERO 68 HE keyboard are
driven by the same engine, so one wave sweeps both.

    python rgbsync/main.py --effect wave --fps 30
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from rgbsync.core import SyncEngine, Wave
from rgbsync.adapters.hero68 import Hero68Adapter


def main():
    engine = SyncEngine(Wave(floor=0.10), fps=30)
    try:
        from rgbsync.adapters.clevo import ClevoAdapter
        engine.add(ClevoAdapter())
    except Exception as exc:
        print("laptop adapter unavailable: %s" % exc)
    engine.add(Hero68Adapter())
    print("devices:", [a.name for a in engine.adapters])
    engine.run(0.0)


if __name__ == "__main__":
    try:
        main()
    except KeyboardInterrupt:
        print("\nstopped")
