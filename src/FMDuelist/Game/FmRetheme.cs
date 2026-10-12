using FMDuelist.Data;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;

namespace FMDuelist.Game;

/// <summary>
/// Every remaining potion and relic references a Forbidden Memories card (sheets/retheme.json): its name, the card's
/// in-game text as flavor, and the card's art as the icon, all read from the player's disc. Effects are unchanged.
/// </summary>
public static partial class FmRetheme
{
    public static void ApplyLoc(LocManager loc)
    {
        if (!FmData.Loaded) return;
        try
        {
            var relics = new Dictionary<string, string>();
            foreach (var (key, id) in Relics)
            {
                var c = FmData.Get(id);
                relics[key + ".title"] = Esc(c.Name) + (KnockOffs.Contains(key) ? "???" : "");
                var flavor = Flavor(c);
                if (flavor.Length > 0) relics[key + ".flavor"] = flavor;
            }
            loc.GetTable("relics").MergeWith(relics);

            var potions = new Dictionary<string, string>();
            foreach (var (key, id) in Potions) potions[key + ".title"] = Esc(FmData.Get(id).Name);
            loc.GetTable("potions").MergeWith(potions);
            Log.Info($"[FMDuelist] Re-themed {Relics.Count} relics and {Potions.Count} potions");
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] retheme loc: " + ex.Message); }
    }

    static string Esc(string s) => s.Replace("{", "(").Replace("}", ")").Replace("[", "(").Replace("]", ")");
    static string Flavor(FmCardInfo c) => Esc(string.Join(" ", c.Desc.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim())));

    public static int? RelicArt(RelicModel r) => Relics.TryGetValue(r.Id.Entry, out var id) ? id : null;

    // ---------------- potion icons: the card art in a rounded gold frame ----------------
    static readonly Dictionary<int, ImageTexture> _potion = new();
    static ImageTexture? _outline;
    const int S = 96;

    static float Corner(int x, int y, int r)
    {
        int cx = x < r ? r : x >= S - r ? S - 1 - r : x, cy = y < r ? r : y >= S - r ? S - 1 - r : y;
        float d = Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy));
        return Mathf.Clamp(r - d + 0.5f, 0f, 1f);
    }

    public static Texture2D? PotionIcon(int id)
    {
        if (_potion.TryGetValue(id, out var t)) return t;
        var art = FmData.Art(id)?.GetImage();
        if (art == null) return null;
        var a = (Image)art.Duplicate();
        a.Convert(Image.Format.Rgba8);
        a.Resize(S - 10, S - 10, Image.Interpolation.Bilinear);
        var img = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
        var gold = new Color(0.95f, 0.75f, 0.25f); var dark = new Color(0.25f, 0.15f, 0.05f);
        for (int y = 0; y < S; y++)
            for (int x = 0; x < S; x++)
            {
                float outer = Corner(x, y, 16);
                if (outer <= 0) continue;
                bool inner = x >= 5 && y >= 5 && x < S - 5 && y < S - 5;
                var c = inner ? a.GetPixel(x - 5, y - 5) : (x < 2 || y < 2 || x >= S - 2 || y >= S - 2 ? dark : gold);
                c.A = outer;
                img.SetPixel(x, y, c);
            }
        return _potion[id] = ImageTexture.CreateFromImage(img);
    }

    public static Texture2D Outline()
    {
        if (_outline != null) return _outline;
        var img = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
        for (int y = 0; y < S; y++) for (int x = 0; x < S; x++) img.SetPixel(x, y, new Color(1, 1, 1, Corner(x, y, 16)));
        return _outline = ImageTexture.CreateFromImage(img);
    }
}

[HarmonyPatch]
static class PotionIconPatches
{
    [HarmonyPrefix, HarmonyPatch(typeof(PotionModel), nameof(PotionModel.Image), MethodType.Getter)]
    static bool Image(PotionModel __instance, ref Texture2D __result)
    {
        if (FmGuardianStars.PotionIcon(__instance) is { } star) { __result = star; return false; }
        if (!FmRetheme.Potions.TryGetValue(__instance.Id.Entry, out var id) || FmRetheme.PotionIcon(id) is not { } t) return true;
        __result = t;
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(PotionModel), nameof(PotionModel.Outline), MethodType.Getter)]
    static bool Outline(PotionModel __instance, ref Texture2D? __result)
    {
        if (!FmRetheme.Potions.ContainsKey(__instance.Id.Entry) || !FmData.Loaded) return true;
        __result = FmRetheme.Outline();
        return false;
    }
}
