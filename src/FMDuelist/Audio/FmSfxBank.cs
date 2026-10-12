namespace FMDuelist.Audio;

/// <summary>SD_SE.DAT: SPU ADPCM sound effects, each starting after a 16-byte silent block. Decoded at 22.05 kHz.</summary>
public static class FmSfxBank
{
    public const int Rate = 22050;
    static readonly int[,] Filters = { { 0, 0 }, { 60, 0 }, { 115, -52 }, { 98, -55 }, { 122, -60 } };

    public static List<short[]> Decode(byte[] b)
    {
        var res = new List<short[]>();
        for (int i = 0; i + 32 <= b.Length; i += 16)
        {
            if (!IsZero(b, i) || IsZero(b, i + 16)) continue;
            var pcm = DecodeOne(b, i);
            if (pcm.Length >= 200) res.Add(pcm);
        }
        return res;
    }

    static bool IsZero(byte[] b, int o)
    {
        for (int k = 0; k < 16; k++) if (b[o + k] != 0) return false;
        return true;
    }

    static short[] DecodeOne(byte[] b, int off)
    {
        var outp = new List<short>();
        int s1 = 0, s2 = 0, k = off;
        while (k + 16 <= b.Length)
        {
            int h = b[k], fl = b[k + 1], sh = h & 15, f = Math.Min(h >> 4, 4);
            for (int i = 0; i < 28; i++)
            {
                int by = b[k + 2 + i / 2];
                int nib = (i & 1) == 0 ? by & 15 : by >> 4;
                int v = (short)(nib << 12) >> sh;
                v += (s1 * Filters[f, 0] + s2 * Filters[f, 1] + 32) >> 6;
                v = Math.Clamp(v, -32768, 32767);
                outp.Add((short)v);
                s2 = s1; s1 = v;
            }
            k += 16;
            if ((fl & 1) != 0 && k - off > 16) break;
        }
        return outp.ToArray();
    }
}
