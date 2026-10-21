"""Draws the ability and command icons of Justiciar Valor into Common/Textures/UI/Abilities.

The style follows the mod's art: thick black outlines, silver-white shapes and eltex mint accents. Each icon is drawn
at 4x and downsampled for clean edges. Usage: python make_icons.py
"""
import math
import os
from PIL import Image, ImageDraw, ImageFilter

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, "..", "..", "Common", "Textures", "UI", "Abilities")
SIZE = 128
S = 4  # supersampling
W = SIZE * S

BLACK = (12, 14, 14, 255)
SILVER = (226, 232, 230, 255)
SILVER_DARK = (150, 160, 158, 255)
MINT = (150, 222, 200, 255)
MINT_DEEP = (84, 160, 146, 255)
GLOW = (190, 255, 236, 255)
VIOLET = (176, 150, 250, 255)


def canvas():
    return Image.new("RGBA", (W, W), (0, 0, 0, 0))


def finish(img, name):
    os.makedirs(OUT, exist_ok=True)
    img = img.resize((SIZE, SIZE), Image.LANCZOS)
    img.save(os.path.join(OUT, name + ".png"))
    print(name)


def p(x, y):
    return (x * S, y * S)


def ring(d, cx, cy, r, width, color, start=0, end=360):
    box = [(cx - r) * S, (cy - r) * S, (cx + r) * S, (cy + r) * S]
    d.arc(box, start, end, fill=color, width=int(width * S))


def outlined_polygon(d, pts, fill, outline=BLACK, width=5):
    d.polygon([p(x, y) for x, y in pts], fill=fill)
    d.line([p(x, y) for x, y in pts + [pts[0]]], fill=outline, width=int(width * S), joint="curve")


def glow_layer(draw_fn, radius=6):
    layer = canvas()
    draw_fn(ImageDraw.Draw(layer))
    return layer.filter(ImageFilter.GaussianBlur(radius * S))


def justiciar_valor():
    img = canvas()
    # Soft psychic glow behind the rings.
    img.alpha_composite(glow_layer(lambda d: ring(d, 64, 64, 44, 10, (150, 255, 220, 160)), 5))
    d = ImageDraw.Draw(img)
    # Two shock rings broken into plates, outlined.
    for r, w in ((50, 9), (35, 7)):
        for k in range(8):
            a0 = k * 45 + 6
            a1 = a0 + 33
            ring(d, 64, 64, r, w + 6, BLACK, a0 - 2, a1 + 2)
            ring(d, 64, 64, r, w, MINT if r == 50 else SILVER, a0, a1)
    # Radial shock rays.
    for k in range(8):
        a = math.radians(k * 45 + 22.5)
        x0, y0 = 64 + math.cos(a) * 18, 64 + math.sin(a) * 18
        x1, y1 = 64 + math.cos(a) * 27, 64 + math.sin(a) * 27
        d.line([p(x0, y0), p(x1, y1)], fill=BLACK, width=8 * S)
        d.line([p(x0, y0), p(x1, y1)], fill=GLOW, width=4 * S)
    # Eltex core: a crested helmet-like diamond.
    core = [(64, 44), (77, 64), (64, 84), (51, 64)]
    outlined_polygon(d, core, SILVER, width=5)
    outlined_polygon(d, [(64, 52), (70, 64), (64, 76), (58, 64)], MINT, width=2)
    finish(img, "MJV_JusticiarValor")


