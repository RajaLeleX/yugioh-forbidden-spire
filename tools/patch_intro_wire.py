import os, json
H = os.path.expanduser('~/fmsts')

def edit(path, pairs):
    p = f'{H}/{path}'
    s = open(p, encoding='utf-8').read()
    for a, b in pairs:
        if a not in s:
            raise SystemExit(f'missing in {path}: {a[:80]!r}')
        s = s.replace(a, b, 1)
    open(p, 'w', encoding='utf-8').write(s)

m = json.load(open(f'{H}/sheets/media.json'))
m['mainMenu'] = 0
json.dump(m, open(f'{H}/sheets/media.json', 'w'), indent=1)
edit('tools/codegen.py', [(
    "         f\"    public const int DefeatTrack = {M['defeat_YouLose']};\",",
    "         f\"    public const int DefeatTrack = {M['defeat_YouLose']};\",\n"
    "         f\"    public const int MenuTrack = {M['mainMenu']};\",")])

edit('src/FMDuelist/Game/FmMusicImpl.cs', [(
    "    public static void Play(int index)\n    {",
    "    /// <summary>Plays as soon as the disc's music bank has loaded (the menu can open before it has).</summary>\n"
    "    public static async void PlayWhenReady(int index)\n    {\n"
    "        for (int i = 0; i < 200 && FmAudio.Bgm == null; i++) await Task.Delay(50);\n"
    "        Callable.From(() => Play(index)).CallDeferred();\n    }\n\n"
    "    public static void Play(int index)\n    {")])

edit('src/FMDuelist/FmMod.cs', [("        FmAudio.StartBackgroundDecode();", "        FmAudio.StartBackgroundDecode();\n        FmIntro.Load();")])

# any click or key skips the intro
edit('src/FMDuelist/Game/FmUi.cs', [(
    "            if (!FmInput.Handle(inputEvent, __instance.GetViewport())) return true;",
    "            if (FmIntro.Playing && (inputEvent is InputEventMouseButton { Pressed: true } || inputEvent is InputEventKey { Pressed: true }))\n"
    "            {\n                FmIntro.Skip();\n                __instance.GetViewport().SetInputAsHandled();\n                return false;\n            }\n"
    "            if (!FmInput.Handle(inputEvent, __instance.GetViewport())) return true;")])

# portrait fix: the card's own portrait sits inside its own CanvasGroup, which doesn't render inside another CanvasGroup.
p = f'{H}/src/FMDuelist/Game/FmUi.cs'
s = open(p, encoding='utf-8').read()
s = s.replace('''        _group = new CanvasGroup { Material = _burn };
        _holder = new Node2D { Position = Home, Scale = Vector2.One * CardScale };
        _group.AddChild(_holder);
        _root.AddChild(_group);''', '''        _group = new Node2D();
        _holder = new Node2D { Position = Home, Scale = Vector2.One * CardScale };
        _group.AddChild(_holder);
        _root.AddChild(_group);''')
s = s.replace("    CanvasGroup? _group;", "    Node2D? _group;")
s = s.replace('''        _burn.SetShaderParameter("progress", 0.0f);
        _group.Visible = true;''', '''        _group.Visible = true;
        _group.Modulate = Colors.White;
        _holder.Scale = Vector2.One * CardScale;''')
old_burn = s[s.index('    void BurnAway()'):s.index('}', s.index('t.TweenCallback(Callable.From(() => { if (_shownId == 0) group.Visible = false; }));')) + 1]
s = s.replace(old_burn, '''    void BurnAway()
    {
        _shownId = 0;
        var group = _group!; var holder = _holder!;
        // FM-style burn: the card flares orange, chars to black and crumbles away in sparks
        var sparks = new CPUParticles2D
        {
            Position = holder.Position, Amount = 90, Lifetime = 0.9, OneShot = true, Explosiveness = 0.6f,
            EmissionShape = CPUParticles2D.EmissionShapeEnum.Rectangle, EmissionRectExtents = new Vector2(80, 115),
            Direction = new Vector2(0, -1), Spread = 35, InitialVelocityMin = 60, InitialVelocityMax = 180, Gravity = new Vector2(0, -60),
            ScaleAmountMin = 3, ScaleAmountMax = 7, Color = new Color(1f, 0.55f, 0.1f),
        };
        _root!.AddChild(sparks);
        sparks.Emitting = true;
        var t = _root.CreateTween();
        t.TweenProperty(group, "modulate", new Color(2.2f, 1.2f, 0.35f, 1f), 0.15);
        t.TweenProperty(group, "modulate", new Color(0.25f, 0.08f, 0.02f, 1f), 0.45);
        t.Parallel().TweenProperty(holder, "scale", Vector2.One * CardScale * 0.92f, 0.45);
        t.TweenProperty(group, "modulate:a", 0f, 0.35);
        t.TweenCallback(Callable.From(() => { if (_shownId == 0) group.Visible = false; }));
        t.TweenInterval(1.0);
        t.TweenCallback(Callable.From(sparks.QueueFree));
    }''')
open(p, 'w', encoding='utf-8').write(s)

sy = f'{H}/sheets/systems.json'
rows = json.load(open(sy))
for r in [{"id": "intro_fmv", "what": "After the company logos, MOVIE.STR movie 1 plays (MDEC v2 + XA, decoded from the disc); click/key skips", "value": 1, "hook": "music"},
          {"id": "menu_backdrop", "what": "Main menu backdrop = intro frame 255 (FM logo on the flaming stone wall); menu buttons unchanged; menu music track 00", "value": 0, "hook": "music"}]:
    if not any(x['id'] == r['id'] for x in rows): rows.append(r)
json.dump(rows, open(sy, 'w'), indent=1)
print('ok')
