using FMDuelist.Cards;
using FMDuelist.Data;
using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes;

namespace FMDuelist.Game;

/// <summary>
/// Forbidden Memories' screen transitions: the picture darkens to black over half a second (the top a little ahead of
/// the bottom), and comes back up the same way.
/// </summary>
public static class FmFade
{
    const string Shader = @"shader_type canvas_item;
uniform float amount = 0.0;
void fragment(){
  // a soft top-to-bottom gradient: the top darkens first, the bottom follows
  float a = clamp(amount * 1.25 - (UV.y) * 0.25, 0.0, 1.0);
  COLOR = vec4(0.0, 0.0, 0.0, a);
}";

    static CanvasLayer? _layer;
    static ColorRect? _rect;

    static ColorRect Rect()
    {
        if (_layer == null || !GodotObject.IsInstanceValid(_layer))
        {
            _layer = new CanvasLayer { Layer = 125 };
            (Engine.GetMainLoop() as SceneTree)!.Root.AddChild(_layer);
            _rect = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Ignore, Material = new ShaderMaterial { Shader = new Shader { Code = Shader } } };
            _rect.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            _layer.AddChild(_rect);
        }
        return _rect!;
    }

    static async Task To(float target, double seconds)
    {
        if (Engine.GetMainLoop() is not SceneTree) return;
        var r = Rect();
        var mat = (ShaderMaterial)r.Material;
        float from = (float)mat.GetShaderParameter("amount");
        var t = r.CreateTween();
        t.TweenMethod(Callable.From<float>(v => mat.SetShaderParameter("amount", v)), from, target, seconds);
        await r.ToSignal(t, Tween.SignalName.Finished);
        r.MouseFilter = target >= 1 ? Control.MouseFilterEnum.Stop : Control.MouseFilterEnum.Ignore;
    }

    public static Task Out(double seconds = 0.5) => To(1, seconds);
    public static Task In(double seconds = 0.5) => To(0, seconds);
}

/// <summary>FM's card passwords and their Star Chip prices, read from the player's own disc (DATA/WA_MRG.MRG).</summary>
public static class FmPasswords
{
    public const int NotForSale = 999999;
    static Dictionary<int, int>? _byPassword;   // password -> card number
    static int[]? _cost, _password;

    static void Load()
    {
        if (_byPassword != null) return;
        _byPassword = new();
        _cost = new int[FmData.CardCount + 1];
        _password = new int[FmData.CardCount + 1];
        var slus = FmData.Wa; // the table is in WA_MRG.MRG (at 0xFB9808 on the NTSC-U disc)
        if (slus == null) return;
        // the table: 8 bytes per card (Star Chip price, then the password as 8 BCD digits); card 1 is Blue-Eyes
        byte[] first = { 0x3F, 0x42, 0x0F, 0x00, 0x39, 0x11, 0x63, 0x89 };
        int start = -1;
        for (int i = 0; i + 8 <= slus.Length && start < 0; i += 4)
            if (slus.AsSpan(i, 8).SequenceEqual(first)) start = i;
        if (start < 0) { Log.Warn("[FMDuelist] password table not found"); return; }
        for (int id = 1; id <= FmData.CardCount; id++)
        {
            int o = start + (id - 1) * 8;
            if (o + 8 > slus.Length) break;
            _cost[id] = BitConverter.ToInt32(slus, o);
            int pw = 0;
            for (int b = 3; b >= 0; b--) { byte v = slus[o + 4 + b]; pw = pw * 100 + (v >> 4) * 10 + (v & 15); }
            _password[id] = pw;
            _byPassword.TryAdd(pw, id);
        }
        Log.Info($"[FMDuelist] passwords: {_byPassword.Count} cards");
    }

