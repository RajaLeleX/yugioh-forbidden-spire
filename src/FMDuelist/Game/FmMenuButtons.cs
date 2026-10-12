using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace FMDuelist.Game;

/// <summary>
/// The main menu's buttons look and arrive like Forbidden Memories' title menu: each option sits on a dark stone
/// plate, and the rows fly in from alternate sides (first from the left, next from the right, and so on) towards
/// the middle, trailing afterimages. The selected option turns green inside a red frame.
/// </summary>
static class FmMenuButtons
{
    const string PlateName = "FmPlate";
    const float Fly = 0.32f, GhostLag = 0.035f;
    const int Ghosts = 4;

    static readonly Color PlateFill = new(0.10f, 0.12f, 0.16f, 0.92f);
    static readonly Color PlateEdge = new(0.62f, 0.55f, 0.42f);
    static readonly Color FocusEdge = new(0.85f, 0.12f, 0.08f);
    static readonly Color FocusText = new(0.55f, 1f, 0.55f);

    static StyleBoxFlat Box(Color edge) => new()
    {
        BgColor = PlateFill, BorderColor = edge, BorderWidthLeft = 3, BorderWidthRight = 3, BorderWidthTop = 3, BorderWidthBottom = 3,
        ContentMarginLeft = 0, ContentMarginRight = 0, ShadowColor = new Color(0, 0, 0, 0.5f), ShadowSize = 4,
    };

    public static void Apply(NMainMenu menu)
    {
        var tree = menu.GetTree();
        if (menu.GetNodeOrNull<Control>("MainMenuTextButtons") is not { } box) return;
        // FM has no reticle arrows around the selection
        foreach (var f in new[] { "_buttonReticleLeft", "_buttonReticleRight" })
            if (Traverse.Create(menu).Field(f).GetValue() is CanvasItem r) r.Visible = false;
        var rows = box.GetChildren().OfType<NMainMenuTextButton>().Where(b => b.Visible && b.label != null).ToList();
        foreach (var b in rows) Dress(b);
        FlyIn(menu, tree, rows);
    }

    static void Dress(NMainMenuTextButton b)
    {
        var label = b.label!;
        if (label.GetNodeOrNull(PlateName) != null) return;
        var plate = new Panel { Name = PlateName, ShowBehindParent = true, MouseFilter = Control.MouseFilterEnum.Ignore };
        plate.AddThemeStyleboxOverride("panel", Box(PlateEdge));
        plate.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        plate.OffsetLeft = -28; plate.OffsetRight = 28; plate.OffsetTop = -2; plate.OffsetBottom = 2;
        label.AddChild(plate);
    }

    /// <summary>The selected option: green text in a red frame.</summary>
    public static void Focus(NMainMenuTextButton b, bool on)
    {
        if (b.label?.GetNodeOrNull<Panel>(PlateName) is not { } plate) return;
        plate.AddThemeStyleboxOverride("panel", Box(on ? FocusEdge : PlateEdge));
        if (!on) return;
        // the game's own hover tween would turn the word gold
        var tr = Traverse.Create(b).Field("_tween");
        (tr.GetValue() as Tween)?.Kill();
        var t = b.CreateTween().SetParallel();
        t.TweenProperty(b.label, "scale", new Vector2(1.05f, 1.05f), 0.05);
        t.TweenProperty(b.label, "self_modulate", FocusText, 0.05);
        tr.SetValue(t);
    }

    static async void FlyIn(NMainMenu menu, SceneTree tree, List<NMainMenuTextButton> rows)
    {
        try
        {
            float width = menu.GetViewportRect().Size.X;
            // hidden until the screen is showing (after the intro's black fade)
            foreach (var b in rows) b.label!.Modulate = new Color(1, 1, 1, 0);
            var start = Time.GetTicksMsec();
            while ((FmIntro.Playing || FmIntro.Covering) && Time.GetTicksMsec() - start < 60000)
                await menu.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            if (!GodotObject.IsInstanceValid(menu)) return;
            // FM's title first: CLICK TO START
            await FmTitle.WaitForClick(menu);
            if (!GodotObject.IsInstanceValid(menu)) return;
            for (int i = 0; i < rows.Count; i++)
            {
                var b = rows[i];
                if (!GodotObject.IsInstanceValid(b)) continue;
                var label = b.label!;
                var home = label.Position;
                float from = (i % 2 == 0 ? -1 : 1) * width * 0.6f;
                label.Modulate = Colors.White;
                label.Position = home + new Vector2(from, 0);
                var t = label.CreateTween();
                t.TweenProperty(label, "position", home, Fly).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
                for (int g = 1; g <= Ghosts; g++) Ghost(b, label, home, from, g);
            }
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] menu fly-in: " + ex.Message); }
    }

    /// <summary>An afterimage: a fading copy of the plate and its word following the same path a little later.</summary>
    static void Ghost(NMainMenuTextButton b, Control label, Vector2 home, float from, int g)
    {
        var ghost = new Panel { MouseFilter = Control.MouseFilterEnum.Ignore, Size = label.Size + new Vector2(56, 4) };
        ghost.AddThemeStyleboxOverride("panel", Box(PlateEdge));
        var word = new Label
        {
            Text = (label as Label)?.Text ?? "", HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        if (label.GetThemeFont("font") is { } font) word.AddThemeFontOverride("font", font);
        word.AddThemeFontSizeOverride("font_size", label.GetThemeFontSize("font_size"));
        word.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        ghost.AddChild(word);
        var offset = new Vector2(-28, -2);
        ghost.Position = label.Position + offset;
        ghost.Modulate = new Color(1, 1, 1, 0);
        b.AddChild(ghost);
        float alpha = 0.55f / g;
        var t = ghost.CreateTween();
        t.TweenInterval(GhostLag * g);
        t.TweenProperty(ghost, "modulate:a", alpha, 0.01);
        t.TweenProperty(ghost, "position", home + offset, Fly).From(home + offset + new Vector2(from, 0))
            .SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
        t.TweenProperty(ghost, "modulate:a", 0f, 0.12);
        t.TweenCallback(Callable.From(ghost.QueueFree));
    }
}

[HarmonyPatch(typeof(NMainMenuTextButton))]
static class FmMenuButtonFocusPatch
{
    [HarmonyPostfix, HarmonyPatch("OnFocus")]
    static void Focused(NMainMenuTextButton __instance) => FmMenuButtons.Focus(__instance, true);

    [HarmonyPostfix, HarmonyPatch("OnUnfocus")]
    static void Unfocused(NMainMenuTextButton __instance) => FmMenuButtons.Focus(__instance, false);
}
