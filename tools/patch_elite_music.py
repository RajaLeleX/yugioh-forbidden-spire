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
m['eliteByAct'] = [
    [{"track": 29, "weight": 1.0}],
    [{"track": 30, "weight": 0.7}, {"track": 29, "weight": 0.3}],
    [{"track": 33, "weight": 0.6}, {"track": 30, "weight": 0.25}, {"track": 29, "weight": 0.15}],
]
m['eliteNote'] = "Elite fights pick a weighted track for the act (the act's own elite theme most likely; earlier acts' elite themes can return). Elite themes are kept out of battleRandomPool."
elite = {29, 30, 33}
m['battleRandomPool'] = [t for t in m['battleRandomPool'] if t not in elite]
json.dump(m, open(f'{H}/sheets/media.json', 'w'), indent=1)

edit('tools/codegen.py', [(
    "         '    public static readonly int[] ActMapTracks = {",
    "         '    public static readonly int[][] EliteTracks = { ' + ', '.join('new[] { ' + ', '.join(str(e['track']) for e in a) + ' }' for a in M['eliteByAct']) + ' };',\n"
    "         '    public static readonly double[][] EliteWeights = { ' + ', '.join('new[] { ' + ', '.join(repr(float(e['weight'])) for e in a) + ' }' for a in M['eliteByAct']) + ' };',\n"
    "         '    public static readonly int[] ActMapTracks = {")])

edit('src/FMDuelist/Game/FmMusicImpl.cs', [(
    "        if (room.RoomType == RoomType.Boss) { Play(FmMedia.BossTracks[act]); return; }\n",
    "        if (room.RoomType == RoomType.Boss) { Play(FmMedia.BossTracks[act]); return; }\n"
    "        if (room.RoomType == RoomType.Elite) { Play(PickElite(act)); return; }\n"),
    ("    /// <summary>Plays a track once (no loop)",
     "    /// <summary>Weighted pick from the act's elite themes (its own theme most likely).</summary>\n"
     "    static int PickElite(int act)\n    {\n"
     "        var tracks = FmMedia.EliteTracks[Math.Min(act, FmMedia.EliteTracks.Length - 1)];\n"
     "        var weights = FmMedia.EliteWeights[Math.Min(act, FmMedia.EliteWeights.Length - 1)];\n"
     "        double roll = _rng.NextDouble() * weights.Sum();\n"
     "        for (int i = 0; i < tracks.Length; i++) { roll -= weights[i]; if (roll < 0) return tracks[i]; }\n"
     "        return tracks[0];\n    }\n\n"
     "    /// <summary>Plays a track once (no loop)")])

sy = f'{H}/sheets/systems.json'
rows = json.load(open(sy))
if not any(r['id'] == 'elite_music' for r in rows):
    rows.append({"id": "elite_music", "what": "Elite fights: Act 1 #29; Act 2 #30 (70%) / #29 (30%); Act 3 #33 (60%) / #30 (25%) / #29 (15%) (media.eliteByAct)", "value": 29, "hook": "music"})
json.dump(rows, open(sy, 'w'), indent=1)
print('ok', m['battleRandomPool'])
