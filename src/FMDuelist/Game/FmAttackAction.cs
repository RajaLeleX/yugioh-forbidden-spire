using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Runs;

namespace FMDuelist.Game;

/// <summary>Clicked monster attack, run through the game's action queue like a card play (solo only).</summary>
public sealed class FmAttackAction : GameAction
{
    private readonly Player _player;
    private readonly Creature _target;
    private readonly int _zone;

    public FmAttackAction(Player player, int zone, Creature target) { _player = player; _zone = zone; _target = target; }

    public override ulong OwnerId => _player.NetId;
    public override GameActionType ActionType => GameActionType.CombatPlayPhaseOnly;
    public override bool RecordableToReplay => false;

    protected override async Task ExecuteAction()
    {
        if (!_target.IsAlive) return;
        await FmField.Attack(new GameActionPlayerChoiceContext(this), _player, FmField.Slots[_zone], _target);
    }

    public override INetAction ToNetAction() => throw new NotSupportedException("FM Duelist is single player");

    public static void Request(Player player, int zone, Creature target) =>
        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new FmAttackAction(player, zone, target));
}

/// <summary>Combat replays (a debug feature) can't serialize the clicked attack; leave it out of the replay.</summary>
[HarmonyLib.HarmonyPatch(typeof(MegaCrit.Sts2.Core.Multiplayer.Replay.CombatReplayWriter), "RecordGameAction")]
static class ReplaySkipPatch
{
    static bool Prefix(GameAction gameAction) => gameAction is not FmAttackAction;
}
