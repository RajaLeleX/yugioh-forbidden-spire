namespace FMDuelist.Data;

/// <summary>Forbidden Memories text encoding. Table from fmscrambler (MIT, duke1102 et al.).</summary>
public static class FmText
{
    private static readonly Dictionary<byte, char> Map = Build();

    private static Dictionary<byte, char> Build()
    {
        const string t = "18=A 2D=B 2B=C 20=D 25=E 31=F 29=G 23=H 1A=I 3B=J 33=K 2A=L 1E=M 2C=N 21=O 2F=P 3E=Q 26=R 1D=S 1C=T 35=U 39=V 22=W 46=X 24=Y 3F=Z 03=a 15=b 0F=c 0C=d 01=e 13=f 10=g 09=h 05=i 34=j 16=k 0A=l 0E=m 06=n 04=o 14=p 37=q 08=r 07=s 02=t 0D=u 19=v 12=w 36=x 11=y 32=z 38=0 3D=1 3A=2 41=3 4A=4 42=5 4E=6 45=7 57=8 59=9 30=- 3C=# 43=& 0B=. 1F=, 55=a 17=! 1B=' 27=< 28=> 2E=? 44=/ 48=: 4B=) 4C=( 4F=$ 50=* 51=> 54=< 40=\" 56=+ 5B=%";
        var d = new Dictionary<byte, char> { [0x00] = ' ' };
        foreach (var e in t.Split(' '))
            d[Convert.ToByte(e[..2], 16)] = e[3];
        return d;
    }

    public static string Read(byte[] buf, int pos)
    {
        var sb = new System.Text.StringBuilder();
        while (pos < buf.Length)
        {
            byte b = buf[pos++];
            if (b == 0xFF) break;
            if (b == 0xFE) { sb.Append(' '); continue; }
            if (Map.TryGetValue(b, out char c)) sb.Append(c);
        }
        return System.Text.RegularExpressions.Regex.Replace(sb.ToString(), @"\s+", " ").Trim();
    }
}
