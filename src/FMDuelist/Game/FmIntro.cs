using System.Collections.Concurrent;
using FMDuelist.Audio;
using FMDuelist.Data;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace FMDuelist.Game;

/// <summary>
/// FM's intro FMV (MOVIE.STR, first movie) plays after the company logos; its title frame becomes the main menu
/// backdrop; the menu plays FM track 00. Everything is read from the player's own disc.
/// </summary>
public static class FmIntro
{
    const int TitleFrame = 255;
    static FmStr.Movie? _movie;
    static Texture2D? _title;
    static bool _playing, _skip;

    /// <summary>Background load (called from mod init).</summary>
    public static void Load()
    {
        Task.Run(() =>
        {
            try
            {
                if (FmData.DiscPath == null) return;
                using var disc = new FmDisc(FmData.DiscPath);
                var raw = disc.ReadRawSectors("/DATA/MOVIE.STR");
                if (raw == null) { Log.Info("[FMDuelist] Disc image has no raw sectors (.iso); intro movie unavailable."); return; }
                var movies = FmStr.Read(raw);
                if (movies.Count == 0) return;
                _movie = movies[0];
                var rgb = FmStr.DecodeFrame(_movie.Frames[Math.Min(TitleFrame, _movie.Frames.Count - 1)], _movie.Width, _movie.Height);
                var w = _movie.Width; var h = _movie.Height;
                Callable.From(() =>
                {
                    _title = ImageTexture.CreateFromImage(Image.CreateFromData(w, h, false, Image.Format.Rgb8, rgb));
                    ApplyMenuBackground();
                }).CallDeferred();
                Log.Info($"[FMDuelist] Intro movie loaded: {_movie.Frames.Count} frames, {_movie.Audio.Count / 2} audio samples");
            }
            catch (Exception ex) { Log.Warn("[FMDuelist] intro: " + ex.Message); }
        });
    }

    public static bool Playing => _playing;
    /// <summary>The black screen after the intro is still up (the menu's buttons wait for it).</summary>
    public static bool Covering;
    public static void Skip() => _skip = true;

    /// <summary>Plays the intro fullscreen at the movie's 15 fps with its XA audio. Any click or key skips.</summary>
    public static async Task Play()
    {
        for (int i = 0; i < 40 && _movie == null; i++) await Task.Delay(50);
        if (_movie == null || Engine.GetMainLoop() is not SceneTree tree) return;
        var m = _movie;
        _playing = true; _skip = false;
        var layer = new CanvasLayer { Layer = 120 };
        tree.Root.AddChild(layer);
        var black = new ColorRect { Color = Colors.Black, MouseFilter = Control.MouseFilterEnum.Stop, Modulate = new Color(1, 1, 1, 0) };
        black.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(black);
        // the logo screen fades to black first
        var toBlack = black.CreateTween();
        toBlack.TweenProperty(black, "modulate:a", 1f, 0.6);
        await toBlack.ToSignal(toBlack, Tween.SignalName.Finished);
        var view = new TextureRect { ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize, StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, MouseFilter = Control.MouseFilterEnum.Ignore };
        view.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(view);

        // decode ahead on a worker thread
        var queue = new ConcurrentQueue<(int index, byte[] rgb)>();
        var cts = new CancellationTokenSource();
        _ = Task.Run(() =>
        {
            for (int f = 0; f < m.Frames.Count && !cts.IsCancellationRequested; f++)
            {
                while (queue.Count > 20 && !cts.IsCancellationRequested) Thread.Sleep(5);
                queue.Enqueue((f, FmStr.DecodeFrame(m.Frames[f], m.Width, m.Height)));
            }
        });

        var pcm = m.Audio.ToArray();
        var bytes = new byte[pcm.Length * 2];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        var audio = new AudioStreamPlayer
        {
            Stream = new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, Stereo = true, MixRate = m.AudioRate, Data = bytes },
            VolumeDb = -4,
        };
        layer.AddChild(audio);
        var tex = ImageTexture.CreateFromImage(Image.CreateEmpty(m.Width, m.Height, false, Image.Format.Rgb8));
        view.Texture = tex;

