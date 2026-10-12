# Finds a reference picture inside the player's FM disc files by trying raw PS1 image layouts at sector boundaries.
import os, sys, struct, numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(__file__))
from iso import Disc
H = os.path.expanduser('~/fmsts')
ref = Image.open(f'{H}/cache/ref_puzzle.png').convert('RGB')
W, Hh = ref.size
small = np.asarray(ref.resize((40, 20), Image.BILINEAR), np.float32)

def bgr555(a):
    a = a.astype(np.uint32)
    return np.stack([(a & 31) << 3, ((a >> 5) & 31) << 3, ((a >> 10) & 31) << 3], -1).astype(np.uint8)

def score(img):
    s = np.asarray(Image.fromarray(img).resize((40, 20), Image.BILINEAR), np.float32)
    return float(np.mean(np.abs(s - small)))

disc = Disc(sys.argv[1])
files = {p.split(';')[0]: (l, sz) for p, l, sz, _ in disc.walk()}
best = []
for name in ('/DATA/WA_MRG.MRG', '/DATA/SU.MRG', '/SLUS_014.11'):
    data = disc.read(*files[name])
    n16, n8 = W * Hh * 2, W * Hh
    for off in range(0, len(data) - n8, 0x800):
        # 16bpp direct
        if off + n16 <= len(data):
            a = np.frombuffer(data, np.uint16, W * Hh, off).reshape(Hh, W)
            best.append((score(bgr555(a)), name, off, '16bpp'))
        # 8bpp + 256 palette after
        if off + n8 + 512 <= len(data):
            px = np.frombuffer(data, np.uint8, n8, off).reshape(Hh, W)
            pal = bgr555(np.frombuffer(data, np.uint16, 256, off + n8))
            best.append((score(pal[px]), name, off, '8bpp+pal'))
        # palette before pixels
        if off + 512 + n8 <= len(data):
            pal = bgr555(np.frombuffer(data, np.uint16, 256, off))
            px = np.frombuffer(data, np.uint8, n8, off + 512).reshape(Hh, W)
            best.append((score(pal[px]), name, off, 'pal+8bpp'))
best.sort()
for b in best[:8]:
    print(round(b[0], 2), b[1], hex(b[2]), b[3])
