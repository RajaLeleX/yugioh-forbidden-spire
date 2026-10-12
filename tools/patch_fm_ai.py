import os, re
os.chdir(os.path.expanduser('~/fmsts/src/FMDuelist'))

def patch(p, reps):
    s = open(p, encoding='utf-8').read()
    for a, b in reps:
        assert a in s, (p, a[:80]); s = s.replace(a, b, 1)
    open(p, 'w', encoding='utf-8').write(s)

# ---------- the slot: face-down flag ----------
patch('Game/FmField.cs', [
('''    public int MonsterId;''', '''    public int MonsterId;
    /// <summary>His monsters are set face down like in FM: you only see them once they battle.</summary>
    public bool FaceDown;'''),
('''        MonsterId = 0; AtkBonus = DefBonus = TempDef = 0;''', '''        MonsterId = 0; FaceDown = false; AtkBonus = DefBonus = TempDef = 0;'''),
])

# ---------- the visual: card back while face down ----------
patch('Game/FmUi.cs', [
('''        var tween = _root!.CreateTween();
        tween.TweenProperty(_holder, "rotation", _slot.Position == FmPosition.Defense ? -Mathf.Pi / 2 : 0f, 0.2);
        _stats.Visible = _pos.Visible = true;
        _stats.Text = $"{_slot.Atk} / {_slot.Def}";''', '''        var tween = _root!.CreateTween();
        tween.TweenProperty(_holder, "rotation", _slot.Position == FmPosition.Defense ? -Mathf.Pi / 2 : 0f, 0.2);
        SetFaceDown(_slot.FaceDown);
        _stats.Visible = _pos.Visible = true;
        _stats.Text = _slot.FaceDown ? "? / ?" : $"{_slot.Atk} / {_slot.Def}";'''),
('''    NCard? MakeCard(int id)
    {''', '''    Sprite2D? _backSprite;
    bool _wasFaceDown;

    /// <summary>Face down: the FM card back covers the card. Turning face up flips it over.</summary>
    void SetFaceDown(bool down)
    {
        if (_holder == null) return;
        if (_backSprite == null || !GodotObject.IsInstanceValid(_backSprite))
        {
            var tex = FmRivalHand.Back();
            _backSprite = new Sprite2D { Texture = tex, Scale = NCard.defaultSize / new Vector2(tex.GetWidth(), tex.GetHeight()), ZIndex = 10, Visible = false };
            _holder.AddChild(_backSprite);
        }
        _holder.MoveChild(_backSprite, _holder.GetChildCount() - 1);
        if (down) { _backSprite.Visible = true; _wasFaceDown = true; return; }
        if (!_wasFaceDown) { _backSprite.Visible = false; return; }
        // flip face up
        _wasFaceDown = false;
        var holder = _holder; var back = _backSprite;
        var t = holder.CreateTween();
        t.TweenProperty(holder, "scale:x", 0.01f, 0.12);
        t.TweenCallback(Callable.From(() => back.Visible = false));
        t.TweenProperty(holder, "scale:x", CardScale, 0.12);
        FmSfx.Play(FmSfx.Flip);
    }

    NCard? MakeCard(int id)
    {'''),
])

# ---------- the boss AI ----------
p = 'Game/FmRival.cs'
s = open(p, encoding='utf-8').read()

# the turn: one play, chosen like FM's AI
start = s.index('            await FmRivalHand.Think();\n')
start += len('            await FmRivalHand.Think();\n')
end = s.index('            if (FmField.SwordsTurns <= 0) await Attacks(ctx, player);')
s = s[:start] + '''
            // Forbidden Memories: one play per turn (a fusion chain counts as one), picked by priority
            await PlayOne(ctx, player);

''' + s[end:]