def invictus():
    img = canvas()
    d = ImageDraw.Draw(img)
    chevron = [(0, -26), (22, 4), (12, 4), (0, -12), (-12, 4), (-22, 4)]
    # Afterimages: violet, then mint, then the solid chevron in front.
    for off, col, alpha in ((-30, VIOLET, 120), (-15, MINT, 170)):
        ghost = canvas()
        gd = ImageDraw.Draw(ghost)
        pts = [(64 + x, 70 + y - off) for x, y in chevron]
        gd.polygon([p(x, y) for x, y in pts], fill=col[:3] + (alpha,))
        img.alpha_composite(ghost)
    d = ImageDraw.Draw(img)
    for dy in (0, 26):
        pts = [(64 + x, 76 + y - dy) for x, y in chevron]
        outlined_polygon(d, pts, SILVER if dy == 0 else MINT, width=5)
    # Speed streaks.
    for x, y0, y1 in ((30, 30, 70), (98, 30, 70), (22, 50, 98), (106, 50, 98)):
        d.line([p(x, y0), p(x, y1)], fill=BLACK, width=7 * S)
        d.line([p(x, y0 + 2), p(x, y1 - 2)], fill=GLOW, width=3 * S)
    finish(img, "MJV_Invictus")


def celestial_pierce():
    img = canvas()
    angle = math.radians(-45)
    ca, sa = math.cos(angle), math.sin(angle)

    def blade_pts(length, width):
        pts = []
        for i in range(0, 41):
            t = i / 40
            w = width * (0.35 + 0.65 * min(1, t / 0.2)) * (1 - t) ** 0.7
            pts.append((t * length, w))
        top = pts
        bottom = [(x, -y) for x, y in reversed(pts)]
        return top + bottom

    def place(pts, ox, oy):
        return [(ox + x * ca - y * sa, oy + x * sa + y * ca) for x, y in pts]

    img.alpha_composite(glow_layer(lambda d: d.polygon([p(x, y) for x, y in place(blade_pts(118, 18), 16, 112)], fill=(150, 210, 255, 200)), 5))
    d = ImageDraw.Draw(img)
    outlined_polygon(d, place(blade_pts(118, 16), 16, 112), (200, 230, 255, 255), width=5)
    # Fuller and a glint.
    d.line([p(*place([(8, 0)], 16, 112)[0]), p(*place([(100, 0)], 16, 112)[0])], fill=(255, 255, 255, 255), width=3 * S)
    gx, gy = place([(66, 0)], 16, 112)[0]
    for a, l in ((0, 13), (90, 13), (45, 6), (135, 6)):
        r = math.radians(a)
        d.line([p(gx - math.cos(r) * l, gy - math.sin(r) * l), p(gx + math.cos(r) * l, gy + math.sin(r) * l)], fill=(255, 255, 255, 255), width=3 * S)
    # Hilt of the psyblade.
    hx, hy = place([(0, 0)], 16, 112)[0]
    outlined_polygon(d, place([(-5, 20), (5, 20), (5, -20), (-5, -20)], 16, 112), SILVER, width=4)
    d.ellipse([p(hx - 5, hy - 5), p(hx + 5, hy + 5)], fill=MINT, outline=BLACK, width=2 * S)
    finish(img, "MJV_CelestialPierce")


def psy_orb():
    img = canvas()
    img.alpha_composite(glow_layer(lambda d: d.ellipse([p(34, 34), p(94, 94)], fill=(150, 255, 220, 190)), 7))
    d = ImageDraw.Draw(img)
    # Orbit ring with two small orbs.
    ring(d, 64, 64, 46, 9, BLACK)
    ring(d, 64, 64, 46, 4, MINT_DEEP)
    for a in (210, 30):
        r = math.radians(a)
        x, y = 64 + math.cos(r) * 46, 64 + math.sin(r) * 46
        d.ellipse([p(x - 9, y - 9), p(x + 9, y + 9)], fill=MINT, outline=BLACK, width=4 * S)
    # Central orb with a white core.
    d.ellipse([p(42, 42), p(86, 86)], fill=MINT, outline=BLACK, width=5 * S)
    d.ellipse([p(52, 52), p(76, 76)], fill=GLOW)
    d.ellipse([p(57, 57), p(71, 71)], fill=(255, 255, 255, 255))
    finish(img, "MJV_PsyOrb")


if __name__ == "__main__":
    justiciar_valor()
    invictus()
    celestial_pierce()
    psy_orb()
