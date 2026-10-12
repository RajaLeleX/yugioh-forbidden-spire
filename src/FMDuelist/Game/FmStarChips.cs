using System.Text.RegularExpressions;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Logging;

namespace FMDuelist.Game;

/// <summary>
/// The currency is Star Chips, as in Forbidden Memories: every "Gold"/"coin" in the game's text is renamed, and the
/// gold icons (top bar, shop prices, inline text icon, coin bursts, score screen) show the mod's own star chip art.
/// </summary>
public static class FmStarChips
{
    static readonly string[] IconPaths =
    {
        "res://images/packed/sprite_fonts/gold_icon.png",
        "res://images/atlases/ui_atlas.sprites/top_bar/top_bar_gold.tres",
        "res://images/vfx/vfx_coin_explosion/coin_explosion_coin.png",
        "res://images/ui/game_over_screen/score_gold.png",
        "res://images/ui/game_over_screen/badge_money_money.png",
        "res://images/ui/reward_screen/reward_icon_money.png",
        "res://images/vfx/monsters/fake_merchant/fake_merchant_coin_particle.png",
    };
    const string FlipSheet = "res://images/packed/vfx/coin_flip_anim.png";
    static Image? _star;
    static ulong _flipId;
    static readonly Dictionary<(int, int), ImageTexture> _flipSheets = new();
    static bool _dump;
    static readonly HashSet<string> _seen = new();

    // original texture (by instance id) -> star chip texture of the same size
    static readonly Dictionary<ulong, Texture2D> Swap = new();

