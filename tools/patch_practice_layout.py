import os
os.chdir(os.path.expanduser('~/fmsts/src/FMDuelist'))

def patch(p, reps):
    s = open(p, encoding='utf-8').read()
    for a, b in reps:
        assert a in s, (p, a[:80]); s = s.replace(a, b, 1)
    open(p, 'w', encoding='utf-8').write(s)

# zone spacing: tighter in the Free Duel, where both duelists need their own half of the field
patch('Game/FmField.cs', [
('''        node.Position = me.Position + new Godot.Vector2(me.Hitbox.Size.X * 0.5f + 150f + slot.Index * 190f, -75f);''',
 '''        float gap = FmPractice.Active ? FmPractice.ZoneGap : 150f, step = FmPractice.Active ? FmPractice.ZoneStep : 190f;
        node.Position = me.Position + new Godot.Vector2(me.Hitbox.Size.X * 0.5f + gap + slot.Index * step, -75f);'''),
])
patch('Game/FmRival.cs', [
('''                n.Position = pn.Position + new Vector2(-pn.Hitbox.Size.X * 0.5f - 150f - (2 - i) * 190f, -75f);''',
 '''            {
                float gap = FmPractice.Active ? FmPractice.ZoneGap : 150f, step = FmPractice.Active ? FmPractice.ZoneStep : 190f;
                n.Position = pn.Position + new Vector2(-pn.Hitbox.Size.X * 0.5f - gap - (2 - i) * step, -75f);
            }'''),
('''        for (int i = 0; i < 3; i++)
        {
            var c = await CreatureCmd.Add(zones[i].ToMutable(), combat, CombatSide.Enemy, null);
            Slots[i].RivalCreature = c;
        }
        PlaceZones();''', '''        if (Current == FmPractice.Duelist) FmPractice.Layout(player, Priest);
        for (int i = 0; i < 3; i++)
        {
            var c = await CreatureCmd.Add(zones[i].ToMutable(), combat, CombatSide.Enemy, null);
            Slots[i].RivalCreature = c;
        }
        PlaceZones();'''),
('''        FmRivalHand.Hide();''', '''        FmRivalHand.Hide();
        FmPractice.RemoveLabels();'''),
])

patch('Game/FmPractice.cs', [
('''    // ---------------- the premade decks ----------------''', '''    // ---------------- the field: one half each ----------------
    /// <summary>Zone spacing in the Free Duel (tighter than a boss fight's, so both rows fit).</summary>
    public const float ZoneGap = 105f, ZoneStep = 165f;
    static CanvasLayer? _labels;

    /// <summary>You stand at the far left and Duel Master K at the far right, each with your own three zones in front.</summary>
    public static void Layout(MegaCrit.Sts2.Core.Entities.Players.Player player, Creature rival)
    {
        try
        {
            var room = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance;
            if (room?.GetCreatureNode(player.Creature) is not { } me || room.GetCreatureNode(rival) is not { } him) return;
            var view = room.GetViewportRect().Size;
            var toWorld = room.GetViewport().GetCanvasTransform().AffineInverse();
            float y = me.GlobalPosition.Y;
            me.GlobalPosition = new Vector2((toWorld * new Vector2(view.X * 0.12f, 0)).X - me.Size.X * 0.5f, y);
            him.GlobalPosition = new Vector2((toWorld * new Vector2(view.X * 0.88f, 0)).X - him.Size.X * 0.5f, y);
            // Osty (your first zone) follows you
            foreach (var pet in player.Creature.Pets)
                if (room.GetCreatureNode(pet) is { } pn && pet.Monster is not FmZonePet) pn.GlobalPosition = me.GlobalPosition + new Vector2(me.Hitbox.Size.X * 0.5f + ZoneGap * 0.4f, 0);
            ShowLabels(me, him);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] free duel layout: " + ex.Message); }
    }

    /// <summary>FM's YOU / COM tags over each Duel Master K.</summary>
    static void ShowLabels(Control me, Control him)
    {
        RemoveLabels();
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        _labels = new CanvasLayer { Layer = 6 };
        tree.Root.AddChild(_labels);
        void Tag(Control who, string tag, string name, Color color)
        {
            var r = who.Hitbox.GetGlobalRect();
            var screen = who.GetViewport().GetCanvasTransform() * new Vector2(r.GetCenter().X, r.Position.Y);
            var box = new PanelContainer { MouseFilter = Control.MouseFilterEnum.Ignore };
            box.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = color, BorderColor = Colors.White, BorderWidthBottom = 3, BorderWidthTop = 3, BorderWidthLeft = 3, BorderWidthRight = 3, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, ContentMarginLeft = 14, ContentMarginRight = 14, ContentMarginTop = 2, ContentMarginBottom = 2 });
            var l = new Label { Text = $"{tag}  {name}", MouseFilter = Control.MouseFilterEnum.Ignore };
            l.AddThemeFontSizeOverride("font_size", 26);
            l.AddThemeColorOverride("font_color", Colors.White);
            l.AddThemeConstantOverride("outline_size", 6);
            l.AddThemeColorOverride("font_outline_color", Colors.Black);
            box.AddChild(l);
            _labels!.AddChild(box);
            box.Position = screen - new Vector2(130, 70);
        }
        Tag((Control)me, "YOU", "Duel Master K", new Color(0.75f, 0.05f, 0.05f, 0.9f));
        Tag((Control)him, "COM", "Duel Master K", new Color(0.1f, 0.2f, 0.75f, 0.9f));
    }

    public static void RemoveLabels()
    {
        if (_labels != null && GodotObject.IsInstanceValid(_labels)) _labels.QueueFree();
        _labels = null;
    }

    // ---------------- the premade decks ----------------'''),
])
print('ok')
