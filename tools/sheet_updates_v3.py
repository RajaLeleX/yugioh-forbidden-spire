import json, os
H = os.path.expanduser('~/fmsts')
p = f'{H}/sheets/systems.json'; s = json.load(open(p))
ids = {r['id'] for r in s}
add = [
 {"id": "damage_calc", "what": "Monster on field: duelist takes no enemy damage; Attack position: duelist takes (attack total - ATK) if positive, monster destroyed; Defense: nothing", "value": 1, "hook": "enemy_attack"},
 {"id": "monster_self_safe", "what": "The monster only loses HP to enemy powered attacks (never from its own attacks, thorns or self-damage)", "value": 1, "hook": "enemy_attack"},
 {"id": "multi_hit_once", "what": "Multi-hit attacks are summed; counter and reflect fire once per attack", "value": 1, "hook": "enemy_attack"},
 {"id": "hover_preview", "what": "Hovering the field monster or a zone card shows the full card, stats, equips and FM text", "value": 0, "hook": "zone"},
 {"id": "spell_trap_zone", "what": "Row above the duelist: set traps face-down, field spell, equips, Swords with turns left", "value": 0, "hook": "zone"},
 {"id": "relics", "what": "25 Millennium Item / character relics in the Necrobinder pool (sheets/relics.json)", "value": 25, "hook": "relics"},
]
for r in add:
    if r['id'] not in ids: s.append(r)
json.dump(s, open(p, 'w'), indent=1)
p = f'{H}/sheets/hooks.json'; k = json.load(open(p))
kid = {h['id'] for h in k}
for h in [{"id": "zone", "target": "FmZone.Tick (SceneTree.ProcessFrame)", "kind": "frame", "does": "zone row and hover preview"},
          {"id": "relics", "target": "FmRelic.BeforeCombatStart / FmBonus", "kind": "model_hook", "does": "relic effects by kind"},
          {"id": "input", "target": "NGame._Input", "kind": "harmony_prefix", "does": "right-click marks, click attack, position toggle"}]:
    if h['id'] not in kid: k.append(h)
for h in k:
    if h['id'] == 'hand_right_click': h['target'] = 'FmInput.Handle'
json.dump(k, open(p, 'w'), indent=1)

# preflight addition: relic rows
pf = f'{H}/tools/preflight.py'; t = open(pf).read()
if 'relics.json' not in t:
    t = t.replace("print('rows:", """RL = S('relics')
KINDS = {'hand_size','death_burn','combat_weak','combat_trap','fusion_bonus','empty_field_block','heal_after_combat','heal_bonus','first_summon_atk','combat_draw','type_boost','counter_bonus','defense_boost','start_field','double_summon','fusion_keep'}
for r in RL:
    need('relics', r, ['id', 'cls', 'title', 'rarity', 'kind', 'art', 'text'])
    if r['kind'] not in KINDS: problems.append(f"relics[{r['id']}] kind {r['kind']} not implemented")
    if r['art'] not in C: problems.append(f"relics[{r['id']}] art {r['art']} not on disc")
    if r['kind'] == 'type_boost' and not r['types']: problems.append(f"relics[{r['id']}] type_boost without types")
    if r['kind'] == 'start_field' and r['value'] not in {s['id'] for s in spells if s['kind'] == 'field'}: problems.append(f"relics[{r['id']}] field {r['value']} not a field spell row")
    if r['kind'] == 'combat_trap' and r['value'] not in {s['id'] for s in spells if s['kind'].startswith('trap')}: problems.append(f"relics[{r['id']}] trap {r['value']} not a trap row")
    if f"class {r['cls']} " not in src: problems.append(f"{r['cls']} not generated")
print('rows:""", 1)
    open(pf, 'w').write(t)
print('ok')
