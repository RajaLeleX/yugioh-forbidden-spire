namespace FMDuelist.Audio;

/// <summary>
/// Minimal PlayStation SEQ + VAB renderer (SPU ADPCM, ADSR, pitch, pan, pitch bend, NRPN loops).
/// No Godot dependency so it can be tested from a console app.
/// </summary>
public sealed class PsxSeqSynth
{
    public const int Rate = 44100;

    // ---------------- VAB ----------------
    sealed class Tone
    {
        public int Vol, Pan, Center, Shift, Min, Max, PbMin, PbMax, Adsr1, Adsr2, Vag;
    }

    sealed class Program_
    {
        public int Vol, Pan;
        public List<Tone> Tones = new();
    }

    readonly Program_?[] _progs = new Program_?[128];
    readonly short[][] _vagPcm = new short[256][];
    readonly int[] _vagLoopStart = new int[256];
    readonly bool[] _vagLoops = new bool[256];
    int _masterVol = 127;

    static readonly int[,] Filters = { { 0, 0 }, { 60, 0 }, { 115, -52 }, { 98, -55 }, { 122, -60 } };

    /// <summary>vh = VAB header bytes (starting at "pBAV"), vb = sample body.</summary>
    public PsxSeqSynth(byte[] vh, int vhOff, byte[] vb, int vbOff)
    {
        int ps = BitConverter.ToUInt16(vh, vhOff + 18);
        _masterVol = vh[vhOff + 24];
        int progBase = vhOff + 32;
        int toneBase = progBase + 128 * 16;
        int t = 0;
        for (int p = 0; p < 128 && t < ps; p++)
        {
            int o = progBase + p * 16;
            int tones = vh[o];
            if (tones == 0) continue;
            var pr = new Program_ { Vol = vh[o + 1], Pan = vh[o + 4] };
            for (int k = 0; k < 16; k++)
            {
                int to = toneBase + (t * 16 + k) * 32;
                if (k >= tones) continue;
                pr.Tones.Add(new Tone
                {
                    Vol = vh[to + 2], Pan = vh[to + 3], Center = vh[to + 4], Shift = vh[to + 5],
                    Min = vh[to + 6], Max = vh[to + 7], PbMin = vh[to + 12], PbMax = vh[to + 13],
                    Adsr1 = BitConverter.ToUInt16(vh, to + 16), Adsr2 = BitConverter.ToUInt16(vh, to + 18),
                    Vag = BitConverter.ToUInt16(vh, to + 22),
                });
            }
            _progs[p] = pr;
            t++;
        }
        int sizeTable = toneBase + ps * 16 * 32;
        int pos = vbOff;
        for (int v = 0; v < 256; v++)
        {
            int size = BitConverter.ToUInt16(vh, sizeTable + v * 2) << 3;
            if (v > 0 && size > 0 && pos + size <= vb.Length) DecodeVag(v, vb, pos, size);
            pos += size;
        }
    }

    void DecodeVag(int v, byte[] d, int off, int size)
    {
        var pcm = new List<short>(size / 16 * 28);
        int s1 = 0, s2 = 0;
        for (int b = 0; b + 16 <= size; b += 16)
        {
            int hdr = d[off + b], flags = d[off + b + 1];
            int shift = hdr & 0xF, filter = Math.Min((hdr >> 4) & 0xF, 4);
            if ((flags & 4) != 0) _vagLoopStart[v] = pcm.Count;
            for (int i = 0; i < 28; i++)
            {
                int by = d[off + b + 2 + i / 2];
                int nib = (i & 1) == 0 ? by & 0xF : by >> 4;
                int sample = (short)(nib << 12) >> shift;
                sample += (s1 * Filters[filter, 0] + s2 * Filters[filter, 1] + 32) >> 6;
                sample = Math.Clamp(sample, -32768, 32767);
                pcm.Add((short)sample);
                s2 = s1; s1 = sample;
            }
            if ((flags & 1) != 0)
            {
                _vagLoops[v] = (flags & 2) != 0;
                break;
            }
        }
        _vagPcm[v] = pcm.ToArray();
    }

    // ---------------- voices ----------------
    sealed class Voice
    {
        public int Channel, Note;
        public short[] Pcm = null!;
        public int LoopStart;
        public bool Loops;
        public double Pos, Step;
        public double BaseRatio;
        public int PbMin, PbMax;
        public float VolL, VolR;
        public Adsr Env = null!;
        public bool Done;
    }

    /// <summary>SPU ADSR envelope (psx-spx), stepped at 44.1 kHz.</summary>
    sealed class Adsr
    {
        enum Phase { Attack, Decay, Sustain, Release, Off }
        Phase _ph = Phase.Attack;
        int _level;
        int _counter;
        readonly int _ar, _dr, _sl, _sr, _rr;
        readonly bool _aExp, _sExp, _sDec, _rExp;

