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

# all zones re-placed after any summon (the game re-lays out pets when one is added)
edit('src/FMDuelist/Game/FmField.cs', [("        PlaceZone(player, slot);\n", "        foreach (var z in Slots) PlaceZone(player, z);\n")])

# rest-site music: FM's Build Deck theme
m = json.load(open(f'{H}/sheets/media.json'))
m.setdefault('restSite_BuildDeck', 31)
json.dump(m, open(f'{H}/sheets/media.json', 'w'), indent=1)
edit('tools/codegen.py', [(
    "         '    public static readonly Dictionary<string, int> Sfx",
    "         f\"    public const int RestSiteTrack = {M['restSite_BuildDeck']};\",\n         '    public static readonly Dictionary<string, int> Sfx")])
edit('src/FMDuelist/Game/FmMusicImpl.cs', [(
    "        if (room is not CombatRoom) { Stop(); return; }",
    "        if (room is RestSiteRoom) { Play(FmMedia.RestSiteTrack); return; }\n        if (room is not CombatRoom) { Stop(); return; }")])

sy = f'{H}/sheets/systems.json'
rows = json.load(open(sy))
upd = {
    'one_monster': ('three_zones', 'Three monster zones (zone 1 = Osty, zones 2-3 = FmZonePet pets). Summon asks for a zone; an occupied zone fuses with its monster first', 3, 'osty_summon'),
}
for r in rows:
    if r['id'] in upd:
        nid, what, val, hook = upd[r['id']]; r.update(id=nid, what=what, value=val, hook=hook)
for r in [{"id": "front_zone_hit", "what": "Enemy attacks land on the front living zone (1, then 2, then 3)", "value": 1, "hook": "enemy_attack"},
          {"id": "trap_limit", "what": "At most 3 traps set at once", "value": 3, "hook": "can_play_monster"},
          {"id": "rest_music", "what": "Rest sites play FM's Build Deck music (media.json restSite_BuildDeck)", "value": 0, "hook": "music"}]:
    if not any(x['id'] == r['id'] for x in rows): rows.append(r)
json.dump(rows, open(sy, 'w'), indent=1)
print('ok')
