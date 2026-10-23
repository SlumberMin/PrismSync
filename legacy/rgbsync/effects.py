# -*- coding: utf-8 -*-
"""
Effect registry, shared by the CLI (main.py) and the GUI.

Each entry knows how to build itself from three generic knobs - speed,
brightness and a base colour - so the UI can render one slider set for every
effect instead of special-casing each one.
"""
from typing import Callable, Dict, List

from .core import (Aurora, Breathe, Comet, Duo, Effect, Flow, HueSweep, Rainbow,
                   Static, TrioFlow, Wave)


def _speed(base: float, mult: float) -> float:
    """Slider 0.3..2.5 -> a per-effect speed multiplier."""
    return max(0.02, base * max(0.1, mult))


def _val(bright: float) -> float:
    return max(0.05, min(1.0, bright))


SPECS: List[Dict] = [
    {
        "id": "trio",
        "label": "三连流",
        "hint": "一条色带从耳机流过笔记本、再扫过键盘",
        # span 2.5 puts exactly one full rainbow across the keyboard slice, so
        # the board reads red -> violet -> red instead of repeating colours
        "build": lambda s, b, c: TrioFlow(speed=_speed(0.18, s), span=2.5,
                                          val=_val(b)),
    },
    {
        "id": "aurora",
        "label": "极光",
        "hint": "青紫色带柔和翻涌，久看也不刺眼",
        "build": lambda s, b, c: Aurora(speed=_speed(0.10, s), val=_val(b)),
    },
    {
        "id": "duo",
        "label": "双色流转",
        "hint": "两种颜色在桌面上互相推挤",
        "build": lambda s, b, c: Duo(speed=_speed(0.14, s), val=_val(b)),
    },
    {
        "id": "flow",
        "label": "流光",
        "hint": "整条彩虹横向滑动，全亮无暗区",
        "build": lambda s, b, c: Flow(speed=_speed(0.12, s), val=_val(b)),
    },
    {
        "id": "wave",
        "label": "波浪",
        "hint": "一道亮光扫过，其余保持微亮",
        "build": lambda s, b, c: Wave(speed=_speed(0.35, s), val=_val(b)),
    },
    {
        "id": "comet",
        "label": "彗星",
        "hint": "亮头带长尾，一直亮着",
        "build": lambda s, b, c: Comet(speed=_speed(0.30, s), val=_val(b)),
    },
    {
        "id": "rainbow",
        "label": "七彩同步",
        "hint": "三台设备完全同色，一起变色",
        "build": lambda s, b, c: Rainbow(speed=_speed(0.12, s), val=_val(b)),
    },
    {
        "id": "hue",
        "label": "色相渐变",
        "hint": "每台设备显示同一条渐变，整体缓慢转色",
        "build": lambda s, b, c: HueSweep(speed=_speed(0.20, s)),
    },
    {
        "id": "breathe",
        "label": "呼吸",
        "hint": "一起缓慢明暗起伏",
        "build": lambda s, b, c: Breathe(color=c, period=max(0.6, 2.4 / max(0.1, s)),
                                         depth=0.55 * _val(b)),
    },
    {
        "id": "static",
        "label": "常亮",
        "hint": "固定一个颜色",
        "build": lambda s, b, c: Static(color=c),
    },
]

BY_ID: Dict[str, Dict] = {spec["id"]: spec for spec in SPECS}
DEFAULT_EFFECT = "trio"


def build(effect_id: str, speed: float = 1.0, brightness: float = 1.0,
          color=(255, 0, 0)) -> Effect:
    spec = BY_ID.get(effect_id) or BY_ID[DEFAULT_EFFECT]
    return spec["build"](speed, brightness, color)


def catalog() -> List[Dict]:
    """What the UI needs: no lambdas, JSON friendly."""
    return [{"id": s["id"], "label": s["label"], "hint": s["hint"]} for s in SPECS]
