using FMDuelist.Data;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace FMDuelist.Game;

/// <summary>
/// Spell &amp; trap zone shown above the duelist (set traps face-down, field spell, equips on the monster, Swords),
/// plus a large card preview when hovering the field monster or any zone card.
/// </summary>
public static class FmZone
{
    const float Small = 0.5f;
    static CanvasLayer? _layer;
    static Control? _row;
    static Control? _preview;
    static string _sig = "";
    static readonly List<(Control box, int id, bool faceDown, string note)> _items = new();
    static int _previewId = -1;
    static FmSlot? _hoverZone;

    static bool Valid => _layer != null && GodotObject.IsInstanceValid(_layer);

    static void EnsureLayer()
    {
        if (Valid || Engine.GetMainLoop() is not SceneTree tree) return;
        _layer = new CanvasLayer { Layer = 4, Name = "FMDuelistZone" };
        _row = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        _layer.AddChild(_row);
        tree.Root.AddChild(_layer);
        _sig = "";
    }

    public static bool OverlayOpen => (MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack.Instance?.ScreenCount ?? 0) > 0;

    public static void Tick(Vector2 mouse)
    {
        var player = FmField.Duelist;
        bool inCombat = NCombatRoom.Instance != null && player?.Creature.CombatState != null;
        if (!inCombat) { if (Valid) _layer!.Visible = false; HidePreview(); return; }
        // a card choice (or any other screen) is open on top: no field previews in its way
        if (OverlayOpen) { if (Valid) _layer!.Visible = false; HidePreview(); _hoverZone = null; return; }
        EnsureLayer();
        _layer!.Visible = true;

        // rebuild the row when its contents change
        var entries = new List<(int id, bool down, string note)>();
        if (player!.Creature.GetPower<Powers.FmFieldPower>() is { } fp) _field = fp.Amount; else _field = 0;
        foreach (var z in FmField.Slots)
            foreach (var e in z.Equips) entries.Add((e, false, $"EQUIP {z.Index + 1}"));
        if (FmField.SwordsTurns > 0) entries.Add((348, false, $"{FmField.SwordsTurns} turns"));
        string sig = string.Join(",", entries.Select(e => $"{e.id}{e.down}{e.note}"));
        if (sig != _sig) Rebuild(entries);
        _sig = sig;

        if (MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance?.GetCreatureNode(player.Creature) is { } node)
        {
            var r = node.Hitbox.GetGlobalRect();
            _row!.Position = new Vector2(r.Position.X - 20, r.Position.Y - 150);
        }

        // hover preview
        int hover = -1; bool down = false; string note = "";
        foreach (var (box, id, fd, n) in _items)
            if (GodotObject.IsInstanceValid(box) && box.GetGlobalRect().HasPoint(mouse)) { hover = id; down = fd; note = n; }
        _hoverZone = FmField.Slots.LastOrDefault(z => z.Alive(player) && z.Visual.IsMouseOver(mouse));
        if (hover < 0 && _hoverZone != null) { hover = _hoverZone.MonsterId; note = "MONSTER"; }
        // his monsters: face-up ones show the card, face-down ones only that they're face down (as in FM)
        if (hover < 0 && FmRival.Active)
            foreach (var z in FmRival.Slots)
                if (z.MonsterId > 0 && z.Visual.IsMouseOverCard(mouse))
                {
                    if (z.FaceDown) { hover = FaceDownMonster; note = "A face-down monster"; }
                    else { hover = z.MonsterId; note = $"COM zone {z.Index + 1} - {(z.Position == FmPosition.Attack ? "Attack" : "Defense")} position\nATK {z.Atk} / DEF {z.Def}"; }
                    // aiming an attack: who wins the Guardian Star match
                    if (FmField.Aiming && FmField.Attacker is { MonsterId: > 0 } att)
                    {
                        var (sa, sd, _) = FmStars.Battle(att, z, player);
                        string mine = FmStars.Name(att.Star), theirs = z.FaceDown ? "?" : FmStars.Name(z.Star);
                        note += z.FaceDown ? $"\nGuardian Star: {mine} vs ? (hidden until it flips)"
                            : sa > 0 ? $"\nGuardian Star: {mine} beats {theirs}!  Your monster +{sa}" + (sd < 0 ? $", theirs {sd}" : "")
                            : sd > 0 ? $"\nGuardian Star: {theirs} beats {mine}...  Their monster +{sd}"
                            : $"\nGuardian Star: {mine} vs {theirs}: no effect";
                        hover = z.FaceDown ? hover : z.MonsterId;
                    }
                }
        // the trap zones over each head
        if (hover < 0 && FmFieldHud.HoverAt(mouse) is { } trap)
        {
            if (trap.mine) { hover = trap.id; note = "Your set trap"; }
            else { hover = FaceDownTrap; note = "A face-down card"; }
        }
        // the field box (top left)
        if (hover < 0 && _field > 0 && FmFieldHud.FieldBoxAt(mouse)) { hover = _field; note = "FIELD in play"; }
        if (hover < 0) HidePreview(); else ShowPreview(hover, note, mouse);
    }

