using System.Collections.Concurrent;
using System.Text.Json;
using FMDuelist.Audio;
using FMDuelist.Data;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;

namespace FMDuelist.Game;

/// <summary>Plays FM music rendered from the player's disc during combat (track choice from sheets/media.json).</summary>
public static partial class FmMusic
{
    private static AudioStreamPlayer? _player;
    private static int _playing = -1;
    public static bool IsPlaying => _playing >= 0;
    private static readonly ConcurrentDictionary<int, (short[] pcm, int loop, int end)> _cache = new();
    private static readonly Random _rng = new();
    private static int _battleTrack = -1, _battleTrackFloor = -99;

    static partial void OnRoomEnteredImpl(AbstractRoom room, Player owner)
    {
        if (!FmRules.IsFmPlayer(owner) || FmAudio.Bgm == null) return;
        // the Free Duel with Duel Master K: silent until the duel starts, then track 39 on a loop
        if (FmPractice.Active) { if (room.RoomType == RoomType.Monster) Play(FmPractice.MusicTrack); else Stop(); return; }
        if (room is RestSiteRoom) { Play(FmMedia.RestSiteTrack); return; }
        // Simon Muran's duel plays his own theme (FM track 12)
        if (room is CombatRoom duel && FmStoryAct1.Duels.FirstOrDefault(d => d.Encounter == duel.Encounter?.GetType()) is { Key: "simon" }) { Play(12); return; }
        // each act's starting room (the Ancient before floor 1)
        if (room.RoomType == RoomType.Event && owner.RunState.ActFloor == 0)
        {
            int a = owner.RunState.CurrentActIndex;
            if (a >= 0 && a < FmMedia.ActStartTracks.Length && FmMedia.ActStartTracks[a] >= 0) { Play(FmMedia.ActStartTracks[a]); return; }
        }
        if (room is not CombatRoom)
        {
            // the act's map theme also plays in events, shops and treasure rooms
            int ma = owner.RunState.CurrentActIndex;
            if (room.RoomType == RoomType.Shop && ma >= 0 && ma < FmMedia.ActShopTracks.Length && FmMedia.ActShopTracks[ma] >= 0) { Play(FmMedia.ActShopTracks[ma]); return; }
            if (ma >= 0 && ma < FmMedia.ActMapTracks.Length && FmMedia.ActMapTracks[ma] >= 0) Play(FmMedia.ActMapTracks[ma]);
            else Stop();
            return;
        }
        int act = Math.Clamp(owner.RunState.CurrentActIndex, 0, 2);
        if (room.RoomType == RoomType.Boss) { Play(FmMedia.BossTracks[act]); return; }
        if (room.RoomType == RoomType.Elite) { Play(PickElite(act)); return; }
        // Act 1 opens with the same track for its first few normal fights
        if (act == 0 && room.RoomType == RoomType.Monster && FmMedia.Act1OpeningTrack >= 0)
        {
            var hist = owner.RunState.MapPointHistory;
            int fights = hist.Count > 0 ? hist[0].Count(e => e.Rooms.Any(r => r.RoomType == RoomType.Monster)) : 0;
            if (fights <= FmMedia.Act1OpeningFights) { Play(FmMedia.Act1OpeningTrack); return; }
        }
        // normal and elite fights: a random FM track, changed only every BattleTrackFloors floors
        int floor = owner.RunState.TotalFloor;
        if (_battleTrack < 0 || floor - _battleTrackFloor >= FmMedia.BattleTrackFloors || floor < _battleTrackFloor)
        {
            _battleTrack = FmMedia.BattleRandomPool[_rng.Next(FmMedia.BattleRandomPool.Length)];
            _battleTrackFloor = floor;
        }
        Play(_battleTrack);
    }

    /// <summary>Weighted pick from the act's elite themes (its own theme most likely).</summary>
    static int PickElite(int act)
    {
        var tracks = FmMedia.EliteTracks[Math.Min(act, FmMedia.EliteTracks.Length - 1)];
        var weights = FmMedia.EliteWeights[Math.Min(act, FmMedia.EliteWeights.Length - 1)];
        double roll = _rng.NextDouble() * weights.Sum();
        for (int i = 0; i < tracks.Length; i++) { roll -= weights[i]; if (roll < 0) return tracks[i]; }
        return tracks[0];
    }

    /// <summary>Plays a track once (no loop), e.g. the act-boss victory fanfare.</summary>
    public static void PlayOnce(int index)
    {
        _playing = -1;
        _once = true;
        Play(index);
    }

    static bool _once;

