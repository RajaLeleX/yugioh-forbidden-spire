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
m['defeat_YouLose'] = 47
json.dump(m, open(f'{H}/sheets/media.json', 'w'), indent=1)
edit('tools/codegen.py', [(
    "         f\"    public const int BossFanfareTrack = {M['bossVictoryFanfare']};\",",
    "         f\"    public const int BossFanfareTrack = {M['bossVictoryFanfare']};\",\n"
    "         f\"    public const int DefeatTrack = {M['defeat_YouLose']};\",")])

open(f'{H}/src/FMDuelist/Game/FmDefeatMusic.cs', 'w').write('''using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;

namespace FMDuelist.Game;

/// <summary>Losing a run plays FM's "You Lose" music (once) on the game over screen.</summary>
[HarmonyPatch(typeof(NGameOverScreen), nameof(NGameOverScreen._Ready))]
static class DefeatMusicPatch
{
    static void Postfix()
    {
        try
        {
            if (!FmRules.RunActive || FmField.Duelist is not { } p || p.Creature.IsAlive) return;
            FmMusic.PlayOnce(FmMedia.DefeatTrack);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] defeat music: " + ex.Message); }
    }
}
''')
sy = f'{H}/sheets/systems.json'
rows = json.load(open(sy))
if not any(r['id'] == 'defeat_music' for r in rows):
    rows.append({"id": "defeat_music", "what": "Losing a run plays media.json defeat_YouLose (track 47) once on the game over screen", "value": 47, "hook": "music"})
json.dump(rows, open(sy, 'w'), indent=1)
print('ok')
