import os, re, json
H = os.path.expanduser('~/fmsts')
p = f'{H}/src/FMDuelist/Relics/MillenniumPuzzle.cs'
s = open(p, encoding='utf-8').read()
start = s.index('    // --- Swords of Revealing Light, traps, and FM damage calculation ---')
end = s.index('    // --- FM hand: draw up to 5')
s = s[:start] + '''    // --- Swords of Revealing Light, traps, and FM damage calculation (per hit) ---
    // While a monster is on the field the duelist takes no enemy damage. Each enemy hit is its own battle:
    // Attack position: the monster hits back for its ATK, is destroyed, and the duelist takes what the hit exceeds ATK by.
    // Defense position: Block soaks the hit; an unbroken Block reflects what is left of it; a broken Block destroys the monster.
    // The monster only dies to enemy attacks. Once it is gone, later hits land on the duelist normally.
    private readonly Queue<(Creature dealer, FmPosition pos, int atk, int block, decimal amount)> _hits = new();

    public override decimal ModifyHpLostBeforeOsty(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (amount <= 0 || !IsMineOrOsty(target)) return amount;
        if (FmField.SwordsTurns > 0) return 0;
        bool enemyHit = dealer != null && dealer.IsEnemy;
        if (target.Monster is Osty && !(enemyHit && props.IsPoweredAttack())) return 0;
        if (enemyHit && props.IsPoweredAttack() && FmField.Traps.Count > 0)
        {
            for (int i = 0; i < FmField.Traps.Count; i++)
            {
                int id = FmField.Traps[i];
                if (!FmSheet.Traps.TryGetValue(id, out var t)) continue;
                if (t.kind == FmTrapKind.DestroyLe && amount > t.value) continue;
                FmField.Traps.RemoveAt(i);
                _sprung.Add((dealer!, id, amount));
                _hits.Clear();
                return 0;
            }
        }
        if (target == Owner.Creature && enemyHit && FmField.HasMonster(Owner)) return 0;
        return amount;
    }

    public override decimal ModifyHpLostAfterOsty(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
        => FmField.SwordsTurns > 0 && IsMineOrOsty(target) ? 0 : amount;

    public override Task BeforeDamageReceived(PlayerChoiceContext ctx, Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (dealer != null && dealer.IsEnemy && props.IsPoweredAttack() && IsMineOrOsty(target) && FmField.HasMonster(Owner))
            _hits.Enqueue((dealer, FmField.Position, FmField.Atk, Owner.Osty!.Block, amount));
        return Task.CompletedTask;
    }

    public override async Task AfterDamageReceived(PlayerChoiceContext ctx, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (_sprung.Count > 0)
        {
            var list = _sprung.ToList();
            _sprung.Clear();
            foreach (var (attacker, id, hit) in list)
            {
                FmSfx.Play(FmSfx.Trap);
                var trap = FmSheet.Traps[id];
                if (attacker.IsAlive)
                {
                    switch (trap.kind)
                    {
                        case FmTrapKind.DestroyLe: await CreatureCmd.Damage(ctx, attacker, trap.value, ValueProp.Unpowered, Owner.Creature, null); break;
                        case FmTrapKind.DestroyAny: await CreatureCmd.Damage(ctx, attacker, 3000, ValueProp.Unpowered, Owner.Creature, null); break;
                        case FmTrapKind.ReflectBurn: await CreatureCmd.Damage(ctx, attacker, hit, ValueProp.Unpowered, Owner.Creature, null); break;
                        case FmTrapKind.Weak: await PowerCmd.Apply<WeakPower>(ctx, attacker, trap.value, Owner.Creature, null); break;
                        case FmTrapKind.ReverseBuffs:
                            foreach (var p in attacker.Powers.Where(p => p.Type == PowerType.Buff).ToList()) await PowerCmd.Remove(p);
                            break;
                    }
                }
                if (Owner.Creature.GetPower<FmTrapPower>() is { } tp) await PowerCmd.Decrement(tp);
                FmField.NotifyChanged();
            }
        }

        if (_hits.Count == 0 || dealer == null || !IsMineOrOsty(target)) return;
        var h = _hits.Dequeue();
        var foe = h.dealer;
        int hit = (int)h.amount;
        if (h.pos == FmPosition.Attack)
        {
            int excess = hit - h.atk;
            if (excess > 0 && FmField.SwordsTurns <= 0)
                await CreatureCmd.Damage(ctx, Owner.Creature, excess, ValueProp.Unblockable | ValueProp.Unpowered, null, null);
            if (foe.IsAlive) await HitBack(ctx, foe, h.atk * FmBonus.CounterMultiplier(Owner));
        }
        else if (hit <= h.block && h.block - hit > 0 && foe.IsAlive)
        {
            await HitBack(ctx, foe, h.block - hit);
        }
    }

    private async Task HitBack(PlayerChoiceContext ctx, Creature foe, int amount)
    {
        FmSfx.Play(FmSfx.Slash);
        if (MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance?.GetCreatureNode(foe) is { } an) FmSlashVfx.Play(an.Hitbox.GetGlobalRect().GetCenter());
        await CreatureCmd.Damage(ctx, foe, amount, ValueProp.Unpowered, Owner.Creature, FmField.SourceCard);
    }

    public override Task BeforeAttack(AttackCommand command) { _hits.Clear(); return Task.CompletedTask; }

''' + s[end:]
open(p, 'w', encoding='utf-8').write(s)

