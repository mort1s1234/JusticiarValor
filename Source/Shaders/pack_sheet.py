"""Packs the Piloto Studio textures the composite effects sample into one sheet, and generates its cell table.

Sources: Assets/Piloto Studio/Textures of the user's MRKshader合集 Unity project (the originals are not modified).
Outputs:
  Common/Textures/Effects/JusticiarValor/Piloto/Sheet.png   2048x2048 sheet
  MJV_FxLib.shaderlib (between the SHEET markers)           MJV_SHEET_<NAME> rects for the shaders
  ../JusticiarValor/Fx/FxSheet.cs                           the same rects for C#
  PilotoTextures.tsv                                        provenance: cell, source file, crop, mode

Mask cells store their light in every channel (as the baked atlases do), so the shaders read alpha, which neither
engine decodes as sRGB. Colour cells keep the texture's own straight RGBA (the debris rocks).
Every cell keeps a transparent border, so clamped sampling and the first mip levels do not pick up its neighbours.
Rects are xy = uv origin (v from the bottom, the sampling convention of loose mod PNGs on both engines), zw = size.

Usage: python pack_sheet.py [--piloto <Piloto Studio/Textures folder>]
"""
import argparse
import os
import re

import numpy as np
from PIL import Image

HERE = os.path.dirname(os.path.abspath(__file__))
MOD_ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
DEFAULT_PILOTO = os.path.join(os.path.expanduser("~"), "Desktop", "project", "MRKshader合集", "Assets", "Piloto Studio", "Textures")
SIZE = 2048
PAD = 6

# name, source, (x, y, w, h) in the sheet (top-left origin), mode, options
CELLS = [
    # Celestial Pierce: the ground the blade breaks, the dust and the debris it throws up.
    ("RadialCrack", "GroundCracks_Radial.png", (0, 0, 512, 512), "mask", {}),
    ("DustBurst", "Shockwave_Dusty.png", (512, 0, 512, 512), "mask", {}),
    ("Fissure", "TentacleCrack.png", (0, 1280, 1024, 256), "mask", {"rotate": 90, "trim": True, "luma": True, "normalize": False}),
    ("Puffs", "SmokePuff_Quad.png", (1536, 1280, 512, 512), "mask", {}),
    ("Rocks", "Dust_Rocks.png", (1024, 1536, 256, 256), "color", {}),
    # Psy-orb impact.
    ("ImpactFlare", "StarImpactFlare.png", (1024, 0, 512, 512), "mask", {}),
    ("JaggedRing", "Ring_Shockwave2.png", (1536, 0, 512, 512), "mask", {}),
    # Threat sense: sight brackets, the HUD ring and the ping's front.
    ("SightInner", "FX_Tech_Sight_InnerSide_Glowy_T.png", (0, 512, 512, 512), "mask", {}),
    ("HudRing", "Tech_DecalRing2.png", (512, 512, 512, 512), "mask", {}),
    ("PingRing", "Ring_Shockwave.png", (1536, 512, 512, 512), "mask", {}),
    # Warlockbane: the side of the ward that turns the psycast, lit towards the caster.
    ("WardArc", "Ring_EdgeLower.png", (1024, 512, 512, 512), "mask", {}),
    ("Chevron", "FX_Tech_Shapes_Lined_T_Rotated.png", (0, 1536, 256, 256), "mask", {"crop": (0, 0, 512, 512)}),
    # Psy-orb flight: the wake ribbons, its soft core and the sparkles it sheds.
    ("TrailWisp", "RemasterList/Trail13.png", (0, 1024, 1024, 256), "mask", {}),
    ("TrailStarry", "Trail_Starry_2.png", (1024, 1024, 1024, 256), "mask", {}),
    ("Sparkles", "SparklesPickbook.png", (1024, 1280, 512, 256), "mask", {}),
    ("TrailCore", "Trail_TaperedSoft.png", (256, 1536, 256, 256), "mask", {}),
    # Shared glints and glow.
    ("CrossFlare", "StarCrossFlare.png", (512, 1536, 256, 256), "mask", {}),
    ("SoftGlow", "Glowing_Soft_02.png", (768, 1536, 256, 256), "mask", {}),
]


