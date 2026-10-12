# Original artwork for the Millennium Puzzle relic icon: gold inverted pyramid, Eye of Wdjat, glow. Made for this mod.
import os, math
from PIL import Image, ImageDraw, ImageFilter

S = 512
img = Image.new('RGBA', (S, S), (0, 0, 0, 0))

# soft golden aura
aura = Image.new('RGBA', (S, S), (0, 0, 0, 0))
ad = ImageDraw.Draw(aura)
for r in range(230, 60, -6):
    a = int(110 * (1 - (r - 60) / 170) ** 2)
    ad.ellipse([S/2 - r, S/2 - r + 10, S/2 + r, S/2 + r + 10], fill=(255, 205, 80, a))
img = Image.alpha_composite(img, aura.filter(ImageFilter.GaussianBlur(18)))

# pyramid (point down), split into a lit left face and a shaded right face
top_l, top_r, bottom = (88, 150), (424, 150), (256, 470)
mid_top = (256, 150)
body = Image.new('RGBA', (S, S), (0, 0, 0, 0))
bd = ImageDraw.Draw(body)

def grad_poly(points, c0, c1, axis=1):
    mask = Image.new('L', (S, S), 0)
    ImageDraw.Draw(mask).polygon(points, fill=255)
    g = Image.new('RGBA', (S, S))
    gd = ImageDraw.Draw(g)
    for i in range(S):
        t = i / (S - 1)
        col = tuple(int(c0[k] + (c1[k] - c0[k]) * t) for k in range(3)) + (255,)
        if axis == 1: gd.line([(0, i), (S, i)], fill=col)
        else: gd.line([(i, 0), (i, S)], fill=col)
    body.paste(g, (0, 0), mask)

grad_poly([top_l, mid_top, bottom], (255, 228, 120), (176, 118, 24), axis=1)
grad_poly([mid_top, top_r, bottom], (222, 168, 52), (120, 74, 12), axis=1)
# top bevel
grad_poly([(80, 142), (432, 142), (424, 158), (88, 158)], (255, 245, 190), (214, 160, 50), axis=1)

# puzzle-piece seams
seam = (110, 68, 10, 255)
for y in (230, 310, 390):
    t = (y - 150) / (470 - 150)
    xl = top_l[0] + (bottom[0] - top_l[0]) * t
    xr = top_r[0] + (bottom[0] - top_r[0]) * t
    bd.line([(xl, y), (xr, y)], fill=seam, width=3)
bd.line([mid_top, bottom], fill=(130, 84, 16, 255), width=3)
for (x0, y0, x1, y1) in [(170, 150, 210, 230), (342, 150, 302, 230), (210, 230, 232, 310), (302, 230, 280, 310)]:
    bd.line([(x0, y0), (x1, y1)], fill=seam, width=2)

# outline
bd.polygon([top_l, top_r, bottom], outline=(90, 52, 6, 255))
bd.line([top_l, top_r, bottom, top_l], fill=(90, 52, 6, 255), width=5, joint='curve')

# Eye of Wdjat
cx, cy = 256, 252
eye = (40, 22, 4, 255)
bd.ellipse([cx - 62, cy - 26, cx + 62, cy + 26], outline=eye, width=7)
bd.ellipse([cx - 20, cy - 20, cx + 20, cy + 20], fill=eye)
bd.ellipse([cx - 7, cy - 12, cx + 3, cy - 2], fill=(255, 240, 200, 255))
bd.line([(cx - 70, cy - 40), (cx + 70, cy - 40)], fill=eye, width=7)        # brow
bd.line([(cx - 8, cy + 26), (cx - 18, cy + 74)], fill=eye, width=7)         # teardrop line
bd.arc([cx + 2, cy + 18, cx + 62, cy + 80], start=200, end=330, fill=eye, width=7)  # spiral tail
bd.line([(cx + 62, cy), (cx + 82, cy - 6)], fill=eye, width=7)

# loop ring on top (where the cord goes)
bd.ellipse([236, 86, 276, 128], outline=(190, 136, 34, 255), width=9)

img = Image.alpha_composite(img, body)

# sparkle
sp = Image.new('RGBA', (S, S), (0, 0, 0, 0))
sd = ImageDraw.Draw(sp)
sx, sy = 150, 176
for ang in range(0, 180, 45):
    L = 46 if ang % 90 == 0 else 22
    dx, dy = math.cos(math.radians(ang)) * L, math.sin(math.radians(ang)) * L
    sd.line([(sx - dx, sy - dy), (sx + dx, sy + dy)], fill=(255, 255, 235, 235), width=4)
img = Image.alpha_composite(img, sp.filter(ImageFilter.GaussianBlur(1.2)))
img = Image.alpha_composite(img, sp)

out = os.path.expanduser('~/fmsts/dist/FMDuelist/millennium_puzzle.png')
img.resize((256, 256), Image.LANCZOS).save(out)
img.resize((256, 256), Image.LANCZOS).save(os.path.expanduser('~/fmsts/shots/millennium_puzzle_icon.png'))
print(out)
