using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Rooms;

namespace FMDuelist.Game;

/// <summary>Combat music from the disc (filled in by the SEQ/VAB renderer).</summary>
public static partial class FmMusic
{
    public static void OnRoomEntered(AbstractRoom room, Player owner)
    {
        try { OnRoomEnteredImpl(room, owner); }
        catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] music: " + ex.Message); }
    }

    static partial void OnRoomEnteredImpl(AbstractRoom room, Player owner);
}
