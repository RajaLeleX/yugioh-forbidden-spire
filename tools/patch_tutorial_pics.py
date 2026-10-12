import os
H = os.path.expanduser('~/fmsts')
p = f'{H}/src/FMDuelist/Game/FmTutorial.cs'
s = open(p, encoding='utf-8').read()

def rep(a, b):
    global s
    assert a in s, a[:70]
    s = s.replace(a, b, 1)

# pages understand {{img:ID:SIZE}} (card art from the disc) and {{marked}} (drawn example of a marked hand)
rep('''        var text = new RichTextLabel
        {
            Name = name, BbcodeEnabled = true, Text = bbcode, ScrollActive = true, FitContent = false,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };''', '''        var text = new RichTextLabel
        {
            Name = name, BbcodeEnabled = true, ScrollActive = true, FitContent = false,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        Fill(text, bbcode);''')
rep('''    static Button MakeButton(''', '''    static readonly System.Text.RegularExpressions.Regex Marker = new(@"\\{\\{(img:(\\d+):(\\d+)|marked)\\}\\}");

    static void Fill(RichTextLabel label, string bbcode)
    {
        int at = 0;
        foreach (System.Text.RegularExpressions.Match m in Marker.Matches(bbcode))
        {
            if (m.Index > at) label.AppendText(bbcode[at..m.Index]);
            at = m.Index + m.Length;
            try
            {
                if (m.Value == "{{marked}}") { if (MarkedExample() is { } ex) label.AddImage(ex, ex.GetWidth(), ex.GetHeight()); continue; }
                int id = int.Parse(m.Groups[2].Value), size = int.Parse(m.Groups[3].Value);
                if (FmData.Art(id) is { } t) label.AddImage(t, size * 102 / 96, size);
            }
            catch { }
        }
        if (at < bbcode.Length) label.AppendText(bbcode[at..]);
    }

    static string Pic(int id, int size) => $"{{{{img:{id}:{size}}}}}";

    // tiny 3x5 digits for the order badges
    static readonly string[] Digits = { "111101101101111", "010110010010111", "111001111100111" };
    static ImageTexture? _marked;
    static int[] _markedIds = Array.Empty<int>();

    /// <summary>Three hand cards glowing yellow with their chain numbers 1, 2, 3 in the top-right corner.</summary>
    static ImageTexture? MarkedExample()
    {
        if (_marked != null) return _marked;
        const int cw = 102, ch = 96, gap = 26, pad = 8;
        int n = Math.Min(3, _markedIds.Length);
        if (n == 0) return null;
        var img = Image.CreateEmpty(n * (cw + 2 * pad) + (n - 1) * gap, ch + 2 * pad, false, Image.Format.Rgba8);
        var glow = new Color(1f, 0.85f, 0.2f);
        for (int i = 0; i < n; i++)
        {
            var art = FmData.Art(_markedIds[i])?.GetImage();
            if (art == null) continue;
            var a = (Image)art.Duplicate(); a.Convert(Image.Format.Rgba8);
            int ox = i * (cw + 2 * pad + gap);
            for (int y = 0; y < ch + 2 * pad; y++)
                for (int x = 0; x < cw + 2 * pad; x++)
                {
                    int dx = Math.Max(0, Math.Max(pad - x, x - (cw + pad - 1))), dy = Math.Max(0, Math.Max(pad - y, y - (ch + pad - 1)));
                    float d = Mathf.Sqrt(dx * dx + dy * dy);
                    if (d > 0) { if (d <= pad) img.SetPixel(ox + x, y, new Color(glow, 1f - d / pad)); }
                }
            img.BlitRect(a, new Rect2I(0, 0, cw, ch), new Vector2I(ox + pad, pad));
            // number badge: dark circle with a yellow digit
            int bx = ox + pad + cw - 20, by = pad + 4, r = 15;
            for (int y = -r; y <= r; y++) for (int x = -r; x <= r; x++)
                if (x * x + y * y <= r * r) img.SetPixel(bx + x, by + 12 + y, x * x + y * y >= (r - 2) * (r - 2) ? glow : new Color(0.08f, 0.05f, 0.02f));
            var dg = Digits[i];
            for (int y = 0; y < 5; y++) for (int x = 0; x < 3; x++)
                if (dg[y * 3 + x] == '1')
                    img.FillRect(new Rect2I(bx - 6 + x * 4, by + 12 - 10 + y * 4, 4, 4), glow);
        }
        return _marked = ImageTexture.CreateFromImage(img);
    }

    static Button MakeButton(''')

