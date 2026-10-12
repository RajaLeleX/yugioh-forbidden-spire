namespace FMDuelist.Audio;

/// <summary>SD_BGM.DAT: a table of (?, sector, size) entries; each chunk holds VB samples, then the VAB header and SEQ.</summary>
public static class FmMusicBank
{
    public static int Count(byte[] bgm)
    {
        int n = 0;
        while (n < 256 && BitConverter.ToUInt32(bgm, n * 8) != 0) n++;
        return n;
    }

    public static (short[] pcm, int loopStart, int loopEnd)? Render(byte[] bgm, int index, double maxSeconds = 240)
    {
        int n = Count(bgm);
        if (index < 0 || index >= n) return null;
        int start = BitConverter.ToUInt16(bgm, index * 8 + 2) * 0x800;
        int end = index + 1 < n ? BitConverter.ToUInt16(bgm, (index + 1) * 8 + 2) * 0x800 : bgm.Length;
        int vh = Find(bgm, start, end, "pBAV");
        if (vh < 0) return null;
        int seq = Find(bgm, vh, end, "pQES");
        if (seq < 0) return null;
        var synth = new PsxSeqSynth(bgm, vh, bgm, start);
        return synth.Render(bgm, seq, maxSeconds);
    }

    static int Find(byte[] d, int from, int to, string sig)
    {
        for (int i = from; i + 4 <= to; i++)
            if (d[i] == sig[0] && d[i + 1] == sig[1] && d[i + 2] == sig[2] && d[i + 3] == sig[3]) return i;
        return -1;
    }

    public static byte[] ToWav(short[] pcm, int rate = PsxSeqSynth.Rate)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int bytes = pcm.Length * 2;
        w.Write("RIFF"u8); w.Write(36 + bytes); w.Write("WAVE"u8); w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)2);
        w.Write(rate); w.Write(rate * 4); w.Write((short)4); w.Write((short)16); w.Write("data"u8); w.Write(bytes);
        foreach (var s in pcm) w.Write(s);
        return ms.ToArray();
    }
}
