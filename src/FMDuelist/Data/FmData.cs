using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace FMDuelist.Data;

public enum FmType { Dragon, Spellcaster, Zombie, Warrior, BeastWarrior, Beast, WingedBeast, Fiend, Fairy, Insect, Dinosaur, Reptile, Fish, SeaSerpent, Machine, Thunder, Aqua, Pyro, Rock, Plant, Magic, Trap, Ritual, Equip }

public sealed class FmCardInfo
{
    public int Id;
    public string Name = "";
    public string Desc = "";
    public int Atk, Def, Level, Attribute, GuardianStar1, GuardianStar2;
    public FmType Type;
    public bool IsMonster => Type <= FmType.Plant;
}

/// <summary>Everything the mod reads from the player's own Forbidden Memories disc.</summary>
public static class FmData
{
    public const int CardCount = 722;
    public static readonly FmCardInfo[] Cards = new FmCardInfo[CardCount + 1];
    public static readonly Dictionary<long, int> Fusions = new();
    public static readonly Dictionary<int, HashSet<int>> EquipTargets = new();
    public static bool Loaded { get; private set; }
    public static string? Error { get; private set; }
    public static string? DiscPath { get; private set; }
    private static byte[]? _wa;
    private static readonly Dictionary<int, Texture2D> _art = new();

    public static readonly string[] TypeNames = { "Dragon", "Spellcaster", "Zombie", "Warrior", "Beast-Warrior", "Beast", "Winged Beast", "Fiend", "Fairy", "Insect", "Dinosaur", "Reptile", "Fish", "Sea Serpent", "Machine", "Thunder", "Aqua", "Pyro", "Rock", "Plant", "Magic", "Trap", "Ritual", "Equip" };

