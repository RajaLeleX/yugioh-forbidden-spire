using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace FMDuelist.Game;

/// <summary>
/// In FM runs every item is a Forbidden Memories card or a Guardian Star, so using one doesn't toss it like a potion:
/// the duelist casts it. The card's art rises over Duel Master K on a spinning magic circle, breaks into sparks that
/// stream to the target, and a sigil closes over it: a purple seal pressing down on an enemy (a magic debuff), or a
/// golden circle with rising light on your side (a blessing). Drawn and synthesized in code.
/// </summary>
[HarmonyPatch(typeof(NItemThrowVfx), nameof(NItemThrowVfx.Create))]
static class FmSpellCastPatch
{
    static void Postfix(Vector2 sourcePosition, Vector2 targetPosition, Texture2D? itemTexture, NItemThrowVfx? __result)
    {
        try
        {
            if (__result == null || FmField.Duelist == null || NCombatRoom.Instance is not { } room) return;
            if (!FmRules.IsFmPlayer(FmField.Duelist)) return;
            __result.Visible = false; // the toss still runs its course (and frees itself), just unseen
            FmSpellCastVfx.Play(room.CombatVfxContainer, sourcePosition, targetPosition, itemTexture);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] spell cast vfx: " + ex.Message); }
    }
}

public static class FmSpellCastVfx
{
    static readonly Color Violet = new(0.72f, 0.35f, 1f), VioletDeep = new(0.38f, 0.1f, 0.62f), Gold = new(1f, 0.83f, 0.35f);