s = s.replace('''    static async Task Summon(Player player)
    {''', '''    /// <summary>
    /// His one play this turn, in FM's order of priorities:
    ///   1. a heal when he's hurt;  2. Raigeki / Dark Hole when your field outclasses his;
    ///   3. a monster or fusion that beats your strongest (or fills his empty field);
    ///   4. an equip that lets one of his monsters beat your strongest;  5. a burn spell;
    ///   6. any monster for an empty zone;  7. a trap set face down;  8. any equip that fits.
    /// </summary>
    static async Task PlayOne(PlayerChoiceContext ctx, Player player)
    {
        var mine = FmField.Living(player).ToList();
        int myTop = mine.Count == 0 ? 0 : mine.Max(m => m.Position == FmPosition.Attack ? m.Atk : m.Def);

        // 1. heal when hurt
        if (Priest!.CurrentHp <= LifePoints * 0.55 && Hand.FirstOrDefault(id => Spell(id)?.Kind == FmSpellKind.Heal) is int heal and > 0)
        {
            await UseCard(heal);
            Banner($"{Name} plays {FmData.Get(heal).Name}!");
            await FmHealVfx.Play(heal, Priest);
            await FmRules.Raw(() => CreatureCmd.Heal(Priest, Spell(heal)!.Value));
            await Wait(0.6);
            return;
        }
        // 2. Raigeki: wipe your field when you're ahead on monsters
        if (Hand.Contains(337) && mine.Count > 0 && (mine.Count >= 2 || mine.Max(m => m.Atk) > (Living.Any() ? Living.Max(m => m.Atk) : 0)))
        {
            await UseCard(337);
            Banner($"{Name} plays Raigeki!");
            try { await FmSpellVfx.Raigeki(mine.Select(m => m.Creature(player)).OfType<Creature>()); } catch { }
            foreach (var m in mine) await DestroyMine(m, player);
            await Wait(0.5);
            return;
        }
        // ...Dark Hole only when your field is clearly stronger than his
        if (Hand.Contains(336) && mine.Count > 0 && mine.Sum(m => m.Atk) > Living.Sum(m => m.Atk) + 1000)
        {
            await UseCard(336);
            Banner($"{Name} plays Dark Hole!");
            try { await FmSpellVfx.DarkHole(); } catch { }
            foreach (var m in mine) await DestroyMine(m, player);
            await DestroyAll();
            await Wait(0.5);
            return;
        }
        // 3. a monster that wins battles (or anything at all when his field is empty)
        var plan = BestSummon();
        if (plan != null && (plan.Value.atk > myTop || !Living.Any()) && await Summon(player, plan.Value)) return;
        // 4. an equip that turns one of his monsters into a winner
        if (await Equip(player, needWin: true)) return;
        // 5. burn
        if (Hand.FirstOrDefault(id => Spell(id)?.Kind is FmSpellKind.Burn or FmSpellKind.BurnSelfDmg) is int burn and > 0)
        {
            await UseCard(burn);
            Banner($"{Name} plays {FmData.Get(burn).Name}!");
            await Lp(ctx, player.Creature, Spell(burn)!.Value);
            await Wait(0.4);
            return;
        }
        // 6. any monster for an empty zone (it'll go into Defense if it can't win)
        if (plan != null && Slots.Any(z => z.MonsterId == 0) && await Summon(player, plan.Value)) return;
        // 7. a trap
        if (await SetTrap()) return;
        // 8. any equip
        if (await Equip(player, needWin: false)) return;
        // 9. a stronger monster over his weakest
        if (plan != null) await Summon(player, plan.Value);
    }

    /// <summary>His strongest summon from hand: a single monster, a 2-3 card fusion, or a fusion onto his field.</summary>
    static (int atk, int result, List<int> chain, FmSlot? onto)? BestSummon()
    {
        var monsters = Hand.Where(id => FmData.Get(id).IsMonster).ToList();
        int bestAtk = -1, bestResult = 0; List<int> bestChain = new(); FmSlot? onto = null;
        void Consider(List<int> chain, int result, FmSlot? field)
        {
            int atk = FmData.Get(result).Atk;
            if (atk > bestAtk || (atk == bestAtk && chain.Count < bestChain.Count)) { bestAtk = atk; bestResult = result; bestChain = chain; onto = field; }
        }
        foreach (var m in monsters) Consider(new List<int> { m }, m, null);
        for (int i = 0; i < monsters.Count; i++)
            for (int j = 0; j < monsters.Count; j++)
            {
                if (i == j || FmData.Fuse(monsters[i], monsters[j]) is not int r || !FmData.Get(r).IsMonster) continue;
                Consider(new List<int> { monsters[i], monsters[j] }, r, null);
                for (int k = 0; k < monsters.Count; k++)
                    if (k != i && k != j && FmData.Fuse(r, monsters[k]) is int r2 && FmData.Get(r2).IsMonster)
                        Consider(new List<int> { monsters[i], monsters[j], monsters[k] }, r2, null);
            }
        foreach (var f in Living)
            foreach (var m in monsters)
                if (FmData.Fuse(f.MonsterId, m) is int r && FmData.Get(r).IsMonster && FmData.Get(r).Atk > f.Atk)
                    Consider(new List<int> { f.MonsterId, m }, r, f);
        return bestResult == 0 ? null : (bestAtk, bestResult, bestChain, onto);
    }

    static async Task<bool> Summon(Player player, (int atk, int result, List<int> chain, FmSlot? onto) plan)
    {
        var (bestAtk, bestResult, bestChain, onto) = plan;
''', 1)

# body of the old Summon: drop its own search, keep the play
old_search_start = s.index('        var monsters = Hand.Where(id => FmData.Get(id).IsMonster).ToList();\n        int bestAtk = -1, bestResult = 0; List<int> bestChain = new(); FmSlot? onto = null;\n        void Consider', s.index('static async Task<bool> Summon(Player player, (int atk'))
old_search_end = s.index('        if (bestResult == 0) return;\n', old_search_start) + len('        if (bestResult == 0) return;\n')
s = s[:old_search_start] + s[old_search_end:]

