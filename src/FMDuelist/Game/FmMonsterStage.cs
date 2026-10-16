using FMDuelist.Data;
using Godot;

namespace FMDuelist.Game;

/// <summary>
/// A small stage for showing a monster off, like FM's 3D model viewer: a black room drawn in white wireframe (a
/// gridded back wall and a floor in perspective), the monster's picture standing in it and a training dummy it
/// keeps attacking with its own attack from the duels (no damage numbers). Everything is drawn inside its own
/// viewport, so it fits whatever window holds it.
/// </summary>
public sealed class FmMonsterStage
{
    const float W = 1100, H = 620, Horizon = 300;
    public readonly SubViewportContainer View;
    readonly SubViewport _vp;
    readonly Node2D _world;
    readonly int _id;
    readonly Node2D _monster, _dummy;
    readonly Vector2 _monsterHome, _dummyHome;

    /// <param name="monsterX">Where the monster stands (0..1100); the dummy stands to its right.</param>
    public FmMonsterStage(int id, float monsterX = 330, float dummyX = 840)
    {
        _id = id;
        View = new SubViewportContainer { Stretch = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        _vp = new SubViewport { Disable3D = true, TransparentBg = false, RenderTargetUpdateMode = SubViewport.UpdateMode.Always, Size = new Vector2I((int)W, (int)H) };
        View.AddChild(_vp);
        _world = new Node2D();
        _vp.AddChild(_world);
        _world.AddChild(new Polygon2D { Color = Colors.Black, Polygon = new[] { new Vector2(-2000, -2000), new Vector2(3000, -2000), new Vector2(3000, 3000), new Vector2(-2000, 3000) } });
        Room();
        _monsterHome = new Vector2(monsterX, 470);
        _dummyHome = new Vector2(dummyX, 470);
        _monster = Monster(id);
        _monster.Position = _monsterHome;
        _world.AddChild(_monster);
        _dummy = Dummy();
        _dummy.Position = _dummyHome;
        _world.AddChild(_dummy);
        View.Resized += Fit;
        Fit();
        Loop();
    }

    void Fit()
    {
        var s = View.Size;
        if (s.X < 2 || s.Y < 2) return;
        float k = Math.Min(s.X / W, s.Y / H);
        _world.Scale = new Vector2(k, k);
        _world.Position = new Vector2((s.X - W * k) / 2, (s.Y - H * k) / 2);
    }

    // ---------------- the room ----------------
    void Line(Vector2 a, Vector2 b, float width = 2.5f, float alpha = 0.9f) =>
        _world.AddChild(new Line2D { Points = new[] { a, b }, Width = width, DefaultColor = new Color(1, 1, 1, alpha), Antialiased = true });

    void Room()
    {
        // back wall: FM's big squares
        for (float y = 30; y < Horizon; y += 90) Line(new Vector2(0, y), new Vector2(W, y), 2.5f, 0.85f);
        for (float x = 60; x < W; x += 120) Line(new Vector2(x, 0), new Vector2(x, Horizon), 2.5f, 0.85f);
        Line(new Vector2(0, Horizon), new Vector2(W, Horizon), 3f, 1f);
        // the floor in perspective: lines running away to a vanishing point, and cross lines closer together far away
        var vp = new Vector2(W * 0.5f, 120);
        for (float x0 = -1400; x0 <= W + 1400; x0 += 130)
        {
            var top = new Vector2(x0 * 0.35f + W * 0.325f, Horizon);
            var dir = top - vp;
            var bottom = top + dir * ((H - Horizon) / dir.Y);
            Line(top, bottom, 2.5f, 0.9f);
        }
        foreach (float t in new[] { 0.06f, 0.15f, 0.28f, 0.46f, 0.72f, 1.05f })
            Line(new Vector2(0, Horizon + (H - Horizon) * t), new Vector2(W, Horizon + (H - Horizon) * t), 2.5f, 0.9f);
    }

    // ---------------- who's in it ----------------
    static Node2D Monster(int id)
    {
        var root = new Node2D();
        // a shadow on the floor
        root.AddChild(new Polygon2D { Color = new Color(0, 0, 0, 0.75f), Polygon = Ellipse(120, 26), Position = new Vector2(0, 6) });
        root.AddChild(new Polygon2D { Color = new Color(1, 1, 1, 0.12f), Polygon = Ellipse(118, 24), Position = new Vector2(0, 6) });
        if (FmData.Art(id) is { } art)
        {
            float h = 250, k = h / art.GetHeight();
            var body = new Node2D { Name = "Body" };
            body.AddChild(new Sprite2D { Texture = art, Scale = new Vector2(k, k), Position = new Vector2(0, -h / 2 - 8), TextureFilter = CanvasItem.TextureFilterEnum.Nearest });
            float w = art.GetWidth() * k;
            var frame = new Line2D { Width = 4, DefaultColor = new Color(0.95f, 0.85f, 0.45f), Closed = true, Points = new[] { new Vector2(-w / 2, -h - 8), new Vector2(w / 2, -h - 8), new Vector2(w / 2, -8), new Vector2(-w / 2, -8) } };
            body.AddChild(frame);
            root.AddChild(body);
            // idle: a slow bob, like the models breathing
            var t = body.CreateTween().SetLoops();
            t.TweenProperty(body, "position:y", -10f, 0.9).SetTrans(Tween.TransitionType.Sine);
            t.TweenProperty(body, "position:y", 0f, 0.9).SetTrans(Tween.TransitionType.Sine);
        }
        return root;
    }

    /// <summary>A straw training dummy on a post, outlined in white like the room.</summary>
    static Node2D Dummy()
    {
        var root = new Node2D();
        var straw = new Color(0.78f, 0.66f, 0.42f);
        var dark = new Color(0.45f, 0.33f, 0.18f);
        root.AddChild(new Polygon2D { Color = new Color(0, 0, 0, 0.75f), Polygon = Ellipse(70, 16), Position = new Vector2(0, 6) });
        var body = new Node2D { Name = "Body" };
        root.AddChild(body);
        body.AddChild(new Polygon2D { Color = dark, Polygon = Rect(-9, -150, 18, 150) });                 // post
        body.AddChild(new Polygon2D { Color = dark, Polygon = Rect(-85, -175, 170, 14) });                // arm bar
        body.AddChild(new Polygon2D { Color = straw, Polygon = Ellipse(52, 70), Position = new Vector2(0, -170) }); // body
        body.AddChild(new Polygon2D { Color = straw, Polygon = Ellipse(32, 32), Position = new Vector2(0, -268) }); // head
        foreach (var (r, at) in new[] { (Ellipse(52, 70), new Vector2(0, -170)), (Ellipse(32, 32), new Vector2(0, -268)) })
            body.AddChild(new Line2D { Points = r, Closed = true, Width = 3, DefaultColor = Colors.White, Position = at, Antialiased = true });
        // a target painted on its chest and rope bands
        body.AddChild(new Line2D { Points = Ellipse(26, 26), Closed = true, Width = 4, DefaultColor = new Color(0.8f, 0.15f, 0.1f), Position = new Vector2(0, -175) });
        body.AddChild(new Polygon2D { Color = new Color(0.8f, 0.15f, 0.1f), Polygon = Ellipse(9, 9), Position = new Vector2(0, -175) });
        foreach (float y in new[] { -215f, -130f })
            body.AddChild(new Line2D { Points = new[] { new Vector2(-46, y), new Vector2(46, y) }, Width = 5, DefaultColor = dark });
        return root;
    }

    static Vector2[] Ellipse(float rx, float ry, int n = 28)
    {
        var p = new Vector2[n];
        for (int i = 0; i < n; i++) { float a = Mathf.Tau * i / n; p[i] = new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry); }
        return p;
    }

