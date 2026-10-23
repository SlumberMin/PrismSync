# -*- coding: utf-8 -*-
"""
HERO 68 HE (AULA) adapter - aligned with the official AULA_CTRL client.

Everything here mirrors the vendor implementation (AULA_CTRL_V1.0.17):

* packet layout (configs/test.py)
      [0] report id 0x09  [1] cmd  [2] param  [3] reserved
      [4] total packets [5] packet index [6] payload length [7:] payload
      [63] CRC = (0xFF - sum(packet[0:63])) & 0xFF
* device UID via cmd 0x82 / param 0x01 (answer carries 6 UID bytes)
* per-model key layout taken from the vendor's own configs/keyboard_uid.py
* per-key colour stream via cmd 0x06, payload = [keyId>>8, keyId&0xFF, R, G, B]
  with at most 11 keys per packet, wrapped in the official setup/commit
  sequence (0x01/0x80 -> 0x04/0x02 -> 0x04/0x04 ... 0x01/0x00)
* light box (18 LED strip around the board) via cmd 0x0E
* built-in firmware effects via cmd 0x04 / param 0x01,
  payload = [effect id, 0x07, R, G, B, 0x14, 0x01]
"""
import os
import sys
import time
from typing import Dict, List, Optional

import hid

from .base import Adapter

VID, PID = 0x372E, 0x103E
REPORT_ID = 0x09
REPORT_LEN = 64
MAX_PAYLOAD = 56            # buffer[7..62]
USAGE_PAGE = 0xFF60
KEYS_PER_PACKET = 11        # cmd 0x06 form (one key per packet in practice)
MAX_KEYS_PER_SEG = 52       # cmd 0x08: 4 header bytes + 52 key ids = 56
LIGHTBOX_LEDS = 18

# key ids this board actually answers to (vendor layout for UID 110000000003)
ALL_KEYS = [1] + list(range(15, 76)) + [77, 98, 99, 102, 103]

# key groups exactly as the device defined them itself (cmd 0x83)
DEVICE_GROUPS = [
    [0x01, 0x0F, 0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16],
    [0x17, 0x18, 0x19, 0x1A, 0x1B, 0x62, 0x1C, 0x1D, 0x1E],
    [0x1F, 0x20, 0x21, 0x22, 0x23, 0x24, 0x25, 0x26, 0x27],
    [0x28, 0x29, 0x63, 0x2A, 0x2B, 0x2C, 0x2D, 0x2E, 0x2F],
    [0x30, 0x31, 0x32, 0x33, 0x34, 0x35, 0x36, 0x66, 0x37],
    [0x38, 0x39, 0x3A, 0x3B, 0x3C, 0x3D, 0x3E, 0x3F, 0x40],
    [0x41, 0x42, 0x4A, 0x67, 0x43, 0x44, 0x45, 0x46, 0x47],
    [0x48, 0x49, 0x4C, 0x4B, 0x4D],
]
DEVICE_KEYS = sorted({k for g in DEVICE_GROUPS for k in g})

AULA_DIR = r"C:\Program Files (x86)\AULA\AULA_CTRL"

# Built-in firmware effects (slot id in the 0x04 command)
EFFECTS = {
    0x01: "常亮灯效", 0x02: "呼吸模式", 0x03: "梦幻彩虹", 0x04: "一触即发",
    0x05: "雨中漫步", 0x06: "彩虹转轮", 0x07: "涟漪扩散", 0x08: "繁星点点",
    0x09: "踏雪无痕", 0x0A: "川流不息", 0x0B: "随波逐流", 0x0C: "如影随形",
    0x0D: "正弦光波", 0x0E: "左右扫描", 0x0F: "旋转风车", 0x10: "七彩瀑布",
    0x11: "花开富贵", 0x12: "旋转风暴", 0x13: "自定义",
}
EFFECT_CUSTOM = 0x13

# fallback layout (HERO 68 HE) if the vendor configs are unavailable
FALLBACK_KEYS = list(ALL_KEYS)


