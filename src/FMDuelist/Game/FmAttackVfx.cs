using FMDuelist.Data;
using Godot;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace FMDuelist.Game;

public enum FmAttackStyle { Slash, Claw, Bite, Sting, Fireball, Breath, Lightning, Water, Magic, Holy, Dark, Laser, Rock, Vines, Wind }

/// <summary>
/// Per-monster attacks. Each monster's attack is built from its own card data: the style comes from its type, name
/// and card text (a "Flame Swordsman" swings a flaming sword, a "Thunder Dragon" breathes lightning), the colors from
/// its card art and attribute, and the size and number of hits from its ATK and level.
/// </summary>
public static class FmAttackVfx
{
    // ---------------- what the monster does ----------------
    static readonly (string[] words, FmAttackStyle style)[] Keywords =
    {
        (new[] { "thunder", "lightning", "electric", "bolt", "volt", "spark" }, FmAttackStyle.Lightning),
        (new[] { "flame", "fire", "burn", "blaze", "inferno", "magma", "lava", "hinotama", "sun" }, FmAttackStyle.Fireball),
        (new[] { "laser", "cannon", "gun", "ray", "beam", "missile", "robot", "cyber" }, FmAttackStyle.Laser),
        (new[] { "sword", "blade", "knight", "samurai", "swordsman", "axe", "saber", "ninja", "warrior" }, FmAttackStyle.Slash),
        (new[] { "claw", "fang", "wolf", "tiger", "lion", "cat", "panther" }, FmAttackStyle.Claw),
        (new[] { "mouth", "jaw", "shark", "eater", "bite", "devour" }, FmAttackStyle.Bite),
        (new[] { "magic", "magician", "mage", "witch", "wizard", "sorcer", "spell" }, FmAttackStyle.Magic),
        (new[] { "angel", "goddess", "holy", "light", "fairy", "elf", "harp" }, FmAttackStyle.Holy),
        (new[] { "ghost", "skull", "dark", "shadow", "curse", "soul", "grave", "demon", "fiend" }, FmAttackStyle.Dark),
        (new[] { "water", "sea", "ocean", "aqua", "wave", "tide", "turtle", "fish" }, FmAttackStyle.Water),
        (new[] { "wind", "storm", "hurricane", "tornado", "gust", "feather" }, FmAttackStyle.Wind),
        (new[] { "rock", "stone", "boulder", "golem", "giant" }, FmAttackStyle.Rock),
        (new[] { "needle", "sting", "poison", "bee", "scorpion" }, FmAttackStyle.Sting),
        (new[] { "vine", "plant", "flower", "tree", "thorn", "rose" }, FmAttackStyle.Vines),
    };

    static FmAttackStyle ByType(FmType t) => t switch
    {
        FmType.Dragon => FmAttackStyle.Breath,
        FmType.Spellcaster => FmAttackStyle.Magic,
        FmType.Zombie or FmType.Fiend => FmAttackStyle.Dark,
        FmType.Warrior or FmType.BeastWarrior => FmAttackStyle.Slash,
        FmType.Beast or FmType.Dinosaur => FmAttackStyle.Claw,
        FmType.WingedBeast => FmAttackStyle.Wind,
        FmType.Fairy => FmAttackStyle.Holy,
        FmType.Insect => FmAttackStyle.Sting,
        FmType.Reptile or FmType.Fish => FmAttackStyle.Bite,
        FmType.SeaSerpent or FmType.Aqua => FmAttackStyle.Water,
        FmType.Machine => FmAttackStyle.Laser,
        FmType.Thunder => FmAttackStyle.Lightning,
        FmType.Pyro => FmAttackStyle.Fireball,
        FmType.Rock => FmAttackStyle.Rock,
        FmType.Plant => FmAttackStyle.Vines,
        _ => FmAttackStyle.Slash,
    };

    static readonly Dictionary<int, (FmAttackStyle style, Color main, Color accent)> _cache = new();

