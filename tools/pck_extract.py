# Dev helper: list / extract files from the game's Godot 4 .pck (reference only; nothing extracted is shipped).
import os, struct, sys

PCK = r"C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2\SlayTheSpire2.pck"

def entries(f):
    magic = f.read(4)
    assert magic == b'GDPC', magic
    ver, major, minor, patch = struct.unpack('<4I', f.read(16))
    base = 0
    if ver >= 2:
        flags, base = struct.unpack('<IQ', f.read(12))
        if ver >= 3:
            dir_ofs, = struct.unpack('<Q', f.read(8))
            f.seek(dir_ofs)
        else:
            f.read(16 * 4)
    else:
        f.read(16 * 4)
    n, = struct.unpack('<I', f.read(4))
    out = []
    for _ in range(n):
        ln, = struct.unpack('<I', f.read(4))
        path = f.read(ln).rstrip(b'\0').decode('utf-8', 'replace')
        ofs, size = struct.unpack('<QQ', f.read(16))
        f.read(16)
        fl = struct.unpack('<I', f.read(4))[0] if ver >= 2 else 0
        out.append((path, base + ofs, size, fl))
    return out

if __name__ == '__main__':
    pat = sys.argv[1]
    dest = sys.argv[2] if len(sys.argv) > 2 else None
    with open(PCK, 'rb') as f:
        for path, ofs, size, fl in entries(f):
            if pat in path:
                print(path, size, fl)
                if dest:
                    f.seek(ofs); data = f.read(size)
                    p = os.path.join(dest, path.replace('res://', '').replace('/', '_'))
                    open(p, 'wb').write(data)
