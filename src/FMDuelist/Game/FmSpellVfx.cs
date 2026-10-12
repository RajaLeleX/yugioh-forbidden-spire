using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace FMDuelist.Game;

/// <summary>Raigeki: lightning crashes onto every enemy. Dark Hole: a black hole opens, swallows the field, collapses.</summary>
public static class FmSpellVfx
{
    static readonly RandomNumberGenerator Rng = new();

    public static async Task Raigeki(IEnumerable<Creature> enemies)
    {
        if (NCombatRoom.Instance is not { } room || Engine.GetMainLoop() is not SceneTree tree) return;
        var layer = new CanvasLayer { Layer = 70 };
        tree.Root.AddChild(layer);
        var view = room.GetViewportRect().Size;
        var dim = new ColorRect { Color = new Color(0.02f, 0.03f, 0.12f, 0f), Size = view, MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(dim);
        await Run(Tw(dim, "color:a", 0.6f, 0.15));

        var targets = enemies.Select(e => room.GetCreatureNode(e)).Where(n => n != null).Select(n => n!.Hitbox.GetGlobalRect().GetCenter()).ToList();
        var add = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        for (int strike = 0; strike < 2; strike++)
        {
            var bolts = new List<Node2D>();
            foreach (var t in targets)
            {
                var start = new Vector2(t.X + Rng.RandfRange(-140, 140), -20);
                bolts.Add(Bolt(layer, start, t, 26f, new Color(0.55f, 0.75f, 1f, 0.55f), add));
                bolts.Add(Bolt(layer, start, t, 7f, new Color(1f, 1f, 1f, 1f), add));
            }
            var flash = new ColorRect { Color = new Color(0.85f, 0.9f, 1f, 0.75f), Size = view, MouseFilter = Control.MouseFilterEnum.Ignore };
            layer.AddChild(flash);
            if (strike == 0) FmSfx.Thunder();
            NGame.Instance?.ScreenShake(MegaCrit.Sts2.Core.Nodes.Vfx.Utilities.ShakeStrength.Strong, MegaCrit.Sts2.Core.Nodes.Vfx.Utilities.ShakeDuration.Short, 90f);
            await Wait(tree, 0.06);
            foreach (var b in bolts) b.Visible = false;
            await Wait(tree, 0.04);
            foreach (var b in bolts) b.Visible = true;
            var f = flash.CreateTween().SetParallel();
            f.TweenProperty(flash, "color:a", 0f, 0.25);
            foreach (var b in bolts) f.TweenProperty(b, "modulate:a", 0f, 0.3);
            await Run(f);
            foreach (var b in bolts) b.QueueFree();
            flash.QueueFree();
        }
        var end = dim.CreateTween();
        end.TweenProperty(dim, "color:a", 0f, 0.25);
        end.TweenCallback(Callable.From(layer.QueueFree));
    }

    /// <summary>A jagged bolt with a couple of forks.</summary>
    static Node2D Bolt(Node parent, Vector2 a, Vector2 b, float width, Color color, Material mat)
    {
        var root = new Node2D();
        root.AddChild(Jagged(a, b, width, color, mat, 14, 55f));
        for (int k = 0; k < 2; k++)
        {
            float t = Rng.RandfRange(0.3f, 0.7f);
            var from = a.Lerp(b, t);
            var to = from + new Vector2(Rng.RandfRange(-160, 160), Rng.RandfRange(60, 160));
            root.AddChild(Jagged(from, to, width * 0.5f, color, mat, 6, 30f));
        }
        parent.AddChild(root);
        return root;
    }

    static Line2D Jagged(Vector2 a, Vector2 b, float width, Color color, Material mat, int segs, float jitter)
    {
        var line = new Line2D { Width = width, DefaultColor = color, Material = mat, JointMode = Line2D.LineJointMode.Sharp };
        for (int i = 0; i <= segs; i++)
        {
            var p = a.Lerp(b, i / (float)segs);
            if (i > 0 && i < segs) p += new Vector2(Rng.RandfRange(-jitter, jitter), Rng.RandfRange(-jitter * 0.3f, jitter * 0.3f));
            line.AddPoint(p);
        }
        return line;
    }

    public static async Task DarkHole()
    {
        if (NCombatRoom.Instance is not { } room || Engine.GetMainLoop() is not SceneTree tree) return;
        var layer = new CanvasLayer { Layer = 70 };
        tree.Root.AddChild(layer);
        var view = room.GetViewportRect().Size;
        var center = new Vector2(view.X * 0.5f, view.Y * 0.42f);
        var dim = new ColorRect { Color = new Color(0.03f, 0f, 0.06f, 0f), Size = view, MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(dim);

        var hole = new Node2D { Position = center, Scale = Vector2.One * 0.05f };
        layer.AddChild(hole);
        var add = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
        // swirling accretion arms
        for (int arm = 0; arm < 5; arm++)
        {
            var line = new Line2D { Width = 34, DefaultColor = new Color(0.62f, 0.25f, 1f, 0.55f), Material = add };
            for (int i = 0; i < 40; i++)
            {
                float t = i / 39f;
                float ang = arm * Mathf.Tau / 5 + t * 5.5f;
                float r = 60 + t * 330;
                line.AddPoint(new Vector2(Mathf.Cos(ang), Mathf.Sin(ang) * 0.55f) * r);
            }
            line.WidthCurve = new Curve();
            line.WidthCurve.AddPoint(new Vector2(0, 1));
            line.WidthCurve.AddPoint(new Vector2(1, 0.1f));
            hole.AddChild(line);
        }
        // event horizon glow and the black core
        hole.AddChild(new Polygon2D { Polygon = Ellipse(150, 110, 48), Color = new Color(0.9f, 0.45f, 1f, 0.6f), Material = add });
        hole.AddChild(new Polygon2D { Polygon = Ellipse(120, 88, 48), Color = new Color(0, 0, 0, 1) });

        var grow = hole.CreateTween().SetParallel();
        grow.TweenProperty(hole, "scale", Vector2.One, 0.7).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
        grow.TweenProperty(hole, "rotation", -Mathf.Tau * 1.5f, 1.6);
        grow.TweenProperty(dim, "color:a", 0.7f, 0.6);
        FmSfx.Play(FmSfx.Fusion);
        await Wait(tree, 1.3);

        // collapse: everything is pulled into the core, then an implosion flash
        var collapse = hole.CreateTween().SetParallel();
        collapse.TweenProperty(hole, "scale", Vector2.One * 0.02f, 0.3).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);
        collapse.TweenProperty(hole, "rotation", -Mathf.Tau * 3f, 0.3);
        await Run(collapse);
        hole.QueueFree();
        var flash = new ColorRect { Color = new Color(0.95f, 0.85f, 1f, 0.9f), Size = view, MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(flash);
        FmSfx.Play(FmSfx.Destroy);
        NGame.Instance?.ScreenShake(MegaCrit.Sts2.Core.Nodes.Vfx.Utilities.ShakeStrength.Strong, MegaCrit.Sts2.Core.Nodes.Vfx.Utilities.ShakeDuration.Short, 90f);
        var end = layer.CreateTween().SetParallel();
        end.TweenProperty(flash, "color:a", 0f, 0.5);
        end.TweenProperty(dim, "color:a", 0f, 0.6);
        end.Chain().TweenCallback(Callable.From(layer.QueueFree));
    }

    static Vector2[] Ellipse(float rx, float ry, int n) => Enumerable.Range(0, n).Select(i => new Vector2(Mathf.Cos(i * Mathf.Tau / n) * rx, Mathf.Sin(i * Mathf.Tau / n) * ry)).ToArray();
    static Tween Tw(Node n, string prop, Variant v, double d) { var t = n.CreateTween(); t.TweenProperty(n, prop, v, d); return t; }
    static async Task Run(Tween t) => await t.ToSignal(t, Tween.SignalName.Finished);
    static async Task Wait(SceneTree tree, double s) { var timer = tree.CreateTimer(s); await timer.ToSignal(timer, SceneTreeTimer.SignalName.Timeout); }
}
