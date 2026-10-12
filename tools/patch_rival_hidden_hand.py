import os
H = os.path.expanduser('~/fmsts/src/FMDuelist')

def patch(path, pairs):
    p = f'{H}/{path}'
    s = open(p, encoding='utf-8').read()
    for a, b in pairs:
        if a not in s:
            raise SystemExit(f'missing in {path}: {a[:80]!r}')
        s = s.replace(a, b, 1)
    open(p, 'w', encoding='utf-8').write(s)

patch('Game/FmRival.cs', [
    ('''    public const int HandSize = 5;''',
     '''    /// <summary>How many cards he shows: always a hand of 5 face-down cards...</summary>
    public const int HandSize = 5;
    /// <summary>...but like FM's boss duelists he secretly chooses from a hidden pool of up to 20 cards.</summary>
    public const int HiddenHandSize = 20;
    /// <summary>Face-down cards still shown this turn (5 at the start of his turn, fewer as he plays them).</summary>
    static int _visible;'''),
    ('''        FmRivalHand.Show(Hand.Count);''', '''        _visible = Math.Min(HandSize, Hand.Count);
        FmRivalHand.Show(_visible);'''),
    ('''        while (Hand.Count < HandSize)''', '''        while (Hand.Count < HiddenHandSize)'''),
    ('''    static async Task UseCards(IReadOnlyList<int> ids, Vector2? to = null)
    {
        var used = new List<int>();
        foreach (var id in ids)
        {
            int i = Enumerable.Range(0, Hand.Count).FirstOrDefault(k => Hand[k] == id && !used.Contains(k), -1);
            if (i >= 0) used.Add(i);
        }
        if (used.Count == 0) return;
        try { await FmRivalHand.Pick(used); await FmRivalHand.Play(used, to); } catch (Exception ex) { Log.Warn("[FMDuelist] rival hand: " + ex.Message); }
        foreach (var i in used.OrderByDescending(i => i)) Hand.RemoveAt(i);
    }''', '''    static async Task UseCards(IReadOnlyList<int> ids, Vector2? to = null)
    {
        // the real cards come out of his hidden pool...
        var used = new List<int>();
        foreach (var id in ids)
        {
            int i = Enumerable.Range(0, Hand.Count).FirstOrDefault(k => Hand[k] == id && !used.Contains(k), -1);
            if (i >= 0) used.Add(i);
        }
        if (used.Count == 0) return;
        foreach (var i in used.OrderByDescending(i => i)) Hand.RemoveAt(i);
        // ...while the cursor picks that many of the 5 face-down cards on show
        var shown = Enumerable.Range(0, _visible).OrderBy(_ => Rng.Next()).Take(used.Count).OrderBy(i => i).ToList();
        if (shown.Count == 0) return;
        try { await FmRivalHand.Pick(shown); await FmRivalHand.Play(shown, to); } catch (Exception ex) { Log.Warn("[FMDuelist] rival hand: " + ex.Message); }
        _visible -= shown.Count;
    }'''),
    ('''            Draw();
            FmRivalHand.SetCount(Hand.Count);''', '''            Draw();
            _visible = Math.Min(HandSize, Hand.Count);
            FmRivalHand.SetCount(_visible);'''),
])
print('ok')
