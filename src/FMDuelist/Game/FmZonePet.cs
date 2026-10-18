using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.MonsterMoves.MonsterMoveStateMachine;

namespace FMDuelist.Game;

/// <summary>Monster zones 2 and 3: pet creatures that borrow Osty's skeleton (hidden behind the card) and never act.</summary>
public abstract class FmZonePet : MonsterModel
{
    public abstract int ZoneIndex { get; }
    public override int MinInitialHp => 1;
    public override int MaxInitialHp => 1;
    public override bool IsHealthBarVisible => false;
    protected override string VisualsPath => SceneHelper.GetScenePath("creature_visuals/osty");

    protected override MonsterMoveStateMachine GenerateMoveStateMachine()
    {
        var idle = new MoveState("NOTHING_MOVE", (IReadOnlyList<Creature> _) => Task.CompletedTask);
        idle.FollowUpState = idle;
        return new MonsterMoveStateMachine(new List<MonsterState> { idle }, idle);
    }

    public override CreatureAnimator GenerateAnimator(MegaSprite controller)
    {
        var idle = new AnimState("idle_loop", isLooping: true);
        var hurt = new AnimState("hurt");
        var die = new AnimState("die");
        idle.AddBranch("Hit", hurt);
        hurt.NextState = idle;
        var animator = new CreatureAnimator(idle, controller);
        animator.AddAnyState("Dead", die);
        animator.AddAnyState("Hit", hurt);
        return animator;
    }
}

public sealed class FmZonePet2 : FmZonePet { public override int ZoneIndex => 1; }
public sealed class FmZonePet3 : FmZonePet { public override int ZoneIndex => 2; }
public sealed class FmZonePet4 : FmZonePet { public override int ZoneIndex => 3; }
public sealed class FmZonePet5 : FmZonePet { public override int ZoneIndex => 4; }