# Fusing tab: worked examples with pictures
rep('''        tabs.AddChild(Page("Fusing", FusingText()));''', '''        tabs.AddChild(Page("Fusing", FusingText(player)));''')
rep('''    static string FusingText() => string.Join("\\n",
        $"{H}How to fuse{HE}",
        "1. [b]Right-click[/b] cards in your hand to add them to a fusion chain. Each one glows yellow and shows its order number.",''',
'''    static string FusingText(Player player)
    {
        string example = "";
        try
        {
            var deck = player.Deck.Cards.OfType<FmMonsterCard>().Select(c => c.FmId).Distinct().ToList();
            var best = Fusions(deck, player, int.MaxValue).Take(2).ToList();
            if (best.Count > 0)
            {
                _markedIds = new[] { best[0].a, best[0].b }.Concat(deck.Where(d => d != best[0].a && d != best[0].b).Take(1)).ToArray();
                _marked = null;
                var lines = new List<string> { "", $"{H}Example from your deck{HE}" };
                foreach (var f in best)
                {
                    lines.Add($"{Pic(f.a, 96)}  [font_size=34]+[/font_size]  {Pic(f.b, 96)}  [font_size=34]=[/font_size]  {Pic(f.r, 96)}");
                    lines.Add($"{FmData.Get(f.a).Name} + {FmData.Get(f.b).Name} = [color=#ffd15a]{FmData.Get(f.r).Name}[/color] ({FmData.Get(f.r).Atk}/{FmData.Get(f.r).Def})");
                    lines.Add("");
                }
                example = string.Join("\\n", lines);
            }
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] fusion example: " + ex.Message); }
        return FusingBody(example);
    }

    static string FusingBody(string example) => string.Join("\\n",
        $"{H}How to fuse{HE}",
        "1. [b]Right-click[/b] cards in your hand to add them to a fusion chain. Each one glows yellow and shows its order number.",
        _markedIds.Length > 0 ? "{{marked}}" : "",''')
rep('''        "4. When the chain is done, choose Attack or Defense for the monster that's left.",''',
    '''        "4. When the chain is done, choose Attack or Defense for the monster that's left.",
        example,''')

# cheat sheet lines get small pictures
rep('''    static List<string> BestFusions(List<int> ids, Player player, int maxAtk, int maxResults, int perMaterial = 3)
    {
        var found = new List<(int a, int b, int r)>();''', '''    static List<(int a, int b, int r)> Fusions(List<int> ids, Player player, int maxAtk)
    {
        var found = new List<(int a, int b, int r)>();
        for (int i = 0; i < ids.Count; i++)
            for (int j = i + 1; j < ids.Count; j++)
            {
                if (FmData.Fuse(ids[i], ids[j]) is not int r) continue;
                var rc = FmData.Get(r);
                if (!rc.IsMonster || !FmFusion.DragonAllowed(r, player)) continue;
                if (rc.Atk <= Math.Max(FmData.Get(ids[i]).Atk, FmData.Get(ids[j]).Atk) || rc.Atk > maxAtk) continue;
                found.Add((ids[i], ids[j], r));
            }
        return found.OrderByDescending(f => FmData.Get(f.r).Atk).ToList();
    }

    static List<string> BestFusions(List<int> ids, Player player, int maxAtk, int maxResults, int perMaterial = 3)
    {
        var found = new List<(int a, int b, int r)>();''')
rep('''            outp.Add($"- {FmData.Get(f.a).Name} + {FmData.Get(f.b).Name} = [color=#ffd15a]{r.Name}[/color] ({r.Atk}/{r.Def})");''',
    '''            outp.Add($"{Pic(f.a, 44)} + {Pic(f.b, 44)} = {Pic(f.r, 44)}   {FmData.Get(f.a).Name} + {FmData.Get(f.b).Name} = [color=#ffd15a]{r.Name}[/color] ({r.Atk}/{r.Def})");''')
open(p, 'w', encoding='utf-8').write(s)
print('ok')