    public static int? Card(int password) { Load(); return _byPassword!.TryGetValue(password, out var id) ? id : null; }
    public static int Password(int id) { Load(); return id > 0 && id < _password!.Length ? _password[id] : 0; }
    /// <summary>A card a password can buy: it has a price and this game has it.</summary>
    public static bool Buyable(int id) => Cost(id) != NotForSale && Cost(id) > 0 && FmSheet.TypeFor(id) != null;
    public static IEnumerable<int> AllBuyable() { for (int id = 1; id <= FmData.CardCount; id++) if (Buyable(id)) yield return id; }
    public static int Cost(int id) { Load(); return id > 0 && id < _cost!.Length ? _cost[id] : NotForSale; }
}

/// <summary>
/// Forbidden Memories' PASSWORD screen (from the shop): your Star Chips, the 8-digit password (click a digit, then the
/// up and down arrows; the side arrows move between digits) and OK / END. A real card's password flips the card over
/// and shows its number and price with EXCHANGE / QUIT. A wrong password only makes the error sound; OK can't be
/// pressed again for 2 seconds. The PASSWORD REVEALER looks any card up by name: for its price plus a 50 Star Chip
/// fee it reveals the password and buys the card, with a Drop Rate Boost for the next combat. One card per shop visit.
/// </summary>
public static class FmPasswordScreen
{
    static readonly Color Navy = new(0.08f, 0.1f, 0.26f, 0.97f), Stone = new(0.62f, 0.6f, 0.55f), Yellow = new(0.92f, 0.86f, 0.15f),
        Bar = new(0.86f, 0.9f, 0.1f, 0.85f), Ink = new(0.05f, 0.06f, 0.15f);
    public const int RevealFee = 50;
    public static bool IsOpen { get; private set; }

    // the shop room a card was last bought in by password (one per visit)
    static WeakReference<object>? _boughtIn;
    static bool BoughtHere(Player p) => _boughtIn != null && _boughtIn.TryGetTarget(out var r) && ReferenceEquals(r, p.RunState.CurrentRoom);

