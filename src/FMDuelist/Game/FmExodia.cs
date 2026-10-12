using FMDuelist.Cards;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace FMDuelist.Game;

/// <summary>All five pieces of the Forbidden One in hand: every enemy is obliterated and the duel is won.</summary>
public static class FmExodia
{
    /// <summary>Head, right arm, left arm, right leg, left leg (FM card numbers).</summary>
    public static readonly int[] Pieces = { 21, 19, 20, 17, 18 };
    static bool _firing;

    public static bool Complete(Player p) =>
        p.PlayerCombatState is { } pcs && Pieces.All(id => pcs.Hand.Cards.Any(c => c is FmCardBase f && f.FmId == id));

    public static async Task CheckAndObliterate(Player p)
    {
        if (_firing || !Complete(p)) return;
        var combat = p.Creature.CombatState;
        if (combat == null || !combat.Enemies.Any(e => e.IsAlive)) return;
        _firing = true;
        try
        {
            Log.Info("[FMDuelist] EXODIA! All five pieces of the Forbidden One are in hand.");
            await Play();
            foreach (var e in combat.Enemies.Where(e => e.IsAlive).ToList())
                await CreatureCmd.Kill(e, force: true);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] exodia: " + ex.Message); }
        finally { _firing = false; }
    }

    /// <summary>The five cards rise and assemble into Exodia, seals blaze, "OBLITERATE!", and a beam sweeps the enemy side.</summary>
    static async Task Play()
    {
        if (NCombatRoom.Instance is not { } room || Engine.GetMainLoop() is not SceneTree tree) return;
        var layer = new CanvasLayer { Layer = 95 };
        tree.Root.AddChild(layer);
        var view = room.GetViewportRect().Size;
        var center = new Vector2(view.X * 0.42f, view.Y * 0.46f);

        var dim = new ColorRect { Color = new Color(0.05f, 0, 0.08f, 0), Size = view, MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(dim);
        await Run(Tw(dim, "color:a", 0.75f, 0.4));

        // assembled body: head center, arms beside, legs below
        var spots = new[]
        {
            new Vector2(0, -150), new Vector2(-190, -40), new Vector2(190, -40), new Vector2(-95, 200), new Vector2(95, 200),
        };
        var cards = new List<Node2D>();
        for (int i = 0; i < Pieces.Length; i++)
        {
            var n = new Node2D { Position = new Vector2(view.X * (0.2f + 0.15f * i), view.Y + 200), Scale = Vector2.One * 0.9f, Modulate = new Color(1, 1, 1, 0) };
            n.AddChild(FmCardFace.Create(Pieces[i]));
            layer.AddChild(n);
            cards.Add(n);
        }
        for (int i = 0; i < cards.Count; i++)
        {
            var t = cards[i].CreateTween().SetParallel();
            t.TweenProperty(cards[i], "position", center + spots[i], 0.55).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            t.TweenProperty(cards[i], "modulate:a", 1f, 0.25);
            FmSfx.Play(FmSfx.Mark);
            await Wait(tree, 0.22);
        }
        await Wait(tree, 0.45);

        // golden seals: the cards blaze one after another
        var add = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        var glow = new Polygon2D { Polygon = Circle(330, 48), Color = new Color(1f, 0.75f, 0.2f, 0f), Position = center + new Vector2(0, 30), Material = add };
        layer.AddChild(glow);
        foreach (var c in cards)
        {
            var t = c.CreateTween();
            t.TweenProperty(c, "modulate", new Color(2.2f, 1.9f, 1.1f, 1f), 0.12);
            t.TweenProperty(c, "modulate", new Color(1.4f, 1.25f, 0.9f, 1f), 0.2);
        }
        await Run(Tw(glow, "color:a", 0.55f, 0.5));
        FmSfx.Play(FmSfx.Fusion);

        var shout = new Label { Text = "OBLITERATE!!", HorizontalAlignment = HorizontalAlignment.Center, Size = new Vector2(view.X, 120), Position = new Vector2(0, view.Y * 0.06f), PivotOffset = new Vector2(view.X / 2, 60), Scale = Vector2.One * 0.3f, MouseFilter = Control.MouseFilterEnum.Ignore };
        shout.AddThemeFontSizeOverride("font_size", 96);
        shout.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.25f));
        shout.AddThemeColorOverride("font_outline_color", new Color(0.35f, 0.05f, 0f));
        shout.AddThemeConstantOverride("outline_size", 24);
        layer.AddChild(shout);
        await Run(Tw(shout, "scale", Vector2.One, 0.25));

        // the beam: a blinding bar of light sweeps across the enemy side
        var beam = new ColorRect { Color = new Color(1f, 0.95f, 0.7f, 0.9f), Position = new Vector2(center.X, center.Y - 70), Size = new Vector2(0, 140), MouseFilter = Control.MouseFilterEnum.Ignore, Material = add };
        layer.AddChild(beam);
        FmSfx.Play(FmSfx.Slash);
        await Run(Tw(beam, "size:x", view.X - center.X + 40, 0.35));
        var flash = new ColorRect { Color = new Color(1, 1, 1, 0), Size = view, MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(flash);
        var f = flash.CreateTween();
        f.TweenProperty(flash, "color:a", 1f, 0.12);
        f.TweenProperty(flash, "color:a", 0f, 0.6);
        FmSfx.Play(FmSfx.Destroy);
        await Wait(tree, 0.25);
        var fade = layer.CreateTween().SetParallel();
        fade.TweenProperty(dim, "color:a", 0f, 0.8);
        fade.TweenProperty(beam, "color:a", 0f, 0.5);
        fade.TweenProperty(glow, "color:a", 0f, 0.6);
        fade.TweenProperty(shout, "modulate:a", 0f, 1.0);
        foreach (var c in cards) fade.TweenProperty(c, "modulate:a", 0f, 0.8);
        fade.Chain().TweenCallback(Callable.From(layer.QueueFree));
    }

    static Vector2[] Circle(float r, int n) => Enumerable.Range(0, n).Select(i => new Vector2(Mathf.Cos(i * Mathf.Tau / n), Mathf.Sin(i * Mathf.Tau / n)) * r).ToArray();
    static Tween Tw(Node n, string prop, Variant v, double d) { var t = n.CreateTween(); t.TweenProperty(n, prop, v, d); return t; }
    static async Task Run(Tween t) => await t.ToSignal(t, Tween.SignalName.Finished);
    static async Task Wait(SceneTree tree, double s) { var timer = tree.CreateTimer(s); await timer.ToSignal(timer, SceneTreeTimer.SignalName.Timeout); }
}
