using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using FMDuelist.Relics;

namespace FMDuelist.Game;

/// <summary>
/// Forbidden Memories' FREE DUEL screen: the carved stone wall, the Build Deck box and the faces of the duelists you
/// can play in FM's 5-wide grid, the chosen face lit, and the bottom bar with its name and your WIN / LOSS record
/// against it. FM's Free Duel menu theme (track 44) loops while it is up. The wall and faces come from the player's
/// own footage (devdata/story, local only); without them the faces fall back to card backs on plain stone.
/// </summary>
public static class FmFreeDuelMenu
{
    public const int MenuTheme = 44;
    const float CellW = 212, CellH = 230, GapX = 251.75f, GapY = 251, X0 = 88, Y0 = 155;
    const int Cols = 5, VisibleRows = 3;

    /// <summary>Shows the menu; returns the chosen duelist, or null for leaving. Build Deck opens from its box.</summary>
    public static async Task<FmRival.Duelist?> Pick(Player player, IReadOnlyList<FmRival.Duelist> foes)
    {
        if (NGame.Instance is not { } game || Engine.GetMainLoop() is not SceneTree tree) return null;
        int music = FmMusic.Current;
        await FmFade.Out();
        FmMusic.Play(MenuTheme);
        var layer = new CanvasLayer { Layer = 107 };
        game.AddChild(layer);
        FmRival.Duelist? chosen = null;
        bool done = false;
        try
        {
            var view = layer.GetViewport().GetVisibleRect().Size;
            layer.AddChild(new ColorRect { Color = Colors.Black, Size = view, MouseFilter = Control.MouseFilterEnum.Stop });
            float sw = Math.Min(view.X, view.Y * 4f / 3f), sh = sw * 3f / 4f, u = sh / 1080f;
            var stage = new Control { Position = new Vector2((view.X - sw) / 2, (view.Y - sh) / 2), Size = new Vector2(sw, sh), ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
            layer.AddChild(stage);
            Vector2 U(float x, float y) => new(x * u, y * u);
            if (FmScene.Tex("fd_bg") is { } bgTex)
                stage.AddChild(new TextureRect { Texture = bgTex, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, Size = stage.Size, MouseFilter = Control.MouseFilterEnum.Ignore });
            else
            {
                stage.AddChild(new ColorRect { Color = new Color(0.2f, 0.24f, 0.22f), Size = stage.Size, MouseFilter = Control.MouseFilterEnum.Ignore });
                var t = FmTextBox.Text("FREE DUEL", u * 5f, new Color(0.62f, 0.75f, 0.68f));
                t.Position = U(720 - 9 * 20, 40);
                stage.AddChild(t);
            }

            // the slots: Build Deck first, then the duelists in FM's own order (their duelist numbers)
            var slots = new List<(string name, FmRival.Duelist? foe)> { ("Build Deck", null) };
            slots.AddRange(foes.OrderBy(f => f.DuelistIndex).Select(f => (FmRestDuel.ListName(f), (FmRival.Duelist?)f)));
            int sel = Math.Min(1, slots.Count - 1), top = 0;

            var grid = new Control { Size = stage.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
            stage.AddChild(grid);
            var bottom = new Control { Size = stage.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
            stage.AddChild(bottom);
            Panel? lit = null;

            int[] Record(FmRival.Duelist f) => FmRestDuel.Record(player, f.DuelistIndex);

            void ShowBottom()
            {
                foreach (var c in bottom.GetChildren()) c.QueueFree();
                var (name, foe) = slots[sel];
                float cy = 983, s = 3.6f; // letters 8x12 at 3.6 => 43 tall
                var n = FmTextBox.Text(name, u * s);
                n.Position = U(72, cy - 12 * s / 2);
                bottom.AddChild(n);
                if (foe != null)
                {
                    var r = Record(foe);
                    string rec = $"WIN {r[0],2} LOSS {r[1],2}";
                    var w = FmTextBox.Text(rec, u * s);
                    w.Position = U(1300 - rec.Length * 8 * s, cy - 12 * s / 2);
                    bottom.AddChild(w);
                }
                // duels left at this rest site, small, by the title
                string left = $"DUELS LEFT {FmRestDuel.Left(player)}";
                var l = FmTextBox.Text(left, u * 2.2f, new Color(0.8f, 0.9f, 0.82f));
                l.Position = U(1300 - left.Length * 8 * 2.2f, 112);
                bottom.AddChild(l);
            }

            void Light()
            {
                if (lit != null && GodotObject.IsInstanceValid(lit)) lit.QueueFree();
                int vis = sel - top * Cols;
                if (vis < 0 || vis >= Cols * VisibleRows) return;
                float x = X0 + (vis % Cols) * GapX, y = Y0 + (vis / Cols) * GapY;
                lit = new Panel { Position = U(x - 8, y - 8), Size = U(CellW + 16, CellH + 16), MouseFilter = Control.MouseFilterEnum.Ignore };
                int b = Math.Max(2, (int)(9 * u));
                lit.AddThemeStyleboxOverride("panel", new StyleBoxFlat
                {
                    BgColor = Colors.Transparent, BorderColor = new Color(0.95f, 0.8f, 0.25f),
                    BorderWidthLeft = b, BorderWidthRight = b, BorderWidthTop = b, BorderWidthBottom = b,
                    ShadowColor = new Color(0.3f, 0.7f, 1f, 0.7f), ShadowSize = (int)(10 * u),
                });
                grid.AddChild(lit);
                var blink = lit.CreateTween().SetLoops();
                blink.TweenProperty(lit, "modulate:a", 0.45f, 0.3);
                blink.TweenProperty(lit, "modulate:a", 1f, 0.3);
            }

            void Select(int i, bool sound = true)
            {
                i = Math.Clamp(i, 0, slots.Count - 1);
                if (i == sel && lit != null) return;
                sel = i;
                int row = sel / Cols;
                if (row < top) { top = row; DrawGrid(); }
                else if (row >= top + VisibleRows) { top = row - VisibleRows + 1; DrawGrid(); }
                if (sound) FmSfx.Play(FmSfx.Mark);
                Light(); ShowBottom();
            }

            async void Confirm(int i)
            {
                if (done) return;
                Select(i, false);
                FmSfx.Play(FmSfx.Fusion);
                var foe = slots[sel].foe;
                if (foe == null)
                {
                    // Build Deck from the menu, then back here with the menu theme
                    layer.Visible = false;
                    await FmDeckEditor.Open(player);
                    if (GodotObject.IsInstanceValid(layer)) { layer.Visible = true; FmMusic.Play(MenuTheme); }
                    return;
                }
                if (FmRestDuel.Left(player) <= 0) { FmSfx.Play(FmSfx.Fail); return; }
                chosen = foe; done = true;
            }

            void DrawGrid()
            {
                foreach (var c in grid.GetChildren()) c.QueueFree();
                lit = null;
                for (int v = 0; v < Cols * VisibleRows; v++)
                {
                    int i = top * Cols + v;
                    if (i >= slots.Count) break;
                    float x = X0 + (v % Cols) * GapX, y = Y0 + (v / Cols) * GapY;
                    var foe = slots[i].foe;
                    Texture2D? tex = foe == null ? FmScene.Tex("fd_builddeck") : FmScene.Tex($"fd_face_{foe.DuelistIndex:00}");
                    var pic = new TextureRect
                    {
                        Texture = tex ?? FmRivalHand.Back(), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale,
                        Position = U(x - 4, y - 4), Size = U(CellW + 8, CellH + 8), MouseFilter = Control.MouseFilterEnum.Ignore,
                    };
                    grid.AddChild(pic);
                    int idx = i;
                    var hit = new Button { Flat = true, Position = U(x, y), Size = U(CellW, CellH), FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
                    foreach (var st in new[] { "normal", "hover", "pressed", "focus" }) hit.AddThemeStyleboxOverride(st, new StyleBoxEmpty());
                    hit.MouseEntered += () => Select(idx);
                    hit.Pressed += () => Confirm(idx);
                    grid.AddChild(hit);
                }
                Light();
            }

            DrawGrid();
            Select(sel, false);
            ShowBottom();
            await FmFade.In();
            while (!done && GodotObject.IsInstanceValid(layer))
            {
                if (layer.Visible && !FmDeckEditor.IsOpen)
                {
                    if (Input.IsActionJustPressed("ui_left")) Select(sel - 1);
                    if (Input.IsActionJustPressed("ui_right")) Select(sel + 1);
                    if (Input.IsActionJustPressed("ui_up")) Select(sel - Cols);
                    if (Input.IsActionJustPressed("ui_down")) Select(sel + Cols);
                    if (Input.IsActionJustPressed("ui_accept")) Confirm(sel);
                    if (Input.IsActionJustPressed("ui_cancel") || Input.IsMouseButtonPressed(MouseButton.Right)) { FmSfx.Play(FmSfx.Fail); done = true; }
                }
                await layer.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
            await FmFade.Out();
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] free duel menu: " + ex); }
        finally
        {
            if (GodotObject.IsInstanceValid(layer)) layer.QueueFree();
        }
        // leaving: back to what played before; a chosen duel puts on its own theme as it starts
        if (chosen == null) { if (music >= 0) FmMusic.Play(music); else FmMusic.Stop(); }
        Callable.From(() => { _ = FmFade.In(); }).CallDeferred();
        return chosen;
    }
}
