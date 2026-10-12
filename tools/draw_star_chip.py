# Original artwork for the Star Chip currency icon: a beveled golden five-point star with a soft glow. Made for this mod.
import os, math
from PIL import Image, ImageDraw, ImageFilter

S = 256
C = S / 2

def star(r_out, r_in, cx=C, cy=C + 6, rot=-90):
    pts = []
    for i in range(10):
        r = r_out if i % 2 == 0 else r_in
        a = math.radians(rot + i * 36)
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts

img = Image.new('RGBA', (S, S), (0, 0, 0, 0))

glow = Image.new('RGBA', (S, S), (0, 0, 0, 0))
ImageDraw.Draw(glow).polygon(star(118, 52), fill=(255, 220, 90, 150))
img = Image.alpha_composite(img, glow.filter(ImageFilter.GaussianBlur(10)))

body = Image.new('RGBA', (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(body)
d.polygon(star(112, 48), fill=(70, 38, 0, 255))           # dark outline
d.polygon(star(102, 43), fill=(214, 150, 24, 255))         # base gold
# bevel: each point split into a lit and a shaded facet
outer, inner = star(102, 43), star(102, 43)
cx, cy = C, C + 6
for i in range(0, 10, 2):
    tip = outer[i]
    left, right = inner[(i - 1) % 10], inner[(i + 1) % 10]
    lit = i in (0, 8, 6)
    d.polygon([(cx, cy), left, tip], fill=(255, 226, 110, 255) if lit else (236, 180, 40, 255))
    d.polygon([(cx, cy), tip, right], fill=(240, 190, 52, 255) if lit else (176, 112, 10, 255))
img = Image.alpha_composite(img, body)

# sparkle highlight
hl = Image.new('RGBA', (S, S), (0, 0, 0, 0))
hd = ImageDraw.Draw(hl)
hd.ellipse([C - 34, C - 44, C - 6, C - 16], fill=(255, 255, 230, 200))
img = Image.alpha_composite(img, hl.filter(ImageFilter.GaussianBlur(5)))

out = os.path.expanduser('~/fmsts/dist/FMDuelist/star_chip.png')
img.save(out)
print(out)