def load_mask(path, opts):
    im = Image.open(path)
    if "crop" in opts:
        im = im.crop(opts["crop"])
    rgba = np.asarray(im.convert("RGBA")).astype(np.float32) / 255.0
    rgb, alpha = rgba[..., :3], rgba[..., 3]
    # luma: keep a coloured glow dimmer than the white core it surrounds (max would flatten both to full light).
    light = rgb @ np.array([0.30, 0.59, 0.11], dtype=np.float32) if opts.get("luma") else rgb.max(axis=2)
    # Opaque textures carry their shape in brightness, the others in alpha (modulated by any colour they have).
    mask = light if alpha.min() > 0.99 else alpha * light
    if opts.get("normalize", True):
        peak = np.percentile(mask[mask > 0.02], 99.5) if (mask > 0.02).any() else 1.0
        mask = mask / max(peak, 0.05)
    mask = np.clip(mask, 0.0, 1.0)
    img = Image.fromarray((mask * 255.0 + 0.5).astype(np.uint8), "L")
    if opts.get("rotate"):
        img = img.rotate(opts["rotate"], expand=True)
    if opts.get("trim"):
        box = img.point(lambda v: 255 if v > 8 else 0).getbbox()
        if box:
            img = img.crop(box)
    return img


def place(sheet, img, rect, mode):
    x, y, w, h = rect
    inner = img.resize((w - 2 * PAD, h - 2 * PAD), Image.LANCZOS)
    if mode == "mask":
        m = inner
        inner = Image.merge("RGBA", (m, m, m, m))
    sheet.paste(inner, (x + PAD, y + PAD))


def uv_rect(rect):
    x, y, w, h = rect
    return ((x + PAD) / SIZE, 1.0 - (y + h - PAD) / SIZE, (w - 2 * PAD) / SIZE, (h - 2 * PAD) / SIZE)


def fmt(v):
    return f"{v:.6f}"


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--piloto", default=DEFAULT_PILOTO)
    args = parser.parse_args()

    sheet = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    occupied = np.zeros((SIZE, SIZE), dtype=bool)
    rows = []
    for name, src, rect, mode, opts in CELLS:
        x, y, w, h = rect
        if occupied[y:y + h, x:x + w].any():
            raise SystemExit(f"{name} overlaps another cell")
        occupied[y:y + h, x:x + w] = True
        path = os.path.join(args.piloto, src)
        if mode == "mask":
            img = load_mask(path, opts)
        else:
            img = Image.open(path).convert("RGBA")
        place(sheet, img, rect, mode)
        rows.append((name, src, rect, mode, opts))

    tex_dir = os.path.join(MOD_ROOT, "Common", "Textures", "Effects", "JusticiarValor", "Piloto")
    os.makedirs(tex_dir, exist_ok=True)
    sheet.save(os.path.join(tex_dir, "Sheet.png"), optimize=True)

    # Shader constants, between the markers of the shared library.
    lib_path = os.path.join(HERE, "MJV_FxLib.shaderlib")
    with open(lib_path, encoding="utf-8") as f:
        lib = f.read()
    defines = "\n".join(f"#define MJV_SHEET_{name.upper()} vec4({', '.join(fmt(v) for v in uv_rect(rect))})"
                        for name, _, rect, _, _ in rows)
    block = ("// BEGIN SHEET (generated by pack_sheet.py: cells of Common/Textures/Effects/JusticiarValor/Piloto/Sheet.png)\n"
             + defines + "\n// END SHEET")
    lib, count = re.subn(r"// BEGIN SHEET.*?// END SHEET", lambda _: block, lib, flags=re.S)
    if count != 1:
        raise SystemExit("MJV_FxLib.shaderlib needs exactly one // BEGIN SHEET ... // END SHEET block")
    with open(lib_path, "w", encoding="utf-8", newline="\n") as f:
        f.write(lib)

    cs = ["// Generated by Source/Shaders/pack_sheet.py. Do not edit; edit the cell table there and rerun it.",
          "using Vector4 = UnityEngine.Vector4;", "", "namespace JusticiarValor", "{",
          "    // Cells of the packed Piloto texture sheet (Common/Textures/Effects/JusticiarValor/Piloto/Sheet.png): uv origin and size.",
          "    public static class FxSheet", "    {"]
    for name, _, rect, _, _ in rows:
        u, v, du, dv = uv_rect(rect)
        cs.append(f"        public static readonly Vector4 {name} = new Vector4({fmt(u)}f, {fmt(v)}f, {fmt(du)}f, {fmt(dv)}f);")
    cs += ["    }", "}", ""]
    with open(os.path.join(MOD_ROOT, "Source", "JusticiarValor", "Fx", "FxSheet.cs"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(cs))

    with open(os.path.join(HERE, "PilotoTextures.tsv"), "w", encoding="utf-8", newline="\n") as f:
        f.write("cell\tsource\tsheetRect\tmode\toptions\n")
        for name, src, rect, mode, opts in rows:
            f.write(f"{name}\tAssets/Piloto Studio/Textures/{src}\t{','.join(map(str, rect))}\t{mode}\t{opts if opts else ''}\n")
    print(f"Sheet: {len(rows)} cells -> {os.path.join(tex_dir, 'Sheet.png')}")


if __name__ == "__main__":
    main()