    public static void Play(Node parent, Vector2 from, Vector2 to, Texture2D? art)
    {
        bool blessing = Math.Abs(to.X - from.X) < 160; // aimed at your own side: a buff
        var main = blessing ? Gold : Violet;
        var root = new Node2D { ZIndex = 50 };
        parent.AddChild(root);
        FmSpellCastSfx.Play(blessing);

        // 1) the card rises over the caster on a spinning circle
        var castAt = from + new Vector2(0, -70);
        var castCircle = Circle(main, 46, 0.9f);
        castCircle.Position = from + new Vector2(0, 30);
        castCircle.Scale = new Vector2(0.1f, 0.04f);
        root.AddChild(castCircle);
        Tw(castCircle).TweenProperty(castCircle, "scale", new Vector2(1.1f, 0.42f), 0.18).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        Spin(castCircle, 1.6f);

        Node2D card = new() { Position = from + new Vector2(0, -20), Scale = Vector2.One * 0.2f, Modulate = new Color(1, 1, 1, 0) };
        root.AddChild(card);
        if (art != null)
        {
            float k = 78f / Math.Max(1, art.GetHeight());
            card.AddChild(new Sprite2D { Texture = art, Scale = Vector2.One * k });
        }
        card.AddChild(Glow(main, 46, 0.45f));
        var rise = Tw(card).SetParallel();
        rise.TweenProperty(card, "position", castAt, 0.2).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        rise.TweenProperty(card, "scale", Vector2.One, 0.2).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        rise.TweenProperty(card, "modulate:a", 1f, 0.12);
        rise.Chain().TweenProperty(card, "scale", Vector2.One * 1.25f, 0.08);
        rise.Parallel().TweenProperty(card, "modulate", new Color(main.R * 2, main.G * 2, main.B * 2, 0), 0.12);
        rise.Chain().TweenCallback(Callable.From(card.QueueFree));
        var castFade = Tw(castCircle);
        castFade.TweenInterval(0.32);
        castFade.TweenProperty(castCircle, "modulate:a", 0f, 0.2);

        // 2) sparks stream to the target
        var rng = new Random();
        for (int i = 0; i < 14; i++)
        {
            var spark = Diamond(i % 3 == 0 ? Colors.White : main, 5 + (float)rng.NextDouble() * 4);
            spark.Position = castAt + new Vector2((float)rng.NextDouble() * 40 - 20, (float)rng.NextDouble() * 40 - 20);
            spark.Modulate = new Color(1, 1, 1, 0);
            root.AddChild(spark);
            var mid = (spark.Position + to) / 2 + new Vector2((float)rng.NextDouble() * 80 - 40, -90 - (float)rng.NextDouble() * 70);
            float delay = 0.24f + i * 0.012f, fly = 0.26f + (float)rng.NextDouble() * 0.08f;
            var t = Tw(spark);
            t.TweenInterval(delay);
            t.TweenProperty(spark, "modulate:a", 1f, 0.03);
            t.TweenMethod(Callable.From<float>(u =>
            {
                var a = spark.Position;
                var p0 = castAt.Lerp(mid, u); var p1 = mid.Lerp(to, u);
                spark.Position = p0.Lerp(p1, u);
                spark.Rotation = (spark.Position - a).Angle();
            }), 0f, 1f, fly).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
            t.TweenProperty(spark, "modulate:a", 0f, 0.05);
            t.TweenCallback(Callable.From(spark.QueueFree));
        }

        // 3) the sigil closes over the target
        var sigil = Circle(main, 70, 1f);
        sigil.Position = to;
        sigil.Scale = Vector2.One * 0.2f;
        sigil.Modulate = new Color(1, 1, 1, 0);
        root.AddChild(sigil);
        Spin(sigil, blessing ? 1.2f : -1.8f);
        var s = Tw(sigil);
        s.TweenInterval(0.52);
        s.TweenProperty(sigil, "modulate:a", 1f, 0.06);
        s.Parallel().TweenProperty(sigil, "scale", Vector2.One * 1.35f, 0.18).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        if (!blessing)
        {
            // the seal presses down onto the enemy and is absorbed
            s.TweenProperty(sigil, "scale", new Vector2(1.0f, 0.38f), 0.16).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            s.Parallel().TweenProperty(sigil, "position", to + new Vector2(0, 70), 0.16);
            s.TweenProperty(sigil, "modulate", new Color(VioletDeep.R, VioletDeep.G, VioletDeep.B, 0f), 0.35);
            // chains of runes close in from the sides
            for (int side = -1; side <= 1; side += 2)
                for (int k = 0; k < 4; k++)
                {
                    var rune = Diamond(k % 2 == 0 ? main : VioletDeep, 7);
                    rune.Position = to + new Vector2(side * (150 + k * 26), -30 + k * 14);
                    rune.Modulate = new Color(1, 1, 1, 0);
                    root.AddChild(rune);
                    var rt = Tw(rune);
                    rt.TweenInterval(0.56 + k * 0.025);
                    rt.TweenProperty(rune, "modulate:a", 1f, 0.04);
                    rt.TweenProperty(rune, "position", to + new Vector2(side * 10, 20), 0.18).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
                    rt.TweenProperty(rune, "modulate:a", 0f, 0.08);
                    rt.TweenCallback(Callable.From(rune.QueueFree));
                }
        }
        else
        {
            // a blessing: the circle lies under them and light rises
            s.Parallel().TweenProperty(sigil, "position", to + new Vector2(0, 60), 0.18);
            s.TweenProperty(sigil, "scale", new Vector2(1.4f, 0.5f), 0.15);
            s.TweenProperty(sigil, "modulate:a", 0f, 0.4);
            for (int k = 0; k < 10; k++)
            {
                var mote = Diamond(k % 2 == 0 ? Gold : Colors.White, 5);
                mote.Position = to + new Vector2((float)rng.NextDouble() * 120 - 60, 60);
                mote.Modulate = new Color(1, 1, 1, 0);
                root.AddChild(mote);
                var mt = Tw(mote);
                mt.TweenInterval(0.62 + k * 0.03);
                mt.TweenProperty(mote, "modulate:a", 1f, 0.05);
                mt.Parallel().TweenProperty(mote, "position:y", to.Y - 90 - (float)rng.NextDouble() * 60, 0.45).SetTrans(Tween.TransitionType.Sine);
                mt.TweenProperty(mote, "modulate:a", 0f, 0.15);
                mt.TweenCallback(Callable.From(mote.QueueFree));
            }
        }
        var flash = Glow(main, 110, 0.0f);
        flash.Position = to;
        root.AddChild(flash);
        var f = Tw(flash);
        f.TweenInterval(0.54);
        f.TweenProperty(flash, "modulate:a", 0.75f, 0.05);
        f.TweenProperty(flash, "modulate:a", 0f, 0.3);

        var end = Tw(root);
        end.TweenInterval(1.6);
        end.TweenCallback(Callable.From(root.QueueFree));
    }