    static void Rebuild(List<(int id, bool down, string note)> entries)
    {
        foreach (var c in _row!.GetChildren()) c.QueueFree();
        _items.Clear();
        var size = FmCardFace.Size * Small;
        for (int i = 0; i < entries.Count; i++)
        {
            var (id, down, note) = entries[i];
            var box = new Control { Position = new Vector2(i * (size.X + 6), 0), Size = size, MouseFilter = Control.MouseFilterEnum.Ignore };
            var holder = new Node2D { Position = size / 2, Scale = Vector2.One * Small };
            holder.AddChild(down ? CardBack() : FmCardFace.Create(id));
            box.AddChild(holder);
            var lbl = new Label { Text = note, Position = new Vector2(0, size.Y + 2), Size = new Vector2(size.X, 18), HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
            lbl.AddThemeFontSizeOverride("font_size", 13);
            lbl.AddThemeColorOverride("font_outline_color", Colors.Black);
            lbl.AddThemeConstantOverride("outline_size", 5);
            box.AddChild(lbl);
            _row.AddChild(box);
            _items.Add((box, id, down, note));
        }
    }

    /// <summary>Face-down card: FM's brown back.</summary>
    static Control CardBack()
    {
        var s = FmCardFace.Size;
        var root = new Control { Size = s, Position = -s / 2, MouseFilter = Control.MouseFilterEnum.Ignore };
        var tex = FmRivalHand.Back();
        if (tex.GetWidth() != 92)
        {
            root.AddChild(new TextureRect { Texture = tex, Size = s, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore });
            return root;
        }
        var back = new Panel { Size = s, MouseFilter = Control.MouseFilterEnum.Ignore };
        back.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("6b3d1c"), BorderColor = new Color("2a1608"),
            BorderWidthLeft = 5, BorderWidthRight = 5, BorderWidthTop = 5, BorderWidthBottom = 5,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6,
        });
        root.AddChild(back);
        var oval = new Panel { Position = new Vector2(25, 45), Size = new Vector2(100, 128), MouseFilter = Control.MouseFilterEnum.Ignore };
        oval.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color("2c1a0c"), BorderColor = new Color("c99a4a"),
            BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
            CornerRadiusTopLeft = 50, CornerRadiusTopRight = 50, CornerRadiusBottomLeft = 50, CornerRadiusBottomRight = 50,
        });
        root.AddChild(oval);
        return root;
    }

    const int FaceDownMonster = -10, FaceDownTrap = -11;
    static int _field;

    static void ShowPreview(int id, string note, Vector2 mouse)
    {
        if (!Valid) return;
        if (id < 0)
        {
            // a face-down card of his: its back and what it is
            if (_previewId != id || _preview == null || !GodotObject.IsInstanceValid(_preview))
            {
                HidePreview();
                var panel = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
                var back = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore };
                back.Texture = FmRivalHand.Back();
                back.Size = FmCardFace.Size * 2;
                panel.AddChild(back);
                var l = new Label { Text = note, Position = new Vector2(0, FmCardFace.Size.Y * 2 + 8), Size = new Vector2(FmCardFace.Size.X * 2, 40), MouseFilter = Control.MouseFilterEnum.Ignore };
                l.AddThemeFontSizeOverride("font_size", 18);
                l.AddThemeColorOverride("font_outline_color", Colors.Black);
                l.AddThemeConstantOverride("outline_size", 6);
                panel.AddChild(l);
                _layer!.AddChild(panel);
                _preview = panel;
                _previewId = id;
            }
            var v = _layer!.GetViewport().GetVisibleRect().Size;
            float px = mouse.X + 40 + FmCardFace.Size.X * 2 > v.X ? mouse.X - 40 - FmCardFace.Size.X * 2 : mouse.X + 40;
            _preview!.Position = new Vector2(px, Mathf.Clamp(mouse.Y - FmCardFace.Size.Y, 20, v.Y - FmCardFace.Size.Y * 2 - 60));
            return;
        }
        if (_previewId != id || _preview == null || !GodotObject.IsInstanceValid(_preview))
        {
            HidePreview();
            var info = FmData.Get(id);
            var panel = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
            var holder = new Node2D { Position = FmCardFace.Size, Scale = Vector2.One * 2f };
            holder.AddChild(FmCardFace.Create(id));
            panel.AddChild(holder);
            var hz = _hoverZone;
            string extra = note == "MONSTER" && hz != null
                ? $"Zone {hz.Index + 1} - {(hz.Position == FmPosition.Attack ? "Attack" : "Defense")} position\nATK {hz.Atk} / DEF {hz.Def}" +
                  (hz.Equips.Count > 0 ? "\nEquips: " + string.Join(", ", hz.Equips.Select(e => FmData.Get(e).Name)) : "")
                : note;
            var text = new Label
            {
                Text = $"{info.Name}\n{extra}\n\n{info.Desc}", Position = new Vector2(0, FmCardFace.Size.Y * 2 + 8), Size = new Vector2(FmCardFace.Size.X * 2, 160),
                AutowrapMode = TextServer.AutowrapMode.WordSmart, MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            text.AddThemeFontSizeOverride("font_size", 16);
            text.AddThemeColorOverride("font_outline_color", Colors.Black);
            text.AddThemeConstantOverride("outline_size", 6);
            panel.AddChild(text);
            _layer!.AddChild(panel);
            _preview = panel;
            _previewId = id;
        }
        var view = _layer!.GetViewport().GetVisibleRect().Size;
        float x = mouse.X + 40 + FmCardFace.Size.X * 2 > view.X ? mouse.X - 40 - FmCardFace.Size.X * 2 : mouse.X + 40;
        _preview!.Position = new Vector2(x, Mathf.Clamp(mouse.Y - FmCardFace.Size.Y, 20, view.Y - FmCardFace.Size.Y * 2 - 180));
    }

    static void HidePreview()
    {
        if (_preview != null && GodotObject.IsInstanceValid(_preview)) _preview.QueueFree();
        _preview = null;
        _previewId = -1;
    }
}
