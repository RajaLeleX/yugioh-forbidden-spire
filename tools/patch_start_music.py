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
m['startRoom'] = 44
json.dump(m, open(f'{H}/sheets/media.json', 'w'), indent=1)
edit('tools/codegen.py', [(
    "         f\"    public const int RestSiteTrack = {M['restSite_BuildDeck']};\",",
    "         f\"    public const int RestSiteTrack = {M['restSite_BuildDeck']};\",\n"
    "         f\"    public const int StartRoomTrack = {M['startRoom']};\",")])
edit('src/FMDuelist/Game/FmMusicImpl.cs', [(
    "        if (room is RestSiteRoom) { Play(FmMedia.RestSiteTrack); return; }",
    "        if (room is RestSiteRoom) { Play(FmMedia.RestSiteTrack); return; }\n"
    "        // the run's starting room (Neow, before floor 1)\n"
    "        if (room.RoomType == RoomType.Event && owner.RunState.CurrentActIndex == 0 && owner.RunState.ActFloor == 0) { Play(FmMedia.StartRoomTrack); return; }")])
sy = f'{H}/sheets/systems.json'
rows = json.load(open(sy))
if not any(r['id'] == 'start_music' for r in rows):
    rows.append({"id": "start_music", "what": "The run's starting room (Neow) plays media.json startRoom (track 44)", "value": 44, "hook": "music"})
json.dump(rows, open(sy, 'w'), indent=1)
print('ok')