        public Adsr(int a1, int a2)
        {
            _aExp = (a1 & 0x8000) != 0; _ar = (a1 >> 8) & 0x7F; _dr = (a1 >> 4) & 0xF; _sl = a1 & 0xF;
            _sExp = (a2 & 0x8000) != 0; _sDec = (a2 & 0x4000) != 0; _sr = (a2 >> 6) & 0x7F; _rExp = (a2 & 0x20) != 0; _rr = a2 & 0x1F;
        }

        public bool Finished => _ph == Phase.Off;
        public void Release() { if (_ph != Phase.Off) _ph = Phase.Release; }
        public float Value => _level / 32767f;

        void Step(int rate, bool decrease, bool exp)
        {
            int shift = Math.Max(0, (rate >> 2) - 11);
            int step = decrease ? -8 + (rate & 3) : 7 - (rate & 3);
            step <<= Math.Max(0, 11 - (rate >> 2));
            int cycles = 1 << shift;
            if (exp && !decrease && _level > 0x6000) cycles *= 4;
            if (exp && decrease) step = step * _level >> 15;
            if (++_counter < cycles) return;
            _counter = 0;
            _level = Math.Clamp(_level + step, 0, 32767);
        }

        public void Tick()
        {
            switch (_ph)
            {
                case Phase.Attack:
                    Step(_ar, false, _aExp);
                    if (_level >= 32767) { _level = 32767; _ph = Phase.Decay; }
                    break;
                case Phase.Decay:
                    Step(_dr << 2, true, true);
                    if (_level <= (_sl + 1) * 0x800) _ph = Phase.Sustain;
                    break;
                case Phase.Sustain:
                    Step(_sr, _sDec, _sExp);
                    break;
                case Phase.Release:
                    Step(_rr << 2, true, _rExp);
                    if (_level <= 0) _ph = Phase.Off;
                    break;
            }
        }
    }

    sealed class Channel
    {
        public int Program, Volume = 127, Expression = 127, Pan = 64, Bend = 8192, NrpnMsb;
    }

    readonly Channel[] _ch = Enumerable.Range(0, 16).Select(_ => new Channel()).ToArray();
    readonly List<Voice> _voices = new();

    static float PanL(int pan) => (float)Math.Cos(Math.Clamp(pan, 0, 127) / 127.0 * Math.PI / 2);
    static float PanR(int pan) => (float)Math.Sin(Math.Clamp(pan, 0, 127) / 127.0 * Math.PI / 2);

    void NoteOn(int c, int note, int vel)
    {
        var prog = _progs[_ch[c].Program];
        if (prog == null) return;
        foreach (var t in prog.Tones)
        {
            if (note < t.Min || note > t.Max) continue;
            var pcm = t.Vag is > 0 and < 256 ? _vagPcm[t.Vag] : null;
            if (pcm == null || pcm.Length == 0) continue;
            double ratio = Math.Pow(2, (note - t.Center + t.Shift / 128.0) / 12.0);
            float vol = vel / 127f * t.Vol / 127f * prog.Vol / 127f * _masterVol / 127f;
            int pan = Math.Clamp(t.Pan + prog.Pan - 64, 0, 127);
            _voices.Add(new Voice
            {
                Channel = c, Note = note, Pcm = pcm, LoopStart = _vagLoopStart[t.Vag], Loops = _vagLoops[t.Vag],
                BaseRatio = ratio, PbMin = t.PbMin, PbMax = t.PbMax,
                VolL = vol * PanL(pan), VolR = vol * PanR(pan), Env = new Adsr(t.Adsr1, t.Adsr2),
            });
        }
    }

    void NoteOff(int c, int note)
    {
        foreach (var v in _voices)
            if (v.Channel == c && v.Note == note) v.Env.Release();
    }