s = s.replace('''            var weakest = Living.OrderBy(s => s.Atk).First();
            if (weakest.Atk >= bestAtk) return;
            zone = weakest;''', '''            var weakest = Living.OrderBy(s => s.Atk).First();
            if (weakest.Atk >= bestAtk) return false;
            zone = weakest;''', 1)

s = s.replace('''        zone.Clear();
        zone.MonsterId = bestResult;
        zone.Position = mine.Count == 0 || info.Atk >= myBest || info.Atk >= info.Def ? FmPosition.Attack : FmPosition.Defense;
        zone.Visual.Attach(player);
        FmField.NotifyChanged();
        Banner($"{Name} summons {info.Name} ({info.Atk}/{info.Def}) in {(zone.Position == FmPosition.Attack ? "Attack" : "Defense")} position!");
        await Wait(0.9);
    }''', '''        bool fused = bestChain.Count > 1;
        zone.Clear();
        zone.MonsterId = bestResult;
        // FM: if it can't beat anything of yours it goes into Defense; it's set face down unless a fusion revealed it
        zone.Position = mine.Count == 0 || info.Atk > myBest || info.Atk >= info.Def ? FmPosition.Attack : FmPosition.Defense;
        zone.FaceDown = !fused;
        zone.Visual.Attach(player);
        FmField.NotifyChanged();
        Banner(zone.FaceDown
            ? $"{Name} sets a monster face down in {(zone.Position == FmPosition.Attack ? "Attack" : "Defense")} position."
            : $"{Name} summons {info.Name} ({info.Atk}/{info.Def}) in {(zone.Position == FmPosition.Attack ? "Attack" : "Defense")} position!");
        await Wait(0.9);
        return true;
    }''', 1)

# equip: one, optionally only when it wins a battle
s = s.replace('''    static async Task Equip()
    {
        foreach (var id in Hand.Where(id => FmData.Get(id).Type == FmType.Equip).ToList())
        {
            var target = Living.Where(s => FmData.CanEquip(id, s.MonsterId)).OrderByDescending(s => s.Atk).FirstOrDefault();
            if (target == null || Spell(id) is not { } sp) continue;''', '''    static async Task<bool> Equip(Player player, bool needWin)
    {
        var mine = FmField.Living(player).ToList();
        int myTop = mine.Count == 0 ? 0 : mine.Max(m => m.Position == FmPosition.Attack ? m.Atk : m.Def);
        foreach (var id in Hand.Where(id => FmData.Get(id).Type == FmType.Equip).ToList())
        {
            if (Spell(id) is not { } sp) continue;
            var target = Living.Where(s => FmData.CanEquip(id, s.MonsterId))
                .Where(s => !needWin || (s.Atk <= myTop && s.Atk + sp.Value > myTop))
                .OrderByDescending(s => s.Atk).FirstOrDefault();
            if (target == null) continue;''', 1)
s = s.replace('''            target.EquipList.Add((id, sp.Value));
            FmField.NotifyChanged();
        }
    }''', '''            target.EquipList.Add((id, sp.Value));
            FmField.NotifyChanged();
            return true;
        }
        return false;
    }''', 1)

# traps: one at a time
s = s.replace('''    static async Task SetTraps()
    {
        foreach (var id in Hand.Where(id => FmData.Get(id).Type == FmType.Trap).ToList())
        {
            if (Traps.Count >= 3) break;
            await UseCard(id);
            Traps.Add(id);
            FmRivalHand.SetTraps(Traps.Count);
            Banner($"{Name} sets a card face down.");
            await Wait(0.4);
        }
    }''', '''    static async Task<bool> SetTrap()
    {
        if (Traps.Count >= 3 || Hand.FirstOrDefault(id => FmData.Get(id).Type == FmType.Trap) is not int id || id == 0) return false;
        await UseCard(id);
        Traps.Add(id);
        FmRivalHand.SetTraps(Traps.Count);
        Banner($"{Name} sets a card face down.");
        await Wait(0.4);
        return true;
    }''', 1)

# battles reveal face-down monsters
s = s.replace('''    static async Task Battle(PlayerChoiceContext ctx, Player player, FmSlot att, FmSlot def, bool playerAttacks)
    {
        int a = att.Atk;''', '''    static async Task Battle(PlayerChoiceContext ctx, Player player, FmSlot att, FmSlot def, bool playerAttacks)
    {
        // face-down monsters are revealed when they battle
        if (att.FaceDown || def.FaceDown)
        {
            att.FaceDown = def.FaceDown = false;
            FmField.NotifyChanged();
            await Wait(0.35);
        }
        int a = att.Atk;''', 1)
s = s.replace('''            if (target == null)
            {
                Banner($"{FmData.Get(s.MonsterId).Name} attacks you directly!");''', '''            if (s.FaceDown) { s.FaceDown = false; FmField.NotifyChanged(); await Wait(0.35); }
            if (target == null)
            {
                Banner($"{FmData.Get(s.MonsterId).Name} attacks you directly!");''', 1)
open(p, 'w', encoding='utf-8').write(s)
print('ok')
