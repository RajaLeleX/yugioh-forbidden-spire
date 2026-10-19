using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace FMDuelist.Game;

/// <summary>Sound effects: FM's own sounds decoded from the disc when available, otherwise a close game sound.</summary>
public static class FmSfx
{
    public const string Mark = "mark", Fusion = "fusion", Summon = "summon", Flip = "flip", Destroy = "destroy", Trap = "trap", PowerUp = "powerup", Counter = "counter", Draw = "draw", Fail = "fail", Slash = "slash";

    private static readonly Dictionary<string, string> Fallback = new()
    {
        [Mark] = "event:/sfx/ui/clicks/ui_click",
        [Fusion] = "event:/sfx/characters/necrobinder/necrobinder_summon",
        [Summon] = "event:/sfx/characters/necrobinder/necrobinder_summon",
        [Flip] = "event:/sfx/ui/clicks/ui_click",
        [Destroy] = "event:/sfx/characters/osty/osty_die",
        [Trap] = "event:/sfx/ui/relic_activate_general",
        [PowerUp] = "event:/sfx/ui/relic_activate_general",
        [Counter] = "event:/sfx/characters/osty/osty_attack",
        [Draw] = "event:/sfx/ui/clicks/ui_click",
        [Fail] = "event:/sfx/characters/osty/osty_attack",
        [Slash] = "event:/sfx/characters/osty/osty_attack",
    };

    /// <summary>Filled by FmAudio when the disc's sound bank decodes.</summary>
    public static readonly Dictionary<string, AudioStream> Disc = new();
    private static AudioStreamPlayer? _player;