    // ---------------- SEQ ----------------
    /// <summary>Renders the SEQ at seqOff. Returns interleaved stereo 16-bit PCM and the loop-start frame (or -1).</summary>
    public (short[] pcm, int loopStartFrame, int loopEndFrame) Render(byte[] d, int seqOff, double maxSeconds = 240)
    {
        int ppqn = (d[seqOff + 8] << 8) | d[seqOff + 9];
        int tempo = (d[seqOff + 10] << 16) | (d[seqOff + 11] << 8) | d[seqOff + 12];
        int p = seqOff + 15;
        var outL = new List<float>(Rate * 120);
        var outR = new List<float>(Rate * 120);
        int running = 0;
        int loopStartFrame = -1;
        bool sawLoopEnd = false;
        int loopEndFrame = -1;
        int maxFrames = (int)(maxSeconds * Rate);
        double samplesPerTick() => tempo / 1_000_000.0 * Rate / Math.Max(1, ppqn);
        double carry = 0;

        int ReadVar()
        {
            int v = 0;
            for (int i = 0; i < 4; i++)
            {
                int b = d[p++];
                v = (v << 7) | (b & 0x7F);
                if ((b & 0x80) == 0) break;
            }
            return v;
        }

        bool first = true;
        while (p < d.Length && outL.Count < maxFrames)
        {
            int delta = first ? ReadVar() : ReadVar();
            first = false;
            carry += delta * samplesPerTick();
            int n = (int)carry;
            carry -= n;
            Mix(outL, outR, n);
            int st = d[p];
            if ((st & 0x80) != 0) { running = st; p++; }
            int type = running & 0xF0, c = running & 0x0F;
            if (running == 0xFF)
            {
                int meta = d[p++];
                if (meta == 0x51) { tempo = (d[p] << 16) | (d[p + 1] << 8) | d[p + 2]; p += 3; continue; }
                if (meta == 0x2F) { loopEndFrame = outL.Count; break; }
                int len = d[p++]; p += len; continue;
            }
            switch (type)
            {
                case 0x90: { int note = d[p++], vel = d[p++]; if (vel == 0) NoteOff(c, note); else NoteOn(c, note, vel); break; }
                case 0x80: { int note = d[p++]; p++; NoteOff(c, note); break; }
                case 0xB0:
                {
                    int cc = d[p++], val = d[p++];
                    switch (cc)
                    {
                        case 7: _ch[c].Volume = val; break;
                        case 11: _ch[c].Expression = val; break;
                        case 10: _ch[c].Pan = val; break;
                        case 99: _ch[c].NrpnMsb = val; break;
                        case 6:
                            break;
                    }
                    if (cc == 99 && val == 20 && loopStartFrame < 0) loopStartFrame = outL.Count;
                    if (cc == 99 && val == 30) sawLoopEnd = true;
                    break;
                }
                case 0xC0: _ch[c].Program = d[p++] & 0x7F; break;
                case 0xD0: p++; break;
                case 0xA0: p += 2; break;
                case 0xE0: { int lo = d[p++], hi = d[p++]; _ch[c].Bend = (hi << 7) | lo; break; }
                default: p++; break;
            }
            if (sawLoopEnd) { loopEndFrame = outL.Count; break; }
        }
        if (loopEndFrame < 0) loopEndFrame = outL.Count;
        foreach (var v in _voices) v.Env.Release();
        Mix(outL, outR, Rate / 2);
        float peak = 1e-6f;
        for (int i = 0; i < outL.Count; i++) peak = Math.Max(peak, Math.Max(Math.Abs(outL[i]), Math.Abs(outR[i])));
        float g = 0.89f / peak;
        var pcm = new short[outL.Count * 2];
        for (int i = 0; i < outL.Count; i++)
        {
            pcm[i * 2] = (short)Math.Clamp(outL[i] * g * 32767, -32768, 32767);
            pcm[i * 2 + 1] = (short)Math.Clamp(outR[i] * g * 32767, -32768, 32767);
        }
        return (pcm, loopStartFrame, loopEndFrame);
    }

    void Mix(List<float> l, List<float> r, int frames)
    {
        for (int f = 0; f < frames; f++)
        {
            float sl = 0, sr = 0;
            for (int i = 0; i < _voices.Count; i++)
            {
                var v = _voices[i];
                if (v.Done) continue;
                var ch = _ch[v.Channel];
                double bend = (ch.Bend - 8192) / 8192.0;
                double semis = bend >= 0 ? bend * v.PbMax : bend * v.PbMin;
                double step = v.BaseRatio * (semis == 0 ? 1 : Math.Pow(2, semis / 12.0));
                int ip = (int)v.Pos;
                if (ip >= v.Pcm.Length)
                {
                    if (v.Loops && v.LoopStart < v.Pcm.Length) { v.Pos = v.LoopStart + (v.Pos - v.Pcm.Length); ip = (int)v.Pos; }
                    else { v.Done = true; continue; }
                }
                double fr = v.Pos - ip;
                int next = ip + 1 < v.Pcm.Length ? v.Pcm[ip + 1] : (v.Loops ? v.Pcm[v.LoopStart] : 0);
                float s = (float)((v.Pcm[ip] * (1 - fr) + next * fr) / 32768.0);
                v.Env.Tick();
                if (v.Env.Finished) { v.Done = true; continue; }
                float cv = ch.Volume / 127f * ch.Expression / 127f * v.Env.Value;
                float cl = PanL(ch.Pan) * 1.414f, cr = PanR(ch.Pan) * 1.414f;
                sl += s * v.VolL * cv * cl;
                sr += s * v.VolR * cv * cr;
                v.Pos += step;
            }
            l.Add(sl); r.Add(sr);
        }
        _voices.RemoveAll(v => v.Done);
    }
}
