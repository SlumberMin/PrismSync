# -*- coding: utf-8 -*-
"""
HERO 68 HE - final per-key painter.

Why this is the one that works
------------------------------
Measured facts on this board:
  * cmd 0x04 (param 0x01) switches firmware effects        -> visible
  * cmd 0x06 with ONE key paints exactly that key          -> ESC = key 1
  * cmd 0x08 with ONE colour segment per packet, several   -> whole board green
    packets, paints every key

The failure mode we kept hitting: packing several [R,G,B,count,ids...] segments
into one 56-byte packet.  The firmware only consumes the FIRST segment of a
packet, so a 67-colour gradient lit 7 keys and left the rest at their old
colours (which is why the keyboard looked like a patchwork).

Rule: one colour segment per packet, total_pkts = number of packets.

Cost: a solid colour is 1 packet; a gradient is one packet per distinct colour,
so gradients get quantised into ``bands`` levels to keep the frame cheap.

    python hero68_final.py solid FF0000
    python hero68_final.py grad 8
    python hero68_final.py wave 12
"""
import os
import sys
import time

import hid

AULA_DIR = r"C:\Program Files (x86)\AULA\AULA_CTRL"
if AULA_DIR not in sys.path:
    sys.path.insert(0, AULA_DIR)

VID, PID, UP = 0x372E, 0x103E, 0xFF60
REPORT_ID = 0x09
REPORT_LEN = 64
MAX_PAYLOAD = 56
KEYS = [1] + list(range(15, 76)) + [77, 98, 99, 102, 103]
MAX_KEYS_PER_SEG = 52          # 4 + 52 = 56 bytes


def crc(p):
    return (0xFF - (sum(p[:63]) & 0xFF)) & 0xFF


def build(cmd, param, payload=None, total=1, idx=0):
    p = bytearray(REPORT_LEN)
    p[0] = REPORT_ID
    p[1] = cmd
    p[2] = param
    p[3] = 0x00
    p[4] = total
    p[5] = idx
    if payload:
        n = min(len(payload), MAX_PAYLOAD)
        p[6] = n
        p[7:7 + n] = payload
    p[63] = crc(bytes(p))
    return bytes(p)


def quantise(rgb, bands):
    if bands <= 0:
        return rgb
    step = 255 // bands
    return tuple(min(255, (c // max(1, step)) * step) for c in rgb)


class Hero68:
    def __init__(self):
        self.dev = None

    def open(self):
        d = None
        for x in hid.enumerate(VID, PID):
            if x.get("usage_page") == UP:
                d = x
                break
        if d is None:
            raise RuntimeError("HERO 68 HE vendor interface not found")
        self.dev = hid.device()
        self.dev.open_path(d["path"])

    def close(self):
        if self.dev:
            self.dev.close()
            self.dev = None

    def send(self, cmd, param, payload=None, total=1, idx=0):
        self.dev.write(build(cmd, param, payload, total, idx))

    def enter_custom(self):
        self.send(0x01, 0x80, [0x10])
        self.send(0x04, 0x02, [0x13, 0x00])
        self.send(0x04, 0x04, [0x64, 0x04])

    def commit(self):
        self.send(0x01, 0x00)

    def paint(self, colors, bands=0):
        """One colour segment per packet - the only form the firmware accepts."""
        groups = {}
        for k in KEYS:
            rgb = quantise(colors.get(k, (0, 0, 0)), bands)
            groups.setdefault(rgb, []).append(k)

        packets = []
        for rgb, ids in groups.items():
            for s in range(0, len(ids), MAX_KEYS_PER_SEG):
                chunk = ids[s:s + MAX_KEYS_PER_SEG]
                payload = bytearray([rgb[0], rgb[1], rgb[2], len(chunk)])
                for k in chunk:
                    payload.append(k & 0xFF)
                packets.append(bytes(payload))

        t0 = time.perf_counter()
        for i, p in enumerate(packets):
            self.send(0x08, 0x01, p, total=len(packets), idx=i)
        self.commit()
        return len(packets), (time.perf_counter() - t0) * 1000.0


def hsv(h, s=1.0, v=1.0):
    import colorsys
    r, g, b = colorsys.hsv_to_rgb(h % 1.0, s, v)
    return (int(r * 255), int(g * 255), int(b * 255))


def grad_colors(right_blue=True, bands=0):
    n = len(KEYS) - 1
    out = {}
    for i, k in enumerate(KEYS):
        f = i / n
        out[k] = (int(255 * f), 0, int(255 * (1 - f)))
    if not right_blue:
        out = {k: (v[2], 0, v[0]) for k, v in out.items()}
    return out


def main():
    mode = sys.argv[1] if len(sys.argv) > 1 else "solid"
    arg = sys.argv[2] if len(sys.argv) > 2 else ""
    kb = Hero68()
    kb.open()
    try:
        if mode == "solid":
            hexc = arg or "FF0000"
            r = int(hexc[0:2], 16)
            g = int(hexc[2:4], 16)
            b = int(hexc[4:6], 16)
            kb.enter_custom()
            n, ms = kb.paint({k: (r, g, b) for k in KEYS})
            print("solid %s -> %d packet(s), %.1f ms" % (hexc, n, ms))

        elif mode == "grad":
            bands = int(arg) if arg else 8
            kb.enter_custom()
            n, ms = kb.paint(grad_colors(), bands=bands)
            print("gradient bands=%d -> %d packet(s), %.1f ms" % (bands, n, ms))

        elif mode == "bandtest":
            kb.enter_custom()
            for bands in (0, 32, 16, 8):
                n, ms = kb.paint(grad_colors(), bands=bands)
                print("bands=%-2d  packets=%-3d  %.1f ms   (hold 5s)"
                      % (bands, n, ms), flush=True)
                time.sleep(5)

        elif mode == "wave":
            secs = float(arg) if arg else 10.0
            bands = 16
            pos = {k: (i + 0.5) / len(KEYS) for i, k in enumerate(KEYS)}
            kb.enter_custom()
            t0 = time.perf_counter()
            frames = 0
            while time.perf_counter() - t0 < secs:
                now = time.perf_counter() - t0
                head = (now * 0.35) % 1.0
                colors = {}
                for k in KEYS:
                    d = abs(pos[k] - head)
                    d = min(d, 1.0 - d)
                    v = 0.08 + 0.92 * max(0.0, 1.0 - d / 0.25)
                    colors[k] = hsv(head, 1.0, v)
                kb.paint(colors, bands=bands)
                frames += 1
                time.sleep(0.008)
            print("wave %.1fs, %d frames, %.1f fps"
                  % (secs, frames, frames / secs))

        else:
            print("usage: hero68_final.py solid|grad|bandtest|wave [arg]")
    finally:
        kb.close()
    return 0


if __name__ == "__main__":
    sys.exit(main())
