using Godot;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace FMDuelist.Game;

/// <summary>
/// The boss duelist's hand, played like a real opponent's. When his turn starts your hand slides off the bottom of
/// the screen and his slides up in its place: the same fanned layout, card size and hover lift as yours, but every
/// card face down (he knows what they are, you don't). He hovers over cards as if moving a mouse, marks fusion
/// material with order numbers, and drags the cards he plays up onto the field. When his turn ends his hand slides
/// away and yours comes back exactly as you left it. His set traps sit face down above him.
/// </summary>
public static class FmRivalHand
{
    static CanvasLayer? _layer;
    static Node2D? _row;
    static readonly List<Node2D> _cards = new();
    static ImageTexture? _back;
    static int _count;
    static bool _inTurn;
    static Vector2 _origin;          // where the hand's cards are laid out from (screen space)
    static float _uiScale = 1f;      // the hand's on-screen scale
    static Vector2? _handRestPos;    // your hand's position before it slid away

    static readonly Vector2 CardSize = NCard.defaultSize; // 300 x 422, like your cards
    const float HandDrop = 520f;                         // how far a hand slides down to leave the screen

    static bool Valid => _layer != null && GodotObject.IsInstanceValid(_layer);

    /// <summary>Remember how many face-down cards he holds (shown during his turn).</summary>
    public static void Show(int count) => _count = count;

    public static void SetCount(int count)
    {
        _count = count;
        if (_inTurn && Valid) Layout(animate: true);
    }

    public static void Hide()
    {
        if (Valid) _layer!.QueueFree();
        _layer = null; _row = null; _cards.Clear(); _inTurn = false;
        RestorePlayerHand(instant: true);
        if (_trapLayer != null && GodotObject.IsInstanceValid(_trapLayer)) _trapLayer.QueueFree();
        _trapLayer = null; _trapRow = null;
    }

    // ---------------- turn swap ----------------
    /// <summary>Your hand slides off, his slides up in its place.</summary>
    public static async Task BeginTurn()
    {
        if (Engine.GetMainLoop() is not SceneTree tree || NCombatRoom.Instance is not { } room) return;
        var view = room.GetViewportRect().Size;
        var hand = NPlayerHand.Instance;
        if (hand != null && GodotObject.IsInstanceValid(hand))
        {
            _handRestPos ??= hand.Position;
            var xf = hand.CardHolderContainer.GetGlobalTransformWithCanvas();
            _origin = xf.Origin - (hand.Position - _handRestPos.Value); // as if your hand were at its resting place
            _uiScale = xf.Scale.X;
            var t = hand.CreateTween();
            t.TweenProperty(hand, "position", _handRestPos.Value + new Vector2(0, HandDrop), 0.3).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);
        }
        else { _origin = new Vector2(view.X / 2, view.Y - 60); _uiScale = view.Y / 1080f; }

