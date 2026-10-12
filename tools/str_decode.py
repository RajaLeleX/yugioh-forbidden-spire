# Prototype PS1 STR (MDEC v2) frame decoder, checked against ffmpeg frames. Then ported to C#.
import os, struct, numpy as np
from PIL import Image

VLC = [(0x3,2),(0x4,4),(0x5,5),(0x6,7),(0x26,8),(0x21,8),(0xa,10),(0x1d,12),(0x18,12),(0x13,12),(0x10,12),(0x1a,13),(0x19,13),(0x18,13),(0x17,13),(0x1f,14),
 (0x1e,14),(0x1d,14),(0x1c,14),(0x1b,14),(0x1a,14),(0x19,14),(0x18,14),(0x17,14),(0x16,14),(0x15,14),(0x14,14),(0x13,14),(0x12,14),(0x11,14),(0x10,14),(0x18,15),
 (0x17,15),(0x16,15),(0x15,15),(0x14,15),(0x13,15),(0x12,15),(0x11,15),(0x10,15),(0x3,3),(0x6,6),(0x25,8),(0xc,10),(0x1b,12),(0x16,13),(0x15,13),(0x1f,15),
 (0x1e,15),(0x1d,15),(0x1c,15),(0x1b,15),(0x1a,15),(0x19,15),(0x13,16),(0x12,16),(0x11,16),(0x10,16),(0x5,4),(0x4,7),(0xb,10),(0x14,12),(0x14,13),(0x7,5),
 (0x24,8),(0x1c,12),(0x13,13),(0x6,5),(0xf,10),(0x12,12),(0x7,6),(0x9,10),(0x12,13),(0x5,6),(0x1e,12),(0x14,16),(0x4,6),(0x15,12),(0x7,7),(0x11,12),
 (0x5,7),(0x11,13),(0x27,8),(0x10,13),(0x23,8),(0x1a,16),(0x22,8),(0x19,16),(0x20,8),(0x18,16),(0xe,10),(0x17,16),(0xd,10),(0x16,16),(0x8,10),(0x15,16),
 (0x1f,12),(0x1a,12),(0x19,12),(0x17,12),(0x16,12),(0x1f,13),(0x1e,13),(0x1d,13),(0x1c,13),(0x1b,13),(0x1f,16),(0x1e,16),(0x1d,16),(0x1c,16),(0x1b,16)]
LEVEL = list(range(1,41)) + list(range(1,19)) + [1,2,3,4,5] + [1,2,3,4] + [1,2,3]*3 + [1,2]*10 + [1]*15
RUN = [0]*40 + [1]*18 + [2]*5 + [3]*4 + [4]*3 + [5]*3 + [6]*3 + [7,7,8,8,9,9,10,10,11,11,12,12,13,13,14,14,15,15,16,16] + list(range(17,32))
assert len(VLC) == len(LEVEL) == len(RUN) == 111
CODES = {(l, c): i for i, (c, l) in enumerate(VLC)}
ZZ = [0,1,8,16,9,2,3,10,17,24,32,25,18,11,4,5,12,19,26,33,40,48,41,34,27,20,13,6,7,14,21,28,35,42,49,56,57,50,43,36,29,22,15,23,30,37,44,51,58,59,52,45,38,31,39,46,53,60,61,54,47,55,62,63]
QM = [2,16,19,22,26,27,29,34,16,16,22,24,27,29,34,37,19,22,26,27,29,34,34,38,22,22,26,27,29,34,37,40,22,26,27,29,32,35,40,48,26,27,29,32,35,40,48,58,26,27,29,34,38,46,56,69,27,29,35,38,46,56,69,83]
C = np.array([[ (np.sqrt(1/8) if k == 0 else np.sqrt(2/8)) * np.cos((2*n+1)*k*np.pi/16) for n in range(8)] for k in range(8)])