        double fps = m.Audio.Count > 0 ? m.Frames.Count / (m.Audio.Count / 2.0 / m.AudioRate) : 15.0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        audio.Play();
        int shown = -1;
        while (!_skip && shown < m.Frames.Count - 1)
        {
            int due = (int)(clock.Elapsed.TotalSeconds * fps);
            while (queue.TryPeek(out var next) && next.index <= due)
            {
                queue.TryDequeue(out next);
                if (next.index >= due - 1) tex.Update(Image.CreateFromData(m.Width, m.Height, false, Image.Format.Rgb8, next.rgb));
                shown = next.index;
            }
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        }
        cts.Cancel();
        audio.Stop();
        var fade = view.CreateTween();
        fade.TweenProperty(view, "modulate:a", 0f, 0.4);
        await fade.ToSignal(fade, Tween.SignalName.Finished);
        _playing = false;
        // stay black (the logo screen never shows again) until the main menu is up, then fade into it
        HoldBlackUntilMenu(tree, layer, black);
    }

    static void HideLogos(Node n)
    {
        if (n is NLogoAnimation && n is CanvasItem c) c.Visible = false;
        foreach (var ch in n.GetChildren()) HideLogos(ch);
    }

    static bool LogoStillThere(Node n)
    {
        if (n is NLogoAnimation && n.IsInsideTree() && (n as CanvasItem)?.Visible != false) return true;
        foreach (var c in n.GetChildren()) if (LogoStillThere(c)) return true;
        return false;
    }

    static async void HoldBlackUntilMenu(SceneTree tree, CanvasLayer layer, ColorRect black)
    {
        try
        {
            Covering = true;
            black.MouseFilter = Control.MouseFilterEnum.Ignore;
            // the logo screen is hidden for good, so the title can come up after one second of black
            HideLogos(tree.Root);
            var start = Time.GetTicksMsec();
            while (Time.GetTicksMsec() - start < 1000 || NGame.Instance?.MainMenu == null)
            {
                HideLogos(tree.Root);
                if (Time.GetTicksMsec() - start > 10000) break;
                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
            var t = black.CreateTween();
            t.TweenProperty(black, "modulate:a", 0f, 0.5);
            Covering = false;
            await t.ToSignal(t, Tween.SignalName.Finished);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] intro fade: " + ex.Message); }
        Covering = false;
        if (GodotObject.IsInstanceValid(layer)) layer.QueueFree();
    }

    /// <summary>FM's title art (the logo on the flaming stone wall) behind the main menu's own buttons.</summary>
    public static void ApplyMenuBackground()
    {
        // FM's own title screen (the logo on the grey hieroglyph wall) when it's in the local story folder
        var wall = FmScene.Tex("bg_title");
        var title = wall ?? _title;
        if (title == null || NGame.Instance?.MainMenu is not { } menu) return;
        var bg = menu.GetNodeOrNull<Control>("%MainMenuBg");
        if (bg == null || bg.GetNodeOrNull("FMDuelistTitle") != null) return;
        // soft fill: the same frame blurred and darkened, covering the whole screen so there are no bars
        var src = title.GetImage();
        var blur = (Image)src.Duplicate();
        blur.Resize(src.GetWidth() / 10, src.GetHeight() / 10, Image.Interpolation.Bilinear);
        blur.Resize(src.GetWidth(), src.GetHeight(), Image.Interpolation.Cubic);
        var fill = new TextureRect
        {
            Name = "FMDuelistTitle", Texture = ImageTexture.CreateFromImage(blur), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCovered, TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new Color(0.45f, 0.4f, 0.38f),
        };
        fill.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        bg.AddChild(fill);
        // the whole frame (zoomed out), centred, sitting above the menu buttons
        var art = new TextureRect
        {
            Name = "FMDuelistLogo", Texture = title, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = wall != null ? TextureRect.StretchModeEnum.KeepAspectCentered : TextureRect.StretchModeEnum.KeepAspectCovered, TextureFilter = CanvasItem.TextureFilterEnum.Linear,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        // big: the full screen height and most of its width (only a little of the frame's top and bottom is trimmed)
        art.AnchorLeft = wall != null ? 0f : 0.06f; art.AnchorRight = wall != null ? 1f : 0.94f; art.AnchorTop = 0f; art.AnchorBottom = 1f;
        art.OffsetLeft = art.OffsetRight = art.OffsetTop = art.OffsetBottom = 0;
        bg.AddChild(art);
        // feather the frame's edges into the fill
        var edge = new Shader { Code = @"shader_type canvas_item;
void fragment(){ vec4 c = texture(TEXTURE, UV) * COLOR; float e = smoothstep(0.0, 0.06, UV.x) * smoothstep(0.0, 0.06, 1.0 - UV.x) * smoothstep(0.0, 0.06, UV.y) * smoothstep(0.0, 0.06, 1.0 - UV.y); COLOR = vec4(c.rgb, c.a * e); }" };
        art.Material = new ShaderMaterial { Shader = edge };
        // the wall fills the screen's height: feather its sides only
        if (wall != null)
            art.Material = new ShaderMaterial { Shader = new Shader { Code = @"shader_type canvas_item;
void fragment(){ vec4 c = texture(TEXTURE, UV) * COLOR; float e = smoothstep(0.0, 0.04, UV.x) * smoothstep(0.0, 0.04, 1.0 - UV.x); COLOR = vec4(c.rgb, c.a * e); }" } };
        var shade = new ColorRect { Name = "FMDuelistShade", Color = new Color(0, 0, 0, 0.15f), MouseFilter = Control.MouseFilterEnum.Ignore };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        bg.AddChild(shade);
    }
}

/// <summary>The intro FMV runs right after the company logos, before the title.</summary>
[HarmonyPatch(typeof(NLogoAnimation), nameof(NLogoAnimation.PlayAnimation))]
static class IntroAfterLogosPatch
{
    static void Postfix(ref Task __result)
    {
        var logos = __result;
        __result = Chain(logos);
    }

    static async Task Chain(Task logos)
    {
        try { await logos; } catch { }
        try { await FmIntro.Play(); } catch (Exception ex) { Log.Warn("[FMDuelist] intro play: " + ex.Message); }
    }
}

/// <summary>Main menu: FM title backdrop and FM track 00 instead of the game's menu music.</summary>
[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
static class MenuBackdropPatch
{
    static void Postfix(NMainMenu __instance)
    {
        try { FmIntro.ApplyMenuBackground(); } catch (Exception ex) { Log.Warn("[FMDuelist] menu bg: " + ex.Message); }
        // centre the menu buttons under the FM logo (and move the continue-run panel along with them)
        var menu = __instance;
        async void Centre()
        {
            try
            {
                var tree = menu.GetTree();
                for (int i = 0; i < 3; i++) await menu.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
                if (!GodotObject.IsInstanceValid(menu)) return;
                var buttons = menu.GetNodeOrNull<Control>("MainMenuTextButtons");
                if (buttons == null || buttons.HasMeta("fm_centred")) return;
                var kids = buttons.GetChildren().OfType<Control>().Where(c => c.Visible).ToList();
                if (kids.Count == 0) return;
                float centre = kids.Average(c => c.GetGlobalRect().GetCenter().X);
                float delta = menu.GetViewportRect().Size.X / 2 - centre;
                buttons.Position += new Vector2(delta, 0);
                buttons.SetMeta("fm_centred", true);
                if (menu.GetNodeOrNull<Control>("%ContinueRunInfo") is { } info) info.Position += new Vector2(delta, 0);
                FmMenuButtons.Apply(menu);
            }
            catch (Exception ex) { Log.Warn("[FMDuelist] menu centre: " + ex.Message); }
        }
        Centre();
    }
}

[HarmonyPatch(typeof(NAudioManager), nameof(NAudioManager.PlayMusic))]
static class MenuMusicPatch
{
    static bool Prefix(string music)
    {
        if (music != "event:/music/menu_update") return true;
        FmMusic.PlayWhenReady(FmMedia.MenuTrack);
        return false;
    }
}

/// <summary>Character select (after Singleplayer) plays its own FM track; leaving it goes back to the menu track.</summary>
[HarmonyPatch]
static class CharacterSelectMusicPatch
{
    [HarmonyPostfix, HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen), "OnSubmenuOpened")]
    static void Opened() { if (FmMedia.CharacterSelectTrack >= 0) FmMusic.PlayWhenReady(FmMedia.CharacterSelectTrack); }

    [HarmonyPostfix, HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen), "OnSubmenuClosed")]
    static async void Closed()
    {
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree) return;
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            // only when back on the main menu (embarking starts a run, which picks its own music)
            if (MegaCrit.Sts2.Core.Nodes.NRun.Instance == null && NGame.Instance?.MainMenu != null) FmMusic.PlayWhenReady(FmMedia.MenuTrack);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] char select music: " + ex.Message); }
    }
}
