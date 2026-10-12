import os
H = os.path.expanduser('~/fmsts')
p = f'{H}/src/FMDuelist/Game/FmZone.cs'
s = open(p, encoding='utf-8').read()
pairs = [
    ('        foreach (var e in FmField.Equips) entries.Add((e, false, "EQUIP"));',
     '        foreach (var z in FmField.Slots)\n            foreach (var e in z.Equips) entries.Add((e, false, $"EQUIP {z.Index + 1}"));'),
    ('        if (hover < 0 && FmField.MonsterId != 0 && FmOstyVisual.IsMouseOver(mouse)) { hover = FmField.MonsterId; note = "MONSTER"; }',
     '        _hoverZone = FmField.Slots.LastOrDefault(z => z.Alive(player) && z.Visual.IsMouseOver(mouse));\n'
     '        if (hover < 0 && _hoverZone != null) { hover = _hoverZone.MonsterId; note = "MONSTER"; }'),
    ('    static int _previewId = -1;', '    static int _previewId = -1;\n    static FmSlot? _hoverZone;'),
    ('            string extra = note == "MONSTER"\n'
     '                ? $"{(FmField.Position == FmPosition.Attack ? "Attack" : "Defense")} position\\nATK {FmField.Atk} / DEF {FmField.Def}" +\n'
     '                  (FmField.Equips.Count > 0 ? "\\nEquips: " + string.Join(", ", FmField.Equips.Select(e => FmData.Get(e).Name)) : "")\n'
     '                : note;',
     '            var hz = _hoverZone;\n'
     '            string extra = note == "MONSTER" && hz != null\n'
     '                ? $"Zone {hz.Index + 1} - {(hz.Position == FmPosition.Attack ? "Attack" : "Defense")} position\\nATK {hz.Atk} / DEF {hz.Def}" +\n'
     '                  (hz.Equips.Count > 0 ? "\\nEquips: " + string.Join(", ", hz.Equips.Select(e => FmData.Get(e).Name)) : "")\n'
     '                : note;'),
]
for a, b in pairs:
    if a not in s: raise SystemExit('missing: ' + a[:70])
    s = s.replace(a, b, 1)
open(p, 'w', encoding='utf-8').write(s)
print('ok')