    /// <summary>Plays one track once, then (after it finishes and a gap) another track once.</summary>
    public static async void PlayOnceThen(int first, int second, double gapSeconds)
    {
        PlayOnce(first);
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        int target = first;
        // wait for the first track to start, then to end
        for (int i = 0; i < 200 && !(_playing == target && _player != null && GodotObject.IsInstanceValid(_player) && _player.Playing); i++)
            await tree.ToSignal(tree.CreateTimer(0.05), SceneTreeTimer.SignalName.Timeout);
        while (_playing == target && _player != null && GodotObject.IsInstanceValid(_player) && _player.Playing)
            await tree.ToSignal(tree.CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
        if (_playing != target) return;   // something else took over
        await tree.ToSignal(tree.CreateTimer(gapSeconds), SceneTreeTimer.SignalName.Timeout);
        if (_playing == target) PlayOnce(second);
    }

    /// <summary>Plays one track once, then loops another as soon as it finishes (e.g. the win jingle, then the results music).</summary>
    public static async void PlayOnceThenLoop(int first, int second)
    {
        PlayOnce(first);
        if (Engine.GetMainLoop() is not SceneTree tree) return;
        int target = first;
        for (int i = 0; i < 200 && !(_playing == target && _player != null && GodotObject.IsInstanceValid(_player) && _player.Playing); i++)
            await tree.ToSignal(tree.CreateTimer(0.05), SceneTreeTimer.SignalName.Timeout);
        while (_playing == target && _player != null && GodotObject.IsInstanceValid(_player) && _player.Playing)
            await tree.ToSignal(tree.CreateTimer(0.1), SceneTreeTimer.SignalName.Timeout);
        if (_playing == target) { _once = false; Play(second); }
    }

    /// <summary>Plays as soon as the disc's music bank has loaded (the menu can open before it has).</summary>
    public static async void PlayWhenReady(int index)
    {
        for (int i = 0; i < 200 && FmAudio.Bgm == null; i++) await Task.Delay(50);
        Callable.From(() => Play(index)).CallDeferred();
    }

    public static void Play(int index)
    {
        var bgm = FmAudio.Bgm;
        if (bgm == null || index == _playing) return;
        _playing = index;
        bool once = _once;
        _once = false;
        Task.Run(() =>
        {
            try
            {
                var r = _cache.GetOrAdd(index, i => i >= FileTrackBase ? FileTrack(bgm, i) : FmMusicBank.Render(bgm, i) ?? (Array.Empty<short>(), -1, -1));
                if (r.pcm.Length == 0) return;
                var pcm = once ? r.pcm : Seamless(r.pcm, r.loop, r.end);
                Log.Info($"[FMDuelist] track {index}: loop {r.loop}..{r.end} of {r.pcm.Length / 2} frames");
                Callable.From(() => StartOnMainThread(index, pcm, once ? -2 : r.loop)).CallDeferred();
            }
            catch (Exception ex) { Log.Warn("[FMDuelist] music render: " + ex.Message); }
        });
    }

    /// <summary>Tracks from 57 up aren't in the disc's music bank: they're local files next to the mod (never shipped).</summary>
    public const int FileTrackBase = 57;

    /// <summary>The mod's own copy of a local track exists (e.g. track 57, FM's Duel Results theme).</summary>
    public static bool HasFileTrack(int index) => File.Exists(FileTrackPath(index));

    static string FileTrackPath(int index) =>
        Path.Combine(Path.GetDirectoryName(typeof(FmMusic).Assembly.Location) ?? ".", "music", $"track{index}.pcm");

    /// <summary>
    /// A local track: 16-bit stereo PCM at the synth's rate, one exact loop of the song. Its loudness is matched to
    /// FM's own music (the disc's Results of Duel track) so it sits at the same volume as everything else.
    /// </summary>
    static (short[] pcm, int loop, int end) FileTrack(byte[] bgm, int index)
    {
        var path = FileTrackPath(index);
        if (!File.Exists(path)) return (Array.Empty<short>(), -1, -1);
        var bytes = File.ReadAllBytes(path);
        var pcm = new short[bytes.Length / 2];
        Buffer.BlockCopy(bytes, 0, pcm, 0, pcm.Length * 2);
        var reference = _cache.GetOrAdd(39, i => FmMusicBank.Render(bgm, i) ?? (Array.Empty<short>(), -1, -1));
        double Rms(short[] p) { double s = 0; for (int i = 0; i < p.Length; i += 7) s += (double)p[i] * p[i]; return Math.Sqrt(s / Math.Max(1, p.Length / 7)); }
        double gain = reference.pcm.Length > 0 ? Math.Clamp(Rms(reference.pcm) / Math.Max(1, Rms(pcm)), 0.2, 3.0) : 1.0;
        for (int i = 0; i < pcm.Length; i++) pcm[i] = (short)Math.Clamp(pcm[i] * gain, short.MinValue, short.MaxValue);
        Log.Info($"[FMDuelist] track {index}: local file, {pcm.Length / 2} frames, volume x{gain:0.00} to match the disc's music");
        return (pcm, 0, pcm.Length / 2);
    }

    /// <summary>
    /// A looping copy that ends exactly at the song's loop-end point, with the notes still ringing after it (their
    /// release tail) mixed into the loop start, so the jump back is seamless instead of fading out first.
    /// </summary>
    static short[] Seamless(short[] pcm, int loopStart, int loopEnd)
    {
        int frames = pcm.Length / 2, ls = Math.Max(0, loopStart);
        if (loopEnd <= ls || loopEnd >= frames) return pcm;
        var o = new short[loopEnd * 2];
        Array.Copy(pcm, o, o.Length);
        int tail = Math.Min(frames - loopEnd, loopEnd - ls);
        for (int i = 0; i < tail * 2; i++) o[ls * 2 + i] = (short)Math.Clamp(o[ls * 2 + i] + pcm[loopEnd * 2 + i], short.MinValue, short.MaxValue);
        return o;
    }

    private static void StartOnMainThread(int index, short[] pcm, int loop)
    {
        if (index != _playing || Engine.GetMainLoop() is not SceneTree tree) return;
        NRunMusicController.Instance?.StopMusic();
        var bytes = new byte[pcm.Length * 2];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        int frames = pcm.Length / 2;
        var wav = new AudioStreamWav
        {
            Format = AudioStreamWav.FormatEnum.Format16Bits, Stereo = true, MixRate = PsxSeqSynth.Rate, Data = bytes,
            LoopMode = loop == -2 ? AudioStreamWav.LoopModeEnum.Disabled : AudioStreamWav.LoopModeEnum.Forward, LoopBegin = Math.Max(0, loop), LoopEnd = frames,
        };
        if (_player == null || !GodotObject.IsInstanceValid(_player))
        {
            _player = new AudioStreamPlayer { Name = "FMDuelistMusic" };
            tree.Root.AddChild(_player);
        }
        _player.Stream = wav;
        _player.VolumeDb = VolumeDb();
        _player.Play();
        Log.Info($"[FMDuelist] Playing FM track {index}");
    }

    private static float VolumeDb()
    {
        var s = SaveManager.Instance?.SettingsSave;
        float lin = s == null ? 0.25f : s.VolumeBgm * s.VolumeMaster;
        return Mathf.LinearToDb(Mathf.Max(lin, 0.0001f));
    }

    public static void Stop()
    {
        if (_playing < 0) return;
        _playing = -1;
        if (_player != null && GodotObject.IsInstanceValid(_player)) _player.Stop();
        NRunMusicController.Instance?.StopCustomMusic();
    }
}

public static partial class FmAudio
{
    public static byte[]? Bgm { get; private set; }
    public static bool SfxReady;

