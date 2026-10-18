using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace FMDuelist.Game;

/// <summary>
/// Forbidden Memories' Life Point box for every duel, top right: COM's panel (navy) over YOU's panel (red), each with
/// its tab, "LP" and the Life Points, and the cards left in the deck. When Life Points change the number doesn't
/// jump: it rolls down (or up) to the new value with a ticking sound, as FM's counter does.
/// </summary>
public static class FmLifePoints
{
    static CanvasLayer? _layer;
    static Control? _box, _comLp, _youLp, _comDeck, _youDeck;
    static float _comShown = -1, _youShown = -1;
    static int _comDeckShown = -1, _youDeckShown = -1;
    static double _lastTick;
    static AudioStreamWav? _tick;
    static AudioStreamPlayer? _player;
    static bool _subscribed;

    const float Px = 2.6f;           // screen pixels per FM pixel for the box
    const float RollTime = 0.9f;     // a change of any size rolls over about this long

    static readonly Color Navy = new(0.06f, 0.08f, 0.5f), Red = new(0.8f, 0.02f, 0.02f), Rim = new(0.95f, 0.95f, 0.97f);
    static readonly Color Gold = new(0.85f, 0.6f, 0.05f), Yellow = new(1f, 0.82f, 0.1f), Lavender = new(0.55f, 0.55f, 0.95f), Pink = new(1f, 0.85f, 0.85f);

    public static void Clear()
    {
        if (_subscribed && Engine.GetMainLoop() is SceneTree t) t.ProcessFrame -= Tick;
        _subscribed = false;
        if (_layer != null && GodotObject.IsInstanceValid(_layer)) _layer.QueueFree();
        _layer = null; _box = null;
        _comShown = _youShown = -1; _comDeckShown = _youDeckShown = -1;
    }

    /// <summary>Puts the box up for a duel (once).</summary>
    public static void Show()
    {
        if (!FmRival.Active || Engine.GetMainLoop() is not SceneTree tree) return;
        if (_layer != null && GodotObject.IsInstanceValid(_layer)) return;
        _layer = new CanvasLayer { Layer = 55 };
        tree.Root.AddChild(_layer);
        var view = _layer.GetViewport().GetVisibleRect().Size;
        float w = 112 * Px, h = 88 * Px;
        bool topBar = !FmPractice.Active;
        _box = new Control { Size = new Vector2(w, h), Position = new Vector2(view.X - w - 18, topBar ? 78 : 18), MouseFilter = Control.MouseFilterEnum.Ignore };
        _layer.AddChild(_box);
        Panel(_box, new Rect2(14 * Px, 0, 98 * Px, 42 * Px), Navy);
        Panel(_box, new Rect2(14 * Px, 46 * Px, 98 * Px, 42 * Px), Red);
        Tab(_box, new Rect2(0, 17 * Px, 34 * Px, 15 * Px), Navy, "COM", Lavender);
        Tab(_box, new Rect2(0, 50 * Px, 34 * Px, 15 * Px), Red, "YOU", Pink);
        Label(_box, "LP", new Vector2(18 * Px, 5 * Px), 1.45f, Gold);
        Label(_box, "LP", new Vector2(18 * Px, 66 * Px), 1.45f, Yellow);
        _comLp = new Control { Position = new Vector2(44 * Px, 3 * Px), MouseFilter = Control.MouseFilterEnum.Ignore };
        _youLp = new Control { Position = new Vector2(44 * Px, 64 * Px), MouseFilter = Control.MouseFilterEnum.Ignore };
        _comDeck = new Control { Position = new Vector2(84 * Px, 25 * Px), MouseFilter = Control.MouseFilterEnum.Ignore };
        _youDeck = new Control { Position = new Vector2(84 * Px, 47 * Px), MouseFilter = Control.MouseFilterEnum.Ignore };
        foreach (var c in new[] { _comLp, _youLp, _comDeck, _youDeck }) _box.AddChild(c);
        _player = new AudioStreamPlayer { Stream = Tick0(), VolumeDb = -14 };
        _layer.AddChild(_player);
        tree.ProcessFrame += Tick;
        _subscribed = true;
        Tick();
    }