    public static async Task Open(Player player)
    {
        if (IsOpen || NGame.Instance is not { } game || Engine.GetMainLoop() is not SceneTree tree) return;
        IsOpen = true;
        await FmFade.Out();
        var layer = new CanvasLayer { Layer = 110 };
        game.AddChild(layer);
        try
        {
            var view = layer.GetViewport().GetVisibleRect().Size;
            layer.AddChild(new ColorRect { Color = Colors.Black, Size = view, MouseFilter = Control.MouseFilterEnum.Stop });
            float sw = Math.Min(view.X, view.Y * 4f / 3f), sh = sw * 3f / 4f, u = sh / 720f;
            var stage = new Control { Position = new Vector2((view.X - sw) / 2, (view.Y - sh) / 2), Size = new Vector2(sw, sh), MouseFilter = Control.MouseFilterEnum.Ignore };
            layer.AddChild(stage);
            Vector2 U(float x, float y) => new(x * u, y * u);
            Panel Box(Control parent, float x, float y, float w, float h, float border = 10)
            {
                var p = new Panel { Position = U(x, y), Size = U(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };
                int b = Math.Max(2, (int)(border * u));
                p.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Navy, BorderColor = Stone, BorderWidthLeft = b, BorderWidthRight = b, BorderWidthTop = b, BorderWidthBottom = b });
                parent.AddChild(p);
                return p;
            }
            // text in stage units: a letter is 24*scale wide and 36*scale tall
            float TW(string s, float scale) => s.Length * 24 * scale;
            float TH(float scale) => 36 * scale;
            Control Text(Control parent, string s, float x, float y, float scale, Color? ink = null)
            {
                var t = FmTextBox.Text(s, u * 3f * scale, ink);
                t.Position = U(x, y);
                parent.AddChild(t);
                return t;
            }
            // a line whose middle is at height cy
            Control TextMid(Control parent, string s, float x, float cy, float scale, Color? ink = null) => Text(parent, s, x, cy - TH(scale) / 2, scale, ink);
            Label Star(Control parent, float cx, float cy, float size)
            {
                var star = new Label
                {
                    Text = "★", Position = U(cx - size, cy - size), Size = U(size * 2, size * 2), MouseFilter = Control.MouseFilterEnum.Ignore,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                };
                star.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
                star.AddThemeFontOverride("font", FmStars.SymbolFont);
                star.AddThemeFontSizeOverride("font_size", (int)(size * 1.5f * u));
                star.AddThemeColorOverride("font_color", Yellow);
                star.AddThemeColorOverride("font_outline_color", Ink);
                star.AddThemeConstantOverride("outline_size", Math.Max(2, (int)(3 * u)));
                parent.AddChild(star);
                return star;
            }
            Button Hit(Control parent, float x, float y, float w, float h, Action press)
            {
                var b = new Button { Flat = true, Position = U(x, y), Size = U(w, h), FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
                foreach (var st in new[] { "normal", "hover", "pressed", "focus" }) b.AddThemeStyleboxOverride(st, new StyleBoxEmpty());
                b.Pressed += press;
                parent.AddChild(b);
                return b;
            }

            // the card: its back until a password finds one, then it flips over
            float k = 600 * u / FmCardFace.Size.Y;
            var cardHolder = new Node2D { Position = U(215, 380), Scale = Vector2.One * k };
            stage.AddChild(cardHolder);
            int shownCard = 0;
            Tween? flip = null;
            void Face(int id)
            {
                foreach (var c in cardHolder.GetChildren()) c.QueueFree();
                if (id > 0) { cardHolder.AddChild(FmCardFace.Create(id)); return; }
                var back = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore };
                back.Texture = FmRivalHand.Back();
                back.Size = FmCardFace.Size;
                back.Position = -FmCardFace.Size / 2;
                cardHolder.AddChild(back);
            }
            void ShowCard(int id)
            {
                if (id == shownCard) return;
                shownCard = id;
                flip?.Kill();
                // FM: the card turns edge-on, changes side, and turns back, lifting a little as it goes
                flip = cardHolder.CreateTween();
                flip.TweenProperty(cardHolder, "scale", new Vector2(0, k * 1.06f), 0.16).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.In);
                flip.TweenCallback(Callable.From(() => Face(id)));
                flip.TweenProperty(cardHolder, "scale", new Vector2(k * 1.06f, k * 1.06f), 0.16).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
                flip.TweenProperty(cardHolder, "scale", new Vector2(k, k), 0.12);
                FmSfx.Play(FmSfx.Flip);
            }
            Face(0);

            // PASSWORD REVEALER (above the Star Chips)
            var revealBox = Box(stage, 430, 14, 525, 52, 6);
            var revealBar = new ColorRect { Position = U(442, 24), Size = U(501, 32), Color = Bar, Visible = false, MouseFilter = Control.MouseFilterEnum.Ignore };
            stage.AddChild(revealBar);
            const string RevealLabel = "PASSWORD REVEALER";
            TextMid(stage, RevealLabel, 430 + (525 - TW(RevealLabel, 0.75f)) / 2, 40, 0.75f, Yellow);

            // STARCHIP
            Box(stage, 430, 75, 525, 155);
            Text(stage, "STARCHIP", 462, 92, 1.6f, Yellow);
            var chips = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
            stage.AddChild(chips);
            void ShowChips()
            {
                foreach (var c in chips.GetChildren()) c.QueueFree();
                Star(chips, 488, 185, 26);
                TextMid(chips, $"x{player.Gold}", 522, 185, 1.4f);
            }
            ShowChips();

