using FMDuelist.Audio;
if (args[0] == "str")
{
    var movies = FmStr.Read(File.ReadAllBytes(args[1]));
    Console.WriteLine($"movies {movies.Count}: " + string.Join(", ", movies.Select(m => $"{m.Frames.Count}f/{m.Audio.Count / 2}smp@{m.AudioRate}")));
    var m0 = movies[0];
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var rgb = FmStr.DecodeFrame(m0.Frames[255], 320, 240);
    Console.WriteLine($"decode {sw.ElapsedMilliseconds} ms");
    using (var f = File.Create(args[2] + ".ppm")) { f.Write(System.Text.Encoding.ASCII.GetBytes("P6 320 240 255\n")); f.Write(rgb); }
    File.WriteAllBytes(args[2] + ".wav", FmMusicBank.ToWav(m0.Audio.ToArray(), m0.AudioRate));
    return;
}
var bgm = File.ReadAllBytes(args[0]);
Directory.CreateDirectory(args[1]);
for (int i = 0; i < FmMusicBank.Count(bgm); i++)
{
    var r = FmMusicBank.Render(bgm, i, 150);
    if (r != null) File.WriteAllBytes(Path.Combine(args[1], $"{i:00}.wav"), FmMusicBank.ToWav(r.Value.pcm));
}
