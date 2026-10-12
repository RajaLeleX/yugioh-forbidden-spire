namespace FMDuelist.Game;

/// <summary>Decodes FM music and sound effects from the disc on a background thread (implemented in FmAudio.Seq.cs).</summary>
public static partial class FmAudio
{
    public static void StartBackgroundDecode()
    {
        try { StartImpl(); }
        catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] audio: " + ex.Message); }
    }

    static partial void StartImpl();
}

/// <summary>Melty test mode hook (implemented in FmTest.cs).</summary>
public static partial class FmTest
{
    public static void Start() { try { StartImpl(); } catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] test: " + ex.Message); } }
    static partial void StartImpl();
}
