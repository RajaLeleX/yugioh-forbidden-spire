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
    static int[]? _cost;

    static void Load()
    {
        if (_byPassword != null) return;
        _byPassword = new();
        _cost = new int[FmData.CardCount + 1];
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
            _byPassword.TryAdd(pw, id);
        }
        Log.Info($"[FMDuelist] passwords: {_byPassword.Count} cards");
    }

    public static int? Card(int password) { Load(); return _byPassword!.TryGetValue(password, out var id) ? id : null; }
    public static int Cost(int id) { Load(); return id > 0 && id < _cost!.Length ? _cost[id] : NotForSale; }
}

/// <summary>
/// Forbidden Memories' PASSWORD screen (from the shop): your Star Chips, the 8-digit password (click a digit, then the
/// up and down arrows; the side arrows move between digits) and OK / END. A real card's password shows the card, its
/// number and price, and EXCHANGE / QUIT. A wrong password only makes the error sound; OK can't be pressed again for
/// 2 seconds.
/// </summary>
public static class FmPasswordScreen
{
    static readonly Color Navy = new(0.08f, 0.1f, 0.26f, 0.97f), Stone = new(0.62f, 0.6f, 0.55f), Yellow = new(0.92f, 0.86f, 0.15f);
    public static bool IsOpen { get; private set; }

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
            Panel Box(float x, float y, float w, float h)
            {
                var p = new Panel { Position = U(x, y), Size = U(w, h), MouseFilter = Control.MouseFilterEnum.Ignore };
                p.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = Navy, BorderColor = Stone, BorderWidthLeft = (int)(10 * u), BorderWidthRight = (int)(10 * u), BorderWidthTop = (int)(10 * u), BorderWidthBottom = (int)(10 * u) });
                stage.AddChild(p);
                return p;
            }
            Control Text(Control parent, string s, float x, float y, float scale, Color? ink = null)
            {
                var t = FmTextBox.Text(s, u * 3f * scale, ink);
                t.Position = U(x, y);
                parent.AddChild(t);
                return t;
            }
            Button Hit(float x, float y, float w, float h, Action press)
            {
                var b = new Button { Flat = true, Position = U(x, y), Size = U(w, h), FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
                foreach (var st in new[] { "normal", "hover", "pressed" }) b.AddThemeStyleboxOverride(st, new StyleBoxEmpty());
                b.Pressed += press;
                stage.AddChild(b);
                return b;
            }

            // the card: its back until a password finds one
            var cardHolder = new Control { Position = U(215, 385), MouseFilter = Control.MouseFilterEnum.Ignore };
            stage.AddChild(cardHolder);
            void ShowCard(int id)
            {
                foreach (var c in cardHolder.GetChildren()) c.QueueFree();
                float k = 620 * u / FmCardFace.Size.Y;
                if (id > 0)
                {
                    var n = new Node2D { Scale = Vector2.One * k };
                    n.AddChild(FmCardFace.Create(id));
                    cardHolder.AddChild(n);
                }
                else
                {
                    var back = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore };
                    back.Texture = FmRivalHand.Back();
                    back.Size = FmCardFace.Size * k;
                    back.Position = -back.Size / 2;
                    cardHolder.AddChild(back);
                }
            }
            ShowCard(0);

            // STARCHIP
            Box(430, 75, 525, 155);
            Text(stage, "STARCHIP", 460, 98, 1.8f, Yellow);
            var chips = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
            stage.AddChild(chips);
            void ShowChips()
            {
                foreach (var c in chips.GetChildren()) c.QueueFree();
                Text(chips, $"★x{player.Gold}".Replace("★", ""), 505, 158, 1.4f);
                var star = new Label { Text = "★", Position = U(458, 146), MouseFilter = Control.MouseFilterEnum.Ignore };
                star.AddThemeFontOverride("font", FmStars.SymbolFont);
                star.AddThemeFontSizeOverride("font_size", (int)(56 * u));
                star.AddThemeColorOverride("font_color", Yellow);
                chips.AddChild(star);
            }
            ShowChips();

            // the password
            Box(430, 240, 525, 180);
            int[] digits = new int[8];
            int sel = 0;
            var digitRow = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
            stage.AddChild(digitRow);
            const float dx = 510, dw = 49, dy = 300;
            void ShowDigits()
            {
                foreach (var c in digitRow.GetChildren()) c.QueueFree();
                for (int i = 0; i < 8; i++) Text(digitRow, digits[i].ToString(), dx + i * dw, dy, 2.0f);
                // the red circle round the chosen digit, and its up/down arrows
                var ring = new Line2D { Points = Ring(U(dx + sel * dw + 24, dy + 36), 36 * u), Closed = true, Width = 3 * u, DefaultColor = new Color(0.95f, 0.15f, 0.15f) };
                digitRow.AddChild(ring);
                digitRow.AddChild(Arrow(U(dx + sel * dw + 24, dy - 22), 0, u));
                digitRow.AddChild(Arrow(U(dx + sel * dw + 24, dy + 94), 1, u));
            }
            ShowDigits();
            for (int i = 0; i < 8; i++) { int k = i; Hit(dx + i * dw, dy, dw, 72, () => { sel = k; FmSfx.Play(FmSfx.Flip); ShowDigits(); }); }
            stage.AddChild(Arrow(U(480, dy + 36), 2, u)); stage.AddChild(Arrow(U(912, dy + 36), 3, u));
            Hit(458, dy + 11, 44, 50, () => { sel = (sel + 7) % 8; FmSfx.Play(FmSfx.Flip); ShowDigits(); });
            Hit(890, dy + 11, 44, 50, () => { sel = (sel + 1) % 8; FmSfx.Play(FmSfx.Flip); ShowDigits(); });
            void Bump(int d) { digits[sel] = (digits[sel] + d + 10) % 10; FmSfx.Play(FmSfx.Flip); ShowDigits(); }
            // up/down: the arrows over and under the chosen digit (they follow it)
            var upHit = Hit(0, 0, 44, 40, () => Bump(1));
            var downHit = Hit(0, 0, 44, 40, () => Bump(-1));
            void PlaceUpDown() { upHit.Position = U(dx + sel * dw + 2, dy - 42); downHit.Position = U(dx + sel * dw + 2, dy + 74); }

            // the message / choice panel
            Box(430, 430, 525, 255);
            var msg = new Control { MouseFilter = Control.MouseFilterEnum.Ignore };
            stage.AddChild(msg);
            var buttons = new List<Button>();
            int found = 0;
            bool done = false;
            double lastOk = -10;
            void Clear() { foreach (var c in msg.GetChildren()) c.QueueFree(); foreach (var b in buttons) b.QueueFree(); buttons.Clear(); }
            void Prompt()
            {
                Clear();
                Text(msg, "Please enter", 455, 470, 1.25f);
                Text(msg, "'PASSWORD'", 455, 510, 1.25f);
                Text(msg, "OK", 600, 595, 1.4f);
                Text(msg, "END", 720, 595, 1.4f);
                msg.AddChild(new Line2D { Points = Ring(U(578, 620), 16 * u), Closed = true, Width = 3 * u, DefaultColor = new Color(0.4f, 0.55f, 1f) });
                msg.AddChild(new Line2D { Points = Ring(U(698, 620), 16 * u), Closed = true, Width = 3 * u, DefaultColor = new Color(0.9f, 0.25f, 0.2f) });
                buttons.Add(Hit(560, 585, 110, 55, Ok));
                buttons.Add(Hit(680, 585, 140, 55, () => done = true));
            }
            void Offer(int id)
            {
                Clear();
                int cost = FmPasswords.Cost(id);
                Text(msg, $"CARD NUMBER {id:000}", 455, 452, 1.25f);
                Text(msg, $"x{cost}", 505, 492, 1.25f);
                var star = new Label { Text = "★", Position = U(458, 476), MouseFilter = Control.MouseFilterEnum.Ignore };
                star.AddThemeFontOverride("font", FmStars.SymbolFont);
                star.AddThemeFontSizeOverride("font_size", (int)(40 * u));
                star.AddThemeColorOverride("font_color", Yellow);
                msg.AddChild(star);
                var bar = new ColorRect { Position = U(450, 545), Size = U(485, 44), Color = new Color(0.86f, 0.9f, 0.1f, 0.85f), MouseFilter = Control.MouseFilterEnum.Ignore };
                msg.AddChild(bar);
                Text(msg, "EXCHANGE", 470, 552, 1.4f);
                Text(msg, "QUIT", 470, 602, 1.4f);
                var ex = Hit(450, 545, 485, 44, async () =>
                {
                    if (player.Gold < cost) { FmSfx.Play(FmSfx.Fail); return; }
                    FmSfx.Play(FmSfx.Mark);
                    try
                    {
                        await PlayerCmd.LoseGold(cost, player, MegaCrit.Sts2.Core.Entities.Gold.GoldLossType.Spent);
                        var card = player.RunState.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(id))), player);
                        await CardPileCmd.Add(card, PileType.Deck);
                    }
                    catch (Exception e2) { Log.Warn("[FMDuelist] password exchange: " + e2.Message); }
                    ShowChips();
                    found = 0; ShowCard(0); Prompt();
                });
                ex.MouseEntered += () => bar.Position = U(450, 545);
                var quit = Hit(450, 595, 485, 44, () => { FmSfx.Play(FmSfx.Flip); found = 0; ShowCard(0); Prompt(); });
                quit.MouseEntered += () => bar.Position = U(450, 595);
                buttons.Add(ex); buttons.Add(quit);
            }
            void Ok()
            {
                double now = Time.GetTicksMsec() / 1000.0;
                if (now - lastOk < 2.0) return; // FM: no spamming OK
                lastOk = now;
                int pw = 0; foreach (var d in digits) pw = pw * 10 + d;
                // a card the password names, that can be bought and that this game has
                if (FmPasswords.Card(pw) is int id && FmPasswords.Cost(id) != FmPasswords.NotForSale && FmSheet.TypeFor(id) != null)
                {
                    found = id;
                    FmSfx.Play(FmSfx.Fusion);
                    ShowCard(id);
                    Offer(id);
                }
                else FmSfx.Play(FmSfx.Fail);
            }
            Prompt();
            var catcher = new Control { Size = view, MouseFilter = Control.MouseFilterEnum.Ignore };
            layer.AddChild(catcher);

            await FmFade.In();
            while (!done && GodotObject.IsInstanceValid(layer))
            {
                PlaceUpDown();
                if (Input.IsActionJustPressed("ui_up")) Bump(1);
                if (Input.IsActionJustPressed("ui_down")) Bump(-1);
                if (Input.IsActionJustPressed("ui_left")) { sel = (sel + 7) % 8; ShowDigits(); }
                if (Input.IsActionJustPressed("ui_right")) { sel = (sel + 1) % 8; ShowDigits(); }
                if (Input.IsActionJustPressed("ui_cancel")) done = true;
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