    static void Panel(Control parent, Rect2 r, Color fill)
    {
        var p = new Panel { Position = r.Position, Size = r.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
        int b = (int)(1.6f * Px);
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = fill, BorderColor = Rim, BorderWidthLeft = b, BorderWidthRight = b, BorderWidthTop = b, BorderWidthBottom = b,
            CornerRadiusTopLeft = 2, CornerRadiusTopRight = 2, CornerRadiusBottomLeft = 2, CornerRadiusBottomRight = 2,
            ShadowColor = new Color(0, 0, 0, 0.5f), ShadowSize = 3,
        });
        parent.AddChild(p);
    }

    static void Tab(Control parent, Rect2 r, Color fill, string text, Color ink)
    {
        Panel(parent, r, fill);
        Label(parent, text, r.Position + new Vector2(4 * Px, 2 * Px), 1.3f, ink);
    }

    static Control Label(Control parent, string text, Vector2 at, float scale, Color ink)
    {
        var t = FmTextBox.Text(text, Px * scale, ink);
        t.Position = at;
        parent.AddChild(t);
        return t;
    }

    static void Digits(Control holder, string text, float scale, Color ink, float rightEdge)
    {
        foreach (var c in holder.GetChildren()) c.QueueFree();
        var t = FmTextBox.Text(text, Px * scale, ink);
        t.Position = new Vector2(rightEdge - FmTextBox.TextWidth(text, Px * scale), 0);
        holder.AddChild(t);
    }

    /// <summary>Every frame: roll the shown Life Points towards the real ones, and keep the deck counts current.</summary>
    static void Tick()
    {
        if (_layer == null || !GodotObject.IsInstanceValid(_layer) || _box == null) { Clear(); return; }
        if (!FmRival.Active || FmField.Duelist is not { } me || FmRival.Priest is not { } him || NCombatRoom.Instance == null) { _box.Visible = false; return; }
        _box.Visible = true;
        float dt = (float)(_layer.GetProcessDeltaTime());
        bool rolling = false;
        _youShown = Roll(_youShown, me.Creature.CurrentHp, dt, ref rolling, _youLp!, new Color(0.88f, 0.88f, 0.95f));
        _comShown = Roll(_comShown, him.CurrentHp, dt, ref rolling, _comLp!, new Color(0.62f, 0.62f, 0.68f));
        if (rolling && Time.GetTicksMsec() / 1000.0 - _lastTick > 0.045) { _lastTick = Time.GetTicksMsec() / 1000.0; _player?.Play(); }
        int youDeck = PileType.Draw.GetPile(me).Cards.Count, comDeck = FmRival.DeckCount;
        if (youDeck != _youDeckShown) { _youDeckShown = youDeck; Digits(_youDeck!, youDeck.ToString(), 1.45f, new Color(0.88f, 0.88f, 0.95f), 26 * Px); }
        if (comDeck != _comDeckShown) { _comDeckShown = comDeck; Digits(_comDeck!, comDeck.ToString(), 1.45f, new Color(0.62f, 0.62f, 0.68f), 26 * Px); }
    }

    static float _rate;

    static float Roll(float shown, int target, float dt, ref bool rolling, Control holder, Color ink)
    {
        if (shown < 0) { Digits(holder, Fmt(target), 1.9f, ink, 66 * Px); return target; }
        if (Mathf.IsEqualApprox(shown, target)) return shown;
        // fast enough that any change takes about RollTime, never slower than 1000 LP a second
        float speed = Math.Max(1000f, Math.Abs(target - shown) / RollTime);
        float next = shown < target ? Math.Min(target, shown + speed * dt) : Math.Max(target, shown - speed * dt);
        if ((int)next != (int)shown) { Digits(holder, Fmt((int)Math.Round(next)), 1.9f, ink, 66 * Px); rolling = true; }
        _rate = speed;
        return next;
    }

    static string Fmt(int lp) => Math.Max(0, lp).ToString();

    /// <summary>FM's counter tick: a short bright blip.</summary>
    static AudioStreamWav Tick0()
    {
        if (_tick != null) return _tick;
        const int rate = 22050; int n = (int)(rate * 0.025);
        var data = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / rate, env = 1 - (double)i / n;
            double v = (Math.Sin(2 * Math.PI * 1760 * t) > 0 ? 1 : -1) * 0.5 + Math.Sin(2 * Math.PI * 3520 * t) * 0.2;
            short s = (short)(v * env * 9000);
            data[i * 2] = (byte)(s & 0xFF); data[i * 2 + 1] = (byte)((s >> 8) & 0xFF);
        }
        return _tick = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Stereo = false, Data = data };
    }
}
