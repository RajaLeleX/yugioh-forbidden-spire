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
m['actStartRoom'] = [m.pop('startRoom', 44), 26, -1]
m['actStartNote'] = "Track for each act's starting room (Ancient event before floor 1); -1 keeps the game's own music."
json.dump(m, open(f'{H}/sheets/media.json', 'w'), indent=1)

edit('tools/codegen.py', [(
    "         f\"    public const int StartRoomTrack = {M['startRoom']};\",",
    "         '    public static readonly int[] ActStartTracks = { ' + ', '.join(map(str, M['actStartRoom'])) + ' };',")])
edit('src/FMDuelist/Game/FmMusicImpl.cs', [(
    "        // the run's starting room (Neow, before floor 1)\n"
    "        if (room.RoomType == RoomType.Event && owner.RunState.CurrentActIndex == 0 && owner.RunState.ActFloor == 0) { Play(FmMedia.StartRoomTrack); return; }",
    "        // each act's starting room (the Ancient before floor 1)\n"
    "        if (room.RoomType == RoomType.Event && owner.RunState.ActFloor == 0)\n"
    "        {\n"
    "            int a = owner.RunState.CurrentActIndex;\n"
    "            if (a >= 0 && a < FmMedia.ActStartTracks.Length && FmMedia.ActStartTracks[a] >= 0) { Play(FmMedia.ActStartTracks[a]); return; }\n"
    "        }")])
sy = f'{H}/sheets/systems.json'
rows = json.load(open(sy))
for r in rows:
    if r['id'] == 'start_music':
        r['what'] = "Each act's starting room plays media.json actStartRoom (act 1: 44, act 2: 26, act 3: game music)"
json.dump(rows, open(sy, 'w'), indent=1)
print('ok')
