namespace FMDuelist.Audio;

/// <summary>
/// PlayStation STR movie reader for the player's own disc: MDEC v2 video (320x240) and XA ADPCM audio.
/// No Godot dependency. Bitstream rules follow FFmpeg's mdec.c; VLC tables are the MPEG-1 DCT coefficient tables.
/// </summary>
public sealed class FmStr
{
    public sealed class Movie
    {
        public readonly List<byte[]> Frames = new();
        public readonly List<short> Audio = new();   // interleaved stereo
        public int AudioRate = 37800;
        public int Width = 320, Height = 240;
    }

    /// <summary>Splits raw 2352-byte sectors into movies (a frame number of 1 on chunk 0 starts a new one).</summary>
    public static List<Movie> Read(byte[] raw)
    {
        var movies = new List<Movie>();
        Movie? cur = null;
        var chunks = new Dictionary<int, byte[]>();
        int want = 0;
        var xa = new XaState();
        for (int s = 0; s + 2352 <= raw.Length; s += 2352)
        {
            int submode = raw[s + 18], coding = raw[s + 19];
            if ((submode & 4) != 0)
            {
                if (cur == null) continue;
                cur.AudioRate = ((coding >> 2) & 3) == 0 ? 37800 : 18900;
                xa.DecodeSector(raw, s + 24, (coding & 1) != 0, cur.Audio);
                continue;
            }
            int d = s + 24;
            if (BitConverter.ToUInt16(raw, d) != 0x160) continue;
            int cn = BitConverter.ToUInt16(raw, d + 4), cc = BitConverter.ToUInt16(raw, d + 6);
            uint fr = BitConverter.ToUInt32(raw, d + 8);
            if (cn == 0)
            {
                if (fr == 1 || cur == null) { cur = new Movie(); movies.Add(cur); xa = new XaState(); }
                chunks.Clear(); want = cc;
                cur.Width = BitConverter.ToUInt16(raw, d + 16); cur.Height = BitConverter.ToUInt16(raw, d + 18);
            }
            var payload = new byte[2016];
            Buffer.BlockCopy(raw, d + 32, payload, 0, 2016);
            chunks[cn] = payload;
            if (chunks.Count == want && cur != null)
            {
                var frame = new byte[want * 2016];
                for (int k = 0; k < want; k++) if (chunks.TryGetValue(k, out var c)) Buffer.BlockCopy(c, 0, frame, k * 2016, 2016);
                cur.Frames.Add(frame);
            }
        }
        return movies;
    }

    // ---------------- XA ADPCM (4-bit) ----------------
    sealed class XaState { public int L1, L2, R1, R2; public void DecodeSector(byte[] b, int off, bool stereo, List<short> outp) => Xa.Decode(b, off, stereo, this, outp); }

    static class Xa
    {
        static readonly int[] K0 = { 0, 60, 115, 98 }, K1 = { 0, 0, -52, -55 };
        public static void Decode(byte[] b, int off, bool stereo, XaState st, List<short> outp)
        {
            var l = new short[28]; var r = new short[28];
            for (int g = 0; g < 18; g++)
            {
                int go = off + g * 128;
                for (int u = 0; u < 8; u++)
                {
                    int param = b[go + 4 + u];
                    int shift = param & 15, filter = (param >> 4) & 3;
                    bool right = stereo && (u & 1) == 1;
                    int s1 = right ? st.R1 : st.L1, s2 = right ? st.R2 : st.L2;
                    var dst = right ? r : l;
                    for (int i = 0; i < 28; i++)
                    {
                        int by = b[go + 16 + i * 4 + u / 2];
                        int nib = (u & 1) == 0 ? by & 15 : by >> 4;
                        int v = (short)(nib << 12) >> shift;
                        v += (s1 * K0[filter] + s2 * K1[filter] + 32) >> 6;
                        v = Math.Clamp(v, -32768, 32767);
                        s2 = s1; s1 = v;
                        dst[i] = (short)v;
                    }
                    if (right) { st.R1 = s1; st.R2 = s2; } else { st.L1 = s1; st.L2 = s2; }
                    if (!stereo) { for (int i = 0; i < 28; i++) { outp.Add(l[i]); outp.Add(l[i]); } }
                    else if (right) { for (int i = 0; i < 28; i++) { outp.Add(l[i]); outp.Add(r[i]); } }
                }
            }
        }
    }