def crc(packet: bytes) -> int:
    return (0xFF - (sum(packet[:63]) & 0xFF)) & 0xFF


def build(cmd: int, param: int, payload=None, total_pkts: int = 1,
          pkt_idx: int = 0) -> bytes:
    packet = bytearray(REPORT_LEN)
    packet[0] = REPORT_ID
    packet[1] = cmd
    packet[2] = param
    packet[3] = 0x00
    packet[4] = total_pkts
    packet[5] = pkt_idx
    if payload:
        n = min(len(payload), MAX_PAYLOAD)
        packet[6] = n
        packet[7:7 + n] = payload
    packet[63] = crc(bytes(packet))
    return bytes(packet)


class Hero68Adapter(Adapter):
    """Drives every key of the HERO 68 HE independently."""

    name = "hero68"
    # right-hand side of the desk, the widest slice - so a colour band shows
    # several hues across the keys at once
    span = (0.60, 1.0)

    def __init__(self, use_vendor_layout: bool = True, bands: int = 0):
        super().__init__()
        self.use_vendor_layout = use_vendor_layout
        # colour quantisation: each distinct colour costs one packet. 0 = none,
        # which keeps gradients perfectly smooth (67 packets / ~8 ms per frame,
        # comfortably inside a 30 fps budget). Raise it only if the frame rate
        # is starved.
        self.bands = bands
        self.band_step = max(1, 255 // bands) if bands > 0 else 1
        self._dev = None
        self._uid = None
        self._layout = None
        self._keys: List[int] = list(FALLBACK_KEYS)
        self._pos: Dict[int, float] = {}
        self._rebuild_layout()

    # -- layout -----------------------------------------------------------
    def _rebuild_layout(self) -> None:
        """
        Build key-id -> horizontal position (0..1 inside the board).

        AULA's ``ButtonLayout`` hands back a **dict** of ``{"k15": 0.1666, ...}``
        where the value is already the key's horizontal position. Treating that
        as a list-of-rows (the old code did) iterates the dict's keys as strings
        and then the *characters* of each key name, so every lookup fell through
        to the 0.5 default and the whole board got one colour. That was the
        "灯效和预览对不上" bug.
        """
        pos: Dict[int, float] = {}
        src = self._layout

        if isinstance(src, dict):
            for name, x in src.items():
                try:
                    kid = int(str(name).strip().lower().lstrip("k"))
                    pos[kid] = float(x)
                except (ValueError, TypeError):
                    continue
        elif src:
            # list-of-rows fallback
            max_cols = max((len(r) for r in src), default=0) or 1
            for row in src:
                for col, key in enumerate(row):
                    if key != -1:
                        pos[key] = (col + 0.5) / max_cols

        if not pos:
            n = len(self._keys) or 1
            pos = {k: (i + 0.5) / n for i, k in enumerate(self._keys)}

        self._pos = pos
        self.set_layout({"k%d" % k: pos.get(k, 0.5) for k in self._keys})

    def _load_vendor_layout(self) -> None:
        if not self.use_vendor_layout or not os.path.isdir(AULA_DIR):
            return
        try:
            if AULA_DIR not in sys.path:
                sys.path.insert(0, AULA_DIR)
            from configs.keyboard_uid import ButtonLayout
        except Exception as exc:
            print("[hero68] vendor layout unavailable: %s" % exc)
            return
        if not self._uid:
            return
        layout = ButtonLayout.get_layout_or_none(self._uid)
        if not layout:
            return
        vendor_keys = sorted(k for row in layout for k in row if k != -1)
        self._layout = layout
        # the board answers to a fixed id range; the vendor layout only tells us
        # where the keys physically sit (used for the position axis)
        self._keys = list(ALL_KEYS)
        self._rebuild_layout()
        print("[hero68] vendor layout for UID %s: %d keys (device ids %d)"
              % (self._uid, len(vendor_keys), len(self._keys)))

    # -- device plumbing --------------------------------------------------
    def find(self):
        for d in hid.enumerate(VID, PID):
            if d.get("usage_page") == USAGE_PAGE:
                return d
        return None

    def open(self) -> None:
        d = self.find()
        if d is None:
            raise RuntimeError("HERO 68 HE vendor interface (usage_page 0x%04X) not found"
                               % USAGE_PAGE)
        self._dev = hid.device()
        self._dev.open_path(d["path"])
        self._uid = self._read_uid()
        print("[hero68] opened, UID=%s" % self._uid)
        self._load_vendor_layout()
        self.enter_custom()
        time.sleep(0.05)

    def close(self) -> None:
        if self._dev is not None:
            try:
                self._dev.close()
            finally:
                self._dev = None

    def _read_uid(self) -> Optional[str]:
        try:
            self._dev.write(build(0x82, 0x01))
            res = self._dev.read(64, 2000)
        except Exception:
            return None
        if not res or len(res) < 13 or res[1] != 0x82:
            return None
        return "".join("%02x" % b for b in res[7:13])

    def _send(self, packet: bytes) -> None:
        if self._dev is not None:
            self._dev.write(packet)

    # -- lighting ---------------------------------------------------------
    def set_effect(self, effect_id: int, color=(0x90, 0xEE, 0x90)) -> None:
        """Switch to one of the built-in firmware effects (cmd 0x04 / param 0x01)."""
        payload = bytes([effect_id, 0x07, color[0], color[1], color[2], 0x14, 0x01])
        self._send(build(0x04, 0x01, payload))
        self._send(build(0x84, 0x01))

    def enter_custom(self) -> None:
        """Official setup sequence that switches the board into per-key mode."""
        self._send(build(0x01, 0x80, [0x10]))
        self._send(build(0x04, 0x02, [0x13, 0x00]))
        self._send(build(0x04, 0x04, [0x64, 0x04]))

    def commit(self) -> None:
        self._send(build(0x01, 0x00))

    def _paint_lightbox(self, led_colors) -> None:
        """18-LED strip around the board (cmd 0x0E, two packets of nine)."""
        groups = [(led_colors[i] if i < len(led_colors) else (0, 0, 0), [i])
                  for i in range(LIGHTBOX_LEDS)]
        for idx, group in enumerate((groups[:9], groups[9:])):
            payload = bytearray()
            for rgb, ids in group:
                payload.append(rgb[0] & 0xFF)
                payload.append(rgb[1] & 0xFF)
                payload.append(rgb[2] & 0xFF)
                payload.append(len(ids) & 0xFF)
                for lid in ids:
                    payload.append(lid & 0xFF)
            self._send(build(0x0E, 0x01, bytes(payload), total_pkts=2, pkt_idx=idx))
            time.sleep(0.001)

    def apply(self, frame) -> None:
        """
        Push one frame.

        Hard-won rule: the firmware consumes only the FIRST colour segment of a
        packet. Packing several [R,G,B,count,ids...] segments into one 56-byte
        packet makes a 67-colour gradient light just ~7 keys, leaving the rest
        at stale colours. So: one segment per packet, total_pkts = packet count.
        """
        if self._dev is None:
            return

        groups: Dict[tuple, List[int]] = {}
        for k in self._keys:
            rgb = frame.get("k%d" % k) or (0, 0, 0)
            if self.bands > 0:
                rgb = tuple(min(255, (c // self.band_step) * self.band_step)
                            for c in rgb)
            groups.setdefault(rgb, []).append(k)

        packets: List[bytes] = []
        for rgb, ids in groups.items():
            for start in range(0, len(ids), MAX_KEYS_PER_SEG):
                chunk = ids[start:start + MAX_KEYS_PER_SEG]
                payload = bytearray([rgb[0], rgb[1], rgb[2], len(chunk)])
                for k in chunk:
                    payload.append(k & 0xFF)
                packets.append(bytes(payload))

        for i, payload in enumerate(packets):
            self._send(build(0x08, 0x01, payload,
                             total_pkts=len(packets), pkt_idx=i))
        self.commit()
