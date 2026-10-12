using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using FMDuelist.Data;

namespace FMDuelist.Game;

/// <summary>
/// Forbidden Memories' opening scene, played when a new Duel Master K run starts: Heishin finds the forbidden
/// treasure in the palace (still images with FM's stone text box), laughs over the hieroglyph tablet as the view pans
/// across it, then the Forbidden Memories title appears with track 02, on the same timing as the original.
/// The dialogue is read from the player's disc; the pictures are local files (devdata/story, never shipped) until
/// they can be read from the disc too, and the scene still plays (text only) without them.
/// </summary>
public static class FmStoryIntro
{
    const int TitleTrack = 2;

    /// <summary>Where each intro line sits in SLUS_014.11 (NTSC-U), in order.</summary>
    static readonly int[] LineOffsets = { 0x1A1A8F, 0x1A1A9D, 0x1A1AB6, 0x1A1AF5, 0x1A1B01 };

    static string Dir => System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(FmStoryIntro).Assembly.Location) ?? ".", "story");

    static Texture2D? Tex(string name)
    {
        var f = System.IO.Path.Combine(Dir, name + ".png");
        return System.IO.File.Exists(f) && Image.LoadFromFile(f) is { } img ? ImageTexture.CreateFromImage(img) : null;
    }

    public static List<string> Lines()
    {
        var slus = FmData.Slus;
        var lines = new List<string>();
        if (slus == null) return lines;
        foreach (var o in LineOffsets) lines.Add(FmText.ReadStory(slus, o));
        return lines;
    }

    static bool _skip;
    /// <summary>The intro is about to play or playing (story scenes wait for it).</summary>
    public static bool Playing;

    public static async Task Play()
    {
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        _skip = false;
        var lines = Lines();
        string L(int i) => i < lines.Count ? lines[i] : "";
        Log.Info("[FMDuelist] Story intro: " + string.Join(" / ", lines.Select(l => l.Replace("\n", " "))));

        var layer = new CanvasLayer { Layer = 100 };
        tree.Root.AddChild(layer);
        try
        {
            FmMusic.Stop();
            var view = layer.GetViewport().GetVisibleRect().Size;
            var black = new ColorRect { Color = Colors.Black, Size = view, MouseFilter = Control.MouseFilterEnum.Stop };
            layer.AddChild(black);
            // FM's 4:3 screen, centred
            float sw = Math.Min(view.X, view.Y * 4f / 3f), sh = sw * 3f / 4f;
            var stage = new Control { Position = new Vector2((view.X - sw) / 2, (view.Y - sh) / 2), Size = new Vector2(sw, sh), ClipContents = true, MouseFilter = Control.MouseFilterEnum.Ignore };
            layer.AddChild(stage);
            float imgH = sh * 700f / 1080f, boxY = sh * 700f / 1080f, boxH = sh * 380f / 1080f;

            TextureRect Pic(Texture2D? t, float width, float height)
            {
                var r = new TextureRect { Texture = t, Size = new Vector2(width, height), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, Modulate = new Color(1, 1, 1, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
                stage.AddChild(r);
                return r;
            }
            var heishin = Pic(Tex("intro1_heishin"), sw, imgH);
            var close = Pic(Tex("intro2_heishin_close"), sw, imgH);
            var tabletTex = Tex("intro3_tablet");
            float tabletW = tabletTex != null ? sw * tabletTex.GetWidth() / 1440f : sw;
            var tablet = Pic(tabletTex, tabletW, imgH);
            var title = Pic(Tex("intro4_title"), sw, sh);

            // FM's stone text box, with FM's own font and typing
            var boxRect = new Rect2(0, boxY, sw, boxH);
            if (Tex("textbox") is { } tb)
                stage.AddChild(new TextureRect { Texture = tb, Position = boxRect.Position, Size = boxRect.Size, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore });
            else
            {
                var p = new Panel { Position = boxRect.Position, Size = boxRect.Size, MouseFilter = Control.MouseFilterEnum.Ignore };
                p.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.1f, 0.11f, 0.2f), BorderColor = new Color(0.55f, 0.52f, 0.45f), BorderWidthBottom = 14, BorderWidthTop = 22, BorderWidthLeft = 14, BorderWidthRight = 14 });
                stage.AddChild(p);
            }
            var boxBg = stage.GetChild<Control>(stage.GetChildCount() - 1);
            var text = new FmTextBox(stage, boxRect, sh);
            // who is speaking: Heishin's name plate and portrait box (FM's spot, above the text box on the right)
            float k = sw / 1440f;
            var speaker = new Control { Modulate = new Color(1, 1, 1, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
            stage.AddChild(speaker);
            if (FmScene.Tex("icon_heishin") is { } hicon)
                speaker.AddChild(new TextureRect { Texture = hicon, Position = new Vector2(1074 * k, (700 - 225 - 49) * k), Size = new Vector2(213, 225) * k, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore });
            var plate = new PanelContainer { Position = new Vector2(28 * k, boxY - 34 * k), MouseFilter = Control.MouseFilterEnum.Ignore };
            plate.AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.1f, 0.11f, 0.22f, 0.95f), BorderColor = new Color(0.72f, 0.66f, 0.5f), BorderWidthBottom = 3, BorderWidthTop = 3, BorderWidthLeft = 3, BorderWidthRight = 3, ContentMarginLeft = 16 * k, ContentMarginRight = 16 * k, ContentMarginTop = 2 * k, ContentMarginBottom = 2 * k });
            var plateText = new Label { Text = "Heishin", MouseFilter = Control.MouseFilterEnum.Ignore };
            plateText.AddThemeFontOverride("font", new SystemFont { FontNames = new[] { "Lucida Console", "Consolas", "Courier New" }, FontWeight = 700 });
            plateText.AddThemeFontSizeOverride("font_size", (int)(30 * k));
            plateText.AddThemeColorOverride("font_color", new Color(1f, 0.86f, 0.35f));
            plate.AddChild(plateText);
            speaker.AddChild(plate);
            // clicks: first shows the whole box, second goes on (0.4 s cooldown inside the text box)
            black.GuiInput += e => { if (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left }) text.Click(); };

            var skip = new Button { Text = "Skip  ▶", Position = new Vector2(view.X - 190, 24), Size = new Vector2(160, 56), Modulate = new Color(1, 1, 1, 0.75f) };
            skip.AddThemeFontSizeOverride("font_size", 26);
            skip.Pressed += () => _skip = true;
            layer.AddChild(skip);

            void Fade(CanvasItem c, float to, double dur) { var tw = c.CreateTween(); tw.TweenProperty(c, "modulate:a", to, dur); }
            async Task Hold(double s) { var end = Time.GetTicksMsec() / 1000.0 + s; while (!_skip && Time.GetTicksMsec() / 1000.0 < end) await Frame(tree); }
            Task Say(string s) => text.Say(s, true, () => _skip);
            Tween? flicker = null;
            void Flicker(CanvasItem c)
            {
                flicker?.Kill();
                flicker = c.CreateTween().SetLoops();
                flicker.TweenProperty(c, "self_modulate", new Color(0.82f, 0.8f, 0.78f), 1.2).SetTrans(Tween.TransitionType.Sine);
                flicker.TweenProperty(c, "self_modulate", Colors.White, 1.2).SetTrans(Tween.TransitionType.Sine);
            }

            // 1) Heishin before the glowing treasure
            await Hold(3.5); Fade(heishin, 1, 0.4); Flicker(heishin);
            await Hold(2.6);
            var red = heishin.CreateTween(); red.TweenProperty(heishin, "modulate", new Color(0.8f, 0.15f, 0.1f, 0f), 0.45);
            await Hold(2.0);
            // 2) his face: "This is it..." / "I've found it at last..." / "The forbidden treasure..."
            Fade(close, 1, 0.4); Flicker(close);
            await Hold(0.9);
            Fade(speaker, 1, 0.3);
            await Say(L(0)); await Say(L(1)); await Say(L(2));
            text.Clear(); Fade(close, 0, 0.4);
            await Hold(1.2);
            // 3) laughing over the hieroglyph tablet as the view pans across it
            Fade(tablet, 1, 0.25); Flicker(tablet);
            await Hold(0.35);
            var pan = tablet.CreateTween(); pan.TweenProperty(tablet, "position:x", -(tabletW - sw), 5.0).SetTrans(Tween.TransitionType.Linear);
            await Hold(0.25);
            await Say(L(3)); await Say(L(4));
            // 4) the title, on track 02
            text.Clear(); Fade(speaker, 0, 0.5);
            if (!_skip) FmMusic.PlayOnce(TitleTrack);
            Fade(tablet, 0, 2.0); Fade(boxBg, 0, 2.0); Fade(text.Root, 0, 2.0);
            await Hold(4.0);
            Fade(title, 1, 1.0);
            await Hold(9.0);
            Fade(title, 0, 1.0);
            await Hold(1.2);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] story intro: " + ex); }
        finally
        {
            if (GodotObject.IsInstanceValid(layer))
            {
                var tw = layer.CreateTween();
                foreach (var c in layer.GetChildren().OfType<CanvasItem>()) tw.Parallel().TweenProperty(c, "modulate:a", 0f, 0.3);
                await Wait(tree, 0.32);
                layer.QueueFree();
            }
            if (_skip) FmMusic.Stop();
            RestoreRoomMusic();
        }
    }

    static void RestoreRoomMusic()
    {
        try
        {
            var state = RunManager.Instance.DebugOnlyGetState();
            if (state?.CurrentRoom is { } room && state.Players.FirstOrDefault() is { } p) FmMusic.OnRoomEntered(room, p);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] intro music: " + ex.Message); }
    }

    static async Task Frame(SceneTree tree) => await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
    static async Task Wait(SceneTree tree, double s) { var t = tree.CreateTimer(s); await t.ToSignal(t, SceneTreeTimer.SignalName.Timeout); }
}

/// <summary>A new Duel Master K run opens with Forbidden Memories' intro scene (not the practice duel).</summary>
[HarmonyPatch(typeof(NGame), nameof(NGame.StartNewSingleplayerRun))]
static class FmStoryIntroPatch
{
    static void Prefix(CharacterModel character)
    {
        if (character is Necrobinder && !FmPractice.Active) FmStoryIntro.Playing = true;
    }

    static void Postfix(CharacterModel character, ref Task<RunState> __result)
    {
        if (character is not Necrobinder || FmPractice.Active) return;
        __result = After(__result);
    }

    static async Task<RunState> After(Task<RunState> start)
    {
        try
        {
            var state = await start;
            if (!FmPractice.Active) await FmStoryIntro.Play();
            return state;
        }
        finally { FmStoryIntro.Playing = false; }
    }
}
