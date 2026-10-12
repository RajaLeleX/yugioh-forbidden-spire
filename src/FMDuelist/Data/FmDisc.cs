using System.Text;

namespace FMDuelist.Data;

/// <summary>Reads files from the player's own Forbidden Memories disc image (.bin raw 2352 or .iso 2048).</summary>
public sealed class FmDisc : IDisposable
{
    private readonly FileStream _f;
    private readonly int _sectorSize;
    private readonly int _dataOffset;
    private readonly Dictionary<string, (int lba, int size)> _files = new(StringComparer.OrdinalIgnoreCase);

    public FmDisc(string path)
    {
        _f = File.OpenRead(path);
        (_sectorSize, _dataOffset) = DetectLayout();
        ReadDir(RootLba(out int rootSize), rootSize, "");
    }

    private (int, int) DetectLayout()
    {
        foreach (var (size, off) in new[] { (2352, 24), (2352, 16), (2048, 0) })
        {
            var buf = new byte[6];
            _f.Seek(16L * size + off, SeekOrigin.Begin);
            _f.ReadExactly(buf);
            if (buf[1] == 'C' && buf[2] == 'D' && buf[3] == '0' && buf[4] == '0' && buf[5] == '1') return (size, off);
        }
        throw new InvalidDataException("Not a PlayStation disc image");
    }

    private byte[] Sector(int n)
    {
        var b = new byte[2048];
        _f.Seek((long)n * _sectorSize + _dataOffset, SeekOrigin.Begin);
        _f.ReadExactly(b);
        return b;
    }

    public byte[] ReadRange(int lba, int size)
    {
        var outp = new byte[size];
        int pos = 0, n = lba;
        while (pos < size)
        {
            var s = Sector(n++);
            int c = Math.Min(2048, size - pos);
            Buffer.BlockCopy(s, 0, outp, pos, c);
            pos += c;
        }
        return outp;
    }

    private int RootLba(out int size)
    {
        var pvd = Sector(16);
        size = BitConverter.ToInt32(pvd, 156 + 10);
        return BitConverter.ToInt32(pvd, 156 + 2);
    }

    private void ReadDir(int lba, int size, string prefix)
    {
        var data = ReadRange(lba, size);
        int i = 0;
        while (i < data.Length)
        {
            int len = data[i];
            if (len == 0) { i = (i / 2048 + 1) * 2048; continue; }
            int elba = BitConverter.ToInt32(data, i + 2), esz = BitConverter.ToInt32(data, i + 10);
            bool dir = (data[i + 25] & 2) != 0;
            int nl = data[i + 32];
            string name = Encoding.ASCII.GetString(data, i + 33, nl);
            i += len;
            if (nl == 1 && (name[0] == '\0' || name[0] == '\u0001')) continue;
            int semi = name.IndexOf(';');
            if (semi >= 0) name = name[..semi];
            string p = prefix + "/" + name;
            if (dir) ReadDir(elba, esz, p); else _files[p] = (elba, esz);
        }
    }

    public bool Has(string path) => _files.ContainsKey(path);

    /// <summary>Raw 2352-byte sectors of a file (needed for XA audio / STR video); null on a 2048-byte .iso.</summary>
    public byte[]? ReadRawSectors(string path)
    {
        if (_sectorSize != 2352 || !_files.TryGetValue(path, out var f)) return null;
        int n = (f.size + 2047) / 2048;
        var outp = new byte[n * 2352];
        _f.Seek((long)f.lba * 2352, SeekOrigin.Begin);
        _f.ReadExactly(outp);
        return outp;
    }
    public byte[] ReadFile(string path) { var (l, s) = _files[path]; return ReadRange(l, s); }
    public void Dispose() => _f.Dispose();
}
