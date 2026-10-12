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
m['sfx'] = {"mark": 2, "slash": 8, "destroy": 16}
m['sfxNote'] = "Indices into the 23 decoded SD_SE.DAT samples (fm_sfx/sfx_NNN.mp3); defaults chosen by sound profile until confirmed."
json.dump(m, open(f'{H}/sheets/media.json', 'w'), indent=1)

edit('tools/codegen.py', [(
    "f\"    public static readonly int[] BossTracks = {{ {M['bossAct1_Seto']}, {M['bossAct2_KaibaTournamentFinal']}, {M['bossAct3_3DDuelTournamentFinal']} }};\", '}', '']",
    "f\"    public static readonly int[] BossTracks = {{ {M['bossAct1_Seto']}, {M['bossAct2_KaibaTournamentFinal']}, {M['bossAct3_3DDuelTournamentFinal']} }};\",\n"
    "         '    public static readonly Dictionary<string, int> Sfx = new() { ' + ', '.join(f'[\"{k}\"] = {v}' for k, v in M['sfx'].items()) + ' };', '}', '']")])

edit('src/FMDuelist/Game/FmFx.cs', [
    ('Counter = "counter", Draw = "draw", Fail = "fail";', 'Counter = "counter", Draw = "draw", Fail = "fail", Slash = "slash";'),
    ('        [Fail] = "event:/sfx/characters/osty/osty_attack",', '        [Fail] = "event:/sfx/characters/osty/osty_attack",\n        [Slash] = "event:/sfx/characters/osty/osty_attack",'),
    ('            if (key == Mark && !Disc.ContainsKey(Mark)) Disc[Mark] = MakeBeep();', '            if (key == Mark && !Disc.ContainsKey(Mark) && FmAudio.SfxReady) Disc[Mark] = MakeBeep();'),
])

edit('src/FMDuelist/Game/FmMusicImpl.cs', [(
    '''                Bgm = disc.ReadFile("/DATA/SD_BGM.DAT");''',
    '''                Bgm = disc.ReadFile("/DATA/SD_BGM.DAT");
                var se = FmSfxBank.Decode(disc.ReadFile("/DATA/SD_SE.DAT"));
                Callable.From(() =>
                {
                    foreach (var (key, idx) in FmMedia.Sfx)
                        if (idx >= 0 && idx < se.Count) FmSfx.Disc[key] = ToStream(se[idx]);
                    SfxReady = true;
                    Log.Info($"[FMDuelist] Sound effects loaded: {se.Count} samples, {FmSfx.Disc.Count} mapped");
                }).CallDeferred();'''),
    ('    public static byte[]? Bgm { get; private set; }',
     '    public static byte[]? Bgm { get; private set; }\n    public static bool SfxReady;\n\n'
     '    static AudioStreamWav ToStream(short[] pcm)\n    {\n        var bytes = new byte[pcm.Length * 2];\n        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);\n'
     '        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = FmSfxBank.Rate, Data = bytes };\n    }'),
])

edit('src/FMDuelist/Game/FmField.cs', [(
    '''        await DamageCmd.Attack(Atk).FromOsty(osty, SourceCard).Targeting(target).WithNoAttackerAnim()
            .WithHitFx("vfx/vfx_attack_slash", null, "blunt_attack.mp3").Execute(ctx);''',
    '''        if (MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance?.GetCreatureNode(target) is { } hit)
            FmSlashVfx.Play(hit.Hitbox.GetGlobalRect().GetCenter());
        FmSfx.Play(FmSfx.Slash);
        await DamageCmd.Attack(Atk).FromOsty(osty, SourceCard).Targeting(target).WithNoAttackerAnim().Execute(ctx);''')])

# counter-attacks get the slash too
edit('src/FMDuelist/Relics/MillenniumPuzzle.cs', [(
    '''            if (attacker.IsAlive)
            {
                FmSfx.Play(FmSfx.Counter);''',
    '''            if (attacker.IsAlive)
            {
                FmSfx.Play(FmSfx.Slash);
                if (MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance?.GetCreatureNode(attacker) is { } an) FmSlashVfx.Play(an.Hitbox.GetGlobalRect().GetCenter());''')])
print('ok')
