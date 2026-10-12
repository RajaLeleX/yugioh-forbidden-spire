using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace FMDuelist.Game;

/// <summary>
/// The Singleplayer screen's "Standard" button: FM description, and a local picture (seto_standard.png next to the
/// mod, never shipped) in place of its icon when present.
/// </summary>
[HarmonyPatch(typeof(NSingleplayerSubmenu), nameof(NSingleplayerSubmenu._Ready))]
public static class StandardModePatch
{
    public const string Description = "Stop Priest Seto's Empire from taking all of the Millennium Items.";
    static Texture2D? _tex;
    static bool _tried;

    static Texture2D? Picture()
    {
        if (_tried) return _tex;
        _tried = true;
        var file = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(StandardModePatch).Assembly.Location) ?? ".", "seto_standard.png");
        if (System.IO.File.Exists(file) && Image.LoadFromFile(file) is { } img) _tex = ImageTexture.CreateFromImage(img);
        return _tex;
    }

    static void Frame(TextureRect icon, Texture2D tex)
    {
        if (!GodotObject.IsInstanceValid(icon)) return;
        if (!icon.HasMeta("fm_framed"))
        {
            icon.SetMeta("fm_framed", true);
            icon.Connect(Control.SignalName.Resized, Callable.From(() => Frame(icon, tex)));
        }
        icon.GetNodeOrNull("FMDuelistFrame")?.Free();
        icon.GetNodeOrNull("FMDuelistFrameInner")?.Free();
        var box = icon.Size;
        if (box.X < 2 || box.Y < 2) return;
        float k = Math.Min(box.X / tex.GetWidth(), box.Y / tex.GetHeight());
        var size = new Vector2(tex.GetWidth() * k, tex.GetHeight() * k);
        var pos = (box - size) / 2;
        Panel Border(string name, Vector2 p, Vector2 s, Color c, int w)
        {
            var panel = new Panel { Name = name, Position = p, Size = s, MouseFilter = Control.MouseFilterEnum.Ignore };
            panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                DrawCenter = false, BorderColor = c, BorderWidthLeft = w, BorderWidthRight = w, BorderWidthTop = w, BorderWidthBottom = w,
            });
            icon.AddChild(panel);
            return panel;
        }
        Border("FMDuelistFrame", pos - new Vector2(3, 3), size + new Vector2(6, 6), new Color(0.63f, 0.53f, 0.41f), 3);
        Border("FMDuelistFrameInner", pos, size, new Color(0.1f, 0.1f, 0.1f), 1);
    }

    static void Postfix(NSingleplayerSubmenu __instance)
    {
        try
        {
            if (Picture() is not { } tex) return;
            var icon = __instance.GetNodeOrNull<NSubmenuButton>("StandardButton")?.GetNodeOrNull<TextureRect>("Icon");
            if (icon == null) return;
            icon.Texture = tex;
            icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            icon.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
            icon.ClipContents = false; // the frame sits just outside the picture
            icon.Material = null;
            // the same thin gold frame (with a dark inner line) the other modes' pictures have, around the picture itself
            Callable.From(() => Frame(icon, tex)).CallDeferred();
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] standard mode art: " + ex.Message); }
    }
}