class Bits:
    def __init__(s, data): s.d = data; s.pos = 0; s.cur = 0; s.n = 0
    def bit(s):
        if s.n == 0:
            s.cur = s.d[s.pos] | (s.d[s.pos + 1] << 8) if s.pos + 1 < len(s.d) else 0; s.pos += 2; s.n = 16
        s.n -= 1
        return (s.cur >> s.n) & 1
    def bits(s, k):
        v = 0
        for _ in range(k): v = (v << 1) | s.bit()
        return v

QS = 1
MQ = [8,16,19,22,26,27,29,34,16,16,22,24,27,29,34,37,19,22,26,27,29,34,34,38,22,22,26,27,29,34,37,40,22,26,27,29,32,35,40,48,26,27,29,32,35,40,48,58,26,27,29,34,38,46,56,69,27,29,35,38,46,56,69,83]
def block(br):
    dc = br.bits(10)
    if dc & 0x200: dc -= 0x400
    coef = np.zeros(64)
    coef[0] = 2 * dc
    i = 0
    while True:
        code = 0; ln = 0
        while True:
            code = (code << 1) | br.bit(); ln += 1
            if ln == 2 and code == 2: return coef, 0
            if ln == 6 and code == 1:
                run = br.bits(6); lv = br.bits(10)
                if lv & 0x200: lv -= 0x400
                i += run + 1
                if i > 63: return coef, 0
                j = ZZ[i]
                v = (abs(lv) * QS * MQ[j]) >> 3
                v = (v - 1) | 1
                coef[j] = v if lv >= 0 else -v
                break
            if (ln, code) in CODES:
                k = CODES[(ln, code)]; run = RUN[k]; lv = LEVEL[k]
                i += run + 1
                if i > 63: return coef, 0
                j = ZZ[i]
                v = (lv * QS * MQ[j]) >> 3
                coef[j] = -v if br.bit() else v
                break
            if ln > 17: return coef, 0

def idct(coef):
    m = coef.reshape(8, 8)
    return C.T @ m @ C

def frame(data, w, h):
    br = Bits(data[8:])
    global QS; QS = data[4] | (data[5] << 8)
    out = np.zeros((h, w, 3))
    for mx in range(w // 16):
        for my in range(h // 16):
            blocks = [idct(block(br)[0]) for _ in range(6)]
            cr, cb = np.kron(blocks[0], np.ones((2, 2))), np.kron(blocks[1], np.ones((2, 2)))
            y = np.zeros((16, 16))
            y[:8, :8], y[:8, 8:], y[8:, :8], y[8:, 8:] = blocks[2], blocks[3], blocks[4], blocks[5]
            y = y + 128
            r = y + 1.402 * cr; g = y - 0.3437 * cb - 0.7143 * cr; b = y + 1.772 * cb
            out[my*16:my*16+16, mx*16:mx*16+16] = np.stack([r, g, b], -1)
    return np.clip(out, 0, 255).astype(np.uint8)

if __name__ == '__main__':
    raw = open(os.path.expanduser('~/fmsts/cache/MOVIE_raw.str'), 'rb').read()
    frames = []; cur = {}; want = 0
    for i in range(len(raw) // 2352):
        s = raw[i*2352:(i+1)*2352]
        if s[18] & 4: continue
        d = s[24:24+2048]
        if struct.unpack_from('<H', d, 0)[0] != 0x160: continue
        cn, cc, fr = struct.unpack_from('<HHI', d, 4)
        if cn == 0: cur = {}; want = cc
        cur[cn] = d[32:]
        if len(cur) == want: frames.append(b''.join(cur[k] for k in sorted(cur)))
    for k, fr in enumerate((0, len(frames) // 2, len(frames) - 1)):
        img = frame(frames[fr], 320, 240)
        Image.fromarray(img).save(os.path.expanduser(f'~/fmsts/shots/mdec_{k}.png'))
    print('frames', len(frames))
