using FMDuelist.Data;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;

namespace FMDuelist.Game;

/// <summary>
/// Forbidden Memories' Build Deck screen: the CHEST (every card you own) and your DECK (up to 40) side by side,
/// sliding left and right from each other with the red arrows. Each list has FM's ORDER bar of sort buttons and
/// FM's rows: list number, card number, name, ATK / DEF, its picture and its two Guardian Stars. Click a Chest card to
/// put a copy in the Deck (cards already all in the Deck are greyed out), click a Deck card to take it out. Cards
/// won in the last 3 combats are marked NEW.
/// </summary>
public static class FmDeckEditor
{
    static CanvasLayer? _layer;
    static Control? _stage, _slide;
    static float _u;                       // screen pixels per pixel of FM's 1440x1080 frame
    static Player? _player;
    static List<int> _deck = new();
    static bool _showDeck;
    static int _chestTop, _deckTop, _chestOrder, _deckOrder;
    static TaskCompletionSource? _done;
    static Control? _preview;

    public static bool IsOpen => _layer != null && GodotObject.IsInstanceValid(_layer);

    static readonly Color Tan = new(0.6f, 0.5f, 0.34f), Navy = new(0.04f, 0.06f, 0.18f), Rim = new(0.32f, 0.36f, 0.6f), Lime = new(0.25f, 0.85f, 0.2f);
    static readonly string[] Orders = { "NO.", "TYP", "ATK", "DEF", "STR", "NEW", "MAG" };
    static readonly string[] OrderNames = { "Card number", "Type", "ATK", "DEF", "Guardian Star", "New cards first", "Magic, Equip and Trap first" };
    static readonly string[] StarSigns = { "", "♂", "♃", "♄", "♅", "♇", "♆", "☿", "☉", "☽", "♀" };
    const int Rows = 8;

