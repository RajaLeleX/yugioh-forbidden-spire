import os
H = os.path.expanduser('~/fmsts/src/FMDuelist')

def patch(path, pairs):
    p = f'{H}/{path}'
    s = open(p, encoding='utf-8').read()
    for a, b in pairs:
        if a not in s:
            raise SystemExit(f'missing in {path}: {a[:80]!r}')
        s = s.replace(a, b, 1)
    open(p, 'w', encoding='utf-8').write(s)

patch('Game/FmRival.cs', [
    ('''    static readonly int[] Usable = { 336, 337, 338, 339, 340, 341, 342, 343, 344, 345, 346, 347 };''',
     '''    static readonly int[] Usable = { 336, 337, 338, 339, 340, 341, 342, 343, 344, 345, 346, 347, 681, 682, 683, 684, 685, 686 };
    /// <summary>His traps set face down (up to 3): they spring on your attacks.</summary>
    static readonly List<int> Traps = new();'''),
    ('''        Active = false; Priest = null; Current = null; Deck.Clear(); Hand.Clear();''',
     '''        Active = false; Priest = null; Current = null; Deck.Clear(); Hand.Clear(); Traps.Clear();
        FmRivalHand.Hide();'''),
    ('''        BuildDeck();
        Draw();''', '''        BuildDeck();
        Draw();
        FmRivalHand.Show(Hand.Count);'''),
    ('''    // ---------------- his turn ----------------''', '''    // ---------------- his turn ----------------
    /// <summary>He picks these cards from his face-down hand with the cursor and plays them (they leave his hand).</summary>
    static async Task UseCards(IReadOnlyList<int> ids, Vector2? to = null)
    {
        var used = new List<int>();
        foreach (var id in ids)
        {
            int i = Enumerable.Range(0, Hand.Count).FirstOrDefault(k => Hand[k] == id && !used.Contains(k), -1);
            if (i >= 0) used.Add(i);
        }
        if (used.Count == 0) return;
        try { await FmRivalHand.Pick(used); await FmRivalHand.Play(used, to); } catch (Exception ex) { Log.Warn("[FMDuelist] rival hand: " + ex.Message); }
        foreach (var i in used.OrderByDescending(i => i)) Hand.RemoveAt(i);
    }

    static Task UseCard(int id, Vector2? to = null) => UseCards(new[] { id }, to);
'''),
    ('''            Draw();
            foreach (var s in Slots) s.AttackedThisTurn = false;
            await Wait(0.3);''', '''            Draw();
            FmRivalHand.SetCount(Hand.Count);
            foreach (var s in Slots) s.AttackedThisTurn = false;
            await Wait(0.5);
            await FmRivalHand.Think();'''),
    ('''                    Banner($"{Name} plays {FmData.Get(id).Name}!");
                    await FmHealVfx.Play(id, Priest);''', '''                    await UseCard(id);
                    Banner($"{Name} plays {FmData.Get(id).Name}!");
                    await FmHealVfx.Play(id, Priest);'''),
    ('''                    await FmRules.Raw(() => CreatureCmd.Heal(Priest, Spell(id)!.Value));
                    Hand.Remove(id); await Wait(0.6);''', '''                    await FmRules.Raw(() => CreatureCmd.Heal(Priest, Spell(id)!.Value));
                    await Wait(0.6);'''),
    ('''                Banner($"{Name} plays Raigeki!");''', '''                await UseCard(337);
                Banner($"{Name} plays Raigeki!");'''),
    ('''                foreach (var s in mine) await DestroyMine(s, player);
                Hand.Remove(337); await Wait(0.5);''', '''                foreach (var s in mine) await DestroyMine(s, player);
                await Wait(0.5);'''),
    ('''                Banner($"{Name} plays Dark Hole!");''', '''                await UseCard(336);
                Banner($"{Name} plays Dark Hole!");'''),
    ('''                await DestroyAll();
                Hand.Remove(336); await Wait(0.5);''', '''                await DestroyAll();
                await Wait(0.5);'''),
    ('''                Banner($"{Name} plays {FmData.Get(id).Name}!");
                await Lp(ctx, player.Creature, Spell(id)!.Value);
                Hand.Remove(id); await Wait(0.4);''', '''                await UseCard(id);
                Banner($"{Name} plays {FmData.Get(id).Name}!");
                await Lp(ctx, player.Creature, Spell(id)!.Value);
                await Wait(0.4);'''),
    ('''            await Summon(player);
            await Equip();''', '''            await Summon(player);
            await Equip();
            await SetTraps();'''),
    ('''        // play the fusion like yours
        if (bestChain.Count > 1)''', '''        // he picks the cards from his hand with the cursor (a monster already on his field isn't in his hand)
        await UseCards(onto != null ? bestChain.Skip(1).ToList() : bestChain, zone.Visual.CardCenter());
        // play the fusion like yours
        if (bestChain.Count > 1)'''),
    ('''        foreach (var c in (onto != null ? bestChain.Skip(1) : bestChain)) Hand.Remove(c);
''', ''),
    ('''            Banner($"{Name} equips {FmData.Get(id).Name} to {FmData.Get(target.MonsterId).Name}!");''',
     '''            await UseCard(id, target.Visual.CardCenter());
            Banner($"{Name} equips {FmData.Get(id).Name} to {FmData.Get(target.MonsterId).Name}!");'''),
    ('''            target.EquipList.Add((id, sp.Value));
            Hand.Remove(id);''', '''            target.EquipList.Add((id, sp.Value));'''),
    ('''    /// <summary>He only attacks battles he wins''', '''    /// <summary>He sets the traps in his hand face down (up to 3 on the field).</summary>
    static async Task SetTraps()
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
    }

    /// <summary>One of his face-down traps springs on your attacking monster, if one fits. Returns true if your attack was stopped.</summary>
    static async Task<bool> SpringHisTrap(FmSlot attacker, Player player)
    {
        for (int i = 0; i < Traps.Count; i++)
        {
            int id = Traps[i];
            if (!FmSheet.Traps.TryGetValue(id, out var t)) continue;
            bool hits = t.kind == FmTrapKind.DestroyAny || (t.kind == FmTrapKind.DestroyLe && attacker.Atk <= t.value);
            if (!hits) continue;
            Traps.RemoveAt(i);
            FmRivalHand.SetTraps(Traps.Count);
            FmSfx.Play(FmSfx.Trap);
            Banner($"Trap card! {Name} springs {FmData.Get(id).Name} on {FmData.Get(attacker.MonsterId).Name}!");
            await Wait(0.4);
            await DestroyMine(attacker, player);
            return true;
        }
        return false;
    }

    /// <summary>He only attacks battles he wins'''),
    ('''    public static async Task PlayerAttack(PlayerChoiceContext ctx, Player player, FmSlot mine, Creature target)
    {''', '''    public static async Task PlayerAttack(PlayerChoiceContext ctx, Player player, FmSlot mine, Creature target)
    {
        if (await SpringHisTrap(mine, player)) return;'''),
])

patch('Game/FmRivalHand.cs', [
    ('''    static Vector2 Slot(int i, int n) =>''', '''    static Node2D? _trapRow;

    /// <summary>His face-down set traps, a small row beside his hand.</summary>
    public static void SetTraps(int count)
    {
        if (!Valid || _row == null) return;
        if (_trapRow == null || !GodotObject.IsInstanceValid(_trapRow))
        {
            _trapRow = new Node2D { Position = _row.Position + new Vector2(CardSize.X * 3.6f + 60, 10) };
            _layer!.AddChild(_trapRow);
        }
        foreach (var c in _trapRow.GetChildren()) c.QueueFree();
        for (int i = 0; i < count; i++)
        {
            var spr = new Sprite2D { Texture = Back(), Position = new Vector2(i * 52, 0), Scale = CardSize * 0.45f / new Vector2(Back().GetWidth(), Back().GetHeight()), Modulate = new Color(0.85f, 0.75f, 1f) };
            _trapRow.AddChild(spr);
        }
    }

    static Vector2 Slot(int i, int n) =>'''),
    ('''        _layer = null; _row = null; _cursor = null; _cards.Clear();''', '''        _layer = null; _row = null; _cursor = null; _trapRow = null; _cards.Clear();'''),
])
print('ok')