    public static (FmAttackStyle style, Color main, Color accent) For(int id)
    {
        if (_cache.TryGetValue(id, out var v)) return v;
        var c = FmData.Get(id);
        var style = ByType(c.Type);
        // the name says most (a Flame Swordsman burns, a Thunder Dragon zaps); then the card text
        string name = c.Name.ToLowerInvariant(), desc = c.Desc.ToLowerInvariant();
        var hit = Keywords.FirstOrDefault(k => k.words.Any(name.Contains));
        if (hit.words == null) hit = Keywords.FirstOrDefault(k => k.words.Any(w => desc.Contains(w)));
        if (hit.words != null)
        {
            // dragons keep their breath, now in the element they're named for
            style = c.Type == FmType.Dragon && hit.style is FmAttackStyle.Fireball or FmAttackStyle.Lightning or FmAttackStyle.Water or FmAttackStyle.Dark or FmAttackStyle.Holy
                ? FmAttackStyle.Breath : hit.style;
            if (c.Type == FmType.Dragon && hit.style == FmAttackStyle.Lightning) style = FmAttackStyle.Lightning;
        }
        var element = ElementColor(c, hit.words != null ? hit.style : style);
        var art = ArtColor(id) ?? element;
        var main = element.Lerp(art, 0.45f);
        var accent = art.Lightened(0.35f);
        return _cache[id] = (style, main, accent);
    }

    static Color ElementColor(FmCardInfo c, FmAttackStyle s) => s switch
    {
        FmAttackStyle.Fireball => new Color(1f, 0.45f, 0.1f),
        FmAttackStyle.Lightning => new Color(1f, 0.95f, 0.35f),
        FmAttackStyle.Water => new Color(0.25f, 0.6f, 1f),
        FmAttackStyle.Dark => new Color(0.55f, 0.2f, 0.85f),
        FmAttackStyle.Holy => new Color(1f, 0.95f, 0.7f),
        FmAttackStyle.Magic => new Color(0.7f, 0.4f, 1f),
        FmAttackStyle.Laser => new Color(1f, 0.25f, 0.25f),
        FmAttackStyle.Vines => new Color(0.35f, 0.85f, 0.3f),
        FmAttackStyle.Rock => new Color(0.65f, 0.55f, 0.4f),
        FmAttackStyle.Wind => new Color(0.75f, 1f, 0.9f),
        FmAttackStyle.Sting => new Color(0.75f, 1f, 0.3f),
        _ => c.Attribute switch // by the card's attribute: Light, Dark, Earth, Water, Fire, Wind
        {
            0 => new Color(1f, 0.95f, 0.75f), 1 => new Color(0.6f, 0.3f, 0.9f), 2 => new Color(0.85f, 0.7f, 0.45f),
            3 => new Color(0.35f, 0.65f, 1f), 4 => new Color(1f, 0.5f, 0.2f), 5 => new Color(0.7f, 1f, 0.8f), _ => new Color(0.8f, 0.9f, 1f),
        },
    };

    /// <summary>The most vivid color in the monster's card art.</summary>
    static Color? ArtColor(int id)
    {
        var img = FmData.Art(id)?.GetImage();
        if (img == null) return null;
        float r = 0, g = 0, b = 0, w = 0;
        for (int y = 4; y < img.GetHeight() - 4; y += 3)
            for (int x = 4; x < img.GetWidth() - 4; x += 3)
            {
                var p = img.GetPixel(x, y);
                float sat = p.S * p.V;
                if (sat < 0.35f) continue;
                float k = sat * sat;
                r += p.R * k; g += p.G * k; b += p.B * k; w += k;
            }
        if (w <= 0) return null;
        var col = new Color(r / w, g / w, b / w);
        return Color.FromHsv(col.H, Mathf.Max(col.S, 0.55f), Mathf.Max(col.V, 0.85f));
    }

    static bool Melee(FmAttackStyle s) => s is FmAttackStyle.Slash or FmAttackStyle.Claw or FmAttackStyle.Bite;

    /// <summary>Does the card itself fly into the enemy for this monster's attack?</summary>
    public static bool FliesIn(int id) => Melee(For(id).style);

