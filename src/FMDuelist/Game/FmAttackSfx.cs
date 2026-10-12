using Godot;

namespace FMDuelist.Game;

/// <summary>
/// Attack sounds, one per attack style, synthesized in code and timed to the animations: fire whooshes and crackles,
/// water balls splash, dark mist hisses, lasers zap, rocks rumble, and so on. Nothing is shipped as audio files.
/// </summary>
public static class FmAttackSfx
{
    const int Rate = 44100;
    static readonly Dictionary<FmAttackStyle, AudioStreamWav> _cache = new();
    static readonly List<AudioStreamPlayer> _players = new();

    public static void Play(FmAttackStyle style, float size = 1f)
    {
        try
        {
            if (Engine.GetMainLoop() is not SceneTree tree) return;
            if (!_cache.TryGetValue(style, out var wav)) _cache[style] = wav = Build(style);
            var p = _players.FirstOrDefault(x => GodotObject.IsInstanceValid(x) && !x.Playing);
            if (p == null)
            {
                _players.RemoveAll(x => !GodotObject.IsInstanceValid(x));
                if (_players.Count >= 6) p = _players[0];
                else { p = new AudioStreamPlayer { Bus = "Master" }; tree.Root.AddChild(p); _players.Add(p); }
            }
            p.Stream = wav;
            p.VolumeDb = -4 + Mathf.Clamp((size - 1f) * 4f, 0f, 3f);
            p.PitchScale = Mathf.Clamp(1.12f - (size - 0.8f) * 0.18f, 0.85f, 1.12f); // bigger monsters sound deeper
            p.Play();
        }
        catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] attack sfx: " + ex.Message); }
    }

    // ---------------- synthesis ----------------
    sealed class Buf
    {
        public readonly float[] L, R;
        public Buf(double seconds) { L = new float[(int)(seconds * Rate)]; R = new float[L.Length]; }
        public int N => L.Length;
        public void Add(int i, double l, double r) { if (i >= 0 && i < N) { L[i] += (float)l; R[i] += (float)r; } }
    }

    static AudioStreamWav ToWav(Buf b)
    {
        float peak = 1e-4f;
        for (int i = 0; i < b.N; i++) peak = Math.Max(peak, Math.Max(Math.Abs(b.L[i]), Math.Abs(b.R[i])));
        float g = 0.85f / peak;
        var data = new byte[b.N * 4];
        for (int i = 0; i < b.N; i++)
        {
            short l = (short)Math.Clamp(b.L[i] * g * 32767, -32767, 32767), r = (short)Math.Clamp(b.R[i] * g * 32767, -32767, 32767);
            data[i * 4] = (byte)l; data[i * 4 + 1] = (byte)(l >> 8); data[i * 4 + 2] = (byte)r; data[i * 4 + 3] = (byte)(r >> 8);
        }
        return new AudioStreamWav { Format = AudioStreamWav.FormatEnum.Format16Bits, Stereo = true, MixRate = Rate, Data = data };
    }

    /// <summary>Noise through a resonant filter whose cutoff follows <paramref name="cutoff"/>(t) (Hz).</summary>
    static void Noise(Buf b, double start, double len, Func<double, double> cutoff, Func<double, double> env, double gain, int seed, double q = 0.5, double pan = 0)
    {
        var rng = new Random(seed);
        double low = 0, band = 0;
        int s0 = (int)(start * Rate), n = (int)(len * Rate);
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            double f = 2 * Math.Sin(Math.PI * Math.Clamp(cutoff(t), 30, 16000) / Rate);
            double input = rng.NextDouble() * 2 - 1;
            low += f * band; double high = input - low - q * band; band += f * high;
            double v = band * env(t) * gain;
            b.Add(s0 + i, v * (1 - pan * 0.5), v * (1 + pan * 0.5));
        }
    }

    static void Tone(Buf b, double start, double len, Func<double, double> freq, Func<double, double> env, double gain, double square = 0, double pan = 0)
    {
        double ph = 0;
        int s0 = (int)(start * Rate), n = (int)(len * Rate);
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / Rate;
            ph += 2 * Math.PI * freq(t) / Rate;
            double s = Math.Sin(ph), v = (s * (1 - square) + Math.Sign(s) * square) * env(t) * gain;
            b.Add(s0 + i, v * (1 - pan * 0.5), v * (1 + pan * 0.5));
        }
    }

    static Func<double, double> Decay(double k) => t => Math.Exp(-t * k);
    static Func<double, double> Swell(double attack, double k) => t => Math.Min(1, t / attack) * Math.Exp(-Math.Max(0, t - attack) * k);
    static Func<double, double> Bell(double len) => t => Math.Sin(Math.PI * Math.Clamp(t / len, 0, 1));

    static void Thud(Buf b, double at, double f0, double gain) => Tone(b, at, 0.35, t => f0 * (1 - 0.5 * Math.Min(1, t * 6)), Decay(12), gain);
    static void Crackle(Buf b, double start, double len, double rate, double gain, int seed)
    {
        var rng = new Random(seed);
        for (double t = 0; t < len; t += rng.NextDouble() * 2 / rate)
            Noise(b, start + t, 0.012, _ => 3000 + rng.NextDouble() * 5000, Decay(300), gain * (0.4 + rng.NextDouble()), seed + (int)(t * 1000), 0.3, rng.NextDouble() * 2 - 1);
    }

    static AudioStreamWav Build(FmAttackStyle s)
    {
        Buf b;
        switch (s)
        {
            case FmAttackStyle.Slash: // swish + steel ring
                b = new Buf(0.6);
                Noise(b, 0, 0.22, t => 6000 - t * 18000, Bell(0.22), 1.2, 1, 0.35);
                foreach (var (f, a) in new[] { (2400.0, 0.25), (3700.0, 0.15), (5100.0, 0.1) }) Tone(b, 0.12, 0.45, _ => f, Decay(9), a);
                break;
            case FmAttackStyle.Claw: // three scratchy rakes
                b = new Buf(0.55);
                for (int k = 0; k < 3; k++) Noise(b, k * 0.09, 0.12, t => 4500 - t * 15000, Bell(0.12), 1.0, 10 + k, 0.2, k - 1);
                break;
            case FmAttackStyle.Bite: // jaws snap: two chomps and a crunch
                b = new Buf(0.6);
                Thud(b, 0.0, 160, 0.9); Thud(b, 0.13, 130, 1.0);
                Noise(b, 0.13, 0.15, _ => 2500, Decay(25), 0.8, 20, 0.6);
                break;
            case FmAttackStyle.Sting: // quick zips
                b = new Buf(0.6);
                for (int k = 0; k < 4; k++) Tone(b, k * 0.07, 0.08, t => 1800 + t * 30000, Bell(0.08), 0.35, 0.3, (k % 2) * 2 - 1);
                break;
            case FmAttackStyle.Fireball: // whoosh in, crackling flames, boom
                b = new Buf(1.3);
                Noise(b, 0, 0.35, t => 300 + t * 3000, Swell(0.3, 4), 1.0, 30, 0.7);
                Crackle(b, 0.05, 1.0, 60, 0.5, 31);
                Noise(b, 0.32, 0.9, t => 900 - t * 600, Decay(4), 1.4, 32, 0.9);
                Thud(b, 0.32, 70, 1.2);
                break;
            case FmAttackStyle.Breath: // long roaring blast of flame
                b = new Buf(1.4);
                Noise(b, 0, 1.2, t => 500 + 400 * Math.Sin(t * 14), Swell(0.15, 1.6), 1.4, 40, 0.8);
                Noise(b, 0, 1.2, t => 2200 + 800 * Math.Sin(t * 9), Swell(0.2, 2.2), 0.4, 41, 0.4);
                Crackle(b, 0.1, 1.1, 80, 0.45, 42);
                break;
            case FmAttackStyle.Lightning: // crack and a short rumble
                b = new Buf(1.4);
                Noise(b, 0, 0.15, _ => 7000, Decay(30), 1.6, 50, 0.2);
                Noise(b, 0.06, 0.1, _ => 6000, Decay(40), 1.0, 51, 0.2);
                Noise(b, 0.05, 1.3, t => 180 + 60 * Math.Sin(t * 7), Swell(0.1, 2.0), 1.6, 52, 0.9);
                break;
            case FmAttackStyle.Water: // wobbly water ball, then a splash with bubbles
                b = new Buf(1.0);
                Tone(b, 0, 0.3, t => 350 + 120 * Math.Sin(t * 60), Bell(0.3), 0.25);
                Noise(b, 0.3, 0.45, t => 2600 - t * 2500, Decay(7), 1.5, 60, 0.5);
                var rng = new Random(61);
                for (int k = 0; k < 10; k++) { double st = 0.32 + rng.NextDouble() * 0.45, f0 = 500 + rng.NextDouble() * 900; Tone(b, st, 0.08, t => f0 * (1 + t * 8), Decay(40), 0.25); }
                break;
            case FmAttackStyle.Magic: // rising shimmer arpeggio + sparkle
                b = new Buf(1.0);
                double[] notes = { 523, 659, 784, 1047, 1319 };
                for (int k = 0; k < notes.Length; k++) { double f = notes[k]; Tone(b, k * 0.06, 0.5, t => f * (1 + 0.004 * Math.Sin(t * 40)), Decay(6), 0.22, 0, k % 2 == 0 ? -0.6 : 0.6); }
                Noise(b, 0.3, 0.5, _ => 9000, Decay(8), 0.35, 70, 0.3);
                break;
            case FmAttackStyle.Holy: // choir-like swell and a bell
                b = new Buf(1.3);
                foreach (var f in new[] { 392.0, 494.0, 587.0, 784.0 }) Tone(b, 0, 1.0, t => f * (1 + 0.006 * Math.Sin(t * 30)), Swell(0.25, 2.5), 0.2);
                foreach (var (f, a) in new[] { (1568.0, 0.3), (4330.0, 0.12) }) Tone(b, 0.25, 1.0, _ => f, Decay(3.5), a);
                break;
            case FmAttackStyle.Dark: // low drone and hissing, whispering mist
                b = new Buf(1.3);
                Tone(b, 0, 1.2, t => 55 + 8 * Math.Sin(t * 5), Swell(0.3, 1.8), 0.6);
                Noise(b, 0, 1.2, t => 1400 + 900 * Math.Sin(t * 6), Swell(0.4, 1.5), 0.7, 80, 0.15, -0.4);
                Noise(b, 0.1, 1.1, t => 3200 + 1200 * Math.Sin(t * 4 + 1), Swell(0.4, 1.8), 0.4, 81, 0.15, 0.4);
                break;
            case FmAttackStyle.Laser: // pew + electric buzz
                b = new Buf(0.7);
                Tone(b, 0, 0.35, t => 2200 * Math.Exp(-t * 6), Decay(5), 0.5, 0.5);
                Tone(b, 0.05, 0.4, _ => 110, Bell(0.4), 0.25, 0.9);
                break;
            case FmAttackStyle.Rock: // falling rumble, heavy thuds, gravel
                b = new Buf(1.3);
                Noise(b, 0, 1.2, _ => 160, Swell(0.25, 2.2), 1.2, 90, 0.9);
                for (int k = 0; k < 3; k++) { Thud(b, 0.28 + k * 0.3, 80 - k * 8, 1.0); Noise(b, 0.28 + k * 0.3, 0.25, _ => 2500, Decay(14), 0.5, 91 + k, 0.4); }
                break;
            case FmAttackStyle.Vines: // creaking growth, then whip cracks
                b = new Buf(0.9);
                Noise(b, 0, 0.3, t => 600 + t * 1500, Swell(0.25, 3), 0.5, 100, 0.08);
                for (int k = 0; k < 2; k++) { Noise(b, 0.3 + k * 0.12, 0.08, t => 1500 + t * 30000, Bell(0.08), 0.8, 101 + k, 0.3, k * 2 - 1); Noise(b, 0.37 + k * 0.12, 0.05, _ => 5000, Decay(80), 1.4, 103 + k, 0.2); }
                break;
            case FmAttackStyle.Wind: // gusts
            default:
                b = new Buf(1.0);
                Noise(b, 0, 0.9, t => 700 + 1600 * Math.Sin(Math.PI * t / 0.9), Bell(0.9), 1.2, 110, 0.25, -0.3);
                Noise(b, 0.15, 0.6, t => 900 + 1400 * Math.Sin(Math.PI * t / 0.6), Bell(0.6), 0.8, 111, 0.25, 0.4);
                break;
        }
        return ToWav(b);
    }
}