    public static void Load()
    {
        try
        {
            var file = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(FmStarChips).Assembly.Location) ?? ".", "star_chip.png");
            var star = Image.LoadFromFile(file);
            if (star == null) { Log.Warn("[FMDuelist] star_chip.png missing"); return; }
            _star = star;
            _dump = System.IO.File.Exists(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(file)!, "dump_textures"));
            try
            {
                var flip = ResourceLoader.Load<Texture2D>(FlipSheet);
                if (flip != null)
                {
                    _flipId = flip.GetInstanceId();
                    if (_dump) { DirAccess.MakeDirRecursiveAbsolute("user://fmdump"); flip.GetImage()?.SavePng("user://fmdump/coin_flip_anim.png"); }
                }
            }
            catch (Exception ex) { Log.Warn("[FMDuelist] coin flip: " + ex.Message); }
            foreach (var path in IconPaths)
            {
                try
                {
                    var old = ResourceLoader.Load<Texture2D>(path);
                    if (old == null) continue;
                    var tex = path.Contains("reward_icon_money") || path.Contains("badge_money") ? Fit(Pile(star), old.GetSize()) : Fit(star, old.GetSize());
                    Swap[old.GetInstanceId()] = tex;
                    // only the inline text icon is replaced at the file level ([img] tags load it by path); every other
                    // icon is swapped on its node, because game code loads some of them as CompressedTexture2D
                    if (path.EndsWith("gold_icon.png")) tex.TakeOverPath(path);
                }
                catch (Exception ex) { Log.Warn($"[FMDuelist] star chip icon {path}: {ex.Message}"); }
            }
            var tree = (SceneTree)Engine.GetMainLoop();
            tree.Connect(SceneTree.SignalName.NodeAdded, Callable.From<Node>(OnNodeAdded));
            Log.Info($"[FMDuelist] Star Chips: {Swap.Count} icons replaced");
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] star chips: " + ex.Message); }
    }

    /// <summary>A star chip sized like the icon it replaces.</summary>
    public static Texture2D? StarFor(Texture2D? like)
    {
        if (_star == null) return null;
        var size = like?.GetSize() ?? new Vector2(64, 64);
        return Fit(_star, size.X < 4 || size.Y < 4 ? new Vector2(64, 64) : size);
    }

    static ImageTexture Fit(Image src, Vector2 size)
    {
        int w = Math.Max(1, (int)size.X), h = Math.Max(1, (int)size.Y);
        int side = Math.Min(w, h);
        var scaled = (Image)src.Duplicate();
        scaled.Resize(side, side, Image.Interpolation.Lanczos);
        var canvas = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        canvas.BlitRect(scaled, new Rect2I(0, 0, side, side), new Vector2I((w - side) / 2, (h - side) / 2));
        return ImageTexture.CreateFromImage(canvas);
    }

    /// <summary>A small heap of three star chips (for "pile of money" icons).</summary>
    static Image Pile(Image star)
    {
        const int S = 256;
        var canvas = Image.CreateEmpty(S, S, false, Image.Format.Rgba8);
        void Put(int size, int x, int y)
        {
            var s = (Image)star.Duplicate(); s.Convert(Image.Format.Rgba8); s.Resize(size, size, Image.Interpolation.Lanczos);
            canvas.BlendRect(s, new Rect2I(0, 0, size, size), new Vector2I(x, y));
        }
        Put(150, 0, 96); Put(150, 106, 96); Put(170, 43, 10);
        return canvas;
    }

    /// <summary>The spinning-coin sprite sheet, redrawn as a spinning star chip with the same frame grid.</summary>
    static ImageTexture FlipSheetFor(Texture2D old, int hf, int vf)
    {
        if (_flipSheets.TryGetValue((hf, vf), out var t)) return t;
        var size = old.GetSize();
        int w = (int)size.X, h = (int)size.Y, cw = Math.Max(1, w / hf), ch = Math.Max(1, h / vf), n = hf * vf;
        var sheet = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
        int side = Math.Min(cw, ch) * 9 / 10;
        for (int k = 0; k < n; k++)
        {
            int sw = Math.Max(2, (int)(side * Math.Abs(Math.Cos(Math.PI * k / n))));
            var s = (Image)_star!.Duplicate(); s.Convert(Image.Format.Rgba8); s.Resize(sw, side, Image.Interpolation.Bilinear);
            int cx = (k % hf) * cw + (cw - sw) / 2, cy = (k / hf) * ch + (ch - side) / 2;
            sheet.BlitRect(s, new Rect2I(0, 0, sw, side), new Vector2I(cx, cy));
        }
        return _flipSheets[(hf, vf)] = ImageTexture.CreateFromImage(sheet);
    }

    static void Note(Texture2D? t, Node n)
    {
        if (!_dump || t == null) return;
        var p = t is AtlasTexture a ? $"{t.ResourcePath} (atlas {a.Atlas?.ResourcePath} {a.Region})" : t.ResourcePath;
        if (string.IsNullOrEmpty(p) || !_seen.Add(p)) return;
        try
        {
            using var f = Godot.FileAccess.Open("user://fmdump/textures.txt", Godot.FileAccess.FileExists("user://fmdump/textures.txt") ? Godot.FileAccess.ModeFlags.ReadWrite : Godot.FileAccess.ModeFlags.Write);
            if (f == null) return;
            f.SeekEnd(); f.StoreLine($"{p}	{n.GetType().Name}	{n.GetPath()}");
        }
        catch { }
    }

    static void OnNodeAdded(Node n)
    {
        if (Swap.Count == 0) return;
        SwapNode(n);
        // some screens set their textures in _Ready, after the node joined the tree
        if (n is TextureRect or Sprite2D or TextureButton) Callable.From(() => { if (GodotObject.IsInstanceValid(n)) SwapNode(n); }).CallDeferred();
    }

    static void SwapNode(Node n)
    {
        try
        {
            if (_dump)
            {
                if (n is TextureRect dr) Note(dr.Texture, n);
                else if (n is Sprite2D ds) Note(ds.Texture, n);
                else if (n is TextureButton db) Note(db.TextureNormal, n);
                else if (n is GpuParticles2D dg) Note(dg.Texture, n);
                else if (n is CpuParticles2D dc) Note(dc.Texture, n);
            }
            if (_flipId != 0 && n is Sprite2D fs && fs.Texture != null && fs.Texture.GetInstanceId() == _flipId)
            {
                fs.Texture = FlipSheetFor(fs.Texture, Math.Max(1, fs.Hframes), Math.Max(1, fs.Vframes));
                return;
            }
            if (_flipId != 0 && n is AnimatedSprite2D an && an.SpriteFrames != null) SwapFrames(an.SpriteFrames);
            switch (n)
            {
                case TextureRect r when Get(r.Texture) is { } t: r.Texture = t; break;
                case Sprite2D s when Get(s.Texture) is { } t: s.Texture = t; break;
                case TextureButton b when Get(b.TextureNormal) is { } t: b.TextureNormal = t; break;
                case GpuParticles2D g when Get(g.Texture) is { } t: g.Texture = t; break;
                case CpuParticles2D c when Get(c.Texture) is { } t: c.Texture = t; break;
            }
        }
        catch { /* never break the scene over an icon */ }
    }

    static readonly HashSet<ulong> _doneFrames = new();
    static void SwapFrames(SpriteFrames frames)
    {
        if (!_doneFrames.Add(frames.GetInstanceId())) return;
        foreach (var anim in frames.GetAnimationNames())
            for (int i = 0; i < frames.GetFrameCount(anim); i++)
                if (frames.GetFrameTexture(anim, i) is AtlasTexture at && at.Atlas != null && at.Atlas.GetInstanceId() == _flipId)
                {
                    var sz = at.Atlas.GetSize();
                    int hf = Math.Max(1, (int)Math.Round(sz.X / at.Region.Size.X)), vf = Math.Max(1, (int)Math.Round(sz.Y / at.Region.Size.Y));
                    at.Atlas = FlipSheetFor(at.Atlas, hf, vf);
                }
    }

    static Texture2D? Get(Texture2D? t) => t != null && Swap.TryGetValue(t.GetInstanceId(), out var r) ? r : null;

    // ---------------- text ----------------
    // whole words only, never inside [tags], [/tags] or {variables}
    static readonly Regex Word = new(@"(?<![\[/{A-Za-z_])(Gold|gold|GOLD|Coins|coins|Coin|coin)(?![A-Za-z_:}\]])", RegexOptions.Compiled);

    static string Rename(Match m) => m.Value switch
    {
        "Gold" or "Coins" => "Star Chips",
        "GOLD" => "STAR CHIPS",
        "gold" or "coins" => "star chips",
        "Coin" => "Star Chip",
        _ => "star chip",
    };

    static readonly AccessTools.FieldRef<LocManager, Dictionary<string, LocTable>> Tables = AccessTools.FieldRefAccess<LocManager, Dictionary<string, LocTable>>("_tables");
    static readonly AccessTools.FieldRef<LocTable, Dictionary<string, string>> Translations = AccessTools.FieldRefAccess<LocTable, Dictionary<string, string>>("_translations");

    public static void RenameCurrency(LocManager loc)
    {
        try
        {
            int n = 0;
            foreach (var table in Tables(loc).Values)
            {
                var d = Translations(table);
                foreach (var key in d.Keys.ToList())
                {
                    var v = d[key];
                    if (v == null) continue;
                    var r = Word.Replace(v, Rename);
                    if (!ReferenceEquals(r, v) && r != v) { d[key] = r; n++; }
                }
            }
            Log.Info($"[FMDuelist] Star Chips: {n} text entries renamed");
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] star chip text: " + ex.Message); }
    }
}

