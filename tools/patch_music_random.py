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
jingles = [2, 44, 46, 47, 48, 53, 54]   # short non-looping stingers (under ~17 s)
m['battleRandomPool'] = [i for i in range(57) if i not in jingles]
m['battleTrackFloors'] = 2
m['battleNote'] = "Normal/elite fights pick a random track from battleRandomPool and keep it for battleTrackFloors floors; bosses and rest sites use their fixed tracks."
json.dump(m, open(f'{H}/sheets/media.json', 'w'), indent=1)

edit('tools/codegen.py', [(
    "         f\"    public const int RestSiteTrack = {M['restSite_BuildDeck']};\",",
    "         f\"    public const int RestSiteTrack = {M['restSite_BuildDeck']};\",\n"
    "         '    public static readonly int[] BattleRandomPool = { ' + ', '.join(map(str, M['battleRandomPool'])) + ' };',\n"
    "         f\"    public const int BattleTrackFloors = {M['battleTrackFloors']};\",")])

edit('src/FMDuelist/Game/FmMusicImpl.cs', [(
    """        int act = Math.Clamp(owner.RunState.CurrentActIndex, 0, 2);
        int track = room.RoomType == RoomType.Boss
            ? FmMedia.BossTracks[act]
            : FmMedia.DuelPool[_rng.Next(FmMedia.DuelPool.Length)];
        Play(track);""",
    """        int act = Math.Clamp(owner.RunState.CurrentActIndex, 0, 2);
        if (room.RoomType == RoomType.Boss) { Play(FmMedia.BossTracks[act]); return; }
        // normal and elite fights: a random FM track, changed only every BattleTrackFloors floors
        int floor = owner.RunState.TotalFloor;
        if (_battleTrack < 0 || floor - _battleTrackFloor >= FmMedia.BattleTrackFloors || floor < _battleTrackFloor)
        {
            _battleTrack = FmMedia.BattleRandomPool[_rng.Next(FmMedia.BattleRandomPool.Length)];
            _battleTrackFloor = floor;
        }
        Play(_battleTrack);"""),
    ("    private static readonly Random _rng = new();",
     "    private static readonly Random _rng = new();\n    private static int _battleTrack = -1, _battleTrackFloor = -99;"),
])

sy = f'{H}/sheets/systems.json'
rows = json.load(open(sy))
for r in rows:
    if r['id'] == 'fm_music':
        r['what'] = 'Normal/elite fights: random FM track (all non-jingle tracks), kept for 2 floors; bosses and rest sites fixed (media.json)'
json.dump(rows, open(sy, 'w'), indent=1)
print('ok', len(m['battleRandomPool']))