    static Tween Tw(Node n) => n.CreateTween();

    static void Spin(Node2D n, float turnsPerSecond)
    {
        var t = n.CreateTween().SetLoops();
        t.TweenProperty(n, "rotation", n.Rotation + Mathf.Tau * Math.Sign(turnsPerSecond), 1f / Math.Abs(turnsPerSecond)).AsRelative();
    }

    /// <summary>A magic circle: two rings, a six-pointed star and rune dots.</summary>
    static Node2D Circle(Color c, float r, float alpha)
    {
        var n = new Node2D { Modulate = new Color(1, 1, 1, alpha) };
        n.AddChild(Ring(r, 3f, c));
        n.AddChild(Ring(r * 0.82f, 2f, c));
        for (int tri = 0; tri < 2; tri++)
        {
            var pts = new Vector2[4];
            for (int i = 0; i < 4; i++) pts[i] = Vector2.FromAngle(Mathf.Tau * i / 3 + tri * Mathf.Pi - Mathf.Pi / 2) * r * 0.8f;
            n.AddChild(new Line2D { Points = pts, Width = 2f, DefaultColor = c, Antialiased = true });
        }
        for (int i = 0; i < 12; i++)
        {
            var d = Diamond(c, 3.2f);
            d.Position = Vector2.FromAngle(Mathf.Tau * i / 12) * r * 0.91f;
            n.AddChild(d);
        }
        n.AddChild(Glow(c, r * 1.15f, 0.25f));
        return n;
    }

    static Line2D Ring(float r, float w, Color c)
    {
        var pts = new Vector2[49];
        for (int i = 0; i < pts.Length; i++) pts[i] = Vector2.FromAngle(Mathf.Tau * i / 48) * r;
        return new Line2D { Points = pts, Width = w, DefaultColor = c, Antialiased = true };
    }

    static Polygon2D Diamond(Color c, float s) => new()
    {
        Polygon = new[] { new Vector2(s * 1.6f, 0), new Vector2(0, s * 0.7f), new Vector2(-s * 1.6f, 0), new Vector2(0, -s * 0.7f) },
        Color = c,
    };

    /// <summary>A soft round glow (radial alpha falloff).</summary>
    static Polygon2D Glow(Color c, float r, float alpha)
    {
        const int n = 32;
        var pts = new Vector2[n + 1]; var cols = new Color[n + 1];
        pts[0] = Vector2.Zero; cols[0] = new Color(c, 0.9f);
        for (int i = 0; i < n; i++) { pts[i + 1] = Vector2.FromAngle(Mathf.Tau * i / n) * r; cols[i + 1] = new Color(c, 0f); }
        var poly = new int[n * 3];
        for (int i = 0; i < n; i++) { poly[i * 3] = 0; poly[i * 3 + 1] = i + 1; poly[i * 3 + 2] = (i + 1) % n + 1; }
        return new Polygon2D { Polygon = pts, VertexColors = cols, Polygons = new Godot.Collections.Array { poly }, Modulate = new Color(1, 1, 1, alpha) };
    }
}

