using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace FMDuelist.Game;

/// <summary>
/// Enemies read the field like an FM duelist (see FmEnemyAI): when attacking isn't worth the damage it would take back,
/// it switches to Defense Position instead: "&lt;Name&gt; goes into Defense Position!" pops up over it and it gains
/// Block equal to the damage it would have dealt. Only the attack is replaced: the rest of its move (statuses, debuffs,
/// buffs, cards added to your piles, summons) still happens, with a "still uses" message so it's clear.
/// </summary>
[HarmonyPatch(typeof(AttackCommand), nameof(AttackCommand.Execute))]
static class EnemyDefensePatch
{
    static readonly AccessTools.FieldRef<AttackCommand, decimal> DamagePerHit = AccessTools.FieldRefAccess<AttackCommand, decimal>("_damagePerHit");
    static readonly AccessTools.FieldRef<AttackCommand, CalculatedDamageVar?> Calculated = AccessTools.FieldRefAccess<AttackCommand, CalculatedDamageVar?>("_calculatedDamageVar");
    static readonly AccessTools.FieldRef<AttackCommand, int> HitCount = AccessTools.FieldRefAccess<AttackCommand, int>("_hitCount");

    static bool Prefix(AttackCommand __instance, PlayerChoiceContext? choiceContext, ref Task<AttackCommand> __result)
    {
        try
        {
            var attacker = __instance.Attacker;
            if (attacker == null || !attacker.IsEnemy || attacker.IsDead || attacker.CombatState is not { } combat) return true;
            if (__instance.TargetSide != CombatSide.Player || !__instance.DamageProps.IsPoweredAttack()) return true;
            var player = combat.Players.FirstOrDefault(FmRules.IsFmPlayer);
            if (player == null || !FmField.HasMonster(player)) return true;

            decimal baseHit = Calculated(__instance) is { } calc ? calc.Calculate(player.Creature) : DamagePerHit(__instance);
            int hit = (int)Hook.ModifyDamage(player.RunState, combat, player.Creature, attacker, baseHit, __instance.DamageProps, null,
                ModifyDamageHookType.All, CardPreviewMode.None, out _);
            if (hit <= 0) return true;
            int hits = Math.Max(1, HitCount(__instance));
            var plan = FmEnemyAI.Decide(attacker, player, hit, hits);
            if (plan.Attack) { FmEnemyAI.Planned = plan.Target; return true; }
            __result = Defend(attacker, hit * hits, __instance);
            return false;
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] enemy defense: " + ex.Message); return true; }
    }

    static void Postfix(ref Task<AttackCommand> __result)
    {
        // the plan only covers this attack
        var t = __result;
        __result = Clear(t);
    }

    static async Task<AttackCommand> Clear(Task<AttackCommand> t)
    {
        try { return await t; } finally { FmEnemyAI.Planned = null; }
    }

    /// <summary>The enemy that went into Defense Position during this enemy turn (its move carries on).</summary>
    static Creature? _defender;
    static int _defendRound = -1;
    static readonly HashSet<(Creature, string)> _announced = new();

    public static Creature? DefenderNow(ICombatState? combat)
        => _defender != null && combat != null && combat.RoundNumber == _defendRound && combat.CurrentSide == CombatSide.Enemy ? _defender : null;

    /// <summary>"Name still uses X!" once per thing per turn, for the parts of its move that aren't the attack.</summary>
    public static void StillUses(Creature enemy, string what)
    {
        if (!_announced.Add((enemy, what))) return;
        Log.Info($"[FMDuelist] {enemy.Name} defends but still uses {what}");
        _ = Banner(enemy, $"{enemy.Name} still uses {what}!", 60);
    }

    static async Task<AttackCommand> Defend(Creature enemy, int block, AttackCommand cmd)
    {
        if (enemy.CombatState is { } cs && (cs.RoundNumber != _defendRound || _defender != enemy)) _announced.Clear();
        _defender = enemy; _defendRound = enemy.CombatState?.RoundNumber ?? -1;
        Log.Info($"[FMDuelist] {enemy.Name} goes into Defense Position (+{block} Block)");
        var banner = Banner(enemy, $"{enemy.Name} goes into Defense Position!");
        FmSfx.Play(FmSfx.Flip);
        await FmRules.Raw(() => CreatureCmd.GainBlock(enemy, block, ValueProp.Unpowered, null));
        if (banner != null) await banner;
        return cmd;
    }

    /// <summary>A gold message over the enemy that rises and fades (about 1.4 s).</summary>
    static Task? Banner(Creature enemy, string text, float lower = 0)
    {
        if (NCombatRoom.Instance?.GetCreatureNode(enemy) is not { } node || Engine.GetMainLoop() is not SceneTree tree) return null;
        var layer = new CanvasLayer { Layer = 70 };
        tree.Root.AddChild(layer);
        var rect = node.Hitbox.GetGlobalRect();
        var label = new Label
        {
            Text = text, HorizontalAlignment = HorizontalAlignment.Center, Size = new Vector2(560, 60),
            Position = new Vector2(rect.GetCenter().X - 280, rect.Position.Y - 70 + lower), Modulate = new Color(1, 1, 1, 0),
        };
        label.AddThemeFontSizeOverride("font_size", 34);
        label.AddThemeColorOverride("font_color", new Color(0.55f, 0.8f, 1f));
        label.AddThemeConstantOverride("outline_size", 10);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        layer.AddChild(label);
        var t = label.CreateTween();
        t.TweenProperty(label, "modulate:a", 1f, 0.2);
        t.Parallel().TweenProperty(label, "position:y", label.Position.Y - 30, 0.2);
        t.TweenInterval(0.9);
        t.TweenProperty(label, "modulate:a", 0f, 0.3);
        t.TweenCallback(Callable.From(layer.QueueFree));
        return Wait(t);
    }

    static async Task Wait(Tween t) => await t.ToSignal(t, Tween.SignalName.Finished);
}