# heals: all Common; starter: Dian Keto + 3 random healing cards (drawn from the heal rows each run)
m = f'{H}/tools/make_sheets.py'
t = open(m, encoding='utf-8').read()
t = t.replace(" (341,'heal',800,'Self','Uncommon',0),(342,'heal',1000,'Self','Uncommon',1),", " (341,'heal',800,'Self','Common',0),(342,'heal',1000,'Self','Common',1),")
t = t.replace("STARTER_SPELL=[337,336,342]", "STARTER_SPELL=[337,336,342]\nSTARTER_RANDOM_HEALS=dict(count=3, fromIds=[338,339,340,341])")
t = t.replace("w('spells',spells)", "w('spells',spells)\nw('starter_random',[dict(id='heals', count=STARTER_RANDOM_HEALS['count'], fromIds=STARTER_RANDOM_HEALS['fromIds'], distinct=True)])")
open(m, 'w', encoding='utf-8').write(t)

c = f'{H}/tools/codegen.py'
t = open(c, encoding='utf-8').read()
if 'StarterRandom' not in t:
    t = t.replace("out.append('    public static readonly Type[] DragonBuffCards",
        "SR = json.load(open(f'{H}/sheets/starter_random.json'))\n"
        "out.append('    public static readonly (int count, int[] ids)[] StarterRandom = { ' + ', '.join(f\"({r['count']}, new[] {{ {', '.join(map(str, r['fromIds']))} }})\" for r in SR) + ' };')\n"
        "out.append('    public static readonly Type[] DragonBuffCards")
    open(c, 'w', encoding='utf-8').write(t)

f = f'{H}/src/FMDuelist/FmMod.cs'
t = open(f, encoding='utf-8').read()
a = '''    static void Deck(ref IEnumerable<CardModel> __result) =>
        __result = FmSheet.StarterTypes.Select(t => ModelDb.GetById<CardModel>(ModelDb.GetId(t))).ToList();'''
b = '''    static void Deck(ref IEnumerable<CardModel> __result)
    {
        var deck = FmSheet.StarterTypes.Select(t => ModelDb.GetById<CardModel>(ModelDb.GetId(t))).ToList();
        foreach (var (count, ids) in FmSheet.StarterRandom)
            foreach (var id in ids.OrderBy(_ => Random.Shared.Next()).Take(count))
                deck.Add(ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(id))));
        __result = deck;
    }'''
assert a in t
t = t.replace(a, b, 1)
open(f, 'w', encoding='utf-8').write(t)

pf = f'{H}/tools/preflight.py'
t = open(pf, encoding='utf-8').read()
if 'starter_random' not in t:
    t = t.replace("print('rows:", "for r in S('starter_random'):\n    for i in r['fromIds']:\n        if not any(sp['id'] == i and sp['kind'] == 'heal' for sp in spells): problems.append(f'starter_random {i} is not a heal row')\nprint('rows:", 1)
    open(pf, 'w', encoding='utf-8').write(t)

sy = f'{H}/sheets/systems.json'
rows = json.load(open(sy))
for r in rows:
    if r['id'] == 'multi_hit_once':
        r['id'] = 'multi_hit_per_hit'; r['what'] = 'Each hit of a multi-hit attack is calculated separately (hit-back, excess damage, reflect)'
    if r['id'] == 'damage_calc':
        r['what'] = 'Monster on field: duelist takes no enemy damage; per hit in Attack: hit-back ATK, duelist takes (hit - ATK) if positive, monster destroyed; Defense: Block soaks, unbroken Block reflects remainder'
json.dump(rows, open(sy, 'w'), indent=1)
print('ok')
