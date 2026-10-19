using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace FMDuelist.Game;

/// <summary>
/// Forbidden Memories' Life Point box in every fight, top right: COM's panel (navy) over YOU's panel (red), each with
/// its tab, "LP" and the Life Points, and the cards left in the deck. Against a duelist COM is the duelist; against
/// other enemies it's their Life Points all together. When Life Points change the number rolls down (or up) to the
/// new value with a ticking sound, as FM's counter does. Drawn with a clean bold font so it stays sharp.
/// </summary>
public static class FmLifePoints
{
    static CanvasLayer? _layer;
    static Control? _box;
    static Label? _comLp, _youLp, _comDeck, _youDeck;
    static float _comShown = -1, _youShown = -1;
    static double _lastTick;
    static AudioStreamWav? _tick;
    static AudioStreamPlayer? _player;
    static bool _subscribed;
    static bool _youZero, _comZero;

    /// <summary>The duel is over: the loser's counter goes to 0. Returns true when it was showing.</summary>
    public static bool Finish(bool won)
    {
        if (won) _comZero = true; else _youZero = true;
        return _layer != null && GodotObject.IsInstanceValid(_layer);
    }

    const float S = 1.0f;          // overall size
    const float RollTime = 0.9f;   // a change of any size rolls over about this long

    static readonly Color Navy = new(0.06f, 0.08f, 0.5f), Red = new(0.8f, 0.02f, 0.02f), Rim = new(0.95f, 0.95f, 0.97f);
    static readonly Color Gold = new(1f, 0.72f, 0.1f), Lavender = new(0.7f, 0.7f, 1f), Pink = new(1f, 0.88f, 0.88f);
    static readonly SystemFont Bold = new() { FontNames = new[] { "Arial Black", "Segoe UI Black", "Arial" }, FontWeight = 900, Antialiasing = TextServer.FontAntialiasing.Gray };

    public static void Clear()
    {
        if (_subscribed && Engine.GetMainLoop() is SceneTree t) t.ProcessFrame -= Tick;
        _subscribed = false;
        if (_layer != null && GodotObject.IsInstanceValid(_layer)) _layer.QueueFree();
        _layer = null; _box = null;
        _comShown = _youShown = -1;
        _youZero = _comZero = false;
    }

    static Player? Me => RunManager.Instance.DebugOnlyGetState()?.Players.FirstOrDefault(FmRules.IsFmPlayer);