/// <summary>The shop's card-removal coin becomes a Star Chip with a card being cut.</summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantCardRemoval), nameof(MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantCardRemoval._Ready))]
static class CardRemovalStarChipPatch
{
    static ImageTexture? _tex;
    static void Postfix(MegaCrit.Sts2.Core.Nodes.Screens.Shops.NMerchantCardRemoval __instance)
    {
        try
        {
            var visual = __instance.GetNodeOrNull<Sprite2D>("%Visual");
            if (visual?.Texture == null) return;
            if (_tex == null)
            {
                var file = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(typeof(FmStarChips).Assembly.Location) ?? ".", "star_chip_removal.png");
                var img = Image.LoadFromFile(file);
                if (img == null) return;
                var size = visual.Texture.GetSize();
                int w = (int)size.X, h = (int)size.Y, hf = Math.Max(1, visual.Hframes), vf = Math.Max(1, visual.Vframes);
                int cw = w / hf, ch = h / vf, side = Math.Min(cw, ch);
                img.Convert(Image.Format.Rgba8); img.Resize(side, side, Image.Interpolation.Lanczos);
                var sheet = Image.CreateEmpty(w, h, false, Image.Format.Rgba8);
                for (int y = 0; y < vf; y++) for (int x = 0; x < hf; x++)
                    sheet.BlitRect(img, new Rect2I(0, 0, side, side), new Vector2I(x * cw + (cw - side) / 2, y * ch + (ch - side) / 2));
                _tex = ImageTexture.CreateFromImage(sheet);
            }
            visual.Texture = _tex;
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] removal icon: " + ex.Message); }
    }
}


/// <summary>Character select: the starting-gold coin next to the number shows a star chip.</summary>
[HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen), nameof(MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen._Ready))]
static class CharSelectStarChipPatch
{
    static void Postfix(MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect.NCharacterSelectScreen __instance)
    {
        try
        {
            var gold = __instance.GetNodeOrNull("InfoPanel/VBoxContainer/HpGoldSpacer/HpGold/Gold");
            if (gold == null) return;
            foreach (var rect in gold.FindChildren("*", "TextureRect", true, false).OfType<TextureRect>())
                if (FmStarChips.StarFor(rect.Texture) is { } star) rect.Texture = star;
            foreach (var spr in gold.FindChildren("*", "Sprite2D", true, false).OfType<Sprite2D>())
                if (FmStarChips.StarFor(spr.Texture) is { } star) spr.Texture = star;
            if (gold is TextureRect self && FmStarChips.StarFor(self.Texture) is { } s2) self.Texture = s2;
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] char select star chip: " + ex.Message); }
    }
}
