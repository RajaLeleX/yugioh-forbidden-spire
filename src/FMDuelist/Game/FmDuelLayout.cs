using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace FMDuelist.Game;

/// <summary>
/// Where everyone stands. In a duel the two duelists stand at the screen's edges facing each other, and each has
/// five monster zones in front of them in two staggered rows (three on top, two below between them), mirrored about
/// the middle of the screen: your zone 1 is nearest you, his zone 1 nearest him. Against ordinary enemies your five
/// zones stand the same way in front of you.
///
/// Every card is put exactly where it belongs: the zone's creature is moved so that its card's resting spot lands on
/// the zone's place (Osty and the zone creatures carry their cards at different offsets, which made gaps uneven).
/// </summary>
public static class FmDuelLayout
{
    public const int Zones = 5;
    const float Edge = 0.07f;      // duelists stand this far in from the screen's sides
    const float MaxStep = 190f;    // widest spacing between columns
    const float Middle = 70f;      // the gap between the two sides' zones
    const float CardLift = 250f;   // how far above a duelist's feet the two rows are centred
    const float RowGap = 155f;     // top row to bottom row

    static NCombatRoom? Room => NCombatRoom.Instance;
    static NCreature? Node(Creature? c) => c != null ? Room?.GetCreatureNode(c) : null;

    static float _step = MaxStep, _left, _right;

    /// <summary>Column spacing right now.</summary>
    public static float Step => _step;

    /// <summary>Zones 1-3 are the top row, 4-5 the bottom row; columns count outwards from the duelist.</summary>
    static (float col, int row) Cell(int index) => index < 3 ? (index, 0) : (index - 3 + 0.5f, 1);

    /// <summary>Half the width of a duelist (the story duelists' pictures are wider than their bodies).</summary>
    static float HalfWidth(NCreature n, bool rival)
    {
        float half = n.Hitbox.Size.X * 0.5f;
        if (rival && FmStoryAct1.InDuel) half = Math.Max(half, FmStoryAct1.HalfWidth);
        return half;
    }

    static float CenterX(NCreature n) => n.GlobalPosition.X + n.Size.X * 0.5f;
    static float Feet(NCreature n) => n.GlobalPosition.Y + n.Size.Y * 0.5f;

    /// <summary>Puts both duelists at the screen's edges and every zone in its place.</summary>
    public static void Duel(Player player)
    {
        try
        {
            if (Room is not { } room || FmRival.Priest is not { } rival || Node(player.Creature) is not { } me || Node(rival) is not { } him) return;
            var view = room.GetViewportRect().Size;
            var toWorld = room.GetViewport().GetCanvasTransform().AffineInverse();
            float y = me.GlobalPosition.Y;
            me.GlobalPosition = new Vector2((toWorld * new Vector2(view.X * Edge, 0)).X - me.Size.X * 0.5f, y);
            him.GlobalPosition = new Vector2((toWorld * new Vector2(view.X * (1 - Edge), 0)).X - him.Size.X * 0.5f, y);
            _left = CenterX(me) + HalfWidth(me, false) + 20;
            _right = CenterX(him) - HalfWidth(him, true) - 20;
            _step = Math.Clamp((_right - _left - Middle) / 6f, 100f, MaxStep);
            PlaceAll(player);
            FmLifePoints.Show();
        }
        catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] duel layout: " + ex.Message); }
    }

    /// <summary>Against ordinary enemies (or once a duel is laid out): every zone in its place.</summary>
    public static void PlaceAll(Player player)
    {
        if (!FmRival.Active && Node(player.Creature) is { } me)
        {
            _left = CenterX(me) + HalfWidth(me, false) + 20;
            _step = 170f;
        }
        foreach (var s in FmField.Slots) Place(player, s);
        if (FmRival.Active) foreach (var s in FmRival.Slots) Place(player, s);
        FmFieldHud.Refresh();
    }

    /// <summary>Where a zone's card rests (world coordinates).</summary>
    /// <summary>Where a zone's card sits (world coordinates), for the zone picker.</summary>
    public static Vector2? SpotFor(Player player, FmSlot slot)
    {
        if (!FmRival.Active && Node(player.Creature) is { } me) { _left = CenterX(me) + HalfWidth(me, false) + 20; _step = 170f; }
        return Target(player, slot);
    }

    static Vector2? Target(Player player, FmSlot slot)
    {
        if (!FmRival.Active && !slot.Rival && Node(player.Creature) is { } you) { _left = CenterX(you) + HalfWidth(you, false) + 20; _step = 170f; }
        if (Node(player.Creature) is not { } me) return null;
        var (col, row) = Cell(slot.Index);
        float y = Feet(me) - CardLift + (row == 0 ? -RowGap / 2 : RowGap / 2);
        if (slot.Rival)
        {
            if (!FmRival.Active) return null;
            return new Vector2(_right - (col + 0.5f) * _step, y);
        }
        return new Vector2(_left + (col + 0.5f) * _step, y);
    }

    /// <summary>Moves a zone's creature so its card lands on the zone's place.</summary>
    public static void Place(Player player, FmSlot slot)
    {
        if (Target(player, slot) is not { } target) return;
        var creature = slot.Rival ? slot.RivalCreature : slot.Creature(player);
        if (Node(creature) is not { } n) return;
        if (slot.Visual.RestingSpot() is { } card) n.GlobalPosition += target - card;
        else n.GlobalPosition = target - new Vector2(n.Size.X * 0.5f, n.Size.Y * 0.5f - 150);
        // the bottom row is in front of the top row
        n.ZIndex = Cell(slot.Index).row == 1 ? 1 : 0;
    }
}
