using Godot;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace FMDuelist.Game;

/// <summary>
/// Forbidden Memories' title screen in front of the main menu: the logo on the grey hieroglyph wall with a pulsing
/// green "CLICK TO START" where FM says PUSH START BUTTON. A click (or any key) dims the wall a little, as FM does,
/// and then the menu's options fly in.
/// </summary>
static class FmTitle
{
    static readonly Color Green = new(0.55f, 0.86f, 0.12f);

    public static async Task WaitForClick(NMainMenu menu)
    {
        var tree = menu.GetTree();
        var bg = menu.GetNodeOrNull<Control>("%MainMenuBg");
        if (bg == null) return;
        // only the title until the click
        var hidden = new List<CanvasItem>();
        foreach (var c in menu.GetChildren().OfType<CanvasItem>())
            if (c != bg && !bg.IsAncestorOf(c) && !c.IsAncestorOf(bg) && c.Visible) { c.Visible = false; hidden.Add(c); }

        var shade = new ColorRect { Name = "FmTitleShade", Color = Colors.Black, Modulate = new Color(1, 1, 1, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        shade.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        var prompt = new Control { Name = "FmClickToStart", MouseFilter = Control.MouseFilterEnum.Ignore };
        prompt.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        float size = menu.GetViewportRect().Size.Y * 0.058f;
        Label Text(bool outline)
        {
            var l = new Label
            {
                Text = "CLICK TO START", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore, AnchorLeft = 0, AnchorRight = 1, AnchorTop = 0.735f, AnchorBottom = 0.825f,
            };
            l.AddThemeFontOverride("font", new SystemFont { MultichannelSignedDistanceField = true, MsdfPixelRange = 16, FontNames = new[] { "Brush Script MT", "Segoe Script", "Georgia" }, FontItalic = true, FontWeight = 800 });
            l.AddThemeFontSizeOverride("font_size", (int)size);
            l.AddThemeColorOverride("font_color", outline ? new Color(0, 0, 0, 0) : Green);
            if (outline)
            {
                l.AddThemeColorOverride("font_outline_color", new Color(0.05f, 0.05f, 0.03f));
                l.AddThemeConstantOverride("outline_size", (int)(size * 0.22f));
            }
            return l;
        }
        // FM: the black outline stays, the green inside pulses
        var edge = Text(true); var fill = Text(false);
        prompt.AddChild(edge); prompt.AddChild(fill);
        bg.AddChild(shade);
        bg.AddChild(prompt);
        var pulse = fill.CreateTween().SetLoops();
        pulse.TweenProperty(fill, "modulate:a", 0.3f, 0.7).SetTrans(Tween.TransitionType.Sine);
        pulse.TweenProperty(fill, "modulate:a", 1f, 0.7).SetTrans(Tween.TransitionType.Sine);

        // anything clicked or pressed starts
        bool go = false;
        var catcher = new Control { Name = "FmTitleClick", MouseFilter = Control.MouseFilterEnum.Stop, FocusMode = Control.FocusModeEnum.All };
        catcher.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        catcher.GuiInput += e =>
        {
            if (e is InputEventMouseButton { Pressed: true } || e is InputEventKey { Pressed: true, Echo: false } || e is InputEventJoypadButton { Pressed: true }) go = true;
        };
        menu.AddChild(catcher);
        catcher.GrabFocus();
        try
        {
            while (!go && GodotObject.IsInstanceValid(menu) && menu.IsInsideTree())
            {
                await menu.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }
            if (!GodotObject.IsInstanceValid(menu)) return;
            FmSfx.Play(FmSfx.Mark);
            pulse.Kill();
            // the prompt blinks away and the wall dims
            var t = prompt.CreateTween();
            for (int i = 0; i < 3; i++) { t.TweenProperty(prompt, "modulate:a", 0f, 0.05); t.TweenProperty(prompt, "modulate:a", 1f, 0.05); }
            t.TweenProperty(prompt, "modulate:a", 0f, 0.1);
            var d = shade.CreateTween();
            d.TweenProperty(shade, "modulate:a", 0.45f, 0.4);
            await menu.ToSignal(d, Tween.SignalName.Finished);
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] title: " + ex.Message); }
        finally
        {
            if (GodotObject.IsInstanceValid(catcher)) catcher.QueueFree();
            if (GodotObject.IsInstanceValid(prompt)) prompt.QueueFree();
            foreach (var c in hidden) if (GodotObject.IsInstanceValid(c)) c.Visible = true;
        }
    }
}
