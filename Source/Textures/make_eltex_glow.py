"""Builds the eltex glow textures of the Promachos armor and helmet from Mortis's masks.

The masks (EltexGlowMasks/<worn texture name>m.png) are black on the eltex inlays and red elsewhere, on the canvas of the
worn texture they belong to. Each becomes a white texture whose alpha is the inlay, bright, plus a soft halo around it;
PawnRenderNode_EltexGlow draws it additively over the worn texture, tinted and faded in by the gear's charge.
A mask with no inlay (the armor seen from the west) gets no texture: the render node does not draw that facing.
Usage: python make_eltex_glow.py
"""
import os
import re

import numpy as np
from PIL import Image, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
MASKS = os.path.join(HERE, "EltexGlowMasks")
APPAREL = os.path.join(HERE, "..", "..", "Common", "Textures", "Things", "Pawn", "Humanlike", "Apparel")

# The inlay's own brightness, and the halo: a blur of the inlay this wide (pixels of the 512 canvas) and this strong.
CORE = 0.45
HALO_RADIUS = 12
HALO = 0.9


def glow(mask_path):
    red = np.asarray(Image.open(mask_path).convert("RGBA"), dtype=np.float32)[..., 0] / 255.0
    inlay = 1.0 - red
    if inlay.max() < 0.5:
        return None
    halo = Image.fromarray((inlay * 255).astype(np.uint8), "L").filter(ImageFilter.GaussianBlur(HALO_RADIUS))
    alpha = np.clip(CORE * inlay + HALO * np.asarray(halo, dtype=np.float32) / 255.0, 0.0, 1.0)
    rgba = np.full(alpha.shape + (4,), 255, dtype=np.uint8)
    rgba[..., 3] = (alpha * 255 + 0.5).astype(np.uint8)
    return Image.fromarray(rgba, "RGBA")


def main():
    for name in sorted(os.listdir(MASKS)):
        m = re.fullmatch(r"(PromachosArmor|PromachosHelmet)_(.+)m\.png", name)
        if not m:
            continue
        gear, rest = m.groups()
        out = os.path.join(APPAREL, gear, "Glow", gear + "Glow_" + rest + ".png")
        img = glow(os.path.join(MASKS, name))
        if img is None:
            if os.path.exists(out):
                os.remove(out)
            print("no inlay  " + name)
            continue
        os.makedirs(os.path.dirname(out), exist_ok=True)
        img.save(out, optimize=True)
        print(os.path.relpath(out, os.path.join(HERE, "..", "..")))


if __name__ == "__main__":
    main()