        if (!Valid)
        {
            _layer = new CanvasLayer { Layer = 55 };
            tree.Root.AddChild(_layer);
            _row = new Node2D();
            _layer.AddChild(_row);
        }
        _inTurn = true;
        _row!.Position = _origin + new Vector2(0, HandDrop);
        Layout(animate: false);
        await Wait(0.2);
        var up = _row.CreateTween();
        up.TweenProperty(_row, "position", _origin, 0.35).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
        await Wait(0.4);
    }

    /// <summary>His hand slides away (he keeps it for next turn) and yours comes back.</summary>
    public static async Task EndTurn()
    {
        if (!_inTurn) return;
        _inTurn = false;
        if (Valid && _row != null)
        {
            var down = _row.CreateTween();
            down.TweenProperty(_row, "position", _origin + new Vector2(0, HandDrop), 0.3).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.In);
            await Wait(0.32);
            foreach (var c in _cards) c.QueueFree();
            _cards.Clear();
        }
        RestorePlayerHand(instant: false);
        await Wait(0.35);
    }

    static void RestorePlayerHand(bool instant)
    {
        if (_handRestPos is not { } rest) return;
        _handRestPos = null;
        var hand = NPlayerHand.Instance;
        if (hand == null || !GodotObject.IsInstanceValid(hand)) return;
        if (instant) { hand.Position = rest; return; }
        var t = hand.CreateTween();
        t.TweenProperty(hand, "position", rest, 0.4).SetTrans(Tween.TransitionType.Expo).SetEase(Tween.EaseType.Out);
    }

    // ---------------- layout ----------------
    static int N(int n) => Math.Clamp(n, 1, 10);
    static Vector2 Scale(int n) => HandPosHelper.GetScale(N(n));
    static Vector2 Pos(int i, int n) => HandPosHelper.GetPosition(N(n), Math.Clamp(i, 0, N(n) - 1)) * _uiScale;
    static float Angle(int i, int n) => Mathf.DegToRad(HandPosHelper.GetAngle(N(n), Math.Clamp(i, 0, N(n) - 1)));

    static void Layout(bool animate)
    {
        if (_row == null) return;
        int n = Math.Clamp(_count, 0, 10);
        while (_cards.Count > n) { var c = _cards[^1]; _cards.RemoveAt(_cards.Count - 1); c.QueueFree(); }
        while (_cards.Count < n)
        {
            var card = new Node2D { Position = new Vector2(700 * _uiScale, 120 * _uiScale), Scale = Scale(n) * _uiScale };
            var tex = Back();
            card.AddChild(new Sprite2D { Texture = tex, Scale = CardSize / new Vector2(tex.GetWidth(), tex.GetHeight()) });
            _row.AddChild(card);
            _cards.Add(card);
        }
        for (int i = 0; i < _cards.Count; i++)
        {
            var c = _cards[i];
            c.ZIndex = i;
            var s = Scale(n) * _uiScale;
            if (!animate) { c.Position = Pos(i, n); c.Rotation = Angle(i, n); c.Scale = s; continue; }
            var t = c.CreateTween().SetParallel();
            t.TweenProperty(c, "position", Pos(i, n), 0.25).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            t.TweenProperty(c, "rotation", Angle(i, n), 0.25);
            t.TweenProperty(c, "scale", s, 0.25);
            t.TweenProperty(c, "modulate", Colors.White, 0.15);
        }
    }

    // ---------------- playing like a person ----------------
    static int _hovered = -1;

    /// <summary>Hover a card the way your mouse does: it lifts, straightens and grows; the others make room.</summary>
    static async Task HoverTo(int i)
    {
        if (_row == null || i < 0 || i >= _cards.Count) return;
        int n = _cards.Count;
        for (int k = 0; k < n; k++)
        {
            var c = _cards[k];
            if (c.HasMeta("fm_marked") || c.HasMeta("fm_leaving")) continue;
            var t = c.CreateTween().SetParallel();
            if (k == i)
            {
                c.ZIndex = 100;
                t.TweenProperty(c, "position", new Vector2(Pos(k, n).X, -CardSize.Y * 0.32f * _uiScale), 0.12).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
                t.TweenProperty(c, "rotation", 0f, 0.12);
                t.TweenProperty(c, "scale", Scale(n) * _uiScale * 1.18f, 0.12);
            }
            else
            {
                c.ZIndex = k;
                float push = k < i ? -40f : 40f;
                t.TweenProperty(c, "position", Pos(k, n) + new Vector2(push * _uiScale, 0), 0.12);
                t.TweenProperty(c, "rotation", Angle(k, n), 0.12);
                t.TweenProperty(c, "scale", Scale(n) * _uiScale, 0.12);
            }
        }
        _hovered = i;
        FmSfx.Play(FmSfx.Flip);
        await Wait(0.16);
    }

    static void Unhover()
    {
        _hovered = -1;
        int n = _cards.Count;
        for (int k = 0; k < n; k++)
        {
            var c = _cards[k];
            if (c.HasMeta("fm_marked") || c.HasMeta("fm_leaving")) continue;
            c.ZIndex = k;
            var t = c.CreateTween().SetParallel();
            t.TweenProperty(c, "position", Pos(k, n), 0.15);
            t.TweenProperty(c, "rotation", Angle(k, n), 0.15);
            t.TweenProperty(c, "scale", Scale(n) * _uiScale, 0.15);
        }
    }

    /// <summary>He looks over his hand, hovering from card to card.</summary>
    public static async Task Think()
    {
        if (!_inTurn || _cards.Count == 0) return;
        for (int k = 0; k < _cards.Count; k++) { await HoverTo(k); await Wait(0.05); }
        Unhover();
        await Wait(0.15);
    }

    /// <summary>
    /// He chooses these cards: his "mouse" hovers along the hand to each one. A single card is just picked up;
    /// fusion material is marked one by one (raised, glowing, numbered), like right-clicking in your hand.
    /// </summary>
    public static async Task Pick(IReadOnlyList<int> indices)
    {
        if (!_inTurn || _cards.Count == 0) return;
        int at = _hovered >= 0 ? _hovered : indices.Count > 0 ? Math.Max(0, indices[0] - 2) : 0;
        for (int k = 0; k < indices.Count; k++)
        {
            int target = Math.Clamp(indices[k], 0, _cards.Count - 1);
            int step = target >= at ? 1 : -1;
            for (int i = at; ; i += step)
            {
                await HoverTo(i);
                if (i == target) break;
            }
            at = target;
            if (indices.Count > 1)
            {
                var c = _cards[target];
                c.SetMeta("fm_marked", true);
                var t = c.CreateTween();
                t.TweenProperty(c, "position", new Vector2(Pos(target, _cards.Count).X, -CardSize.Y * 0.18f * _uiScale), 0.1);
                Mark(c, k + 1);
                FmSfx.Play(FmSfx.Mark);
                await Wait(0.22);
            }
        }
        await Wait(0.2);
    }

    /// <summary>The chosen cards are dragged up off the hand and onto the field (or the middle of the screen).</summary>
    public static async Task Play(IReadOnlyList<int> indices, Vector2? to = null)
    {
        if (!_inTurn || _row == null) return;
        var view = NCombatRoom.Instance?.GetViewportRect().Size ?? new Vector2(1920, 1080);
        var dest = to ?? view / 2;
        var leaving = indices.Where(i => i >= 0 && i < _cards.Count).Select(i => _cards[i]).Distinct().ToList();
        for (int k = 0; k < leaving.Count; k++)
        {
            var c = leaving[k];
            c.SetMeta("fm_leaving", true);
            c.ZIndex = 200 + k;
            // drag: up out of the hand first, then across to where it's played
            var lift = c.GlobalPosition + new Vector2(0, -CardSize.Y * 0.45f * _uiScale);
            var t = c.CreateTween();
            t.TweenProperty(c, "global_position", lift, 0.14).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            t.Parallel().TweenProperty(c, "rotation", 0f, 0.14);
            t.TweenProperty(c, "global_position", dest, 0.28).SetTrans(Tween.TransitionType.Cubic).SetEase(Tween.EaseType.InOut);
            t.Parallel().TweenProperty(c, "scale", c.Scale * 0.55f, 0.28);
            t.TweenProperty(c, "modulate:a", 0f, 0.12);
            await Wait(0.12);
        }
        await Wait(0.45);
        foreach (var c in leaving) { _cards.Remove(c); c.QueueFree(); }
        _count = _cards.Count;
        foreach (var c in _cards)
        {
            if (c.HasMeta("fm_marked")) c.RemoveMeta("fm_marked");
            foreach (var x in c.GetChildren().Where(x => x is Label || x is Polygon2D).ToList()) x.QueueFree();
        }
        _hovered = -1;
        Layout(animate: true);
    }

    /// <summary>A marked card: gold glow and its order number, like your fusion marks.</summary>
    static void Mark(Node2D card, int n)
    {
        var glow = new Polygon2D
        {
            Polygon = new[] { new Vector2(-CardSize.X / 2 - 10, -CardSize.Y / 2 - 10), new Vector2(CardSize.X / 2 + 10, -CardSize.Y / 2 - 10), new Vector2(CardSize.X / 2 + 10, CardSize.Y / 2 + 10), new Vector2(-CardSize.X / 2 - 10, CardSize.Y / 2 + 10) },
            Color = new Color(1f, 0.85f, 0.25f, 0.55f),
        };
        card.AddChild(glow);
        card.MoveChild(glow, 0);
        var l = new Label { Text = n.ToString(), Position = new Vector2(CardSize.X / 2 - 70, -CardSize.Y / 2 - 10), Size = new Vector2(60, 70), HorizontalAlignment = HorizontalAlignment.Center };
        l.AddThemeFontSizeOverride("font_size", 56);
        l.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.2f));
        l.AddThemeConstantOverride("outline_size", 16);
        l.AddThemeColorOverride("font_outline_color", Colors.Black);
        card.AddChild(l);
    }

    // ---------------- his set traps ----------------
    static CanvasLayer? _trapLayer;
    static Node2D? _trapRow;

    /// <summary>His face-down set traps, a small row above him.</summary>
    public static void SetTraps(int count)
    {
        if (Engine.GetMainLoop() is not SceneTree tree || NCombatRoom.Instance is not { } room) return;
        if (_trapLayer == null || !GodotObject.IsInstanceValid(_trapLayer))
        {
            _trapLayer = new CanvasLayer { Layer = 54 };
            tree.Root.AddChild(_trapLayer);
            _trapRow = new Node2D();
            _trapLayer.AddChild(_trapRow);
        }
        if (FmRival.Priest != null && room.GetCreatureNode(FmRival.Priest) is { } pn)
        {
            var r = pn.Hitbox.GetGlobalRect();
            _trapRow!.Position = new Vector2(r.GetCenter().X - (count - 1) * 30, r.Position.Y - 70);
        }
        foreach (var c in _trapRow!.GetChildren()) c.QueueFree();
        var tex = Back();
        for (int i = 0; i < count; i++)
            _trapRow.AddChild(new Sprite2D { Texture = tex, Position = new Vector2(i * 60, 0), Scale = new Vector2(52, 74) / new Vector2(tex.GetWidth(), tex.GetHeight()), Modulate = new Color(0.85f, 0.75f, 1f) });
    }

    // ---------------- the card back ----------------
    /// <summary>The card back from the player's own FM (card_back.png next to the mod, never shipped), or, without it,
    /// an original one drawn in code: dark brown with a gold border and a glowing oval.</summary>
    public static ImageTexture Back()
    {
        if (_back != null) return _back;
        var file = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(FmRivalHand).Assembly.Location) ?? ".", "card_back.png");
        if (System.IO.File.Exists(file) && Image.LoadFromFile(file) is { } local) return _back = ImageTexture.CreateFromImage(local);
        const int w = 92, h = 132;
        var img = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        var gold = new Color(0.86f, 0.68f, 0.28f); var dark = new Color(0.22f, 0.12f, 0.06f); var mid = new Color(0.38f, 0.22f, 0.1f);
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                bool border = x < 4 || y < 4 || x >= w - 4 || y >= h - 4;
                bool inner = x < 7 || y < 7 || x >= w - 7 || y >= h - 7;
                float dx = (x - w / 2f) / (w * 0.32f), dy = (y - h / 2f) / (h * 0.3f);
                float r = dx * dx + dy * dy;
                Color c = border ? gold : inner ? dark : (r < 1f ? mid.Lerp(gold, Mathf.Clamp(1f - r, 0, 1) * 0.55f) : dark.Lerp(mid, ((x + y) % 9 == 0) ? 0.5f : 0f));
                if (!border && !inner && Math.Abs(r - 1f) < 0.08f) c = gold;
                img.SetPixel(x, y, c);
            }
        return _back = ImageTexture.CreateFromImage(img);
    }

    static async Task Wait(double s)
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        var t = tree.CreateTimer(s);
        await t.ToSignal(t, SceneTreeTimer.SignalName.Timeout);
    }
}
