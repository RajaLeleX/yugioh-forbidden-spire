using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace FMDuelist.Game;

/// <summary>
/// The player as Duel Master K's portrait in combat, and how everyone goes down:
///   a monster card (wild monsters too) burns away: it flares, chars and crumbles into sparks;
///   Duel Master K (you) burns the same way, only bigger: the screen shakes and flashes red, he flares white-hot and
///   crumbles in two waves of embers;
///   a duelist is banished to the Shadow Realm: drained to purple and pulled up into a dark vortex.
/// </summary>
public static class FmDeathFx
{
    // ---------------- Duel Master K in the fight ----------------
    public static void DressPlayer(Player player, int tries = 0)
    {
        try
        {
            var node = NCombatRoom.Instance?.GetCreatureNode(player.Creature);
            if (node == null)
            {
                if (tries < 20 && Engine.GetMainLoop() is SceneTree tree) tree.CreateTimer(0.1).Timeout += () => DressPlayer(player, tries + 1);
                return;
            }
            if (node.Visuals.GetNodeOrNull("FmPlayerPortrait") != null || FmDuelMasterK.Portrait() is not { } tex) return;
            if (node.Visuals.GetCurrentBody() is { } body) body.Visible = false;
            const int h = 200;
            var spr = new Sprite2D { Name = "FmPlayerPortrait", Texture = FmScene.Sized(tex, h), Position = new Vector2(0, -h / 2f), Centered = true, TextureFilter = CanvasItem.TextureFilterEnum.Linear };
            node.Visuals.AddChild(spr);
            FmScene.KeepOnScreen(spr);
            spr.Modulate = new Color(1, 1, 1, 0);
            var t = spr.CreateTween(); t.TweenProperty(spr, "modulate:a", 1f, 0.4);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] dress player: " + ex.Message); }
    }

    static NCreature? Node(Creature? c) => c == null ? null : NCombatRoom.Instance?.GetCreatureNode(c);