    static Vector2[] Rect(float x, float y, float w, float h) => new[] { new Vector2(x, y), new Vector2(x + w, y), new Vector2(x + w, y + h), new Vector2(x, y + h) };

    // ---------------- the attack, over and over ----------------
    bool Alive => GodotObject.IsInstanceValid(View) && View.IsInsideTree();

    async void Loop()
    {
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree) return;
            await Wait(tree, 0.7);
            while (Alive)
            {
                if (View.IsVisibleInTree() && FmData.Get(_id).IsMonster) await Attack(tree);
                await Wait(tree, 1.8);
            }
        }
        catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] monster stage: " + ex.Message); }
    }

    async Task Attack(SceneTree tree)
    {
        var from = _monsterHome + new Vector2(0, -140);
        var to = _dummyHome + new Vector2(0, -175);
        if (FmAttackVfx.FliesIn(_id))
        {
            // melee: the monster lunges at the dummy and springs back
            var t = _monster.CreateTween();
            t.TweenProperty(_monster, "position", _dummyHome - new Vector2(200, 0), 0.18).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            await t.ToSignal(t, Tween.SignalName.Finished);
            if (!Alive) return;
            from = _monster.Position + new Vector2(0, -140);
        }
        await FmAttackVfx.Play(_id, from, to, _world);
        if (!Alive) return;
        Hit();
        if (FmAttackVfx.FliesIn(_id))
        {
            var back = _monster.CreateTween();
            back.TweenProperty(_monster, "position", _monsterHome, 0.35).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        }
    }

    /// <summary>The dummy rocks back and flashes.</summary>
    void Hit()
    {
        if (_dummy.GetNodeOrNull<Node2D>("Body") is not { } body) return;
        var t = body.CreateTween();
        t.TweenProperty(body, "rotation", 0.22f, 0.06);
        t.TweenProperty(body, "rotation", -0.12f, 0.12);
        t.TweenProperty(body, "rotation", 0.05f, 0.12);
        t.TweenProperty(body, "rotation", 0f, 0.12);
        var f = body.CreateTween();
        f.TweenProperty(body, "modulate", new Color(2.2f, 2.2f, 2.2f), 0.04);
        f.TweenProperty(body, "modulate", Colors.White, 0.25);
    }

    static async Task Wait(SceneTree tree, double s) { var t = tree.CreateTimer(s); await t.ToSignal(t, SceneTreeTimer.SignalName.Timeout); }
}