    // ---------------- MDEC v2 ----------------
    static readonly (int code, int len)[] Vlc =
    {
        (0x3,2),(0x4,4),(0x5,5),(0x6,7),(0x26,8),(0x21,8),(0xa,10),(0x1d,12),(0x18,12),(0x13,12),(0x10,12),(0x1a,13),(0x19,13),(0x18,13),(0x17,13),(0x1f,14),
        (0x1e,14),(0x1d,14),(0x1c,14),(0x1b,14),(0x1a,14),(0x19,14),(0x18,14),(0x17,14),(0x16,14),(0x15,14),(0x14,14),(0x13,14),(0x12,14),(0x11,14),(0x10,14),(0x18,15),
        (0x17,15),(0x16,15),(0x15,15),(0x14,15),(0x13,15),(0x12,15),(0x11,15),(0x10,15),(0x3,3),(0x6,6),(0x25,8),(0xc,10),(0x1b,12),(0x16,13),(0x15,13),(0x1f,15),
        (0x1e,15),(0x1d,15),(0x1c,15),(0x1b,15),(0x1a,15),(0x19,15),(0x13,16),(0x12,16),(0x11,16),(0x10,16),(0x5,4),(0x4,7),(0xb,10),(0x14,12),(0x14,13),(0x7,5),
        (0x24,8),(0x1c,12),(0x13,13),(0x6,5),(0xf,10),(0x12,12),(0x7,6),(0x9,10),(0x12,13),(0x5,6),(0x1e,12),(0x14,16),(0x4,6),(0x15,12),(0x7,7),(0x11,12),
        (0x5,7),(0x11,13),(0x27,8),(0x10,13),(0x23,8),(0x1a,16),(0x22,8),(0x19,16),(0x20,8),(0x18,16),(0xe,10),(0x17,16),(0xd,10),(0x16,16),(0x8,10),(0x15,16),
        (0x1f,12),(0x1a,12),(0x19,12),(0x17,12),(0x16,12),(0x1f,13),(0x1e,13),(0x1d,13),(0x1c,13),(0x1b,13),(0x1f,16),(0x1e,16),(0x1d,16),(0x1c,16),(0x1b,16),
    };
    static readonly int[] Level = Build(new[] { (40, 1), (18, 1), (5, 1), (4, 1), (3, 1), (3, 1), (3, 1), (2, 10), (1, 15) });
    static readonly int[] Run = BuildRun();
    static readonly Dictionary<int, int> Codes = BuildCodes();
    static readonly int[] Zz = { 0,1,8,16,9,2,3,10,17,24,32,25,18,11,4,5,12,19,26,33,40,48,41,34,27,20,13,6,7,14,21,28,35,42,49,56,57,50,43,36,29,22,15,23,30,37,44,51,58,59,52,45,38,31,39,46,53,60,61,54,47,55,62,63 };
    static readonly int[] Mq = { 8,16,19,22,26,27,29,34,16,16,22,24,27,29,34,37,19,22,26,27,29,34,34,38,22,22,26,27,29,34,37,40,22,26,27,29,32,35,40,48,26,27,29,32,35,40,48,58,26,27,29,34,38,46,56,69,27,29,35,38,46,56,69,83 };
    static readonly double[,] Cos = BuildCos();

    /// <summary>(count, repeats): levels 1..count, repeated.</summary>
    static int[] Build((int count, int reps)[] parts)
    {
        var l = new List<int>();
        foreach (var (count, reps) in parts) for (int r = 0; r < reps; r++) for (int i = 1; i <= count; i++) l.Add(i);
        return l.ToArray();
    }
    static int[] BuildRun()
    {
        var r = new List<int>();
        r.AddRange(Enumerable.Repeat(0, 40)); r.AddRange(Enumerable.Repeat(1, 18)); r.AddRange(Enumerable.Repeat(2, 5)); r.AddRange(Enumerable.Repeat(3, 4));
        r.AddRange(Enumerable.Repeat(4, 3)); r.AddRange(Enumerable.Repeat(5, 3)); r.AddRange(Enumerable.Repeat(6, 3));
        for (int i = 7; i <= 16; i++) { r.Add(i); r.Add(i); }
        for (int i = 17; i <= 31; i++) r.Add(i);
        return r.ToArray();
    }
    static Dictionary<int, int> BuildCodes()
    {
        var d = new Dictionary<int, int>();
        for (int i = 0; i < Vlc.Length; i++) d[(Vlc[i].len << 16) | Vlc[i].code] = i;
        return d;
    }
    static double[,] BuildCos()
    {
        var c = new double[8, 8];
        for (int k = 0; k < 8; k++) for (int n = 0; n < 8; n++) c[k, n] = (k == 0 ? Math.Sqrt(1.0 / 8) : Math.Sqrt(2.0 / 8)) * Math.Cos((2 * n + 1) * k * Math.PI / 16);
        return c;
    }