    static async Task Wait(double s)
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        var t = tree.CreateTimer(s); await t.ToSignal(t, SceneTreeTimer.SignalName.Timeout);
    }

    static CpuParticles2D Sparks(Vector2 at, Vector2 extents, int amount, Color color, float speed, double life)
        => new()
        {
            Position = at, Amount = amount, Lifetime = life, OneShot = true, Explosiveness = 0.7f,
            EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle, EmissionRectExtents = extents,
            Direction = new Vector2(0, -1), Spread = 45, InitialVelocityMin = speed * 0.4f, InitialVelocityMax = speed, Gravity = new Vector2(0, -70),
            ScaleAmountMin = 3, ScaleAmountMax = 8, Color = color,
        };

    // ---------------- a monster card ----------------
    /// <summary>The card burn (the same as a destroyed monster card on the field).</summary>
    public static async Task CardBurn(Node2D target, float height = 250f)
    {
        if (!GodotObject.IsInstanceValid(target)) return;
        var sparks = Sparks(new Vector2(0, -height / 2), new Vector2(height * 0.32f, height * 0.45f), 90, new Color(1f, 0.55f, 0.1f), 180, 0.9);
        target.GetParent()?.AddChild(sparks);
        sparks.GlobalPosition = target.GlobalPosition + new Vector2(0, -height / 2);
        sparks.Emitting = true;
        FmSfx.Play(FmSfx.Destroy);
        var t = target.CreateTween();
        t.TweenProperty(target, "modulate", new Color(2.2f, 1.2f, 0.35f, 1f), 0.15);
        t.TweenProperty(target, "modulate", new Color(0.25f, 0.08f, 0.02f, 1f), 0.45);
        t.Parallel().TweenProperty(target, "scale", target.Scale * 0.92f, 0.45);
        t.TweenProperty(target, "modulate:a", 0f, 0.35);
        t.TweenInterval(0.9);
        t.TweenCallback(Callable.From(() => { if (GodotObject.IsInstanceValid(sparks)) sparks.QueueFree(); }));
        await Wait(0.95);
    }

    // ---------------- Duel Master K ----------------
    public static async Task Player(Creature me)
    {
        try
        {
            if (Node(me) is not { } node) return;
            var v = node.Visuals;
            var home = v.Position;
            FmSfx.Play(FmSfx.Destroy);
            // the screen pulses red and shakes
            var flash = Flash(new Color(0.85f, 0.05f, 0.02f), 0.38f, 0.9);
            var shake = v.CreateTween();
            for (int i = 0; i < 8; i++) shake.TweenProperty(v, "position", home + new Vector2(i % 2 == 0 ? 14 : -14, i % 3 == 0 ? -6 : 4), 0.045);
            shake.TweenProperty(v, "position", home, 0.05);
            // white-hot, then charred, sinking a little
            var t = v.CreateTween();
            t.TweenProperty(v, "modulate", new Color(3f, 2.4f, 1.4f, 1f), 0.18);
            t.TweenProperty(v, "modulate", new Color(2.2f, 0.9f, 0.25f, 1f), 0.25);
            t.TweenProperty(v, "modulate", new Color(0.15f, 0.04f, 0.02f, 1f), 0.65);
            t.Parallel().TweenProperty(v, "scale", v.Scale * 0.88f, 0.65);
            t.Parallel().TweenProperty(v, "rotation", -0.05f, 0.65);
            var first = Sparks(Vector2.Zero, new Vector2(110, 160), 220, new Color(1f, 0.6f, 0.15f), 260, 1.2);
            node.AddChild(first); first.Position = new Vector2(0, -170); first.Emitting = true;
            await Wait(0.75);
            // the second wave: he crumbles
            var second = Sparks(Vector2.Zero, new Vector2(120, 170), 260, new Color(1f, 0.3f, 0.05f), 340, 1.4);
            node.AddChild(second); second.Position = new Vector2(0, -170); second.Emitting = true;
            FmSfx.PlaySoft(FmSfx.Destroy, -6f, 0.7f);
            var fade = v.CreateTween();
            fade.TweenProperty(v, "modulate:a", 0f, 0.6);
            fade.Parallel().TweenProperty(v, "position:y", home.Y + 30, 0.6);
            await Wait(0.9);
            _ = flash;
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] player death: " + ex.Message); }
    }

    // ---------------- a duelist ----------------
    public static async Task Duelist(Creature duelist)
    {
        try
        {
            if (Node(duelist) is not { } node) return;
            var v = node.Visuals;
            FmSfx.PlaySoft(FmSfx.Destroy, -4f, 0.6f);
            // the vortex opens under them
            var vortex = new Node2D { Position = new Vector2(0, -20), Scale = new Vector2(0.1f, 0.1f) };
            node.AddChild(vortex);
            node.MoveChild(vortex, 0);
            for (int i = 0; i < 4; i++)
                vortex.AddChild(new Polygon2D { Color = new Color(0.25f + 0.08f * i, 0.02f, 0.35f + 0.1f * i, 0.35f), Polygon = Ellipse(170 - i * 35, 46 - i * 9) });
            var spin = vortex.CreateTween().SetLoops();
            spin.TweenProperty(vortex, "rotation", Mathf.Tau, 1.2).AsRelative();
            var open = vortex.CreateTween();
            open.TweenProperty(vortex, "scale", Vector2.One, 0.35).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            var smoke = Sparks(Vector2.Zero, new Vector2(120, 30), 140, new Color(0.45f, 0.1f, 0.65f, 0.9f), 220, 1.3);
            node.AddChild(smoke); smoke.Position = new Vector2(0, -30); smoke.Emitting = true;
            // drained of colour, then pulled up into the dark
            var t = v.CreateTween();
            t.TweenProperty(v, "modulate", new Color(0.75f, 0.5f, 1.3f, 1f), 0.3);
            t.TweenProperty(v, "modulate", new Color(0.25f, 0.1f, 0.4f, 1f), 0.4);
            t.Parallel().TweenProperty(v, "scale", new Vector2(v.Scale.X * 0.25f, v.Scale.Y * 1.35f), 0.7).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            t.Parallel().TweenProperty(v, "position:y", v.Position.Y - 140, 0.7).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            t.TweenProperty(v, "modulate:a", 0f, 0.25);
            await Wait(1.1);
            var close = vortex.CreateTween();
            close.TweenProperty(vortex, "scale", new Vector2(0.01f, 0.01f), 0.3);
            close.TweenCallback(Callable.From(() => { if (GodotObject.IsInstanceValid(vortex)) vortex.QueueFree(); }));
            await Wait(0.35);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] duelist death: " + ex.Message); }
    }

    static ColorRect? Flash(Color color, float alpha, double dur)
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return null;
        var layer = new CanvasLayer { Layer = 60 };
        tree.Root.AddChild(layer);
        var r = new ColorRect { Color = new Color(color, 0f), Size = layer.GetViewport().GetVisibleRect().Size, MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(r);
        var t = r.CreateTween();
        t.TweenProperty(r, "color:a", alpha, dur * 0.25);
        t.TweenProperty(r, "color:a", 0f, dur * 0.75);
        t.TweenCallback(Callable.From(layer.QueueFree));
        return r;
    }

    static Vector2[] Ellipse(float rx, float ry, int n = 28)
    {
        var p = new Vector2[n];
        for (int i = 0; i < n; i++) { float a = Mathf.Tau * i / n; p[i] = new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry); }
        return p;
    }
}