    /// <summary>Short square-wave pick beep in the style of FM's fusion-mark sound (used until the disc's own SFX decode).</summary>
    static AudioStreamWav MakeBeep()
    {
        const int rate = 22050; int n = rate * 70 / 1000;
        var data = new byte[n * 2];
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / rate, env = Math.Min(1, (n - i) / (rate * 0.02));
            double f = i < n / 2 ? 1568 : 2093;
            short v = (short)((Math.Sin(2 * Math.PI * f * t) >= 0 ? 1 : -1) * 6000 * env);
            data[i * 2] = (byte)v; data[i * 2 + 1] = (byte)(v >> 8);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, MixRate = rate, Data = data };
    }

    static AudioStreamWav? _thunder;

    /// <summary>A synthesized thunderclap: a sharp crackling strike, then a long low rolling rumble (stereo).</summary>
    static AudioStreamWav MakeThunder()
    {
        const int rate = 44100; int n = (int)(rate * 2.6);
        var rng = new Random(7);
        var data = new byte[n * 4];
        double lp1 = 0, lp2 = 0, hp = 0, prev = 0, rumbleL = 0, rumbleR = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / rate;
            double w = rng.NextDouble() * 2 - 1;
            // crack: bright noise with a few rapid re-strikes in the first 0.3 s
            double strikes = Math.Exp(-t * 18) + 0.6 * Math.Exp(-Math.Max(0, t - 0.07) * 25) * (t > 0.07 ? 1 : 0) + 0.4 * Math.Exp(-Math.Max(0, t - 0.16) * 22) * (t > 0.16 ? 1 : 0);
            hp = 0.85 * (hp + w - prev); prev = w;
            double crack = hp * strikes;
            // rumble: twice low-passed noise, swelling then fading, with slow wobble
            lp1 += (w - lp1) * 0.02; lp2 += (lp1 - lp2) * 0.02;
            double swell = Math.Min(1, t / 0.12) * Math.Exp(-t * 1.25) * (0.75 + 0.25 * Math.Sin(t * 9 + Math.Sin(t * 3)));
            rumbleL += (lp2 * 9 - rumbleL) * 0.5; rumbleR += (lp2 * 9 * (0.9 + 0.1 * Math.Sin(t * 5)) - rumbleR) * 0.5;
            double l = crack * 0.9 + rumbleL * swell * 1.6, r = crack * 0.8 + rumbleR * swell * 1.6;
            short sl = (short)Math.Clamp(l * 22000, -32767, 32767), sr = (short)Math.Clamp(r * 22000, -32767, 32767);
            data[i * 4] = (byte)sl; data[i * 4 + 1] = (byte)(sl >> 8); data[i * 4 + 2] = (byte)sr; data[i * 4 + 3] = (byte)(sr >> 8);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, Stereo = true, MixRate = rate, Data = data };
    }

    static AudioStreamWav? _summon;
    static AudioStreamPlayer? _summonPlayer;

    /// <summary>
    /// Synthesized summon: a rising magical sweep while the card drops (0.38 s), then on landing a low impact thump and
    /// a bright bell chime that rings out.
    /// </summary>
    static AudioStreamWav MakeSummon()
    {
        const int rate = 44100; const double land = 0.38; int n = (int)(rate * 1.6);
        var rng = new Random(11);
        var data = new byte[n * 4];
        double phase = 0, lp = 0, bp = 0, sh = 0;
        double[] bell = { 1.0, 2.76, 5.40, 8.93 }, bellAmp = { 0.55, 0.3, 0.18, 0.08 };
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / rate, l = 0, r = 0;
            if (t < land + 0.05)
            {
                // sweep: a gliding tone (220 -> 1320 Hz) plus airy filtered noise, swelling into the landing
                double u = Math.Min(1, t / land);
                double f = 220 * Math.Pow(6, u);
                phase += 2 * Math.PI * f / rate;
                double tone = Math.Sin(phase) * 0.35 + Math.Sin(phase * 2.01) * 0.12;
                double w = rng.NextDouble() * 2 - 1;
                lp += (w - lp) * (0.05 + 0.4 * u); bp = lp - sh; sh += bp * 0.1;
                double env = u * u * (t > land ? Math.Max(0, 1 - (t - land) / 0.05) : 1);
                l += (tone + bp * 1.4) * env * 0.6; r += (tone * 0.9 + bp * 1.5) * env * 0.6;
            }
            if (t >= land)
            {
                double d = t - land;
                // impact thump
                double thump = Math.Sin(2 * Math.PI * (90 - 40 * Math.Min(1, d * 6)) * d) * Math.Exp(-d * 14) * 0.9;
                // bell chime (inharmonic partials of 784 Hz)
                double chime = 0;
                for (int k = 0; k < bell.Length; k++) chime += Math.Sin(2 * Math.PI * 784 * bell[k] * d) * bellAmp[k] * Math.Exp(-d * (2.2 + k * 1.8));
                l += thump + chime * 0.8; r += thump + chime * 0.75 * (1 + 0.1 * Math.Sin(d * 30));
            }
            short sl = (short)Math.Clamp(l * 20000, -32767, 32767), sr = (short)Math.Clamp(r * 20000, -32767, 32767);
            data[i * 4] = (byte)sl; data[i * 4 + 1] = (byte)(sl >> 8); data[i * 4 + 2] = (byte)sr; data[i * 4 + 3] = (byte)(sr >> 8);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, Stereo = true, MixRate = rate, Data = data };
    }

    /// <summary>The summon sound (timed to the summon animation: sweep during the drop, chime on landing).</summary>
    public static void SummonSound()
    {
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree) return;
            _summon ??= MakeSummon();
            if (_summonPlayer == null || !GodotObject.IsInstanceValid(_summonPlayer))
            {
                _summonPlayer = new AudioStreamPlayer { MaxPolyphony = 3, Bus = "Master", VolumeDb = -3 };
                tree.Root.AddChild(_summonPlayer);
            }
            _summonPlayer.Stream = _summon;
            _summonPlayer.Play();
        }
        catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] summon sound: " + ex.Message); }
    }

    static AudioStreamWav? _equip;
    static AudioStreamPlayer? _equipPlayer;

    /// <summary>Synthesized equip: a soft descending whoosh as the card spins in (0.7 s), then a bright merge "ding" and a rising sparkle.</summary>
    static AudioStreamWav MakeEquip()
    {
        const int rate = 44100; const double hit = 0.72; int n = (int)(rate * 1.9);
        var rng = new Random(23);
        var data = new byte[n * 4];
        double lp = 0, ph = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / rate, l = 0, r = 0;
            if (t < hit)
            {
                double u = t / hit, w = rng.NextDouble() * 2 - 1;
                lp += (w - lp) * (0.02 + 0.25 * u);
                double env = Math.Sin(Math.PI * Math.Min(1, u)) * (0.4 + 0.6 * u);
                ph += 2 * Math.PI * (900 - 500 * u) / rate;
                l += (lp * 1.6 + Math.Sin(ph) * 0.08) * env; r += (lp * 1.4 + Math.Sin(ph * 1.01) * 0.08) * env;
            }
            if (t >= hit)
            {
                double d = t - hit;
                double ding = Math.Sin(2 * Math.PI * 1318.5 * d) * 0.45 * Math.Exp(-d * 3.5) + Math.Sin(2 * Math.PI * 1975.5 * d) * 0.25 * Math.Exp(-d * 4.5)
                            + Math.Sin(2 * Math.PI * 659.25 * d) * 0.3 * Math.Exp(-d * 5);
                double arp = 0;
                double[] notes = { 1568, 2093, 2637, 3136 };
                for (int k = 0; k < notes.Length; k++) { double s0 = 0.08 + k * 0.07; if (d > s0) arp += Math.Sin(2 * Math.PI * notes[k] * (d - s0)) * 0.12 * Math.Exp(-(d - s0) * 7); }
                l += ding + arp; r += ding + arp * 0.8;
            }
            short sl = (short)Math.Clamp(l * 20000, -32767, 32767), sr = (short)Math.Clamp(r * 20000, -32767, 32767);
            data[i * 4] = (byte)sl; data[i * 4 + 1] = (byte)(sl >> 8); data[i * 4 + 2] = (byte)sr; data[i * 4 + 3] = (byte)(sr >> 8);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, Stereo = true, MixRate = rate, Data = data };
    }

    public static void EquipSound()
    {
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree) return;
            _equip ??= MakeEquip();
            if (_equipPlayer == null || !GodotObject.IsInstanceValid(_equipPlayer))
            {
                _equipPlayer = new AudioStreamPlayer { MaxPolyphony = 2, Bus = "Master", VolumeDb = -3 };
                tree.Root.AddChild(_equipPlayer);
            }
            _equipPlayer.Stream = _equip;
            _equipPlayer.Play();
        }
        catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] equip sound: " + ex.Message); }
    }

    static AudioStreamPlayer? _thunderPlayer;

    /// <summary>Raigeki's thunder: the synthesized clap plus the game's own lightning strike sound.</summary>
    public static void Thunder()
    {
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree) return;
            _thunder ??= MakeThunder();
            if (_thunderPlayer == null || !GodotObject.IsInstanceValid(_thunderPlayer))
            {
                _thunderPlayer = new AudioStreamPlayer { MaxPolyphony = 3, Bus = "Master", VolumeDb = -2 };
                tree.Root.AddChild(_thunderPlayer);
            }
            _thunderPlayer.Stream = _thunder;
            _thunderPlayer.Play();
            SfxCmd.Play("event:/sfx/characters/defect/defect_lightning_evoke");
        }
        catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] thunder: " + ex.Message); }
    }

    static AudioStreamPlayer? _soft;

    /// <summary>A quiet version of a sound (volume in dB, pitch as a factor), for frequent clicks like Build Deck's.</summary>
    public static void PlaySoft(string key, float volumeDb = -12f, float pitch = 1f)
    {
        try
        {
            if (key == Mark && !Disc.ContainsKey(Mark)) Disc[Mark] = MakeBeep();
            if (!Disc.TryGetValue(key, out var stream) || Engine.GetMainLoop() is not SceneTree tree) { Play(key); return; }
            if (_soft == null || !GodotObject.IsInstanceValid(_soft))
            {
                _soft = new AudioStreamPlayer { MaxPolyphony = 4, Bus = "Master" };
                tree.Root.AddChild(_soft);
            }
            _soft.Stream = stream; _soft.VolumeDb = volumeDb; _soft.PitchScale = pitch;
            _soft.Play();
        }
        catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] soft sfx " + key + ": " + ex.Message); }
    }

    public static void Play(string key)
    {
        try
        {
            if (key == Mark && !Disc.ContainsKey(Mark)) Disc[Mark] = MakeBeep();
            if (Disc.TryGetValue(key, out var stream) && Engine.GetMainLoop() is SceneTree tree)
            {
                if (_player == null || !GodotObject.IsInstanceValid(_player))
                {
                    _player = new AudioStreamPlayer { MaxPolyphony = 6, Bus = "Master" };
                    tree.Root.AddChild(_player);
                }
                _player.Stream = stream;
                _player.Play();
                return;
            }
            if (Fallback.TryGetValue(key, out var ev)) SfxCmd.Play(ev);
        }
        catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] sfx " + key + ": " + ex.Message); }
    }
}

