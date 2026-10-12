import os, json
H = os.path.expanduser('~/fmsts/src/FMDuelist')

def edit(path, pairs):
    p = f'{H}/{path}'
    s = open(p, encoding='utf-8').read()
    for a, b in pairs:
        if a not in s:
            raise SystemExit(f'missing in {path}: {a[:80]!r}')
        s = s.replace(a, b, 1)
    open(p, 'w', encoding='utf-8').write(s)

# ---- right-click marking: junk and Step Back can be marked too ----
edit('Game/FmUi.cs', [(
    """            if (model is FmMonsterCard || model is FmSpellCard { Kind: FmSpellKind.Equip })
            {
                if (!FmField.Marked.Remove(model) && FmField.Marked.Count < 5 && (FmField.Marked.Count > 0 || model is FmMonsterCard))""",
    """            if (model is FmMonsterCard || model is FmSpellCard { Kind: FmSpellKind.Equip } || FmJunk.Kind(model) != JunkKind.None)
            {
                if (!FmField.Marked.Remove(model) && FmField.Marked.Count < 5 && (FmField.Marked.Count > 0 || model is not FmSpellCard))""")])

# ---- chain: leading junk waits until the first monster ----
edit('Cards/FmMonsterCard.cs', [
    ("""        if (!chain.Contains(played)) chain.Add(played); // an unmarked card played during a chain joins it last""",
     """        if (!chain.Contains(played)) chain.Add(played); // an unmarked card played during a chain joins it last
        // junk marked before the first monster is used right after it (junk never starts a chain)
        int firstMonster = chain.FindIndex(c => c is FmMonsterCard);
        if (firstMonster > 0)
        {
            var lead = chain.Take(firstMonster).Where(c => FmJunk.Kind(c) != JunkKind.None).ToList();
            chain.RemoveAll(lead.Contains);
            chain.InsertRange(chain.FindIndex(c => c is FmMonsterCard) + 1, lead);
        }"""),
    # resolve junk
    ("""        foreach (var card in chain)
        {
            if (card is not FmCardBase fm) continue;
            if (fm is FmMonsterCard)
            {
                if (cur == 0) { cur = first = fm.FmId; continue; }""",
     """        var destroyed = new List<CardModel>();
        foreach (var card in chain)
        {
            var junk = FmJunk.Kind(card);
            if (junk != JunkKind.None)
            {
                if (cur == 0) continue;
                if (junk == JunkKind.Harmful)
                {
                    // it takes the monster down with it: both are destroyed
                    Log.Info($"[FMDuelist] Fusion {FmData.Get(cur).Name} + {card.Id.Entry}: sacrificed");
                    steps.Add(new FmFusionStep(cur, 0, null, Junk: card, Kind: FmStepKind.Sacrifice));
                    destroyed.Add(card);
                    cur = 0; atkBonus = defBonus = 0; fusions = 0;
                }
                else
                {
                    Log.Info($"[FMDuelist] Fusion {FmData.Get(cur).Name} + {card.Id.Entry}: no fusion");
                    steps.Add(new FmFusionStep(cur, 0, null, Junk: card, Kind: FmStepKind.JunkFail));
                }
                continue;
            }
            if (card is not FmCardBase fm) continue;
            if (fm is FmMonsterCard)
            {
                if (cur == 0)
                {
                    if (first != 0 || steps.Count > 0) steps.Add(new FmFusionStep(0, fm.FmId, null, Kind: FmStepKind.Start));
                    cur = fm.FmId;
                    if (first == 0) first = cur;
                    continue;
                }"""),
    ("""        foreach (var card in chain)
            if (card != played) await CardPileCmd.Add(card, PileType.Discard);""",
     """        foreach (var card in chain)
            if (card != played) await CardPileCmd.Add(card, destroyed.Contains(card) ? PileType.Exhaust : PileType.Discard);"""),
    ("""        return cur == 0 && played is FmCardBase p ? p.FmId : cur;""",
     """        if (cur == 0 && destroyed.Count == 0 && played is FmCardBase p) return p.FmId;
        return cur;"""),
    # nothing left after a sacrifice: no summon (the summon is still used), the field monster (if any) is destroyed
    ("""        int resultId = await FmFusion.Resolve(ctx, chain, Owner, this, fieldMonster);
        var pos""",
     """        int resultId = await FmFusion.Resolve(ctx, chain, Owner, this, fieldMonster);
        if (resultId == 0)
        {
            if (zone.Alive(Owner) && zone.Creature(Owner) is { } gone) await MegaCrit.Sts2.Core.Commands.CreatureCmd.Kill(gone);
            FmField.SummonsThisTurn++;
            FmField.NotifyChanged();
            return;
        }
        var pos"""),
])

