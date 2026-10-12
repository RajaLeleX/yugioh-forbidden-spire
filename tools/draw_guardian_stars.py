# Original icons for the Guardian Star items: the ten planet symbols (Sun, Moon, Mercury, Venus, Mars, Jupiter, Saturn,
# Uranus, Pluto, Neptune) drawn as a grey-mauve glyph with a dark navy outline on a soft round star badge.
import os
from PIL import Image, ImageDraw, ImageFont, ImageFilter

OUT = os.path.expanduser('~/fmsts/dist/FMDuelist/stars')
os.makedirs(OUT, exist_ok=True)
GLYPHS = {'sun': '\u2609', 'moon': '\u263D', 'mercury': '\u263F', 'venus': '\u2640', 'mars': '\u2642',
          'jupiter': '\u2643', 'saturn': '\u2644', 'uranus': '\u2645', 'neptune': '\u2646', 'pluto': '\u2647'}
FONTS = [r'C:\Windows\Fonts\seguisym.ttf', r'C:\Windows\Fonts\seguiemj.ttf', r'C:\Windows\Fonts\arialuni.ttf']
font_path = next(f for f in FONTS if os.path.exists(f))
S = 256
FILL, LINE = (178, 163, 160, 255), (14, 28, 66, 255)
for name, ch in GLYPHS.items():
    img = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    # faint golden halo: the "guardian star" glow
    halo = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    ImageDraw.Draw(halo).ellipse([28, 28, S - 28, S - 28], fill=(255, 214, 120, 120))
    img = Image.alpha_composite(img, halo.filter(ImageFilter.GaussianBlur(22)))
    font = ImageFont.truetype(font_path, 170)
    d = ImageDraw.Draw(img)
    box = d.textbbox((0, 0), ch, font=font, stroke_width=12)
    w, h = box[2] - box[0], box[3] - box[1]
    d.text(((S - w) / 2 - box[0], (S - h) / 2 - box[1]), ch, font=font, fill=FILL, stroke_width=12, stroke_fill=LINE)
    img.save(f'{OUT}/{name}.png')
print('ok', font_path, sorted(os.listdir(OUT)))
