# Lays every sheet row/column over each other; lists unfilled cells and unresolved cross-sheet references.
import json, os, re
H = os.path.expanduser('~/fmsts')
S = lambda n: json.load(open(f'{H}/sheets/{n}.json'))
mons, spells, rits, systems, hooks = S('monsters'), S('spells'), S('rituals'), S('systems'), S('hooks')
D = json.load(open(f'{H}/cache/fm.json')); C = {c['id']: c for c in D['cards']}
problems = []
def need(sheet, row, cols):
    for c in cols:
        v = row.get(c)
        if v is None or v == '': problems.append(f'{sheet}[{row.get("id")}].{c} empty')
for m in mons: need('monsters', m, ['id', 'cls', 'type', 'level', 'atk', 'def_', 'dragonTier', 'act1', 'act2', 'act3'])
IMPL = {'burn', 'burn_selfdmg', 'burn_all', 'dark_hole', 'heal', 'invuln', 'stop_defense', 'strip_buffs', 'stun', 'vulnerable_all', 'field', 'equip',
        'burn_strongest', 'weak_all', 'strip_block_burn_all', 'block', 'block_weak', 'block_cleanse', 'block_heal', 'trap_destroy_le', 'trap_destroy_any', 'trap_reflect_burn', 'trap_weak', 'trap_reverse_buffs', 'trap_absorb'}
for s in spells:
    need('spells', s, ['id', 'cls', 'kind', 'target', 'rarity'])
    if s['kind'] not in IMPL: problems.append(f'spells[{s["id"]}] kind {s["kind"]} not implemented')
    if s['id'] not in C: problems.append(f'spells[{s["id"]}] not on disc')
    if s['kind'] == 'field' and not s['boostTypes']: problems.append(f'spells[{s["id"]}] field without types')
mon_ids = {m['id'] for m in mons}
for r in rits:
    need('rituals', r, ['id', 'cls', 'resultId', 'materials'])
    if r['resultId'] not in mon_ids: problems.append(f'rituals[{r["id"]}] result {r["resultId"]} is not a monster row')
starter = sum(m['starter'] for m in mons) + sum(s['starter'] for s in spells)
if starter == 0: problems.append('no starter cards')
for m in mons:
    if m['starter'] and m['type'] == 'Dragon': problems.append(f'starter dragon {m["id"]}')
for did in (4, 200):
    if did not in mon_ids or C[did]['type'] != 'Dragon' or C[did]['level'] > 4: problems.append(f'dragon buff card {did} invalid')
hook_ids = {h['id'] for h in hooks}
for s in systems:
    need('systems', s, ['id', 'what', 'hook'])
    if s['hook'] not in hook_ids: problems.append(f'systems[{s["id"]}] hook {s["hook"]} missing from hooks.json')
# every hook target must exist in source
src = ''.join(open(os.path.join(dp, f), encoding='utf-8').read() for dp, _, fs in os.walk(f'{H}/src/FMDuelist') for f in fs if f.endswith('.cs'))
for h in hooks:
    sym = re.findall(r'[A-Za-z]+(?=\.|$)', h['target'].split('/')[0].split(' ')[0])
    key = h['target'].split('.')[-1].split('/')[0].split(' ')[0].replace('get_', '')
    if key and key not in src: problems.append(f'hooks[{h["id"]}] target {key} not found in code')
# every generated class exists
for r in mons + spells + rits:
    if f'class {r["cls"]} ' not in src: problems.append(f'{r["cls"]} not generated')
RL = S('relics')
KINDS = {'hand_size','death_burn','combat_weak','combat_trap','fusion_bonus','empty_field_block','heal_after_combat','heal_bonus','first_summon_atk','combat_draw','type_boost','counter_bonus','defense_boost','start_field','double_summon','fusion_keep'}
for r in RL:
    need('relics', r, ['id', 'cls', 'title', 'rarity', 'kind', 'art', 'text'])
    if r['kind'] not in KINDS: problems.append(f"relics[{r['id']}] kind {r['kind']} not implemented")
    if r['art'] not in C: problems.append(f"relics[{r['id']}] art {r['art']} not on disc")
    if r['kind'] == 'type_boost' and not r['types']: problems.append(f"relics[{r['id']}] type_boost without types")
    if r['kind'] == 'start_field' and r['value'] not in {s['id'] for s in spells if s['kind'] == 'field'}: problems.append(f"relics[{r['id']}] field {r['value']} not a field spell row")
    if r['kind'] == 'combat_trap' and r['value'] not in {s['id'] for s in spells if s['kind'].startswith('trap')}: problems.append(f"relics[{r['id']}] trap {r['value']} not a trap row")
    if f"class {r['cls']} " not in src: problems.append(f"{r['cls']} not generated")
for r in S('starter_random'):
    if r['id'] == 'monsters':
        if r['count'] < 1 or r['count'] > 30: problems.append('starter_random monsters count out of range')
        continue
    for i in r['fromIds']:
        if not any(sp['id'] == i and sp['kind'] == 'heal' for sp in spells): problems.append(f'starter_random {i} is not a heal row')
for m in mons:
    if m.get('exodia') and (m['act1'], m['act2'], m['act3']) != ('None', 'None', 'None'): problems.append(f"exodia piece {m['id']} is offered as a reward")
print('rows: monsters', len(mons), 'spells', len(spells), 'rituals', len(rits), 'systems', len(systems), 'hooks', len(hooks))
print('PREFLIGHT', 'CLEAN' if not problems else f'{len(problems)} problems')
for p in problems: print(' -', p)