            // the password
            Box(stage, 430, 240, 525, 180);
            int[] digits = new int[8];
            int sel = 0;
            bool locked = false; // a revealed password can't be edited until the offer is closed
            var digitRow = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
            stage.AddChild(digitRow);
            const float dx = 497, dw = 49, dcy = 330, dScale = 1.8f; // digits' middle line
            float dTop = dcy - 36 * dScale / 2, dCell = 24 * dScale;
            void ShowDigits()
            {
                foreach (var c in digitRow.GetChildren()) c.QueueFree();
                for (int i = 0; i < 8; i++) Text(digitRow, digits[i].ToString(), dx + i * dw + (dw - dCell) / 2, dTop, dScale, locked ? Yellow : null);
                if (locked) return;
                // the red circle round the chosen digit, and its up/down arrows
                float cx = dx + sel * dw + dw / 2;
                digitRow.AddChild(new Line2D { Points = Ring(U(cx, dcy), 36 * u), Closed = true, Width = 3 * u, DefaultColor = new Color(0.95f, 0.15f, 0.15f) });
                digitRow.AddChild(Arrow(U(cx, dcy - 58), 0, u));
                digitRow.AddChild(Arrow(U(cx, dcy + 58), 1, u));
            }
            void Select(int i) { if (locked) return; sel = (i + 8) % 8; FmSfx.Play(FmSfx.Flip); ShowDigits(); }
            void Bump(int d) { if (locked) return; digits[sel] = (digits[sel] + d + 10) % 10; FmSfx.Play(FmSfx.Flip); ShowDigits(); }
            ShowDigits();
            for (int i = 0; i < 8; i++) { int n = i; Hit(stage, dx + i * dw, dcy - 34, dw, 68, () => Select(n)); }
            stage.AddChild(Arrow(U(465, dcy), 2, u)); stage.AddChild(Arrow(U(920, dcy), 3, u));
            Hit(stage, 443, dcy - 22, 44, 44, () => Select(sel - 1));
            Hit(stage, 898, dcy - 22, 44, 44, () => Select(sel + 1));
            // up/down: the arrows over and under the chosen digit (they follow it)
            var upHit = Hit(stage, 0, 0, 44, 44, () => Bump(1));
            var downHit = Hit(stage, 0, 0, 44, 44, () => Bump(-1));
            void PlaceUpDown()
            {
                float cx = dx + sel * dw + dw / 2;
                upHit.Position = U(cx - 22, dcy - 80); downHit.Position = U(cx - 22, dcy + 36);
                upHit.Visible = downHit.Visible = !locked;
            }

            // the message / choice panel: rows on an even grid, each highlight bar centred on its words
            Box(stage, 430, 430, 525, 255);
            var msg = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
            stage.AddChild(msg);
            var buttons = new List<Button>();
            bool done = false, searching = false;
            double lastOk = -10;
            const float tx = 466; // the panel's text margin
            void Clear() { foreach (var c in msg.GetChildren()) c.QueueFree(); foreach (var b in buttons) b.QueueFree(); buttons.Clear(); }
            void ToPrompt() { locked = false; ShowDigits(); ShowCard(0); Prompt(); }

            // a choice row: the bar spans the panel, the word sits in its middle
            void Choice(string label, float cy, Action press, List<ColorRect> bars, bool lit)
            {
                var bar = new ColorRect { Position = U(444, cy - 24), Size = U(497, 48), Color = Bar, Visible = lit, MouseFilter = Control.MouseFilterEnum.Ignore };
                msg.AddChild(bar);
                bars.Add(bar);
                TextMid(msg, label, tx, cy, 1.2f);
                var hit = Hit(stage, 444, cy - 24, 497, 48, press);
                hit.MouseEntered += () => { if (bar.Visible) return; foreach (var b in bars) b.Visible = b == bar; FmSfx.Play(FmSfx.Flip); };
                buttons.Add(hit);
            }

            void Prompt()
            {
                Clear();
                TextMid(msg, "Please enter", tx, 482, 1.2f);
                TextMid(msg, "'PASSWORD'", tx, 530, 1.2f);
                if (BoughtHere(player)) TextMid(msg, "(1 card per visit)", tx, 572, 0.8f, new Color(0.8f, 0.82f, 0.95f));
                // OK and END, each with its button ring just left of the word
                float okX = 560, endX = 735, rowY = 628;
                TextMid(msg, "OK", okX, rowY, 1.3f);
                TextMid(msg, "END", endX, rowY, 1.3f);
                msg.AddChild(new Line2D { Points = Ring(U(okX - 26, rowY), 16 * u), Closed = true, Width = 3 * u, DefaultColor = new Color(0.4f, 0.55f, 1f) });
                msg.AddChild(new Line2D { Points = Ring(U(endX - 26, rowY), 16 * u), Closed = true, Width = 3 * u, DefaultColor = new Color(0.9f, 0.25f, 0.2f) });
                buttons.Add(Hit(stage, okX - 46, rowY - 26, 120, 52, Ok));
                buttons.Add(Hit(stage, endX - 46, rowY - 26, 150, 52, () => done = true));
            }

