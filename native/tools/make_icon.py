# -*- coding: utf-8 -*-
"""Generate native/assets/app.ico: a dark rounded tile with a glowing RGB ring."""
import os
import math
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT_DIR = os.path.join(os.path.dirname(HERE), "assets")
os.makedirs(OUT_DIR, exist_ok=True)

S = 1024  # supersampled master


def rr(draw, box, radius, fill):
    draw.rounded_rectangle(box, radius=radius, fill=fill)


def main():
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    m = int(S * 0.02)
    # tile
    rr(d, (m, m, S - m, S - m), int(S * 0.22), (20, 24, 33, 255))
    # subtle inner edge
    rr(d, (m + 8, m + 8, S - m - 8, S - m - 8), int(S * 0.20), (26, 31, 42, 255))

    cx = cy = S / 2.0
    r = S * 0.29
    width = int(S * 0.115)
    glow = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    gd = ImageDraw.Draw(glow)
    arcs = [
        (150, 250, (77, 163, 255)),   # accent blue
        (30, 120, (180, 92, 255)),    # purple
        (270, 100, (53, 208, 127)),   # green
    ]
    for start, extent, color in arcs:
        gd.arc((cx - r, cy - r, cx + r, cy + r), start=start, end=start + extent,
               fill=color + (255,), width=width)
    # soft glow behind the ring
    blurred = glow.filter(ImageFilter.GaussianBlur(int(S * 0.055)))
    img.alpha_composite(blurred)
    img.alpha_composite(glow)

    # centre spark: three small dots suggesting LEDs
    dots = [
        (cx - S * 0.085, cy + S * 0.02, (255, 90, 130)),
        (cx, cy - S * 0.075, (120, 220, 255)),
        (cx + S * 0.085, cy + S * 0.02, (200, 140, 255)),
    ]
    g2 = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    g2d = ImageDraw.Draw(g2)
    for x, y, color in dots:
        rad = S * 0.042
        g2d.ellipse((x - rad, y - rad, x + rad, y + rad), fill=color + (255,))
    g2 = g2.filter(ImageFilter.GaussianBlur(int(S * 0.012)))
    img.alpha_composite(g2)

    # down-sample for each icon size
    sizes = [256, 128, 64, 48, 32, 24, 16]
    frames = []
    for s in sizes:
        frames.append(img.resize((s, s), Image.LANCZOS))

    ico_path = os.path.join(OUT_DIR, "app.ico")
    frames[0].save(ico_path, format="ICO", sizes=[(s, s) for s in sizes])
    frames[0].save(os.path.join(OUT_DIR, "app_256.png"))
    print("wrote", ico_path)


if __name__ == "__main__":
    main()