    public static string? FindDiscPath()
    {
        string appdata = System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData);
        var candidates = new List<string>();
        string cfg = Path.Combine(appdata, "FMDuelist", "settings.cfg");
        string modCfg = Path.Combine(Path.GetDirectoryName(typeof(FmData).Assembly.Location) ?? ".", "settings.cfg");
        foreach (var file in new[] { cfg, modCfg })
        if (File.Exists(file))
            foreach (var line in File.ReadAllLines(file))
            {
                int eq = line.IndexOf('=');
                if (eq > 0 && line[..eq].Trim() == "FM_DISC") candidates.Add(line[(eq + 1)..].Trim().Trim('"'));
            }
        var env = System.Environment.GetEnvironmentVariable("FMDUELIST_DISC");
        if (!string.IsNullOrEmpty(env)) candidates.Add(env);
        Log.Info($"[FMDuelist] settings {cfg} exists={File.Exists(cfg)} candidates: {string.Join(" | ", candidates.Select(c => c + " -> " + File.Exists(c)))}");
        foreach (var c in candidates)
        {
            if (File.Exists(c)) return c;
            if (Directory.Exists(c))
            {
                var f = Directory.EnumerateFiles(c).FirstOrDefault(p => p.EndsWith(".bin", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".iso", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".img", StringComparison.OrdinalIgnoreCase));
                if (f != null) return f;
            }
        }
        return null;
    }

    public static void Load()
    {
        try
        {
            DiscPath = FindDiscPath() ?? throw new FileNotFoundException("Forbidden Memories disc image not found (set FM_DISC in %APPDATA%/FMDuelist/settings.cfg)");
            using var disc = new FmDisc(DiscPath);
            if (!disc.Has("/SLUS_014.11")) throw new InvalidDataException("This disc is not Yu-Gi-Oh! Forbidden Memories (NTSC-U, SLUS-01411)");
            var slus = _slus = disc.ReadFile("/SLUS_014.11");
            _wa = disc.ReadFile("/DATA/WA_MRG.MRG");
            for (int i = 0; i < CardCount; i++)
            {
                uint v = BitConverter.ToUInt32(slus, 0x1C4A44 + i * 4);
                byte la = slus[0x1C5B33 + i];
                int np = BitConverter.ToUInt16(slus, 0x1C6002 + i * 2);
                int dp = BitConverter.ToUInt16(slus, 0x1B0A02 + i * 2);
                Cards[i + 1] = new FmCardInfo
                {
                    Id = i + 1,
                    Atk = (int)(v & 0x1FF) * 10,
                    Def = (int)((v >> 9) & 0x1FF) * 10,
                    GuardianStar2 = (int)((v >> 18) & 0xF),
                    GuardianStar1 = (int)((v >> 22) & 0xF),
                    Type = (FmType)Math.Min((int)((v >> 26) & 0x1F), (int)FmType.Equip),
                    Level = la & 0xF,
                    Attribute = la >> 4,
                    Name = FmText.Read(slus, 0x1C6800 + np - 0x6000),
                    Desc = FmText.Read(slus, 0x1B11F4 + dp - 0x9F4),
                };
            }
            // Fusion table (WA_MRG 0xB87800), format documented by fmscrambler.
            const int fb = 0xB87800;
            for (int i = 0; i < CardCount; i++)
            {
                int p = BitConverter.ToUInt16(_wa, fb + 2 + i * 2);
                if (p == 0) continue;
                p += fb;
                int n = _wa[p++];
                if (n == 0) n = 511 - _wa[p++];
                while (n > 0)
                {
                    int a = _wa[p], b = _wa[p + 1], c = _wa[p + 2], d = _wa[p + 3], e = _wa[p + 4];
                    p += 5;
                    AddFusion(i + 1, ((a & 3) << 8) | b, (((a >> 2) & 3) << 8) | c); n--;
                    if (n > 0) { AddFusion(i + 1, (((a >> 4) & 3) << 8) | d, (((a >> 6) & 3) << 8) | e); n--; }
                }
            }
            int q = 12079104;
            while (true)
            {
                int eid = BitConverter.ToUInt16(_wa, q); q += 2;
                if (eid == 0) break;
                int cnt = BitConverter.ToUInt16(_wa, q); q += 2;
                var set = new HashSet<int>();
                for (int k = 0; k < cnt; k++) { set.Add(BitConverter.ToUInt16(_wa, q)); q += 2; }
                EquipTargets[eid] = set;
            }
            Loaded = true;
            Log.Info($"[FMDuelist] Loaded Forbidden Memories: {CardCount} cards, {Fusions.Count / 2} fusions, {EquipTargets.Count} equips from {DiscPath}");
        }
        catch (Exception ex)
        {
            Error = ex.Message;
            Log.Error("[FMDuelist] " + ex);
        }
    }

    private static void AddFusion(int a, int b, int r)
    {
        Fusions.TryAdd(Key(a, b), r);
        Fusions.TryAdd(Key(b, a), r);
    }

    public static long Key(int a, int b) => ((long)a << 16) | (uint)b;
    public static int? Fuse(int a, int b) => Fusions.TryGetValue(Key(a, b), out int r) ? r : null;
    public static bool CanEquip(int equipId, int monsterId) => equipId == 657 || (EquipTargets.TryGetValue(equipId, out var s) && s.Contains(monsterId));

    public static FmCardInfo Get(int id) => Loaded && id >= 1 && id <= CardCount ? Cards[id] : Placeholder(id);

    private static FmCardInfo Placeholder(int id) => new() { Id = id, Name = $"FM Card #{id}", Desc = "Forbidden Memories disc not found.", Type = FmType.Magic };

    /// <summary>Card art (102x96, 8bpp + 15-bit palette) from WA_MRG.MRG.</summary>
    public static Texture2D? Art(int id)
    {
        if (_wa == null || id < 1 || id > CardCount) return null;
        if (_art.TryGetValue(id, out var t)) return t;
        const int w = 102, h = 96;
        int o = 0x169000 + (id - 1) * 0x3800;
        var rgba = new byte[w * h * 4];
        for (int k = 0; k < w * h; k++)
        {
            int c = BitConverter.ToUInt16(_wa, o + w * h + _wa[o + k] * 2);
            rgba[k * 4] = (byte)((c & 31) << 3);
            rgba[k * 4 + 1] = (byte)(((c >> 5) & 31) << 3);
            rgba[k * 4 + 2] = (byte)(((c >> 10) & 31) << 3);
            rgba[k * 4 + 3] = 255;
        }
        var img = Image.CreateFromData(w, h, false, Image.Format.Rgba8, rgba);
        t = ImageTexture.CreateFromImage(img);
        _art[id] = t;
        return t;
    }

    public static byte[]? Wa => _wa;

    private static byte[]? _slus;
    /// <summary>The game's executable (card stats, text and the story script).</summary>
    public static byte[]? Slus => _slus;
}
