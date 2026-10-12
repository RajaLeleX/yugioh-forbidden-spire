# Original artwork: the shop's card-removal service as a big Star Chip with a card being cut. Made for this mod.
import os
from PIL import Image, ImageDraw, ImageFilter

S = 256
star = Image.open(os.path.expanduser('~/fmsts/dist/FMDuelist/star_chip.png')).convert('RGBA').resize((S, S), Image.LANCZOS)
img = star.copy()

# a small card, tilted, in the middle of the star
card = Image.new('RGBA', (70, 96), (0, 0, 0, 0))
cd = ImageDraw.Draw(card)
cd.rounded_rectangle([0, 0, 69, 95], 8, fill=(60, 34, 10, 255))
cd.rounded_rectangle([4, 4, 65, 91], 6, fill=(238, 226, 196, 255))
cd.rectangle([11, 12, 58, 50], fill=(150, 104, 40, 255))
for y in (60, 70, 80):
    cd.line([12, y, 57, y], fill=(150, 120, 80, 255), width=3)
card = card.rotate(-12, expand=True, resample=Image.BICUBIC)
img.alpha_composite(card, ((S - card.width) // 2, (S - card.height) // 2 + 14))

# red cut across the card
cut = Image.new('RGBA', (S, S), (0, 0, 0, 0))
d = ImageDraw.Draw(cut)
d.line([88, 182, 170, 94], fill=(60, 0, 0, 255), width=16)
d.line([88, 182, 170, 94], fill=(220, 40, 30, 255), width=10)
img.alpha_composite(cut)

out = os.path.expanduser('~/fmsts/dist/FMDuelist/star_chip_removal.png')
img.save(out)
print(out)