    public static async Task Open(Player player)
    {
        if (IsOpen || NGame.Instance is not { } game) return;
        _player = player;
        _deck = FmDeckBuild.Deck(player);
        _showDeck = true; _chestTop = _deckTop = 0;
        _done = new TaskCompletionSource();
        try
        {
            _layer = new CanvasLayer { Layer = 108 };
            game.AddChild(_layer);
            var view = _layer.GetViewport().GetVisibleRect().Size;
            var back = new ColorRect { Color = new Color(0.08f, 0.06f, 0.04f), Size = view, MouseFilter = Control.MouseFilterEnum.Stop };
            back.GuiInput += OnInput;
            _layer.AddChild(back);
            float sw = Math.Min(view.X, view.Y * 4f / 3f), sh = sw * 3f / 4f;
            _u = sh / 1080f;
            _stage = new Control { Position = new Vector2((view.X - sw) / 2, (view.Y - sh) / 2), Size = new Vector2(sw, sh), ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
            _stage.AddChild(new ColorRect { Color = Tan, Size = _stage.Size, MouseFilter = Control.MouseFilterEnum.Ignore });
            _layer.AddChild(_stage);
            FmSfx.Play(FmSfx.Mark);
            Render();
            await _done.Task;
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] deck editor: " + ex); }
        finally
        {
            if (_layer != null && GodotObject.IsInstanceValid(_layer)) _layer.QueueFree();
            _layer = null;
            FmDeckBuild.Save(player, _deck);
            Log.Info($"[FMDuelist] deck saved: {_deck.Count} cards");
        }
    }

    public static void Close() { FmSfx.Play(FmSfx.Mark); _done?.TrySetResult(); }

    /// <summary>Esc closes; the wheel and arrow keys scroll (from the game's input hook).</summary>
    public static bool HandleInput(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true } k)
        {
            switch (k.Keycode)
            {
                case Key.Escape: Close(); return true;
                case Key.Up: Scroll(-1); return true;
                case Key.Down: Scroll(1); return true;
                case Key.Left: if (_showDeck) Switch(); return true;
                case Key.Right: if (!_showDeck) Switch(); return true;
            }
        }
        if (e is InputEventMouseButton { Pressed: true } m)
        {
            if (m.ButtonIndex == MouseButton.WheelUp) { Scroll(-1); return true; }
            if (m.ButtonIndex == MouseButton.WheelDown) { Scroll(1); return true; }
            if (m.ButtonIndex == MouseButton.Right) { Switch(); return true; }
        }
        return false;
    }

    static void OnInput(InputEvent e) { }

    // ---------------- lists ----------------
    static List<int> ChestRows()
    {
        var ids = FmDeckBuild.Chest(_player!).Distinct().ToList();
        return Sort(ids, _chestOrder);
    }

    static List<int> DeckRows() => Sort(_deck.ToList(), _deckOrder);

    static List<int> Sort(List<int> ids, int order)
    {
        FmCardInfo C(int i) => FmData.Get(i);
        IEnumerable<int> q = order switch
        {
            1 => ids.OrderBy(i => (int)C(i).Type).ThenBy(i => i),
            2 => ids.OrderByDescending(i => C(i).IsMonster ? C(i).Atk : -1).ThenBy(i => i),
            3 => ids.OrderByDescending(i => C(i).IsMonster ? C(i).Def : -1).ThenBy(i => i),
            4 => ids.OrderBy(i => C(i).IsMonster ? C(i).GuardianStar1 : 99).ThenBy(i => i),
            5 => ids.OrderByDescending(i => FmDeckBuild.IsNew(_player!, i)).ThenBy(i => i),
            6 => ids.OrderBy(i => C(i).IsMonster ? 1 : 0).ThenBy(i => i),
            _ => ids.OrderBy(i => i),
        };
        return q.ToList();
    }

    static int Owned(int id) => FmDeckBuild.Chest(_player!).Count(i => i == id);
    static int InDeck(int id) => _deck.Count(i => i == id);

    static void Scroll(int d)
    {
        int count = _showDeck ? _deck.Count : ChestRows().Count;
        int max = Math.Max(0, count - Rows);
        if (_showDeck) _deckTop = Math.Clamp(_deckTop + d, 0, max); else _chestTop = Math.Clamp(_chestTop + d, 0, max);
        Render();
    }

    static async void Switch()
    {
        if (_slide == null || _stage == null) return;
        FmSfx.Play(FmSfx.Flip);
        float dir = _showDeck ? 1 : -1;
        var t = _slide.CreateTween();
        t.TweenProperty(_slide, "position:x", dir * _stage.Size.X, 0.18).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
        await _slide.ToSignal(t, Tween.SignalName.Finished);
        _showDeck = !_showDeck;
        Render();
        _slide!.Position = new Vector2(-dir * _stage.Size.X, 0);
        var t2 = _slide.CreateTween();
        t2.TweenProperty(_slide, "position:x", 0f, 0.18).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
    }

    // ---------------- drawing ----------------
    static Vector2 U(float x, float y) => new(x * _u, y * _u);

    static Panel Box(Control parent, float x, float y, float w, float h, Color fill, Color rim, int border = 4)
    {
        var p = new Panel { Position = U(x, y), Size = U(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };
        int b = Math.Max(1, (int)(border * _u));
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = fill, BorderColor = rim, BorderWidthLeft = b, BorderWidthRight = b, BorderWidthTop = b, BorderWidthBottom = b });
        parent.AddChild(p);
        return p;
    }

    static Control Text(Control parent, string s, float x, float y, float scale, Color? ink = null)
    {
        var t = FmTextBox.Text(s, _u * scale, ink);
        t.Position = U(x, y);
        parent.AddChild(t);
        return t;
    }

    static Button Hit(Control parent, float x, float y, float w, float h, Action onPress, Action? onHover = null)
    {
        var b = new Button { Flat = true, Position = U(x, y), Size = U(w, h), FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
        foreach (var st in new[] { "normal", "hover", "pressed" }) b.AddThemeStyleboxOverride(st, new StyleBoxEmpty());
        b.Pressed += onPress;
        if (onHover != null) b.MouseEntered += onHover;
        parent.AddChild(b);
        return b;
    }

    static void Render()
    {
        if (_stage == null || _player == null) return;
        var keep = _slide?.Position ?? Vector2.Zero;
        _slide?.QueueFree();
        _slide = new Control { Size = _stage.Size, Position = keep, MouseFilter = Control.MouseFilterEnum.Ignore };
        _stage.AddChild(_slide);
        bool deck = _showDeck;
        var rows = deck ? DeckRows() : ChestRows();
        int top = deck ? _deckTop : _chestTop;

        // red arrow to the other list (FM: the chest is to the left of the deck)
        var arrow = new Polygon2D
        {
            Color = new Color(0.85f, 0.08f, 0.06f),
            Polygon = deck ? new[] { U(85, 20), U(85, 125), U(25, 72) } : new[] { U(1355, 20), U(1355, 125), U(1415, 72) },
        };
        _slide.AddChild(arrow);
        if (deck) Hit(_slide, 20, 15, 70, 115, Switch); else Hit(_slide, 1350, 15, 70, 115, Switch);

        // the other list's count (FM's small box by the arrow)
        float bx = deck ? 90 : 1075;
        Box(_slide, bx, 20, 195, 105, Navy, Rim);
        Text(_slide, deck ? "CHEST" : "DECK", bx + 12, 30, 2.4f, new Color(0.75f, 0.75f, 0.85f));
        Text(_slide, (deck ? FmDeckBuild.Chest(_player).Count : _deck.Count).ToString(), bx + 120, 80, 2.6f);

        // ORDER: FM's sort buttons (the fusion sound when clicked)
        float ox = deck ? 295 : 290;
        Box(_slide, ox, 20, 765, 105, Navy, Rim);
        Text(_slide, "ORDER", ox + 14, 40, 4.6f, Colors.White);
        int order = deck ? _deckOrder : _chestOrder;
        for (int i = 0; i < Orders.Length; i++)
        {
            int idx = i;
            float x = ox + 175 + i * 83;
            var cell = Box(_slide, x, 32, 76, 80, new Color(0.08f, 0.1f, 0.12f), i == order ? Lime : new Color(0.2f, 0.22f, 0.18f), i == order ? 6 : 3);
            Text(_slide, Orders[i], x + (76 - Orders[i].Length * 8 * 2.2f) / 2, 58, 2.2f, i == order ? new Color(0.9f, 1f, 0.6f) : new Color(0.55f, 0.48f, 0.25f));
            Hit(_slide, x, 32, 76, 80, () =>
            {
                FmSfx.Play(FmSfx.Fusion);
                if (_showDeck) { _deckOrder = idx; _deckTop = 0; } else { _chestOrder = idx; _chestTop = 0; }
                Render();
            }, () => ShowTip(OrderNames[idx]));
        }

        // the big title: DECK / CHEST and its count
        float tx = deck ? 1075 : 90;
        Box(_slide, tx, 5, 295, 135, Navy, Rim, 6);
        Text(_slide, deck ? "DECK" : "CHEST", tx + 25, 20, 6f, Colors.White);
        Text(_slide, deck ? $"{_deck.Count}/{FmDeckBuild.MaxDeck}" : FmDeckBuild.Chest(_player).Count.ToString(), tx + 60, 95, 3f);

        // the list
        Box(_slide, 65, 150, 1305, 855, Navy, new Color(0.12f, 0.14f, 0.3f), 6);
        for (int r = 0; r < Rows && top + r < rows.Count; r++)
        {
            int id = rows[top + r];
            float y = 160 + r * 106;
            var info = FmData.Get(id);
            bool full = !deck && InDeck(id) >= Owned(id);
            var ink = full ? new Color(0.45f, 0.45f, 0.5f) : Colors.White;
            foreach (var (cx, cw) in new[] { (75f, 80f), (160f, 120f), (290f, 580f), (880f, 190f), (1078f, 84f), (1168f, 162f) })
                Box(_slide, cx, y, cw, 98, new Color(0.03f, 0.04f, 0.14f), new Color(0.14f, 0.16f, 0.32f), 2);
            Text(_slide, $"{top + r + 1:00}", 82, y + 8, 3.4f, ink);
            Text(_slide, $"{id:000}", 166, y + 8, 3.4f, ink);
            Text(_slide, info.Name.Length > 17 ? info.Name[..17] : info.Name, 298, y + 26, 4.0f, ink);
            if (!deck)
            {
                Text(_slide, $"x{Owned(id)}", 780, y + 8, 2.4f, new Color(0.75f, 0.85f, 1f));
                if (InDeck(id) > 0) Text(_slide, $"in deck {InDeck(id)}", 690, y + 68, 2f, new Color(0.6f, 0.95f, 0.6f));
            }
            if (FmDeckBuild.IsNew(_player, id)) Text(_slide, "NEW", 298, y + 4, 2.2f, new Color(1f, 0.85f, 0.2f));
            if (info.IsMonster)
            {
                Text(_slide, info.Atk.ToString().PadLeft(4), 930, y + 8, 3.2f, ink);
                Text(_slide, info.Def.ToString().PadLeft(4), 930, y + 52, 3.2f, ink);
                for (int k = 0; k < 2; k++)
                {
                    int g = k == 0 ? info.GuardianStar1 : info.GuardianStar2;
                    if (g <= 0 || g >= StarSigns.Length) continue;
                    var l = new Label { Text = StarSigns[g], Position = U(1180 + k * 76, y + 6), MouseFilter = Control.MouseFilterEnum.Ignore };
                    l.AddThemeFontSizeOverride("font_size", (int)(62 * _u));
                    l.AddThemeColorOverride("font_color", full ? new Color(0.5f, 0.5f, 0.55f) : new Color(0.8f, 0.8f, 0.88f));
                    _slide.AddChild(l);
                }
            }
            else Text(_slide, FmData.TypeNames[(int)info.Type], 1180, y + 34, 2.6f, new Color(0.85f, 0.75f, 1f));
            if (FmData.Art(id) is { } art)
            {
                var pic = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = full ? new Color(0.5f, 0.5f, 0.5f) : Colors.White };
                pic.Texture = art; pic.Position = U(1084, y + 8); pic.Size = U(72, 82);
                _slide.AddChild(pic);
            }
            int row = top + r;
            Hit(_slide, 75, y, 1255, 98, () => Pick(id, deck, row), () => Hover(id, y));
        }
        // scroll bar, and the up/down arrows FM has on the left
        int total = Math.Max(1, rows.Count);
        float barH = Math.Max(60, 830f * Math.Min(1f, Rows / (float)total));
        float barY = 160 + (830 - barH) * (rows.Count <= Rows ? 0 : top / (float)(rows.Count - Rows));
        _slide.AddChild(new ColorRect { Position = U(1338, barY), Size = U(22, barH), Color = Lime, MouseFilter = Control.MouseFilterEnum.Ignore });
        var up = new Polygon2D { Color = new Color(1f, 0.75f, 0.1f), Polygon = new[] { U(5, 230), U(55, 230), U(30, 170) } };
        var down = new Polygon2D { Color = new Color(1f, 0.75f, 0.1f), Polygon = new[] { U(5, 930), U(55, 930), U(30, 990) } };
        _slide.AddChild(up); _slide.AddChild(down);
        Hit(_slide, 0, 165, 60, 70, () => Scroll(-Rows));
        Hit(_slide, 0, 925, 60, 70, () => Scroll(Rows));

        // done
        Box(_slide, 560, 1015, 320, 58, Navy, Rim);
        Text(_slide, "DONE", 560 + (320 - 4 * 8 * 3.2f) / 2, 1025, 3.2f, new Color(1f, 0.86f, 0.35f));
        Hit(_slide, 560, 1015, 320, 58, Close);
        string hint = deck ? "Click a card to take it out of your Deck." : "Click a card to put a copy in your Deck.";
        Text(_slide, hint, 70, 1035, 1.9f, new Color(0.15f, 0.1f, 0.05f));
    }

    static void Pick(int id, bool fromDeck, int row)
    {
        if (fromDeck)
        {
            if (_deck.Count <= FmDeckBuild.MinSize(_player!)) { ShowTip($"A deck needs at least {FmDeckBuild.MinSize(_player!)} cards."); FmSfx.Play(FmSfx.Fail); return; }
            _deck.Remove(id);
            FmSfx.Play(FmSfx.Flip);
        }
        else
        {
            if (_deck.Count >= FmDeckBuild.MaxDeck) { ShowTip("Your deck is full (40 cards)."); FmSfx.Play(FmSfx.Fail); return; }
            if (InDeck(id) >= Owned(id)) { ShowTip("Every copy you own is already in your deck."); FmSfx.Play(FmSfx.Fail); return; }
            _deck.Add(id);
            FmSfx.Play(FmSfx.Draw);
        }
        Render();
    }

    /// <summary>The card under the mouse, big, beside the screen (or over the list's right side on narrow screens).</summary>
    static void Hover(int id, float y)
    {
        if (_layer == null || _stage == null) return;
        _preview?.QueueFree();
        var view = _layer.GetViewport().GetVisibleRect().Size;
        float k = Math.Min(2.2f, (view.Y * 0.62f) / FmCardFace.Size.Y);
        var holder = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
        var node = new Node2D { Scale = Vector2.One * k };
        var face = FmCardFace.Create(id);
        node.AddChild(face);
        holder.AddChild(node);
        float side = view.X - (_stage.Position.X + _stage.Size.X);
        float cardW = FmCardFace.Size.X * k;
        float x = side >= cardW + 20 ? _stage.Position.X + _stage.Size.X + (side - cardW) / 2 + cardW / 2 : view.X - cardW / 2 - 20;
        holder.Position = new Vector2(x, view.Y / 2);
        _layer.AddChild(holder);
        _preview = holder;
    }

    static Label? _tip;
    static void ShowTip(string text)
    {
        if (_layer == null) return;
        _tip?.QueueFree();
        _tip = new Label { Text = text, Position = new Vector2(20, 20), MouseFilter = Control.MouseFilterEnum.Ignore };
        _tip.AddThemeFontSizeOverride("font_size", 22);
        _tip.AddThemeColorOverride("font_outline_color", Colors.Black);
        _tip.AddThemeConstantOverride("outline_size", 6);
        _layer.AddChild(_tip);
        var t = _tip.CreateTween(); t.TweenInterval(1.6); t.TweenProperty(_tip, "modulate:a", 0f, 0.4);
    }
}
