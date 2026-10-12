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

E = json.load(open(f'{H}/sheets/removed_energy.json'))
upgrade_relics = ["ArchaicTooth", "Astrolabe", "Bellows", "BoneTea", "Claws", "DustyTome", "FishingRod", "FragrantMushroom", "FrozenEgg",
                  "LavaLamp", "MiniatureCannon", "MoltenEgg", "NeowsTalisman", "PaelsTooth", "Pomander", "RazorTooth", "SandCastle",
                  "SilverCrucible", "StoneCracker", "ToxicEgg", "WarHammer", "WarPaint", "Whetstone", "YummyCookie"]
upgrade_potions = ["BlessingOfTheForge", "CosmicConcoction", "CunningPotion"]
upgrade_events = ["AromaOfChaos", "BattlewornDummy", "DoorsOfLightAndDark", "EndlessConveyor", "Reflections", "SapphireSeed",
                  "SpiritGrafter", "TabletOfTruth", "TheFutureOfPotions", "Trial"]
E['relics'] = sorted(set(E['relics'] + upgrade_relics))
E['potions'] = sorted(set(E['potions'] + upgrade_potions))
E['events'] = sorted(set(E.get('events', []) + upgrade_events))
E['note'] = ("Removed from FM runs: everything that touches energy (FM cards cost 0) and everything that upgrades cards "
             "(Forbidden Memories has no upgrades): relics, potions, and events whose options upgrade cards.")
json.dump(E, open(f'{H}/sheets/removed_energy.json', 'w'), indent=1)

# generated names + event filter
p = f'{H}/tools/patch_energy.py'
t = open(p, encoding='utf-8').read()
t = t.replace("    '    static readonly string[] PotionNames = { ' + ', '.join(f'\"{n}\"' for n in E['potions']) + ' };\\n}\\n')",
              "    '    static readonly string[] PotionNames = { ' + ', '.join(f'\"{n}\"' for n in E['potions']) + ' };\\n'\n"
              "    '    static readonly string[] EventNames = { ' + ', '.join(f'\"{n}\"' for n in E.get('events', [])) + ' };\\n}\\n')")
open(p, 'w', encoding='utf-8').write(t)
import subprocess
subprocess.run(['python', p], check=True)

edit('src/FMDuelist/FmEnergy.cs', [
    ("    static HashSet<Type>? _relics, _potions;", "    static HashSet<Type>? _relics, _potions, _events;"),
    ("    public static bool IsEnergyPotion(PotionModel p) => (_potions ??= Resolve(PotionNames, \"Potions\")).Contains(p.GetType());",
     "    public static bool IsEnergyPotion(PotionModel p) => (_potions ??= Resolve(PotionNames, \"Potions\")).Contains(p.GetType());\n"
     "    public static bool IsRemovedEvent(EventModel e) => (_events ??= Resolve(EventNames, \"Events\")).Contains(e.GetType());"),
])
t = open(f'{H}/src/FMDuelist/FmEnergy.cs', encoding='utf-8').read()
t = t.rstrip() + '''

[HarmonyPatch(typeof(EventModel), nameof(EventModel.IsAllowed))]
static class UpgradeEventPatch
{
    static void Postfix(EventModel __instance, IRunState runState, ref bool __result)
    {
        if (__result && runState.Players.Any(FmRules.IsFmPlayer) && FmEnergyFilter.IsRemovedEvent(__instance)) __result = false;
    }
}

/// <summary>The game's menu music isn't playing (FM track 00 is), so its menu music parameters are skipped.</summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Audio.NAudioManager), nameof(MegaCrit.Sts2.Core.Nodes.Audio.NAudioManager.UpdateMusicParameter))]
static class MenuMusicParamPatch
{
    static bool Prefix(string parameter) => parameter != "menu_progress";
}
'''
open(f'{H}/src/FMDuelist/FmEnergy.cs', 'w', encoding='utf-8').write(t + '\n')

# card rewards never roll upgrades
edit('src/FMDuelist/Relics/MillenniumPuzzle.cs', [(
    "    // --- Skip really skips:",
    "    // --- no upgrades in Forbidden Memories ---\n"
    "    public override decimal ModifyCardRewardUpgradeOdds(Player player, CardModel card, decimal odds) => Mine(player) ? 0m : odds;\n\n"
    "    // --- Skip really skips:")])

sy = f'{H}/sheets/systems.json'
rows = json.load(open(sy))
for r in rows:
    if r['id'] == 'no_energy_items':
        r['what'] = 'Relics/potions/events in sheets/removed_energy.json never appear in FM runs (energy + card upgrades); reward upgrade odds are 0'
        r['value'] = len(E['relics']) + len(E['potions']) + len(E['events'])
json.dump(rows, open(sy, 'w'), indent=1)
print('ok', len(E['relics']), len(E['potions']), len(E['events']))
