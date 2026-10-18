using FMDuelist.Data;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace FMDuelist.Game;

/// <summary>
/// The spell and trap zones and the field display, as FM shows them in a duel: five small zones above each
/// duelist's head where set traps lie face down, and a box between them naming the field in play with its picture.
/// </summary>
public static class FmFieldHud
{
    public const int MiniZones = 5;
    static CanvasLayer? _layer;
    static Control? _mine, _his, _field;
    static int _shownMine = -1, _shownHis = -1, _shownField = -1;

    static readonly Color Navy = new(0.1f, 0.12f, 0.3f, 0.92f), Stone = new(0.62f, 0.6f, 0.55f);
    const float W = 40, H = 56, Gap = 6;

    public static void Clear()
    {
        if (_layer != null && GodotObject.IsInstanceValid(_layer)) _layer.QueueFree();
        _layer = null; _mine = _his = _field = null;
        _shownMine = _shownHis = _shownField = -1;
    }

    /// <summary>Redraws whatever changed and puts the rows over the duelists' heads.</summary>
    public static void Refresh()
    {
        try
        {
            if (NCombatRoom.Instance is not { } room || Engine.GetMainLoop() is not SceneTree tree || FmField.Duelist is not { } player) return;
            if (_layer == null || !GodotObject.IsInstanceValid(_layer))
            {
                _layer = new CanvasLayer { Layer = 54 };
                tree.Root.AddChild(_layer);
                _mine = Row(); _his = Row();
                _field = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
                _layer.AddChild(_field);
                tree.ProcessFrame += Follow;
            }
            int mine = FmField.Traps.Count, his = FmRival.Active ? FmRival.TrapCount : -1, field = FmBackdrop.Current;
            if (mine != _shownMine) { Fill(_mine!, mine); _shownMine = mine; }
            if (his != _shownHis) { _his!.Visible = his >= 0; if (his >= 0) Fill(_his, his); _shownHis = his; }
            if (field != _shownField) { Field(field); _shownField = field; }
            Follow();
        }
        catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] field hud: " + ex.Message); }
    }

    static Control Row()
    {
        var row = new Control { MouseFilter = Control.MouseFilterEnum.Ignore, Size = new Vector2(MiniZones * W + (MiniZones - 1) * Gap, H) };
        _layer!.AddChild(row);
        return row;
    }

    /// <summary>Five outlined zones, the first <paramref name="set"/> holding a face-down card.</summary>
    static void Fill(Control row, int set)
    {
        foreach (var c in row.GetChildren()) c.QueueFree();
        var back = FmRivalHand.Back();
        for (int i = 0; i < MiniZones; i++)
        {
            var slot = new Panel { Position = new Vector2(i * (W + Gap), 0), Size = new Vector2(W, H), MouseFilter = Control.MouseFilterEnum.Ignore };
            slot.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0, 0, 0, 0.45f), BorderColor = new Color(Stone, 0.85f),
                BorderWidthLeft = 2, BorderWidthRight = 2, BorderWidthTop = 2, BorderWidthBottom = 2,
            });
            row.AddChild(slot);
            if (i < set)
            {
                var card = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore };
                card.Texture = back;
                card.Position = new Vector2(3, 3);
                card.Size = new Vector2(W - 6, H - 6);
                slot.AddChild(card);
            }
        }
    }

    /// <summary>FM's field box: the field card's picture and its name in FM's letters ("NO FIELD" when there is none).</summary>
    static void Field(int id)
    {
        foreach (var c in _field!.GetChildren()) c.QueueFree();
        float px = 2.2f;
        string name = id > 0 ? FmData.Get(id).Name.ToUpperInvariant() : "NO FIELD";
        float textW = FmTextBox.TextWidth(name, px);
        float w = Math.Max(150, textW + 70), h = 50;
        var box = new Panel { Size = new Vector2(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };
        box.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Navy, BorderColor = Stone, BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
            ShadowColor = new Color(0, 0, 0, 0.5f), ShadowSize = 3,
        });
        _field.AddChild(box);
        _field.Size = box.Size;
        if (id > 0 && FmData.Art(id) is { } art)
        {
            var pic = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = Control.MouseFilterEnum.Ignore, TextureFilter = CanvasItem.TextureFilterEnum.Nearest };
            pic.Texture = art;
            pic.Position = new Vector2(6, 6);
            pic.Size = new Vector2(h - 12, h - 12);
            box.AddChild(pic);
        }
        var text = FmTextBox.Text(name, px, id > 0 ? new Color(1f, 0.86f, 0.4f) : new Color(0.8f, 0.8f, 0.85f));
        text.Position = new Vector2(id > 0 ? h : (w - textW) / 2, (h - 12 * px) / 2);
        box.AddChild(text);
        var tag = FmTextBox.Text("FIELD", 1.4f, new Color(0.8f, 0.85f, 1f));
        tag.Position = new Vector2((w - FmTextBox.TextWidth("FIELD", 1.4f)) / 2, -18);
        _field.AddChild(tag);
    }

    /// <summary>Keeps the rows over the duelists' heads (they move when the duel is laid out) and the field box between them.</summary>
    static void Follow()
    {
        if (_layer == null || !GodotObject.IsInstanceValid(_layer) || NCombatRoom.Instance is not { } room)
        {
            if (Engine.GetMainLoop() is SceneTree t) t.ProcessFrame -= Follow;
            return;
        }
        var me = FmField.Duelist is { } p ? room.GetCreatureNode(p.Creature) : null;
        var him = FmRival.Active && FmRival.Priest != null ? room.GetCreatureNode(FmRival.Priest) : null;
        if (me != null) Over(_mine!, me);
        if (him != null) Over(_his!, him);
        var view = _layer.GetViewport().GetVisibleRect().Size;
        float top = Math.Min(_mine!.Position.Y, him != null ? _his!.Position.Y : _mine.Position.Y);
        _field!.Position = new Vector2((view.X - _field.Size.X) / 2, Math.Max(90, top - 4));
    }

    static void Over(Control row, NCreature who)
    {
        var r = who.Hitbox.GetGlobalRect();
        float headTop = r.Position.Y;
        if (who.Entity == FmRival.Priest && FmStoryAct1.InDuel) headTop = Math.Min(headTop, r.End.Y - 340);
        row.Position = new Vector2(r.GetCenter().X - row.Size.X / 2, Math.Max(80, headTop - row.Size.Y - 18));
    }
}
