# -*- coding: utf-8 -*-
"""
Device adapter interface.

A device adapter knows two things:
  1. which lighting zones it physically has, and where each one sits along the
     left-to-right "position axis" (0.0 = far left, 1.0 = far right);
  2. how to push a color frame to the hardware in its native protocol.

The engine only ever deals in (zone key -> RGB). The position axis exists so
that one shared effect (a wave, a rainbow sweep) reads identically on a laptop
keyboard, an external keyboard and a mouse.
"""
from typing import Dict, List, Tuple

RGB = Tuple[int, int, int]


class Adapter:
    """Base class for all device adapters."""

    name = "adapter"

    #: Where this device sits on the desk, as a fraction of the whole row.
    #: 0.0 = far left, 1.0 = far right. The engine maps every LED of the device
    #: into this slice, so a colour band can travel from the headset, through
    #: the laptop, and on into the keyboard.
    span: Tuple[float, float] = (0.0, 1.0)

    def __init__(self) -> None:
        self._layout: Dict[str, float] = {}

    # -- layout ---------------------------------------------------------
    def zones(self) -> List[str]:
        """Ordered list of zone keys owned by this device."""
        return list(self._layout.keys())

    def positions(self) -> Dict[str, float]:
        """zone key -> position on this device's own 0..1 axis."""
        return dict(self._layout)

    def global_positions(self) -> Dict[str, float]:
        """zone key -> position on the shared desk axis (after span mapping)."""
        a, b = self.span
        return {z: a + p * (b - a) for z, p in self._layout.items()}

    def center(self) -> float:
        """Where the device sits on the desk axis, for single-zone devices."""
        a, b = self.span
        return (a + b) / 2.0

    def set_layout(self, layout: Dict[str, float]) -> None:
        self._layout = dict(layout)

    def set_span(self, start: float, end: float) -> None:
        self.span = (start, end)

    # -- control --------------------------------------------------------
    def open(self) -> None:
        pass

    def close(self) -> None:
        pass

    def apply(self, frame) -> None:
        """Push one frame to the hardware."""
        raise NotImplementedError