    /// <summary>Puts the box up for a fight (once).</summary>
    public static void Show()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || Me == null) return;
        if (_layer != null && GodotObject.IsInstanceValid(_layer)) return;
        _layer = new CanvasLayer { Layer = 55 };
        tree.Root.AddChild(_layer);
        var view = _layer.GetViewport().GetVisibleRect().Size;
        float w = 290 * S, h = 200 * S;
        bool topBar = !FmPractice.Active;
        _box = new Control { Size = new Vector2(w, h), Position = new Vector2(view.X - w - 18, topBar ? 78 : 18), MouseFilter = Control.MouseFilterEnum.Ignore };
        _layer.AddChild(_box);
        Panel(new Rect2(36 * S, 0, 254 * S, 96 * S), Navy);
        Panel(new Rect2(36 * S, 104 * S, 254 * S, 96 * S), Red);
        Panel(new Rect2(0, 44 * S, 86 * S, 38 * S), Navy);
        Panel(new Rect2(0, 118 * S, 86 * S, 38 * S), Red);
        Text("COM", new Vector2(8 * S, 46 * S), 26, Lavender);
        Text("YOU", new Vector2(8 * S, 120 * S), 26, Pink);
        Text("LP", new Vector2(48 * S, 4 * S), 30, Gold);
        Text("LP", new Vector2(48 * S, 154 * S), 30, Gold);
        _comLp = Text("", new Vector2(100 * S, -2 * S), 44, new Color(0.8f, 0.82f, 0.9f), 180);
        _youLp = Text("", new Vector2(100 * S, 148 * S), 44, Colors.White, 180);
        _comDeck = Text("", new Vector2(150 * S, 50 * S), 30, new Color(0.8f, 0.82f, 0.9f), 130);
        _youDeck = Text("", new Vector2(150 * S, 106 * S), 30, Colors.White, 130);
        _player = new AudioStreamPlayer { Stream = Tick0(), VolumeDb = -14 };
        _layer.AddChild(_player);
        tree.ProcessFrame += Tick;
        _subscribed = true;
        Tick();
    }

    static void Panel(Rect2 r, Color fill)
    {
        var p = new Panel { Position = r.Position, Size = r.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = fill, BorderColor = Rim, BorderWidthLeft = 4, BorderWidthRight = 4, BorderWidthTop = 4, BorderWidthBottom = 4,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
            ShadowColor = new Color(0, 0, 0, 0.5f), ShadowSize = 4,
        });
        _box!.AddChild(p);
    }

    /// <summary>A crisp label; with a width it's right-aligned inside it (for the numbers).</summary>
    static Label Text(string s, Vector2 at, int size, Color ink, float width = 0)
    {
        var l = new Label { Text = s, Position = at, MouseFilter = Control.MouseFilterEnum.Ignore };
        l.AddThemeFontOverride("font", Bold);
        l.AddThemeFontSizeOverride("font_size", (int)(size * S));
        l.AddThemeColorOverride("font_color", ink);
        l.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        l.AddThemeConstantOverride("outline_size", (int)(6 * S));
        l.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
        if (width > 0) { l.HorizontalAlignment = HorizontalAlignment.Right; l.Size = new Vector2(width * S, size * 1.3f * S); }
        _box!.AddChild(l);
        return l;
    }

    /// <summary>Every frame: roll the shown Life Points towards the real ones, and keep the deck counts current.</summary>
    static void Tick()
    {
        if (_layer == null || !GodotObject.IsInstanceValid(_layer) || _box == null) { Clear(); return; }
        var me = Me;
        var room = NCombatRoom.Instance;
        var combat = me?.Creature.CombatState;
        if (me == null || room == null || combat == null) { Clear(); return; }
        _box.Visible = true;
        // COM: the duelist, or the other enemies' Life Points together
        int comLp = FmRival.Active && FmRival.Priest is { } him ? him.CurrentHp
            : combat.Enemies.Where(e => e.IsAlive && !FmRival.IsZoneCreature(e)).Sum(e => e.CurrentHp);
        float dt = (float)_layer.GetProcessDeltaTime();
        bool rolling = false;
        _youShown = Roll(_youShown, _youZero ? 0 : me.Creature.CurrentHp, dt, ref rolling, _youLp!);
        _comShown = Roll(_comShown, _comZero ? 0 : comLp, dt, ref rolling, _comLp!);
        if (rolling && Time.GetTicksMsec() / 1000.0 - _lastTick > 0.045) { _lastTick = Time.GetTicksMsec() / 1000.0; _player?.Play(); }
        _youDeck!.Text = PileType.Draw.GetPile(me).Cards.Count.ToString();
        _comDeck!.Text = FmRival.Active ? FmRival.DeckCount.ToString() : "";
    }

    static float Roll(float shown, int target, float dt, ref bool rolling, Label label)
    {
        if (shown < 0) { SetNumber(label, target); return target; }
        if (Mathf.IsEqualApprox(shown, target)) return shown;
        float speed = Math.Max(1000f, Math.Abs(target - shown) / RollTime);
        float next = shown < target ? Math.Min(target, shown + speed * dt) : Math.Max(target, shown - speed * dt);
        if ((int)next != (int)shown) { SetNumber(label, (int)Math.Round(next)); rolling = true; }
        return next;
    }

    /// <summary>The number, shrunk to fit when it has more than 4 digits.</summary>
    static void SetNumber(Label label, int value)
    {
        string t = Math.Max(0, value).ToString();
        label.Text = t;
        label.AddThemeFontSizeOverride("font_size", (int)(44 * S * Math.Min(1f, 4.3f / Math.Max(4, t.Length))));
    }

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