    static AudioStreamWav ToStream(short[] pcm)
    {
        var bytes = new byte[pcm.Length * 2];
        Buffer.BlockCopy(pcm, 0, bytes, 0, bytes.Length);
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = FmSfxBank.Rate, Data = bytes };
    }

    static partial void StartImpl()
    {
        Task.Run(() =>
        {
            try
            {
                if (FmData.DiscPath == null) return;
                using var disc = new FmDisc(FmData.DiscPath);
                Bgm = disc.ReadFile("/DATA/SD_BGM.DAT");
                var se = FmSfxBank.Decode(disc.ReadFile("/DATA/SD_SE.DAT"));
                Callable.From(() =>
                {
                    foreach (var (key, idx) in FmMedia.Sfx)
                        if (idx >= 0 && idx < se.Count) FmSfx.Disc[key] = ToStream(se[idx]);
                    SfxReady = true;
                    Log.Info($"[FMDuelist] Sound effects loaded: {se.Count} samples, {FmSfx.Disc.Count} mapped");
                }).CallDeferred();
                Log.Info($"[FMDuelist] Music bank loaded: {FmMusicBank.Count(Bgm)} tracks");
            }
            catch (Exception ex) { Log.Warn("[FMDuelist] music bank: " + ex.Message); }
        });
    }
}