    sealed class Bits
    {
        readonly byte[] _d; int _pos, _cur, _n;
        public Bits(byte[] d, int start) { _d = d; _pos = start; }
        public int Bit()
        {
            if (_n == 0) { _cur = _pos + 1 < _d.Length ? _d[_pos] | (_d[_pos + 1] << 8) : 0; _pos += 2; _n = 16; }
            _n--;
            return (_cur >> _n) & 1;
        }
        public int Get(int k) { int v = 0; for (int i = 0; i < k; i++) v = (v << 1) | Bit(); return v; }
    }

    static void Block(Bits br, int qs, double[] coef)
    {
        Array.Clear(coef);
        int dc = br.Get(10); if ((dc & 0x200) != 0) dc -= 0x400;
        coef[0] = 2 * dc;
        int i = 0;
        while (true)
        {
            int code = 0, len = 0;
            while (true)
            {
                code = (code << 1) | br.Bit(); len++;
                if (len == 2 && code == 2) return;
                if (len == 6 && code == 1)
                {
                    int run = br.Get(6), lv = br.Get(10); if ((lv & 0x200) != 0) lv -= 0x400;
                    i += run + 1; if (i > 63) return;
                    int j = Zz[i];
                    int v = (Math.Abs(lv) * qs * Mq[j]) >> 3; v = (v - 1) | 1;
                    coef[j] = lv >= 0 ? v : -v;
                    break;
                }
                if (Codes.TryGetValue((len << 16) | code, out int k))
                {
                    i += Run[k] + 1; if (i > 63) return;
                    int j = Zz[i];
                    int v = (Level[k] * qs * Mq[j]) >> 3;
                    coef[j] = br.Bit() == 1 ? -v : v;
                    break;
                }
                if (len > 17) return;
            }
        }
    }

    static void Idct(double[] c, double[] o)
    {
        var tmp = new double[64];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                double s = 0;
                for (int k = 0; k < 8; k++) s += Cos[k, x] * c[y * 8 + k];
                tmp[y * 8 + x] = s;
            }
        for (int x = 0; x < 8; x++)
            for (int y = 0; y < 8; y++)
            {
                double s = 0;
                for (int k = 0; k < 8; k++) s += Cos[k, y] * tmp[k * 8 + x];
                o[y * 8 + x] = s;
            }
    }

    /// <summary>Decodes one frame to RGB8 bytes (width*height*3).</summary>
    public static byte[] DecodeFrame(byte[] data, int w, int h)
    {
        int qs = data.Length > 6 ? data[4] | (data[5] << 8) : 1;
        var br = new Bits(data, 8);
        var rgb = new byte[w * h * 3];
        var coef = new double[64];
        var blk = new double[6][];
        for (int b = 0; b < 6; b++) blk[b] = new double[64];
        for (int mx = 0; mx < w / 16; mx++)
            for (int my = 0; my < h / 16; my++)
            {
                for (int b = 0; b < 6; b++) { Block(br, qs, coef); Idct(coef, blk[b]); }
                for (int py = 0; py < 16; py++)
                    for (int px = 0; px < 16; px++)
                    {
                        int yb = 2 + (py >= 8 ? 2 : 0) + (px >= 8 ? 1 : 0);
                        double Y = blk[yb][(py & 7) * 8 + (px & 7)] + 128;
                        double cr = blk[0][(py >> 1) * 8 + (px >> 1)], cb = blk[1][(py >> 1) * 8 + (px >> 1)];
                        int o = ((my * 16 + py) * w + mx * 16 + px) * 3;
                        rgb[o] = Clamp(Y + 1.402 * cr);
                        rgb[o + 1] = Clamp(Y - 0.3437 * cb - 0.7143 * cr);
                        rgb[o + 2] = Clamp(Y + 1.772 * cb);
                    }
            }
        return rgb;
    }

    static byte Clamp(double v) => (byte)Math.Clamp((int)Math.Round(v), 0, 255);
}
