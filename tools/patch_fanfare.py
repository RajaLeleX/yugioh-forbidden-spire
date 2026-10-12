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
m['bossVictoryFanfare'] = 46
json.dump(m, open(f'{H}/sheets/media.json', 'w'), indent=1)
edit('tools/codegen.py', [(
    "         f\"    public const int StartRoomTrack = {M['startRoom']};\",",
    "         f\"    public const int StartRoomTrack = {M['startRoom']};\",\n"
    "         f\"    public const int BossFanfareTrack = {M['bossVictoryFanfare']};\",")])

edit('src/FMDuelist/Game/FmMusicImpl.cs', [
    ("    public static void Play(int index)\n    {\n        var bgm = FmAudio.Bgm;\n        if (bgm == null || index == _playing) return;\n        _playing = index;",
     "    /// <summary>Plays a track once (no loop), e.g. the act-boss victory fanfare.</summary>\n"
     "    public static void PlayOnce(int index)\n    {\n        _playing = -1;\n        _once = true;\n        Play(index);\n    }\n\n"
     "    static bool _once;\n\n"
     "    public static void Play(int index)\n    {\n        var bgm = FmAudio.Bgm;\n        if (bgm == null || index == _playing) return;\n        _playing = index;\n        bool once = _once;\n        _once = false;"),
    ("                Callable.From(() => StartOnMainThread(index, r.pcm, r.loop)).CallDeferred();",
     "                Callable.From(() => StartOnMainThread(index, r.pcm, once ? -2 : r.loop)).CallDeferred();"),
    ("            LoopMode = AudioStreamWav.LoopModeEnum.Forward, LoopBegin = Math.Max(0, loop), LoopEnd = frames,",
     "            LoopMode = loop == -2 ? AudioStreamWav.LoopModeEnum.Disabled : AudioStreamWav.LoopModeEnum.Forward, LoopBegin = Math.Max(0, loop), LoopEnd = frames,"),
])

edit('src/FMDuelist/Relics/MillenniumPuzzle.cs', [(
    "    public override Task AfterCombatEnd(CombatRoom room)",
    "    public override Task AfterCombatVictory(CombatRoom room)\n    {\n"
    "        if (room.RoomType == RoomType.Boss) FmMusic.PlayOnce(FmMedia.BossFanfareTrack);\n"
    "        return Task.CompletedTask;\n    }\n\n"
    "    public override Task AfterCombatEnd(CombatRoom room)")])

sy = f'{H}/sheets/systems.json'
rows = json.load(open(sy))
if not any(r['id'] == 'boss_fanfare' for r in rows):
    rows.append({"id": "boss_fanfare", "what": "Beating an act boss plays media.json bossVictoryFanfare (track 46) once", "value": 46, "hook": "music"})
json.dump(rows, open(sy, 'w'), indent=1)
print('ok')