            // the card's number and price, and EXCHANGE / QUIT (with the reveal fee when it came from the Revealer)
            void Offer(int id, bool revealed)
            {
                Clear();
                int cost = FmPasswords.Cost(id), total = cost + (revealed ? RevealFee : 0);
                TextMid(msg, $"CARD NUMBER {id:000}", tx, 470, 1.0f);
                Star(msg, tx + 14, 512, 18);
                TextMid(msg, revealed ? $"x{cost}+{RevealFee}" : $"x{cost}", tx + 34, 512, 1.0f);
                var bars = new List<ColorRect>();
                if (BoughtHere(player))
                {
                    TextMid(msg, "1 card per visit!", tx, 568, 1.0f, new Color(1f, 0.5f, 0.45f));
                    Choice("QUIT", 628, () => { FmSfx.Play(FmSfx.Flip); ToPrompt(); }, bars, true);
                    return;
                }
                Choice("EXCHANGE", 568, async () =>
                {
                    if (player.Gold < total || BoughtHere(player)) { FmSfx.Play(FmSfx.Fail); return; }
                    FmSfx.Play(FmSfx.Mark);
                    try
                    {
                        await PlayerCmd.LoseGold(total, player, MegaCrit.Sts2.Core.Entities.Gold.GoldLossType.Spent);
                        var card = player.RunState.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(id))), player);
                        await CardPileCmd.Add(card, PileType.Deck);
                        _boughtIn = new WeakReference<object>(player.RunState.CurrentRoom!);
                        if (revealed) await Relics.FmDropBoost.Grant(player);
                    }
                    catch (Exception e2) { Log.Warn("[FMDuelist] password exchange: " + e2.Message); }
                    ShowChips();
                    ToPrompt();
                }, bars, true);
                Choice("QUIT", 628, () => { FmSfx.Play(FmSfx.Flip); ToPrompt(); }, bars, false);
            }

            void Ok()
            {
                double now = Time.GetTicksMsec() / 1000.0;
                if (now - lastOk < 2.0) return; // FM: no spamming OK
                lastOk = now;
                int pw = 0; foreach (var d in digits) pw = pw * 10 + d;
                // a card the password names, that can be bought and that this game has
                if (FmPasswords.Card(pw) is int id && FmPasswords.Buyable(id))
                {
                    FmSfx.Play(FmSfx.Fusion);
                    ShowCard(id);
                    Offer(id, false);
                }
                else FmSfx.Play(FmSfx.Fail);
            }

            // the Revealer's search: type part of a name, click the card
            var search = Box(stage, 430, 75, 525, 610);
            search.Visible = false;
            search.MouseFilter = Control.MouseFilterEnum.Stop;
            var crisp = FmTextBox.CrispFont;
            int fs = Math.Max(10, (int)(22 * u));
            var title = new Label { Text = $"PASSWORD REVEALER  (+{RevealFee} fee)", Position = U(26, 18), Size = U(473, 34), VerticalAlignment = VerticalAlignment.Center };
            title.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
            title.AddThemeFontOverride("font", crisp); title.AddThemeFontSizeOverride("font_size", fs); title.AddThemeColorOverride("font_color", Yellow);
            search.AddChild(title);
            var field = new LineEdit { PlaceholderText = "Card name or number...", Position = U(22, 58), Size = U(481, 46), ClearButtonEnabled = true, CaretBlink = true };
            field.AddThemeFontOverride("font", crisp); field.AddThemeFontSizeOverride("font_size", fs);
            field.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.55f), BorderColor = Stone, BorderWidthBottom = 2, BorderWidthTop = 2, BorderWidthLeft = 2, BorderWidthRight = 2, ContentMarginLeft = 10 * u, ContentMarginRight = 10 * u });
            field.AddThemeStyleboxOverride("focus", new StyleBoxEmpty());
            search.AddChild(field);
            var scroll = new ScrollContainer { Position = U(22, 116), Size = U(481, 410), HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
            search.AddChild(scroll);
            var list = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            list.AddThemeConstantOverride("separation", 0);
            scroll.AddChild(list);
            var back = new Button { Text = "BACK", Position = U(22, 540), Size = U(481, 48), FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
            back.AddThemeFontOverride("font", crisp); back.AddThemeFontSizeOverride("font_size", fs);
            back.AddThemeColorOverride("font_color", Colors.White); back.AddThemeColorOverride("font_hover_color", Ink);
            back.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color(0, 0, 0, 0.35f) });
            back.AddThemeStyleboxOverride("hover", new StyleBoxFlat { BgColor = Bar });
            back.AddThemeStyleboxOverride("pressed", new StyleBoxFlat { BgColor = Bar });
            search.AddChild(back);
            void CloseSearch() { searching = false; search.Visible = false; field.ReleaseFocus(); }
            back.Pressed += () => { FmSfx.Play(FmSfx.Flip); CloseSearch(); };

            Control Row(int id)
            {
                var info = FmData.Get(id);
                int total = FmPasswords.Cost(id) + RevealFee;
                var row = new Button { CustomMinimumSize = U(0, 40), FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand, ClipText = true };
                row.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
                row.AddThemeStyleboxOverride("hover", new StyleBoxFlat { BgColor = Bar });
                row.AddThemeStyleboxOverride("pressed", new StyleBoxFlat { BgColor = Bar });
                var name = new Label { Text = $"{id:000}  {info.Name}", Position = U(8, 0), Size = U(360, 40), VerticalAlignment = VerticalAlignment.Center, ClipText = true, MouseFilter = Control.MouseFilterEnum.Ignore };
                var price = new Label { Text = $"★{total}", Position = U(368, 0), Size = U(100, 40), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, MouseFilter = Control.MouseFilterEnum.Ignore };
                foreach (var l in new[] { name, price })
                {
                    l.AddThemeStyleboxOverride("normal", new StyleBoxEmpty());
                    l.AddThemeFontOverride("font", crisp); l.AddThemeFontSizeOverride("font_size", (int)(fs * 0.9f));
                    l.AddThemeColorOverride("font_color", player.Gold >= total ? Colors.White : new Color(0.65f, 0.65f, 0.72f));
                    row.AddChild(l);
                }
                row.MouseEntered += () => { name.AddThemeColorOverride("font_color", Ink); price.AddThemeColorOverride("font_color", Ink); };
                row.MouseExited += () => { var c = player.Gold >= total ? Colors.White : new Color(0.65f, 0.65f, 0.72f); name.AddThemeColorOverride("font_color", c); price.AddThemeColorOverride("font_color", c); };
                row.Pressed += () =>
                {
                    CloseSearch();
                    // the password shows in the digits (in yellow), the card flips over, and the offer adds the fee
                    var pw = FmPasswords.Password(id).ToString("00000000");
                    for (int i = 0; i < 8; i++) digits[i] = pw[i] - '0';
                    locked = true;
                    ShowDigits();
                    FmSfx.Play(FmSfx.Fusion);
                    ShowCard(id);
                    Offer(id, true);
                };
                return row;
            }
            void Fill(string q)
            {
                foreach (var c in list.GetChildren()) c.QueueFree();
                q = q.Trim();
                var hits = FmPasswords.AllBuyable()
                    .Where(id => q.Length == 0 || FmData.Get(id).Name.Contains(q, StringComparison.OrdinalIgnoreCase) || id.ToString().TrimStart('0') == q.TrimStart('0') || id.ToString("000") == q)
                    .Take(80);
                foreach (var id in hits) list.AddChild(Row(id));
            }
            field.TextChanged += Fill;
            Fill("");
            var revealHit = Hit(stage, 430, 14, 525, 52, () =>
            {
                if (BoughtHere(player)) { FmSfx.Play(FmSfx.Fail); return; }
                FmSfx.Play(FmSfx.Mark);
                searching = true;
                search.Visible = true;
                stage.MoveChild(search, -1);
                field.Text = ""; Fill("");
                field.GrabFocus();
            });
            revealHit.MouseEntered += () => revealBar.Visible = true;
            revealHit.MouseExited += () => revealBar.Visible = false;

            Prompt();
            await FmFade.In();
            while (!done && GodotObject.IsInstanceValid(layer))
            {
                PlaceUpDown();
                if (searching)
                {
                    if (Input.IsActionJustPressed("ui_cancel")) CloseSearch();
                }
                else
                {
                    if (Input.IsActionJustPressed("ui_up")) Bump(1);
                    if (Input.IsActionJustPressed("ui_down")) Bump(-1);
                    if (Input.IsActionJustPressed("ui_left")) Select(sel - 1);
                    if (Input.IsActionJustPressed("ui_right")) Select(sel + 1);
                    if (Input.IsActionJustPressed("ui_cancel")) done = true;
                }
                await layer.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
            FmSfx.Play(FmSfx.Flip);
            await FmFade.Out();
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] password screen: " + ex); }
        finally
        {
            if (GodotObject.IsInstanceValid(layer)) layer.QueueFree();
            IsOpen = false;
        }
        await FmFade.In();
    }

    static Vector2[] Ring(Vector2 c, float r, int n = 24)
    {
        var p = new Vector2[n];
        for (int i = 0; i < n; i++) { float a = Mathf.Tau * i / n; p[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r; }
        return p;
    }

    /// <summary>FM's green-and-gold arrow (0 up, 1 down, 2 left, 3 right).</summary>
    static Node2D Arrow(Vector2 at, int dir, float u)
    {
        var n = new Node2D { Position = at, Rotation = dir switch { 0 => 0, 1 => Mathf.Pi, 2 => -Mathf.Pi / 2, _ => Mathf.Pi / 2 } };
        n.AddChild(new Polygon2D { Color = new Color(0.1f, 0.3f, 0.1f), Polygon = new[] { new Vector2(-20, -20) * u, new Vector2(20, -20) * u, new Vector2(20, 20) * u, new Vector2(-20, 20) * u } });
        n.AddChild(new Polygon2D { Color = new Color(0.85f, 0.8f, 0.3f), Polygon = new[] { new Vector2(0, -14) * u, new Vector2(13, 2) * u, new Vector2(5, 2) * u, new Vector2(5, 14) * u, new Vector2(-5, 14) * u, new Vector2(-5, 2) * u, new Vector2(-13, 2) * u } });
        return n;
    }
}

/// <summary>The shop's card removal service is Forbidden Memories' PASSWORD instead (Star Chip icon, no fixed price).</summary>
[HarmonyLib.HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantCardRemoval))]
static class FmPasswordShopPatch
{
    static Player? FmPlayer => MegaCrit.Sts2.Core.Runs.RunManager.Instance.DebugOnlyGetState()?.Players.FirstOrDefault(FmRules.IsFmPlayer);

    [HarmonyLib.HarmonyPrefix, HarmonyLib.HarmonyPatch("OnTryPurchase")]
    static bool Purchase(ref Task __result)
    {
        if (FmPlayer is not { } p) return true;
        __result = FmPasswordScreen.Open(p);
        return false;
    }

    [HarmonyLib.HarmonyPostfix, HarmonyLib.HarmonyPatch("UpdateVisual")]
    static void Visual(MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantCardRemoval __instance)
    {
        if (FmPlayer == null) return;
        // no price tag: each card has its own price on the password screen
        if (__instance.GetNodeOrNull<Control>("Cost") is { } cost) cost.Visible = false;
    }
}