/// <summary>The casting sound: a shimmering arpeggio, then a low descending seal (debuff) or a bright rising chime (blessing).</summary>
static class FmSpellCastSfx
{
    const int Rate = 44100;
    static AudioStreamWav? _curse, _bless;
    static AudioStreamPlayer? _player;

    public static void Play(bool blessing)
    {
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree) return;
            var wav = blessing ? (_bless ??= Build(true)) : (_curse ??= Build(false));
            if (_player == null || !GodotObject.IsInstanceValid(_player)) { _player = new AudioStreamPlayer { Bus = "Master", VolumeDb = -6 }; tree.Root.AddChild(_player); }
            _player.Stream = wav;
            _player.Play();
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] spell cast sfx: " + ex.Message); }
    }

    static AudioStreamWav Build(bool blessing)
    {
        int n = (int)(1.3 * Rate);
        var L = new float[n]; var R = new float[n];
        void Tone(double start, double len, Func<double, double> freq, Func<double, double> env, double gain, double pan)
        {
            double ph = 0; int s0 = (int)(start * Rate), m = (int)(len * Rate);
            for (int i = 0; i < m && s0 + i < n; i++)
            {
                double t = (double)i / Rate;
                ph += 2 * Math.PI * freq(t) / Rate;
                double v = (Math.Sin(ph) + 0.3 * Math.Sin(2 * ph)) * env(t) * gain;
                L[s0 + i] += (float)(v * (1 - pan * 0.5)); R[s0 + i] += (float)(v * (1 + pan * 0.5));
            }
        }
        Func<double, double> Ding(double k) => t => Math.Min(1, t * 200) * Math.Exp(-t * k);
        // the cast: a quick shimmering arpeggio as the card rises
        double[] up = { 659.3, 830.6, 987.8, 1318.5 };
        for (int i = 0; i < up.Length; i++) Tone(i * 0.05, 0.6, _ => up[i], Ding(7), 0.35, i % 2 == 0 ? -0.6 : 0.6);
        // sparkle trail
        var rng = new Random(7);
        for (int i = 0; i < 12; i++) { double f = 2200 + rng.NextDouble() * 2600; Tone(0.25 + i * 0.022, 0.15, _ => f, Ding(30), 0.12, rng.NextDouble() * 2 - 1); }
        if (blessing)
        {
            double[] ch = { 523.3, 659.3, 784.0, 1046.5 };
            foreach (var f in ch) Tone(0.55, 0.75, t => f * (1 + 0.003 * Math.Sin(t * 30)), Ding(4), 0.3, 0);
        }
        else
        {
            // the seal: a dark falling tone and a muffled thump with a dissonant shimmer
            Tone(0.52, 0.7, t => 330 * Math.Pow(0.45, t / 0.5), Ding(4.5), 0.55, 0);
            Tone(0.52, 0.7, t => 349 * Math.Pow(0.45, t / 0.5), Ding(4.5), 0.35, 0);
            Tone(0.62, 0.35, t => 90 * (1 - 0.4 * Math.Min(1, t * 6)), Ding(10), 0.9, 0);
            Tone(0.66, 0.5, _ => 1480, Ding(8), 0.1, -0.5);
            Tone(0.68, 0.5, _ => 1568, Ding(8), 0.1, 0.5);
        }
        float peak = 1e-4f;
        for (int i = 0; i < n; i++) peak = Math.Max(peak, Math.Max(Math.Abs(L[i]), Math.Abs(R[i])));
        float g = 0.85f / peak;
        var data = new byte[n * 4];
        for (int i = 0; i < n; i++)
        {
            short l = (short)Math.Clamp(L[i] * g * 32767, -32767, 32767), r = (short)Math.Clamp(R[i] * g * 32767, -32767, 32767);
            data[i * 4] = (byte)l; data[i * 4 + 1] = (byte)(l >> 8); data[i * 4 + 2] = (byte)r; data[i * 4 + 3] = (byte)(r >> 8);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, Stereo = true, MixRate = Rate, Data = data };
    }
}
