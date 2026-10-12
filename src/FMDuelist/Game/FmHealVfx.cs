using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace FMDuelist.Game;

/// <summary>
/// A unique animation and sound for every healing spell, all ending in the same healing chime and green sparkles:
///   Mooyan Curry - a steaming bowl, eaten (crunchy bites and a slurp)
///   Red Medicine - a red potion bottle tipped to the duelist's lips (gulps)
///   Goblin's Secret Remedy - a bubbling green flask (fizz and pops)
///   Soul of the Pure - a pure white soul descends with a halo (angelic choir)
///   Dian Keto the Cure Master - a pillar of Healing Light with a glowing cross (shimmering light)
///   Eternal Draught - a blue draught, drunk (gulps)
/// All drawn and synthesized in code.
/// </summary>
public static class FmHealVfx
{
    enum Kind { Curry, Medicine, Remedy, Soul, DianKeto, Draught }

    static Kind KindOf(int id) => id switch
    {
        338 => Kind.Curry, 339 => Kind.Medicine, 340 => Kind.Remedy, 341 => Kind.Soul, 342 => Kind.DianKeto, 664 => Kind.Draught,
        _ => Kind.Medicine,
    };

    public static async Task Play(int cardId, Creature who)
    {
        if (NCombatRoom.Instance?.GetCreatureNode(who) is not { } node || Engine.GetMainLoop() is not SceneTree tree) return;
        var kind = KindOf(cardId);
        var layer = new CanvasLayer { Layer = 60 };
        tree.Root.AddChild(layer);
        var rect = node.Hitbox.GetGlobalRect();
        var head = new Vector2(rect.GetCenter().X, rect.Position.Y + rect.Size.Y * 0.2f);
        var center = rect.GetCenter();
        PlaySound(kind);
        try
        {
            switch (kind)
            {
                case Kind.Curry: await Curry(layer, tree, head); break;
                case Kind.Medicine: await Bottle(layer, tree, head, new Color(0.9f, 0.1f, 0.15f)); break;
                case Kind.Draught: await Bottle(layer, tree, head, new Color(0.25f, 0.55f, 1f)); break;
                case Kind.Remedy: await Remedy(layer, tree, head); break;
                case Kind.Soul: await Soul(layer, tree, center); break;
                case Kind.DianKeto: await HealingLight(layer, tree, center, rect.Size.Y); break;
            }
            Restore(layer, center, rect.Size);
        }
        catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] heal vfx: " + ex.Message); }
        var t = tree.CreateTimer(1.6);
        t.Connect(SceneTreeTimer.SignalName.Timeout, Callable.From(layer.QueueFree));
    }

    // ---------------- pictures ----------------
    static CanvasItemMaterial Add() => new() { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
    static async Task Run(Tween t) => await t.ToSignal(t, Tween.SignalName.Finished);
    static async Task Wait(SceneTree tree, double s) { var t = tree.CreateTimer(s); await t.ToSignal(t, SceneTreeTimer.SignalName.Timeout); }

    static Vector2[] Circle(float r, int n = 24, float squash = 1)
    {
        var p = new Vector2[n];
        for (int i = 0; i < n; i++) { float a = Mathf.Tau * i / n; p[i] = new Vector2(Mathf.Cos(a) * r, Mathf.Sin(a) * r * squash); }
        return p;
    }

    static CpuParticles2D Particles(Node layer, Vector2 at, Color c, int amount, double life, Vector2 dir, float spread, float vMin, float vMax, Vector2 grav, float sMin, float sMax, bool add = true, bool oneShot = true)
    {
        var p = new CpuParticles2D
        {
            Position = at, Amount = amount, Lifetime = life, OneShot = oneShot, Explosiveness = oneShot ? 0.6f : 0f, Direction = dir, Spread = spread,
            InitialVelocityMin = vMin, InitialVelocityMax = vMax, Gravity = grav, ScaleAmountMin = sMin, ScaleAmountMax = sMax, Color = c, LocalCoords = false,
        };
        if (add) p.Material = Add();
        layer.AddChild(p);
        p.Emitting = true;
        return p;
    }

    /// <summary>A bowl of curry floats in, steaming, and is eaten in three bites.</summary>
    static async Task Curry(Node layer, SceneTree tree, Vector2 head)
    {
        var bowl = new Node2D { Position = head + new Vector2(0, -160), Modulate = new Color(1, 1, 1, 0) };
        bowl.AddChild(new Polygon2D { Polygon = new[] { new Vector2(-70, 0), new Vector2(70, 0), new Vector2(50, 45), new Vector2(-50, 45) }, Color = new Color(0.95f, 0.95f, 0.9f) });
        bowl.AddChild(new Polygon2D { Polygon = Circle(70, 24, 0.25f), Color = new Color(0.65f, 0.38f, 0.1f) });            // curry
        bowl.AddChild(new Polygon2D { Polygon = Circle(28, 16, 0.3f), Color = new Color(1f, 1f, 0.95f), Position = new Vector2(-22, -2) }); // rice
        layer.AddChild(bowl);
        var steam = Particles(layer, head + new Vector2(0, -165), new Color(1, 1, 1, 0.35f), 30, 1.2, Vector2.Up, 20, 30, 70, new Vector2(0, -20), 6, 12, add: false, oneShot: false);
        var t = bowl.CreateTween();
        t.TweenProperty(bowl, "modulate:a", 1f, 0.2);
        t.TweenProperty(bowl, "position", head + new Vector2(0, -60), 0.35).SetTrans(Tween.TransitionType.Sine);
        await Run(t);
        for (int i = 0; i < 3; i++)
        {
            var bite = bowl.CreateTween();
            bite.TweenProperty(bowl, "scale", new Vector2(1.1f, 0.85f), 0.08);
            bite.TweenProperty(bowl, "scale", new Vector2(1f - i * 0.15f, 1f - i * 0.15f), 0.12);
            await Run(bite);
            await Wait(tree, 0.12);
        }
        steam.Emitting = false;
        var gone = bowl.CreateTween();
        gone.TweenProperty(bowl, "modulate:a", 0f, 0.2);
        await Run(gone);
    }

    /// <summary>A potion bottle tips to the duelist's lips; the liquid drains in gulps.</summary>
    static async Task Bottle(Node layer, SceneTree tree, Vector2 head, Color liquid)
    {
        var b = new Node2D { Position = head + new Vector2(90, -120), Modulate = new Color(1, 1, 1, 0), Rotation = 0.3f };
        var glass = new Color(0.85f, 0.95f, 1f, 0.55f);
        b.AddChild(new Polygon2D { Polygon = new[] { new Vector2(-10, -70), new Vector2(10, -70), new Vector2(10, -40), new Vector2(34, -10), new Vector2(34, 50), new Vector2(-34, 50), new Vector2(-34, -10), new Vector2(-10, -40) }, Color = glass });
        var fill = new Polygon2D { Polygon = new[] { new Vector2(-30, -6), new Vector2(30, -6), new Vector2(30, 46), new Vector2(-30, 46) }, Color = liquid };
        b.AddChild(fill);
        b.AddChild(new Polygon2D { Polygon = new[] { new Vector2(-12, -82), new Vector2(12, -82), new Vector2(12, -68), new Vector2(-12, -68) }, Color = new Color(0.55f, 0.35f, 0.2f) }); // cork
        layer.AddChild(b);
        var t = b.CreateTween();
        t.TweenProperty(b, "modulate:a", 1f, 0.2);
        t.TweenProperty(b, "position", head + new Vector2(40, -30), 0.3).SetTrans(Tween.TransitionType.Sine);
        t.Parallel().TweenProperty(b, "rotation", -2.2f, 0.3);
        await Run(t);
        var drops = Particles(layer, head + new Vector2(10, -20), liquid, 24, 0.4, Vector2.Down, 25, 40, 120, new Vector2(0, 300), 3, 6, add: false, oneShot: false);
        for (int i = 0; i < 3; i++)
        {
            var g = fill.CreateTween();
            g.TweenProperty(fill, "scale:y", 1f - (i + 1) * 0.33f, 0.18);
            g.Parallel().TweenProperty(b, "position:y", head.Y - 36, 0.09).SetTrans(Tween.TransitionType.Sine);
            g.TweenProperty(b, "position:y", head.Y - 30, 0.09);
            await Run(g);
            await Wait(tree, 0.1);
        }
        drops.Emitting = false;
        var away = b.CreateTween();
        away.TweenProperty(b, "rotation", 0.3f, 0.2);
        away.Parallel().TweenProperty(b, "modulate:a", 0f, 0.3);
        await Run(away);
    }

    /// <summary>A green flask bubbles over, then bursts into a healing cloud.</summary>
    static async Task Remedy(Node layer, SceneTree tree, Vector2 head)
    {
        var f = new Node2D { Position = head + new Vector2(0, -150), Modulate = new Color(1, 1, 1, 0) };
        f.AddChild(new Polygon2D { Polygon = Circle(42), Color = new Color(0.85f, 0.95f, 1f, 0.5f) });
        f.AddChild(new Polygon2D { Polygon = Circle(36), Color = new Color(0.3f, 0.85f, 0.25f) });
        f.AddChild(new Polygon2D { Polygon = new[] { new Vector2(-12, -70), new Vector2(12, -70), new Vector2(12, -36), new Vector2(-12, -36) }, Color = new Color(0.85f, 0.95f, 1f, 0.6f) });
        layer.AddChild(f);
        var t = f.CreateTween();
        t.TweenProperty(f, "modulate:a", 1f, 0.2);
        await Run(t);
        var bubbles = Particles(layer, head + new Vector2(0, -220), new Color(0.55f, 1f, 0.45f, 0.9f), 26, 0.8, Vector2.Up, 35, 40, 110, new Vector2(0, -30), 4, 9, oneShot: false);
        for (int i = 0; i < 4; i++)
        {
            var w = f.CreateTween();
            w.TweenProperty(f, "rotation", (i % 2 == 0 ? 0.15f : -0.15f), 0.1);
            await Run(w);
        }
        bubbles.Emitting = false;
        Particles(layer, f.Position, new Color(0.5f, 1f, 0.4f, 0.8f), 50, 0.9, Vector2.Down, 60, 60, 220, new Vector2(0, 160), 6, 14);
        var pop = f.CreateTween().SetParallel();
        pop.TweenProperty(f, "scale", Vector2.One * 1.6f, 0.2);
        pop.TweenProperty(f, "modulate:a", 0f, 0.2);
        await Run(pop);
    }

    /// <summary>A pure white soul drifts down with a halo and merges into the duelist.</summary>
    static async Task Soul(Node layer, SceneTree tree, Vector2 center)
    {
        var s = new Node2D { Position = center + new Vector2(0, -420), Modulate = new Color(1, 1, 1, 0) };
        s.AddChild(new Polygon2D { Polygon = Circle(60), Color = new Color(1f, 1f, 0.9f, 0.4f), Material = Add() });
        s.AddChild(new Polygon2D { Polygon = Circle(30), Color = new Color(1f, 1f, 1f, 0.95f), Material = Add() });
        var halo = new Line2D { Width = 6, DefaultColor = new Color(1f, 0.9f, 0.5f), Closed = true, Position = new Vector2(0, -55), Material = Add() };
        foreach (var p in Circle(34, 28, 0.3f)) halo.AddPoint(p);
        s.AddChild(halo);
        layer.AddChild(s);
        var trail = Particles(layer, s.Position, new Color(1f, 1f, 0.9f, 0.8f), 40, 0.6, Vector2.Up, 40, 10, 40, Vector2.Zero, 3, 6, oneShot: false);
        var t = s.CreateTween();
        t.TweenProperty(s, "modulate:a", 1f, 0.25);
        t.TweenProperty(s, "position", center, 0.7).SetTrans(Tween.TransitionType.Sine);
        t.Parallel().TweenMethod(Callable.From<Vector2>(p => trail.Position = p), s.Position, center, 0.7);
        await Run(t);
        trail.Emitting = false;
        var merge = s.CreateTween().SetParallel();
        merge.TweenProperty(s, "scale", Vector2.One * 2.4f, 0.3);
        merge.TweenProperty(s, "modulate:a", 0f, 0.3);
        await Run(merge);
    }

    /// <summary>Dian Keto's Healing Light: a pillar of light from above and a great glowing cross.</summary>
    static async Task HealingLight(Node layer, SceneTree tree, Vector2 center, float height)
    {
        var grad = new Gradient();
        grad.SetColor(0, new Color(0.6f, 1f, 0.7f, 0f));
        grad.SetColor(1, new Color(0.85f, 1f, 0.85f, 0.95f));
        var beam = new Sprite2D { Texture = new GradientTexture2D { Gradient = grad, Width = 8, Height = 256, FillFrom = Vector2.Zero, FillTo = new Vector2(0, 1) }, Material = Add(), Position = center + new Vector2(0, -360), Scale = new Vector2(1, 4f) };
        layer.AddChild(beam);
        var t = beam.CreateTween();
        t.TweenProperty(beam, "scale:x", 26f, 0.25).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
        await Run(t);
        var cross = new Node2D { Position = center + new Vector2(0, -20), Scale = Vector2.One * 0.2f, Modulate = new Color(1, 1, 1, 0) };
        cross.AddChild(new Polygon2D { Polygon = new[] { new Vector2(-22, -90), new Vector2(22, -90), new Vector2(22, 90), new Vector2(-22, 90) }, Color = new Color(0.7f, 1f, 0.75f), Material = Add() });
        cross.AddChild(new Polygon2D { Polygon = new[] { new Vector2(-70, -30), new Vector2(70, -30), new Vector2(70, 12), new Vector2(-70, 12) }, Color = new Color(0.7f, 1f, 0.75f), Material = Add() });
        layer.AddChild(cross);
        var c = cross.CreateTween().SetParallel();
        c.TweenProperty(cross, "scale", Vector2.One * 1.2f, 0.35).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        c.TweenProperty(cross, "modulate:a", 1f, 0.2);
        Particles(layer, center + new Vector2(0, -height * 0.3f), new Color(0.75f, 1f, 0.8f), 70, 1.0, Vector2.Down, 15, 120, 300, new Vector2(0, 80), 3, 7);
        await Run(c);
        await Wait(tree, 0.35);
        var f = beam.CreateTween().SetParallel();
        f.TweenProperty(beam, "scale:x", 1f, 0.4);
        f.TweenProperty(beam, "modulate:a", 0f, 0.4);
        f.TweenProperty(cross, "modulate:a", 0f, 0.4);
        await Run(f);
    }

    /// <summary>The shared finish: green sparkles rise around the duelist.</summary>
    static void Restore(Node layer, Vector2 center, Vector2 size)
    {
        var p = Particles(layer, center + new Vector2(0, size.Y * 0.3f), new Color(0.45f, 1f, 0.55f), 60, 1.1, Vector2.Up, 20, 80, 200, new Vector2(0, -40), 3, 7);
        p.EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle;
        p.EmissionRectExtents = new Vector2(size.X * 0.4f, 20);
    }

    // ---------------- sounds ----------------
    const int Rate = 44100;
    static readonly Dictionary<Kind, AudioStreamWav> _sfx = new();
    static AudioStreamPlayer? _player;

    static void PlaySound(Kind k)
    {
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree) return;
            if (!_sfx.TryGetValue(k, out var wav)) _sfx[k] = wav = Build(k);
            if (_player == null || !GodotObject.IsInstanceValid(_player)) { _player = new AudioStreamPlayer { Bus = "Master", VolumeDb = -3, MaxPolyphony = 2 }; tree.Root.AddChild(_player); }
            _player.Stream = wav;
            _player.Play();
        }
        catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] heal sfx: " + ex.Message); }
    }

    static AudioStreamWav Build(Kind k)
    {
        double len = k == Kind.DianKeto ? 2.4 : 2.0;
        int n = (int)(len * Rate);
        var L = new double[n]; var R = new double[n];
        var rng = new Random(31 + (int)k);
        void Tone(double at, double dur, Func<double, double> f, Func<double, double> env, double g, double pan = 0)
        {
            double ph = 0; int s0 = (int)(at * Rate), m = (int)(dur * Rate);
            for (int i = 0; i < m && s0 + i < n; i++) { double t = (double)i / Rate; ph += 2 * Math.PI * f(t) / Rate; double v = Math.Sin(ph) * env(t) * g; L[s0 + i] += v * (1 - pan * 0.5); R[s0 + i] += v * (1 + pan * 0.5); }
        }
        void Noise(double at, double dur, double cut, Func<double, double> env, double g)
        {
            double lp = 0; int s0 = (int)(at * Rate), m = (int)(dur * Rate); double a = Math.Min(1, 2 * Math.PI * cut / Rate);
            for (int i = 0; i < m && s0 + i < n; i++) { double t = (double)i / Rate; lp += (rng.NextDouble() * 2 - 1 - lp) * a; L[s0 + i] += lp * env(t) * g; R[s0 + i] += lp * env(t) * g; }
        }
        Func<double, double> Dec(double kk) => t => Math.Exp(-t * kk);
        Func<double, double> Bell(double d) => t => Math.Sin(Math.PI * Math.Clamp(t / d, 0, 1));
        void Gulp(double at) { Tone(at, 0.16, t => 260 - 900 * t, Bell(0.16), 0.5); Noise(at, 0.1, 600, Dec(30), 0.4); }

        double chimeAt = 1.0;
        switch (k)
        {
            case Kind.Curry: // three crunchy bites and a slurp
                for (int i = 0; i < 3; i++) { Noise(0.6 + i * 0.32, 0.09, 3500, Dec(40), 0.8); Tone(0.6 + i * 0.32, 0.12, _ => 140, Dec(25), 0.4); }
                Noise(1.5, 0.3, 1800, Bell(0.3), 0.4);
                chimeAt = 1.6; break;
            case Kind.Medicine:
            case Kind.Draught: // three gulps
                for (int i = 0; i < 3; i++) Gulp(0.55 + i * 0.3);
                chimeAt = 1.45; break;
            case Kind.Remedy: // fizzing bubbles and pops
                Noise(0.2, 0.9, 5000, Bell(0.9), 0.25);
                for (int i = 0; i < 16; i++) { double st = 0.2 + rng.NextDouble() * 0.8, f0 = 400 + rng.NextDouble() * 900; Tone(st, 0.06, t => f0 * (1 + t * 12), Dec(60), 0.3); }
                Noise(0.95, 0.15, 2500, Dec(20), 0.6);
                chimeAt = 1.1; break;
            case Kind.Soul: // angelic choir swelling as the soul descends
                foreach (var f in new[] { 523.25, 659.25, 783.99, 1046.5 }) Tone(0.1, 1.2, t => f * (1 + 0.006 * Math.Sin(t * 30)), t => Math.Min(1, t / 0.5) * Math.Exp(-Math.Max(0, t - 0.7) * 3), 0.16);
                chimeAt = 1.05; break;
            case Kind.DianKeto: // "Healing Light": a bright rising shimmer and a warm pad
                for (int i = 0; i < 8; i++) { double f = 880 * Math.Pow(2, i / 7.0); Tone(i * 0.06, 0.8, _ => f, Dec(4), 0.12, i % 2 == 0 ? -0.6 : 0.6); }
                foreach (var f in new[] { 392.0, 493.88, 587.33 }) Tone(0.25, 1.3, _ => f, t => Math.Min(1, t / 0.3) * Math.Exp(-Math.Max(0, t - 0.6) * 2.5), 0.18);
                Noise(0.25, 0.9, 9000, Bell(0.9), 0.12);
                chimeAt = 1.2; break;
        }
        // the shared healing chime: a rising three-note sparkle
        double[] notes = { 1318.5, 1567.98, 2093 };
        for (int i = 0; i < notes.Length; i++) { double f = notes[i]; Tone(chimeAt + i * 0.09, 0.7, _ => f, Dec(5), 0.2, i - 1); }

        double peak = 1e-4; for (int i = 0; i < n; i++) peak = Math.Max(peak, Math.Max(Math.Abs(L[i]), Math.Abs(R[i])));
        var data = new byte[n * 4];
        for (int i = 0; i < n; i++)
        {
            short l = (short)(L[i] / peak * 0.85 * 32767), r = (short)(R[i] / peak * 0.85 * 32767);
            data[i * 4] = (byte)l; data[i * 4 + 1] = (byte)(l >> 8); data[i * 4 + 2] = (byte)r; data[i * 4 + 3] = (byte)(r >> 8);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, Stereo = true, MixRate = Rate, Data = data };
    }
}