public static class FmVfx
{
    /// <summary>Swords of Revealing Light: glowing swords rain down around the enemies and stay as a faint barrier.</summary>
    public static void SwordsOfRevealingLight()
    {
        var room = NCombatRoom.Instance;
        if (room == null) return;
        var layer = new Node2D { ZIndex = 50 };
        room.AddChild(layer);
        var size = room.GetViewportRect().Size;
        var rng = new RandomNumberGenerator();
        for (int i = 0; i < 9; i++)
        {
            var sword = MakeSword();
            float x = size.X * (0.45f + 0.5f * i / 8f) + rng.RandfRange(-20, 20);
            float yEnd = size.Y * rng.RandfRange(0.45f, 0.6f);
            sword.Position = new Vector2(x, -200);
            sword.Modulate = new Color(1, 1, 1, 0);
            layer.AddChild(sword);
            var t = sword.CreateTween();
            t.TweenInterval(i * 0.07);
            t.Parallel().TweenProperty(sword, "modulate:a", 1f, 0.15);
            t.TweenProperty(sword, "position:y", yEnd, 0.35).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
            t.TweenProperty(sword, "modulate", new Color(1.6f, 1.5f, 0.8f, 1f), 0.08);
            t.TweenProperty(sword, "modulate", new Color(1, 1, 1, 0f), 1.4).SetDelay(0.8);
        }
        var cleanup = layer.CreateTween();
        cleanup.TweenInterval(3.5);
        cleanup.TweenCallback(Callable.From(layer.QueueFree));
    }

    private static Node2D MakeSword()
    {
        var n = new Node2D();
        var glow = new Color(1f, 0.95f, 0.55f);
        n.AddChild(new Polygon2D { Color = glow with { A = 0.35f }, Polygon = new[] { new Vector2(-14, -10), new Vector2(14, -10), new Vector2(4, 190), new Vector2(-4, 190) } });
        n.AddChild(new Polygon2D { Color = new Color(1f, 1f, 0.9f), Polygon = new[] { new Vector2(-6, 0), new Vector2(6, 0), new Vector2(0, 180) } });
        n.AddChild(new Polygon2D { Color = glow, Polygon = new[] { new Vector2(-26, -6), new Vector2(26, -6), new Vector2(26, 4), new Vector2(-26, 4) } });
        n.AddChild(new Polygon2D { Color = new Color(0.9f, 0.75f, 0.3f), Polygon = new[] { new Vector2(-4, -50), new Vector2(4, -50), new Vector2(4, -6), new Vector2(-4, -6) } });
        return n;
    }
}