# ---- animation ----
edit('Game/FmFusionAnim.cs', [
    ("public record FmFusionStep(int Current, int Next, int? Result, bool IsEquip = false, bool EquipOk = false);",
     "public enum FmStepKind { Fuse, JunkFail, Sacrifice, Start }\n\n"
     "/// <summary>One step of a fusion chain: current + next -> result (null = failed, next replaces current). Junk steps carry the junk card.</summary>\n"
     "public record FmFusionStep(int Current, int Next, int? Result, bool IsEquip = false, bool EquipOk = false,\n"
     "    MegaCrit.Sts2.Core.Models.CardModel? Junk = null, FmStepKind Kind = FmStepKind.Fuse);"),
    ("""        Node2D cur = Holder(layer, firstId, center + new Vector2(-170, 0));
        await Run(Tw(cur, "position", center, 0.2));

        foreach (var s in steps)
        {
            var next = Holder(layer, s.Next, center + new Vector2(view.X * 0.6f, 0));""",
     """        Node2D? cur = Holder(layer, firstId, center + new Vector2(-170, 0));
        await Run(Tw(cur, "position", center, 0.2));

        foreach (var s in steps)
        {
            if (s.Kind == FmStepKind.Start)
            {
                cur = Holder(layer, s.Next, center + new Vector2(view.X * 0.6f, 0));
                await Run(Tw(cur, "position", center, 0.25));
                continue;
            }
            if (cur == null) continue;
            var next = s.Junk != null ? JunkHolder(layer, s.Junk, center + new Vector2(view.X * 0.6f, 0)) : Holder(layer, s.Next, center + new Vector2(view.X * 0.6f, 0));"""),
    ("""            if (s.IsEquip)
            {""",
     """            if (s.Kind == FmStepKind.JunkFail)
            {
                // junk never fuses: it bounces off the monster and is knocked away
                FmSfx.Play(FmSfx.Fail);
                var bump = next.CreateTween();
                bump.TweenProperty(next, "position", center + new Vector2(40, 0), 0.08);
                bump.TweenProperty(next, "position", center + new Vector2(view.X * 0.7f, -260), 0.35).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
                bump.Parallel().TweenProperty(next, "rotation", 2.5f, 0.35);
                await Run(bump);
                next.QueueFree();
                continue;
            }
            if (s.Kind == FmStepKind.Sacrifice)
            {
                // the cursed card drags the monster down: both burn away
                var hit = next.CreateTween();
                hit.TweenProperty(next, "position", center + new Vector2(40, 0), 0.12);
                await Run(hit);
                FmSfx.Play(FmSfx.Destroy);
                await Burn(layer, cur, next);
                cur = null;
                continue;
            }
            if (s.IsEquip)
            {"""),
    ("""                cur.QueueFree();
                cur = next;
            }
        }
        await Wait(tree, 0.35);
        var fade = dim.CreateTween().SetParallel();
        fade.TweenProperty(dim, "color:a", 0f, 0.2);
        fade.TweenProperty(cur, "modulate:a", 0f, 0.2);""",
     """                cur.QueueFree();
                cur = next;
            }
        }
        await Wait(tree, 0.35);
        var fade = dim.CreateTween().SetParallel();
        fade.TweenProperty(dim, "color:a", 0f, 0.2);
        if (cur != null) fade.TweenProperty(cur, "modulate:a", 0f, 0.2);"""),
    ("""    static Node2D Holder(Node parent, int id, Vector2 pos)""",
     """    /// <summary>A base-game card (junk) shown with the game's own card visuals.</summary>
    static Node2D JunkHolder(Node parent, MegaCrit.Sts2.Core.Models.CardModel junk, Vector2 pos)
    {
        var n = new Node2D { Position = pos, Scale = Vector2.One * Scale };
        try
        {
            var model = junk.CardScope?.CreateCard(MegaCrit.Sts2.Core.Models.ModelDb.GetById<MegaCrit.Sts2.Core.Models.CardModel>(junk.Id), junk.Owner!);
            if (model != null && MegaCrit.Sts2.Core.Nodes.Cards.NCard.Create(model) is { } card)
            {
                card.MouseFilter = Control.MouseFilterEnum.Ignore;
                card.Scale = Vector2.One * 0.5f;
                n.AddChild(card);
            }
        }
        catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] junk card visual: " + ex.Message); }
        parent.AddChild(n);
        return n;
    }

    /// <summary>Both cards flare, char and crumble away in sparks.</summary>
    static async Task Burn(Node layer, params Node2D[] cards)
    {
        foreach (var c in cards)
        {
            var sparks = new CpuParticles2D
            {
                Position = c.Position, Amount = 70, Lifetime = 0.9, OneShot = true, Explosiveness = 0.6f,
                EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle, EmissionRectExtents = new Vector2(80, 115),
                Direction = new Vector2(0, -1), Spread = 35, InitialVelocityMin = 60, InitialVelocityMax = 180, Gravity = new Vector2(0, -60),
                ScaleAmountMin = 3, ScaleAmountMax = 7, Color = new Color(1f, 0.55f, 0.1f),
            };
            layer.AddChild(sparks);
            sparks.Emitting = true;
        }
        var t = cards[0].CreateTween().SetParallel();
        foreach (var c in cards) t.TweenProperty(c, "modulate", new Color(2.2f, 1.2f, 0.35f, 1f), 0.15);
        await Run(t);
        var t2 = cards[0].CreateTween().SetParallel();
        foreach (var c in cards) t2.TweenProperty(c, "modulate", new Color(0.25f, 0.08f, 0.02f, 0f), 0.6);
        await Run(t2);
        foreach (var c in cards) c.QueueFree();
    }

    /// <summary>Step Back: the Frantic Escape cards spin together into one, then it hits the field and vanishes.</summary>
    public static async Task PlayStepBack(MegaCrit.Sts2.Core.Models.CardModel card, int steps)
    {
        if (NCombatRoom.Instance is not { } room || Engine.GetMainLoop() is not SceneTree tree) return;
        var layer = new CanvasLayer { Layer = 90 };
        tree.Root.AddChild(layer);
        var view = room.GetViewportRect().Size;
        var center = view / 2 + new Vector2(0, -60);
        Node2D cur = JunkHolder(layer, card, center + new Vector2(-170, 0));
        await Run(Tw(cur, "position", center, 0.2));
        for (int i = 1; i < steps; i++)
        {
            var next = JunkHolder(layer, card, center + new Vector2(view.X * 0.6f, 0));
            await Run(Tw(next, "position", center + new Vector2(150, 0), 0.22));
            FmSfx.Play(FmSfx.Fusion);
            var spin = cur.CreateTween().SetParallel();
            spin.TweenProperty(cur, "position", center, 0.35);
            spin.TweenProperty(next, "position", center, 0.35);
            spin.TweenProperty(cur, "rotation", Mathf.Tau, 0.35);
            spin.TweenProperty(next, "rotation", -Mathf.Tau, 0.35);
            await Run(spin);
            next.QueueFree();
            cur.Rotation = 0;
        }
        var label = new Label { Text = $"STEP BACK x{steps}", Position = center + new Vector2(-150, 150), Size = new Vector2(300, 60), HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", 40);
        label.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.3f));
        label.AddThemeConstantOverride("outline_size", 8);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        layer.AddChild(label);
        FmSfx.Play(FmSfx.PowerUp);
        await Wait(tree, 0.6);
        var fade = cur.CreateTween().SetParallel();
        fade.TweenProperty(cur, "scale", Vector2.One * 0.2f, 0.3);
        fade.TweenProperty(cur, "modulate:a", 0f, 0.3);
        fade.TweenProperty(label, "modulate:a", 0f, 0.4);
        await Run(fade);
        layer.QueueFree();
    }

    static Node2D Holder(Node parent, int id, Vector2 pos)"""),
])
print('ok')
