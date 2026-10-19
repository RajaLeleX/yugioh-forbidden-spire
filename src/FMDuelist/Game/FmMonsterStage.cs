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
    /// <summary>Cards the stage can show: monsters attack; spells, traps and rituals are cast by Duel Master K (equips aren't shown).</summary>
    public static bool HasScene(int id) => FmData.Get(id) is var c && (c.IsMonster || c.Type is FmType.Magic or FmType.Trap or FmType.Ritual);

    static Node2D Monster(int id)
    {
        if (!FmData.Get(id).IsMonster) return Caster();
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

    /// <summary>Duel Master K (his portrait) standing in the room to cast the card.</summary>
    static Node2D Caster()
    {
        var root = new Node2D();
        root.AddChild(new Polygon2D { Color = new Color(0, 0, 0, 0.75f), Polygon = Ellipse(110, 24), Position = new Vector2(0, 6) });
        var body = new Node2D { Name = "Body" };
        root.AddChild(body);
        if (FmDuelMasterK.Portrait() is { } tex)
        {
            const int h = 270;
            body.AddChild(new Sprite2D { Texture = FmScene.Sized(tex, h), Position = new Vector2(0, -h / 2f - 6), TextureFilter = CanvasItem.TextureFilterEnum.Linear });
        }
        else body.AddChild(new Polygon2D { Color = new Color(0.3f, 0.3f, 0.35f), Polygon = Rect(-50, -260, 100, 260) });
        var t = body.CreateTween().SetLoops();
        t.TweenProperty(body, "position:y", -6f, 1.1).SetTrans(Tween.TransitionType.Sine);
        t.TweenProperty(body, "position:y", 0f, 1.1).SetTrans(Tween.TransitionType.Sine);
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
                if (View.IsVisibleInTree())
                {
                    var info = FmData.Get(_id);
                    if (info.IsMonster) await Attack(tree);
                    else if (info.Type == FmType.Trap) await Trap(tree);
                    else if (info.Type is FmType.Magic or FmType.Ritual) await Cast(tree);
                }
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

    // ---------------- spells and traps ----------------
    Node2D CardArt(bool faceDown)
    {
        var n = new Node2D();
        const float h = 120;
        Texture2D? tex = faceDown ? FmRivalHand.Back() : FmData.Art(_id);
        if (tex != null)
        {
            float k = h / tex.GetHeight(), w = tex.GetWidth() * k;
            n.AddChild(new Sprite2D { Name = "Pic", Texture = tex, Scale = new Vector2(k, k), TextureFilter = CanvasItem.TextureFilterEnum.Linear });
            n.AddChild(new Line2D { Width = 3, DefaultColor = new Color(0.95f, 0.85f, 0.45f), Closed = true, Points = new[] { new Vector2(-w / 2, -h / 2), new Vector2(w / 2, -h / 2), new Vector2(w / 2, h / 2), new Vector2(-w / 2, h / 2) } });
        }
        _world.AddChild(n);
        return n;
    }

    string Name => FmData.Get(_id).Name.ToLowerInvariant();
    bool Has(params string[] words) => words.Any(w => Name.Contains(w));

    /// <summary>Duel Master K holds the card up, it glows, and its effect lands (on the dummy, or on him for a heal).</summary>
    async Task Cast(SceneTree tree)
    {
        var card = CardArt(false);
        var start = _monsterHome + new Vector2(70, -150);
        card.Position = start; card.Modulate = new Color(1, 1, 1, 0);
        var up = card.CreateTween();
        up.TweenProperty(card, "modulate:a", 1f, 0.2);
        up.Parallel().TweenProperty(card, "position", start + new Vector2(0, -150), 0.35).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        up.TweenProperty(card, "modulate", new Color(2f, 2f, 1.6f, 1f), 0.15);
        up.TweenProperty(card, "modulate", Colors.White, 0.2);
        await up.ToSignal(up, Tween.SignalName.Finished);
        if (!Alive) return;
        var from = card.Position;
        var at = _dummyHome + new Vector2(0, -175);
        if (Has("remedy", "curry", "medicine", "soul of the pure", "dian keto", "recovery", "heal"))
        {
            // a heal: green light rains on Duel Master K
            Burst(_monsterHome + new Vector2(0, -140), new Color(0.4f, 1f, 0.5f), 70, up: true);
            await Wait(tree, 0.7);
        }
        else if (Has("raigeki", "thunder", "lightning", "spark"))
        {
            for (int i = 0; i < 3; i++) Bolt(at + new Vector2(-60 + i * 60, 0));
            await Wait(tree, 0.12); Hit(); await Wait(tree, 0.5);
        }
        else if (Has("dark hole", "black hole"))
        {
            var hole = new Node2D { Position = at + new Vector2(0, 40), Scale = new Vector2(0.1f, 0.1f) };
            for (int i = 0; i < 4; i++) hole.AddChild(new Polygon2D { Color = new Color(0.2f + 0.08f * i, 0f, 0.3f + 0.1f * i, 0.45f), Polygon = Ellipse(140 - i * 30, 60 - i * 12) });
            _world.AddChild(hole);
            var spin = hole.CreateTween().SetParallel();
            spin.TweenProperty(hole, "scale", Vector2.One, 0.35).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
            spin.TweenProperty(hole, "rotation", Mathf.Tau, 1.0);
            await Wait(tree, 0.3); Hit(); await Wait(tree, 0.6);
            var close = hole.CreateTween(); close.TweenProperty(hole, "scale", new Vector2(0.01f, 0.01f), 0.25); close.TweenCallback(Callable.From(hole.QueueFree));
        }
        else if (Has("swords of revealing"))
        {
            for (int i = 0; i < 5; i++) Sword(_dummyHome + new Vector2(-150 + i * 75, 0), i * 0.08);
            await Wait(tree, 0.55); Hit(); await Wait(tree, 0.6);
        }
        else
        {
            // an orb of the spell's power flies at the dummy (fire for burn spells, green magic otherwise)
            bool fire = Has("hinotama", "final flame", "ookazi", "fire", "flame", "burn", "tremendous");
            var c = fire ? new Color(1f, 0.5f, 0.1f) : FmData.Get(_id).Type == FmType.Ritual ? new Color(0.5f, 0.7f, 1f) : new Color(0.45f, 1f, 0.55f);
            var orb = new Node2D { Position = from };
            orb.AddChild(new Polygon2D { Color = new Color(c, 0.35f), Polygon = Ellipse(30, 30) });
            orb.AddChild(new Polygon2D { Color = c, Polygon = Ellipse(16, 16) });
            _world.AddChild(orb);
            var fly = orb.CreateTween();
            fly.TweenProperty(orb, "position", at, 0.35).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            await fly.ToSignal(fly, Tween.SignalName.Finished);
            orb.QueueFree();
            if (!Alive) return;
            Burst(at, c, 50, up: false); Hit();
            await Wait(tree, 0.5);
        }
        var fade = card.CreateTween(); fade.TweenProperty(card, "modulate:a", 0f, 0.3); fade.TweenCallback(Callable.From(card.QueueFree));
    }

    /// <summary>A trap: the dummy charges, the face-down card in front of Duel Master K flips up, and the dummy is thrown back.</summary>
    async Task Trap(SceneTree tree)
    {
        var card = CardArt(true);
        card.Position = _monsterHome + new Vector2(140, -70);
        card.Modulate = new Color(1, 1, 1, 0);
        var show = card.CreateTween(); show.TweenProperty(card, "modulate:a", 1f, 0.25);
        await Wait(tree, 0.45);
        if (!Alive) return;
        // the dummy attacks
        var charge = _dummy.CreateTween();
        charge.TweenProperty(_dummy, "position", _monsterHome + new Vector2(300, 0), 0.3).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        await charge.ToSignal(charge, Tween.SignalName.Finished);
        if (!Alive) return;
        // the trap springs: it flips face up with a flash
        var flip = card.CreateTween();
        flip.TweenProperty(card, "scale:x", 0f, 0.1);
        flip.TweenCallback(Callable.From(() =>
        {
            if (card.GetNodeOrNull<Sprite2D>("Pic") is { } pic && FmData.Art(_id) is { } art)
            {
                pic.Texture = art; pic.Scale = Vector2.One * (120f / art.GetHeight());
            }
        }));
        flip.TweenProperty(card, "scale:x", 1f, 0.1);
        flip.TweenProperty(card, "modulate", new Color(2f, 1.4f, 2f, 1f), 0.08);
        flip.TweenProperty(card, "modulate", Colors.White, 0.2);
        await flip.ToSignal(flip, Tween.SignalName.Finished);
        if (!Alive) return;
        var where = _dummy.Position + new Vector2(0, -175);
        if (Has("hole"))
        {
            // Trap Hole: the dummy drops out of sight, then climbs back to its spot
            var drop = _dummy.CreateTween();
            drop.TweenProperty(_dummy, "position:y", _dummyHome.Y + 160, 0.25).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            drop.Parallel().TweenProperty(_dummy, "modulate:a", 0f, 0.25);
            await drop.ToSignal(drop, Tween.SignalName.Finished);
            _dummy.Position = _dummyHome;
            var back = _dummy.CreateTween(); back.TweenProperty(_dummy, "modulate:a", 1f, 0.35);
        }
        else
        {
            Burst(where, new Color(1f, 0.45f, 0.8f), 60, up: false);
            Hit();
            var back = _dummy.CreateTween();
            back.TweenProperty(_dummy, "position", _dummyHome, 0.35).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        }
        await Wait(tree, 0.6);
        var fade = card.CreateTween(); fade.TweenProperty(card, "modulate:a", 0f, 0.3); fade.TweenCallback(Callable.From(card.QueueFree));
    }

    void Burst(Vector2 at, Color c, int amount, bool up)
    {
        var p = new CpuParticles2D
        {
            Position = at, Amount = amount, Lifetime = 0.8, OneShot = true, Explosiveness = 0.85f,
            EmissionShape = CpuParticles2D.EmissionShapeEnum.Sphere, EmissionSphereRadius = 40,
            Direction = new Vector2(0, -1), Spread = up ? 30 : 180, InitialVelocityMin = 80, InitialVelocityMax = 240, Gravity = new Vector2(0, up ? -120 : 200),
            ScaleAmountMin = 3, ScaleAmountMax = 7, Color = c,
        };
        _world.AddChild(p);
        p.Emitting = true;
        var t = p.CreateTween(); t.TweenInterval(1.2); t.TweenCallback(Callable.From(p.QueueFree));
    }

    void Bolt(Vector2 to)
    {
        var rng = new Random();
        var pts = new List<Vector2>();
        for (float y = 0; y < to.Y; y += 40) pts.Add(new Vector2(to.X + rng.Next(-28, 28), y));
        pts.Add(to);
        var l = new Line2D { Points = pts.ToArray(), Width = 6, DefaultColor = new Color(1f, 1f, 0.6f), Antialiased = true };
        _world.AddChild(l);
        var t = l.CreateTween(); t.TweenProperty(l, "modulate:a", 0f, 0.45); t.TweenCallback(Callable.From(l.QueueFree));
    }

    void Sword(Vector2 ground, double delay)
    {
        var s = new Node2D { Position = ground + new Vector2(0, -420) };
        s.AddChild(new Polygon2D { Color = new Color(1f, 0.95f, 0.6f), Polygon = new[] { new Vector2(0, 60), new Vector2(-7, 0), new Vector2(-7, -60), new Vector2(7, -60), new Vector2(7, 0) } });
        s.AddChild(new Polygon2D { Color = new Color(0.85f, 0.7f, 0.2f), Polygon = Rect(-18, -66, 36, 8) });
        s.Modulate = new Color(1, 1, 1, 0);
        _world.AddChild(s);
        var t = s.CreateTween();
        t.TweenInterval(delay);
        t.TweenProperty(s, "modulate:a", 1f, 0.05);
        t.Parallel().TweenProperty(s, "position", ground + new Vector2(0, -40), 0.3).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        t.TweenInterval(0.6);
        t.TweenProperty(s, "modulate:a", 0f, 0.35);
        t.TweenCallback(Callable.From(s.QueueFree));
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