    // ---------------- playing it ----------------
    /// <summary>Plays the attack from the monster's card to the target; returns when it connects.</summary>
    /// <param name="host">Where to draw it (e.g. the Library's small stage); null = over the battle.</param>
    public static async Task Play(int id, Vector2 from, Vector2 to, Node? host = null)
    {
        if ((host == null && NCombatRoom.Instance is null) || Engine.GetMainLoop() is not SceneTree tree) return;
        var c = FmData.Get(id);
        var (style, main, accent) = For(id);
        float size = Mathf.Clamp(0.8f + c.Atk / 3000f, 0.8f, 1.9f);
        int hits = 1 + Math.Clamp(c.Level, 1, 12) / 4;
        Node layer;
        if (host != null) { layer = new Node2D(); host.AddChild(layer); }
        else { layer = new CanvasLayer { Layer = 60 }; tree.Root.AddChild(layer); }
        FmAttackSfx.Play(style, size);
        try
        {
            switch (style)
            {
                case FmAttackStyle.Slash: for (int i = 0; i < hits; i++) { Slash(layer, to, main, accent, size, -0.6f + i * 1.1f); await Wait(tree, 0.09); } break;
                case FmAttackStyle.Claw: for (int i = 0; i < Math.Max(2, hits); i++) { Claw(layer, to, main, size, i % 2 == 0 ? 1 : -1); await Wait(tree, 0.1); } break;
                case FmAttackStyle.Bite: await Bite(layer, tree, to, main, size); break;
                case FmAttackStyle.Sting: for (int i = 0; i < 2 + hits; i++) { _ = Projectile(layer, tree, from, to + Jitter(30), accent, 6 * size, 0.18, trail: false, needle: true); await Wait(tree, 0.06); } await Wait(tree, 0.15); break;
                case FmAttackStyle.Fireball: await Projectile(layer, tree, from, to, main, 22 * size, 0.32, trail: true); break;
                case FmAttackStyle.Breath: await Breath(layer, tree, from, to, main, accent, size); break;
                case FmAttackStyle.Lightning: await Lightning(layer, tree, from, to, main, accent, hits); break;
                case FmAttackStyle.Water: await Projectile(layer, tree, from, to, main, 18 * size, 0.3, trail: true); Splash(layer, to, main, size); break;
                case FmAttackStyle.Magic: await Projectile(layer, tree, from, to, main, 16 * size, 0.34, trail: true, sparkle: accent); Star(layer, to, accent, size); break;
                case FmAttackStyle.Holy: await Pillar(layer, tree, to, main, size); break;
                case FmAttackStyle.Dark: await Miasma(layer, tree, to, main, size); break;
                case FmAttackStyle.Laser: await Beam(layer, tree, from, to, main, accent, size); break;
                case FmAttackStyle.Rock: await Rocks(layer, tree, to, main, size, hits); break;
                case FmAttackStyle.Vines: await Vines(layer, tree, from, to, main, size); break;
                case FmAttackStyle.Wind: for (int i = 0; i < 2 + hits / 2; i++) { _ = Gust(layer, tree, from, to, main, size); await Wait(tree, 0.08); } await Wait(tree, 0.2); break;
            }
            Burst(layer, to, main, accent, size);
        }
        catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] attack vfx: " + ex.Message); }
        var cleanup = tree.CreateTimer(1.6);
        cleanup.Connect(SceneTreeTimer.SignalName.Timeout, Callable.From(layer.QueueFree));
    }

    // ---------------- building blocks ----------------
    static readonly Random Rng = new();
    static Vector2 Jitter(float r) => new((float)(Rng.NextDouble() * 2 - 1) * r, (float)(Rng.NextDouble() * 2 - 1) * r);
    static CanvasItemMaterial Add() => new() { BlendMode = CanvasItemMaterial.BlendModeEnum.Add };
    static async Task Wait(SceneTree tree, double s) { var t = tree.CreateTimer(s); await t.ToSignal(t, SceneTreeTimer.SignalName.Timeout); }
    static async Task Run(Tween t) => await t.ToSignal(t, Tween.SignalName.Finished);

    static Vector2[] Circle(float r, int n = 20)
    {
        var p = new Vector2[n];
        for (int i = 0; i < n; i++) { float a = Mathf.Tau * i / n; p[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r; }
        return p;
    }

    static void Burst(Node layer, Vector2 at, Color main, Color accent, float size)
    {
        var sparks = new CpuParticles2D
        {
            Position = at, Amount = (int)(40 * size), Lifetime = 0.6, OneShot = true, Explosiveness = 1f, Material = Add(),
            Direction = Vector2.Up, Spread = 180, InitialVelocityMin = 150, InitialVelocityMax = 420 * size, Gravity = new Vector2(0, 400),
            ScaleAmountMin = 3, ScaleAmountMax = 7, Color = accent,
        };
        layer.AddChild(sparks);
        sparks.Emitting = true;
        var flash = new Polygon2D { Polygon = Circle(40 * size), Color = new Color(main, 0.9f), Position = at, Material = Add() };
        layer.AddChild(flash);
        var t = flash.CreateTween().SetParallel();
        t.TweenProperty(flash, "scale", Vector2.One * 2.6f, 0.25).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
        t.TweenProperty(flash, "modulate:a", 0f, 0.3);
    }

    static void Slash(Node layer, Vector2 at, Color main, Color accent, float size, float angle)
    {
        var root = new Node2D { Position = at, Rotation = angle, Scale = Vector2.One * 0.35f * size };
        layer.AddChild(root);
        root.AddChild(new Polygon2D { Polygon = Crescent(260, 34), Color = new Color(main, 0.6f), Material = Add() });
        root.AddChild(new Polygon2D { Polygon = Crescent(240, 14), Color = new Color(accent.Lightened(0.5f), 0.95f), Material = Add() });
        var t = root.CreateTween();
        t.TweenProperty(root, "scale", Vector2.One * 1.1f * size, 0.08).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
        t.TweenProperty(root, "modulate:a", 0f, 0.22);
        t.Parallel().TweenProperty(root, "scale", new Vector2(1.35f, 0.6f) * size, 0.22);
    }

    static Vector2[] Crescent(float radius, float thickness)
    {
        const int n = 20;
        var pts = new List<Vector2>();
        for (int i = 0; i <= n; i++) { float a = Mathf.Lerp(-1.1f, 1.1f, i / (float)n); pts.Add(new Vector2(Mathf.Sin(a) * radius, -Mathf.Cos(a) * radius * 0.35f)); }
        for (int i = n; i >= 0; i--) { float a = Mathf.Lerp(-1.1f, 1.1f, i / (float)n); pts.Add(new Vector2(Mathf.Sin(a) * radius, -Mathf.Cos(a) * radius * 0.35f + thickness * Mathf.Cos(a * 1.4f))); }
        return pts.ToArray();
    }

    static void Claw(Node layer, Vector2 at, Color main, float size, int dir)
    {
        var root = new Node2D { Position = at, Rotation = dir * 0.5f, Modulate = new Color(1, 1, 1, 0) };
        layer.AddChild(root);
        for (int i = -1; i <= 1; i++)
        {
            var mark = new Polygon2D
            {
                Polygon = new[] { new Vector2(i * 28 - 4, -110), new Vector2(i * 28 + 4, -110), new Vector2(i * 28 + 7, 0), new Vector2(i * 28, 110), new Vector2(i * 28 - 7, 0) },
                Color = main.Lightened(0.3f), Material = Add(), Scale = Vector2.One * size,
            };
            root.AddChild(mark);
        }
        var t = root.CreateTween();
        t.TweenProperty(root, "modulate:a", 1f, 0.05);
        t.Parallel().TweenProperty(root, "position", at + new Vector2(dir * 25, 25), 0.12);
        t.TweenProperty(root, "modulate:a", 0f, 0.25);
    }

    static async Task Bite(Node layer, SceneTree tree, Vector2 at, Color main, float size)
    {
        Node2D Jaw(int sign)
        {
            var j = new Node2D { Position = at + new Vector2(0, sign * 140 * size) };
            var pts = new List<Vector2>();
            for (int i = 0; i <= 8; i++) { pts.Add(new Vector2(-110 + i * 27.5f, 0)); if (i < 8) pts.Add(new Vector2(-96 + i * 27.5f, -sign * 34)); }
            pts.Add(new Vector2(110, sign * 20)); pts.Add(new Vector2(-110, sign * 20));
            j.AddChild(new Polygon2D { Polygon = pts.ToArray(), Color = main.Lightened(0.4f), Material = Add(), Scale = Vector2.One * size });
            layer.AddChild(j);
            return j;
        }
        var top = Jaw(-1); var bottom = Jaw(1);
        var t = top.CreateTween().SetParallel();
        t.TweenProperty(top, "position", at + new Vector2(0, -12), 0.14).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);
        t.TweenProperty(bottom, "position", at + new Vector2(0, 12), 0.14).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);
        await Run(t);
        var f = top.CreateTween().SetParallel();
        f.TweenProperty(top, "modulate:a", 0f, 0.25);
        f.TweenProperty(bottom, "modulate:a", 0f, 0.25);
    }

    static async Task Projectile(Node layer, SceneTree tree, Vector2 from, Vector2 to, Color color, float radius, double time, bool trail, Color? sparkle = null, bool needle = false)
    {
        var ball = new Node2D { Position = from };
        layer.AddChild(ball);
        if (needle)
        {
            float a = (to - from).Angle();
            ball.AddChild(new Polygon2D { Polygon = new[] { new Vector2(22, 0), new Vector2(-14, -radius / 2), new Vector2(-14, radius / 2) }, Color = color, Rotation = a, Material = Add() });
        }
        else
        {
            ball.AddChild(new Polygon2D { Polygon = Circle(radius * 1.7f), Color = new Color(color, 0.45f), Material = Add() });
            ball.AddChild(new Polygon2D { Polygon = Circle(radius), Color = color.Lightened(0.5f), Material = Add() });
        }
        if (trail || sparkle != null)
        {
            var tr = new CpuParticles2D
            {
                Amount = 50, Lifetime = 0.35, LocalCoords = false, Material = Add(), Spread = 180, InitialVelocityMin = 10, InitialVelocityMax = 60,
                ScaleAmountMin = radius * 0.25f, ScaleAmountMax = radius * 0.5f, Color = sparkle ?? color, Gravity = Vector2.Zero,
            };
            ball.AddChild(tr);
            tr.Emitting = true;
        }
        var t = ball.CreateTween();
        t.TweenProperty(ball, "position", to, time).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        await Run(t);
        ball.GetChildren().OfType<CpuParticles2D>().ToList().ForEach(p => p.Emitting = false);
        foreach (var poly in ball.GetChildren().OfType<Polygon2D>()) poly.Visible = false;
    }

    static async Task Breath(Node layer, SceneTree tree, Vector2 from, Vector2 to, Color main, Color accent, float size)
    {
        var dir = (to - from);
        float dist = dir.Length();
        var stream = new CpuParticles2D
        {
            Position = from, Amount = (int)(140 * size), Lifetime = dist / 900.0, LocalCoords = false, Material = Add(),
            Direction = dir.Normalized(), Spread = 9, InitialVelocityMin = 850, InitialVelocityMax = 950, Gravity = Vector2.Zero,
            ScaleAmountMin = 6 * size, ScaleAmountMax = 16 * size, Color = main,
        };
        var ramp = new Gradient(); ramp.SetColor(0, accent.Lightened(0.6f)); ramp.SetColor(1, new Color(main, 0f));
        stream.ColorRamp = ramp;
        layer.AddChild(stream);
        stream.Emitting = true;
        await Wait(tree, dist / 900.0 + 0.1);
        await Wait(tree, 0.35);
        stream.Emitting = false;
    }

    static Line2D Bolt(Vector2 a, Vector2 b, Color c, float width)
    {
        var l = new Line2D { Width = width, DefaultColor = c, Material = Add() };
        int n = 9;
        for (int i = 0; i <= n; i++)
        {
            var p = a.Lerp(b, i / (float)n);
            if (i > 0 && i < n) p += Jitter(26);
            l.AddPoint(p);
        }
        return l;
    }

    static async Task Lightning(Node layer, SceneTree tree, Vector2 from, Vector2 to, Color main, Color accent, int hits)
    {
        for (int k = 0; k < 1 + hits; k++)
        {
            var src = k == 0 ? from : to + new Vector2((float)(Rng.NextDouble() - 0.5) * 200, -700);
            var outer = Bolt(src, to, new Color(main, 0.6f), 16);
            var inner = Bolt(src, to, accent.Lightened(0.7f), 5);
            layer.AddChild(outer); layer.AddChild(inner);
            var t = outer.CreateTween().SetParallel();
            t.TweenProperty(outer, "modulate:a", 0f, 0.25).SetDelay(0.06);
            t.TweenProperty(inner, "modulate:a", 0f, 0.25).SetDelay(0.06);
            await Wait(tree, 0.07);
        }
    }

    static void Splash(Node layer, Vector2 at, Color main, float size)
    {
        var drops = new CpuParticles2D
        {
            Position = at, Amount = (int)(50 * size), Lifetime = 0.8, OneShot = true, Explosiveness = 1f, Material = Add(),
            Direction = Vector2.Up, Spread = 70, InitialVelocityMin = 250, InitialVelocityMax = 520, Gravity = new Vector2(0, 1100),
            ScaleAmountMin = 4, ScaleAmountMax = 9, Color = main.Lightened(0.3f),
        };
        layer.AddChild(drops);
        drops.Emitting = true;
        var ring = new Line2D { Width = 8, DefaultColor = new Color(main, 0.8f), Closed = true, Position = at + new Vector2(0, 60), Material = Add() };
        foreach (var p in Circle(50, 32)) ring.AddPoint(new Vector2(p.X, p.Y * 0.3f));
        layer.AddChild(ring);
        var t = ring.CreateTween().SetParallel();
        t.TweenProperty(ring, "scale", Vector2.One * 3f * size, 0.45);
        t.TweenProperty(ring, "modulate:a", 0f, 0.45);
    }

    static void Star(Node layer, Vector2 at, Color c, float size)
    {
        var pts = new List<Vector2>();
        for (int i = 0; i < 10; i++) { float a = Mathf.Tau * i / 10 - Mathf.Pi / 2; float r = i % 2 == 0 ? 90 : 34; pts.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r * size); }
        var star = new Polygon2D { Polygon = pts.ToArray(), Color = c.Lightened(0.4f), Position = at, Material = Add(), Scale = Vector2.One * 0.3f };
        layer.AddChild(star);
        var t = star.CreateTween().SetParallel();
        t.TweenProperty(star, "scale", Vector2.One * 1.4f, 0.3).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
        t.TweenProperty(star, "rotation", 1.2f, 0.45);
        t.TweenProperty(star, "modulate:a", 0f, 0.45).SetDelay(0.1);
    }

    static async Task Pillar(Node layer, SceneTree tree, Vector2 at, Color main, float size)
    {
        var grad = new Gradient(); grad.SetColor(0, new Color(main, 0f)); grad.SetColor(1, new Color(main.Lightened(0.5f), 0.95f));
        var tex = new GradientTexture2D { Gradient = grad, Width = 8, Height = 256, FillFrom = Vector2.Zero, FillTo = new Vector2(0, 1) };
        var beam = new Sprite2D { Texture = tex, Material = Add(), Position = at + new Vector2(0, -300), Scale = new Vector2(2, 2.8f) };
        layer.AddChild(beam);
        var t = beam.CreateTween();
        t.TweenProperty(beam, "scale:x", 22f * size, 0.15).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
        await Run(t);
        var f = beam.CreateTween();
        f.TweenProperty(beam, "scale:x", 1f, 0.4);
        f.Parallel().TweenProperty(beam, "modulate:a", 0f, 0.4);
        Star(layer, at, main, size);
    }

    static async Task Miasma(Node layer, SceneTree tree, Vector2 at, Color main, float size)
    {
        var swirl = new CpuParticles2D
        {
            Position = at, Amount = (int)(80 * size), Lifetime = 0.7, OneShot = true, Explosiveness = 0.3f,
            EmissionShape = CpuParticles2D.EmissionShapeEnum.Sphere, EmissionSphereRadius = 150 * size,
            Gravity = Vector2.Zero, RadialAccelMin = -500, RadialAccelMax = -350, TangentialAccelMin = 300, TangentialAccelMax = 500,
            ScaleAmountMin = 6, ScaleAmountMax = 14, Color = new Color(main.Darkened(0.2f), 0.85f),
        };
        layer.AddChild(swirl);
        swirl.Emitting = true;
        var orb = new Polygon2D { Polygon = Circle(110 * size), Color = new Color(0.08f, 0.02f, 0.12f, 0.0f), Position = at };
        layer.AddChild(orb);
        var t = orb.CreateTween();
        t.TweenProperty(orb, "color:a", 0.75f, 0.3);
        t.Parallel().TweenProperty(orb, "scale", Vector2.One * 0.2f, 0.45).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        await Run(t);
        orb.QueueFree();
    }

    static async Task Beam(Node layer, SceneTree tree, Vector2 from, Vector2 to, Color main, Color accent, float size)
    {
        var outer = new Line2D { Points = new[] { from, to }, Width = 2, DefaultColor = new Color(main, 0.7f), Material = Add() };
        var core = new Line2D { Points = new[] { from, to }, Width = 1, DefaultColor = accent.Lightened(0.7f), Material = Add() };
        layer.AddChild(outer); layer.AddChild(core);
        var t = outer.CreateTween().SetParallel();
        t.TweenProperty(outer, "width", 34f * size, 0.1);
        t.TweenProperty(core, "width", 10f * size, 0.1);
        await Run(t);
        await Wait(tree, 0.12);
        var f = outer.CreateTween().SetParallel();
        f.TweenProperty(outer, "width", 0f, 0.25);
        f.TweenProperty(core, "width", 0f, 0.25);
    }

    static async Task Rocks(Node layer, SceneTree tree, Vector2 at, Color main, float size, int hits)
    {
        for (int i = 0; i < 1 + hits; i++)
        {
            var pts = new List<Vector2>();
            for (int k = 0; k < 8; k++) { float a = Mathf.Tau * k / 8; pts.Add(new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (float)(30 + Rng.NextDouble() * 18) * size); }
            var rock = new Polygon2D { Polygon = pts.ToArray(), Color = main.Darkened(0.25f), Position = at + new Vector2((float)(Rng.NextDouble() - 0.5) * 160, -650) };
            layer.AddChild(rock);
            var t = rock.CreateTween().SetParallel();
            t.TweenProperty(rock, "position", at + Jitter(40), 0.28).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            t.TweenProperty(rock, "rotation", 3f, 0.28);
            await Run(t);
            var debris = new CpuParticles2D
            {
                Position = rock.Position, Amount = 24, Lifetime = 0.6, OneShot = true, Explosiveness = 1f, Direction = Vector2.Up, Spread = 80,
                InitialVelocityMin = 150, InitialVelocityMax = 380, Gravity = new Vector2(0, 1200), ScaleAmountMin = 4, ScaleAmountMax = 9, Color = main,
            };
            layer.AddChild(debris);
            debris.Emitting = true;
            rock.QueueFree();
        }
    }

    static async Task Vines(Node layer, SceneTree tree, Vector2 from, Vector2 to, Color main, float size)
    {
        var lines = new List<(Line2D line, Vector2 ctrl)>();
        for (int k = -1; k <= 1; k += 2)
        {
            var l = new Line2D { Width = 10 * size, DefaultColor = main, JointMode = Line2D.LineJointMode.Round, EndCapMode = Line2D.LineCapMode.Round };
            layer.AddChild(l);
            lines.Add((l, (from + to) / 2 + new Vector2(0, k * 180)));
        }
        const int steps = 14;
        for (int i = 1; i <= steps; i++)
        {
            float u = i / (float)steps;
            foreach (var (l, ctrl) in lines) l.AddPoint(from.Lerp(ctrl, u).Lerp(ctrl.Lerp(to, u), u));
            await Wait(tree, 0.02);
        }
        foreach (var (l, _) in lines) { var t = l.CreateTween(); t.TweenProperty(l, "modulate:a", 0f, 0.35).SetDelay(0.1); }
    }

    static async Task Gust(Node layer, SceneTree tree, Vector2 from, Vector2 to, Color main, float size)
    {
        var arc = new Line2D { Width = 8 * size, DefaultColor = new Color(main, 0.8f), Material = Add() };
        var ctrl = (from + to) / 2 + new Vector2(0, (float)(Rng.NextDouble() - 0.5) * 300);
        for (int i = 0; i <= 12; i++) { float u = i / 12f; arc.AddPoint(from.Lerp(ctrl, u).Lerp(ctrl.Lerp(to, u), u) - (to - from) * 0.0f); }
        arc.Modulate = new Color(1, 1, 1, 0);
        layer.AddChild(arc);
        var t = arc.CreateTween();
        t.TweenProperty(arc, "modulate:a", 1f, 0.08);
        t.TweenProperty(arc, "modulate:a", 0f, 0.25);
        await Run(t);
    }
}
