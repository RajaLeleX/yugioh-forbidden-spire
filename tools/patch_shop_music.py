import os, json
H = os.path.expanduser('~/fmsts')

def edit(path, pairs):
    p = f'{H}/{path}'
    s = open(p, encoding='utf-8').read()
    for a, b in pairs:
        if a not in s:
            raise SystemExit(f'missing in {path}: {a[:80]!r}')
        s = s.replace(a, b, 1)
    open(p, 'w', encoding='utf-8').write(s)

m = json.load(open(f'{H}/sheets/media.json'))
m['actShop'] = [15, 9, 15]
m['actShopNote'] = "Track for each act's shops; -1 uses the act's map theme."
json.dump(m, open(f'{H}/sheets/media.json', 'w'), indent=1)

edit('tools/codegen.py', [(
    "         '    public static readonly int[] ActMapTracks = { ' + ', '.join(map(str, M['actMap'])) + ' };',",
    "         '    public static readonly int[] ActMapTracks = { ' + ', '.join(map(str, M['actMap'])) + ' };',\n"
    "         '    public static readonly int[] ActShopTracks = { ' + ', '.join(map(str, M.get('actShop', [-1, -1, -1]))) + ' };',")])

edit('src/FMDuelist/Game/FmMusicImpl.cs', [(
    "            int ma = owner.RunState.CurrentActIndex;\n",
    "            int ma = owner.RunState.CurrentActIndex;\n"
    "            if (room.RoomType == RoomType.Shop && ma >= 0 && ma < FmMedia.ActShopTracks.Length && FmMedia.ActShopTracks[ma] >= 0) { Play(FmMedia.ActShopTracks[ma]); return; }\n")])
print('ok')
