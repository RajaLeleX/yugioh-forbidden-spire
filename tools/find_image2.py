# Wider search for a reference picture: all disc files, 8bpp/4bpp/16bpp, palette before/after, sector or 4-byte aligned TIMs.
import os, sys, struct, numpy as np
from PIL import Image
sys.path.insert(0, os.path.dirname(__file__))
from iso import Disc
H = os.path.expanduser('~/fmsts')
ref = Image.open(f'{H}/cache/ref_puzzle.png').convert('RGB')
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
sizes = [(320, 160), (320, 240), (256, 128), (256, 256), (320, 128)]
for name in ('/DATA/MODEL.MRG', '/DATA/SU.MRG', '/DATA/WA_MRG.MRG'):
    data = disc.read(*files[name])
    print(name, len(data), flush=True)
    # TIM files anywhere (4-byte aligned)
    i = 0
    while True:
        i = data.find(b'\x10\x00\x00\x00', i)
        if i < 0 or i + 40 > len(data): break
        flags = struct.unpack_from('<I', data, i + 4)[0]
        if i % 4 == 0 and flags in (8, 9, 2):
            try:
                p = i + 8; pal = None
                if flags & 8:
                    blen = struct.unpack_from('<I', data, p)[0]; cw, ch = struct.unpack_from('<HH', data, p + 8)
                    pal = bgr555(np.frombuffer(data, np.uint16, cw * ch, p + 12)); p += blen
                blen = struct.unpack_from('<I', data, p)[0]; ww, hh = struct.unpack_from('<HH', data, p + 8)
                bpp = 4 if (flags & 7) == 0 else 8 if (flags & 7) == 1 else 16
                w = ww * (16 // bpp)
                if 64 <= w <= 640 and 32 <= hh <= 512:
                    raw = np.frombuffer(data, np.uint8, ww * hh * 2, p + 12)
                    if bpp == 8: img = pal[raw.reshape(hh, w)]
                    elif bpp == 4:
                        idx = np.stack([raw & 15, raw >> 4], -1).reshape(hh, w); img = pal[idx]
                    else: img = bgr555(np.frombuffer(data, np.uint16, w * hh, p + 12)).reshape(hh, w, 3)
                    best.append((score(img), name, i, f'TIM {w}x{hh} {bpp}bpp'))
            except Exception:
                pass
        i += 4
    for (W, Hh) in sizes:
        n8 = W * Hh
        for off in range(0, len(data) - n8 - 512, 0x800):
            px = np.frombuffer(data, np.uint8, n8, off).reshape(Hh, W)
            pal = bgr555(np.frombuffer(data, np.uint16, 256, off + n8))
            best.append((score(pal[px]), name, off, f'{W}x{Hh} 8bpp+pal'))
            pal2 = bgr555(np.frombuffer(data, np.uint16, 256, off))
            px2 = np.frombuffer(data, np.uint8, n8, off + 512).reshape(Hh, W)
            best.append((score(pal2[px2]), name, off, f'{W}x{Hh} pal+8bpp'))
    best.sort(); best = best[:30]
for b in best[:12]:
    print(round(b[0], 2), b[1], hex(b[2]), b[3])
