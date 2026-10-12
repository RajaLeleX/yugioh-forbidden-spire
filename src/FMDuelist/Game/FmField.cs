using FMDuelist.Data;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace FMDuelist.Game;

public enum FmPosition { Attack, Defense }

/// <summary>One monster zone. Zone 0 is Osty; zones 1 and 2 are extra pet creatures (FmZonePet2/3).</summary>
public sealed class FmSlot
{
    public int Index;
    public int MonsterId;
    /// <summary>His monsters are set face down like in FM: you only see them once they battle.</summary>
    public bool FaceDown;
    public FmPosition Position;
    public int AtkBonus, DefBonus;
    /// <summary>DEF added until your next turn (block-multiplying items in FM runs).</summary>
    public int TempDef;
    /// <summary>Equip cards played this combat that this monster can use (equips last the whole combat).</summary>
    /// <summary>Equip cards on this monster (id, bonus); they stay while it's on the field.</summary>
    public readonly List<(int id, int value)> EquipList = new();
    public List<int> Equips => EquipList.Select(e => e.id).ToList();
    int EquipBonus => MonsterId == 0 ? 0 : EquipList.Sum(e => e.value);
    public bool AttackedThisTurn;
    public uint? TargetCombatId;
    public CardModel? SourceCard;
    public readonly FmSlotVisual Visual;

    public FmSlot(int i, bool rival = false) { Index = i; Rival = rival; Visual = new FmSlotVisual(this); }

    /// <summary>A zone of the boss duelist (FmRival), standing on an enemy zone creature.</summary>
    public readonly bool Rival;
    public Creature? RivalCreature;

    public Creature? Creature(Player? p) => Rival ? RivalCreature : p == null ? null : Index == 0
        ? p.Osty
        : p.PlayerCombatState?.Pets.FirstOrDefault(c => c.Monster is FmZonePet z && z.ZoneIndex == Index);

    public bool Alive(Player? p) => (Rival || p != null) && MonsterId != 0 && Creature(p) is { IsAlive: true };

    public int Atk => MonsterId == 0 ? 0 : Rival ? FmData.Get(MonsterId).Atk + AtkBonus + EquipBonus + FmField.FieldBonus(MonsterId) - FmRival.BoundAtk : FmData.Get(MonsterId).Atk + AtkBonus + EquipBonus + FmGuardianStars.TurnAtk + FmField.FieldBonus(MonsterId) + Relics.FmBonus.TypeBonus(FmField.Duelist, MonsterId);
    public int Def => MonsterId == 0 ? 0 : Rival ? FmData.Get(MonsterId).Def + DefBonus + EquipBonus + FmField.FieldBonus(MonsterId) : FmData.Get(MonsterId).Def + DefBonus + TempDef + EquipBonus + FmGuardianStars.TurnDef + FmPlating.DefBonus(FmField.Duelist) + FmField.FieldBonus(MonsterId) + Relics.FmBonus.TypeBonus(FmField.Duelist, MonsterId)
        + (Position == FmPosition.Defense ? Relics.FmBonus.DefenseBonus(FmField.Duelist) : 0);

    public void Clear()
    {
        MonsterId = 0; FaceDown = false; AtkBonus = DefBonus = TempDef = 0; AttackedThisTurn = false; EquipList.Clear(); TargetCombatId = null; SourceCard = null;
        Position = FmPosition.Attack;
    }
}

/// <summary>The duelist's three monster zones plus per-combat duel state. Solo only.</summary>
public static class FmField
{
    public const int ZoneCount = 3;
    public const int MaxTraps = 3;
    public static readonly FmSlot[] Slots = { new(0), new(1), new(2) };

    public static int PendingAtkBonus, PendingDefBonus;
    /// <summary>Equips used in a fusion chain (id, bonus); they go onto the monster the chain summons.</summary>
    public static readonly List<(int id, int value)> PendingEquips = new();
    public static bool SummonedThisTurn;
    public static bool Aiming;
    public static Player? Duelist;
    public static int SwordsTurns;
    /// <summary>Spellbinding Circle was cast: every enemy is stunned for its next turn.</summary>
    public static bool BindStun;
    public static readonly List<int> Traps = new();
    public static readonly HashSet<FmType> FieldBoost = new();
    public static readonly List<CardModel> Marked = new();
    public static int SummonsThisTurn, SummonsThisCombat;
    /// <summary>Raigeki or Dark Hole was played this turn (only one of them per turn).</summary>
    public static bool MassSpellThisTurn;
    public static event Action? Changed;

    public static bool HasMonster(Player? p) => Slots.Any(s => s.Alive(p));
    public static IEnumerable<FmSlot> Living(Player? p) => Slots.Where(s => s.Alive(p));
    /// <summary>Enemy attacks hit the front monster: zone 1, then 2, then 3.</summary>
    public static FmSlot? Front(Player? p) => Slots.FirstOrDefault(s => s.Alive(p));

    /// <summary>
    /// Which of your monsters an enemy hit goes for (every hit decides again):
    /// 1. one it overpowers in Attack position, so the excess reaches your Life Points (biggest excess first);
    /// 2. one it destroys in battle: Attack position with ATK below the hit, Defense position with DEF below the hit;
    /// 3. otherwise the one that hurts it least (Defense position first, then the lowest ATK).
    /// </summary>
    public static FmSlot? EnemyTarget(Player? p, int hit)
    {
        // the enemy's tactical plan for this attack (FmEnemyAI) comes first
        if (FmEnemyAI.Planned is { } planned && planned.Alive(p)) return planned;
        FmSlot? best = null; double bestScore = double.MinValue;
        foreach (var s in Living(p))
        {
            double score = s.Position == FmPosition.Attack
                ? (hit > s.Atk ? 3_000_000 + (hit - s.Atk) : 1_000_000 - s.Atk)
                : (hit > s.Def ? 2_000_000 - s.Def : 1_500_000 - s.Def);
            if (score > bestScore) { bestScore = score; best = s; }
        }
        return best;
    }
    public static FmSlot? SlotOf(Creature? c)
    {
        var p = Duelist;
        if (c == null || p == null) return null;
        return Slots.FirstOrDefault(s => s.Creature(p) == c);
    }
    public static int FieldBonus(int id) => FieldBoost.Contains(FmData.Get(id).Type) ? 500 : 0;
    public static bool CanSummon(Player? p) => SummonsThisTurn < Relics.FmBonus.SummonsPerTurn(p);
    public static bool CanSetTrap => Traps.Count < MaxTraps;

    public static void ResetCombat()
    {
        foreach (var s in Slots) s.Clear();
        FmGuardianStars.Reset();
        SummonedThisTurn = false; SummonsThisTurn = 0; SummonsThisCombat = 0; MassSpellThisTurn = false; Aiming = false; SwordsTurns = 0; BindStun = false;
        Traps.Clear(); FieldBoost.Clear(); Marked.Clear(); PendingEquips.Clear(); PendingAtkBonus = PendingDefBonus = 0;
        try { FmBackdrop.Clear(); } catch { }
        NotifyChanged();
    }

    public static void NotifyChanged() => Changed?.Invoke();

    /// <summary>Puts the FM monster into a zone with 1 HP, replacing whatever was there.</summary>
    public static async Task Summon(PlayerChoiceContext ctx, Player player, FmSlot slot, int monsterId, FmPosition pos, Creature? target, CardModel source)
    {
        Duelist = player;
        slot.Clear();
        slot.MonsterId = monsterId; slot.Position = pos; slot.AtkBonus = PendingAtkBonus; slot.DefBonus = PendingDefBonus;
        slot.EquipList.AddRange(PendingEquips);
        PendingAtkBonus = PendingDefBonus = 0; PendingEquips.Clear();
        slot.TargetCombatId = target?.CombatId; slot.SourceCard = source;
        SummonedThisTurn = true;
        if (SummonsThisTurn >= 1 && Relics.FmBonus.ExtraSummonCost(player) > 0)
            await FmRules.Raw(() => CreatureCmd.Damage(ctx, player.Creature, Relics.FmBonus.ExtraSummonCost(player), ValueProp.Unblockable | ValueProp.Unpowered, null, null));
        if (SummonsThisCombat == 0) slot.AtkBonus += Relics.FmBonus.FirstSummonAtk(player);
        SummonsThisTurn++; SummonsThisCombat++;

        var creature = slot.Creature(player);
        if (slot.Index == 0)
        {
            if (player.IsOstyAlive) await CreatureCmd.SetMaxAndCurrentHp(player.Osty!, FmRules.MonsterHp);
            else await OstyCmd.Summon(ctx, player, FmRules.MonsterHp, source);
        }
        else if (creature is { IsAlive: true })
        {
            await CreatureCmd.SetMaxAndCurrentHp(creature, FmRules.MonsterHp);
        }
        else
        {
            creature = slot.Index == 1 ? await PlayerCmd.AddPet<FmZonePet2>(player) : await PlayerCmd.AddPet<FmZonePet3>(player);
            await CreatureCmd.SetMaxAndCurrentHp(creature, FmRules.MonsterHp);
        }
        foreach (var z in Slots) PlaceZone(player, z);
        slot.Visual.Attach(player);
        NotifyChanged();
    }

    /// <summary>Zones 2 and 3 stand in a row to the right of Osty's spot.</summary>
    public static void PlaceZone(Player player, FmSlot slot)
    {
        if (slot.Index == 0) return;
        var room = NCombatRoom.Instance;
        var me = room?.GetCreatureNode(player.Creature);
        var node = slot.Creature(player) is { } c ? room?.GetCreatureNode(c) : null;
        if (me == null || node == null) return;
        node.Position = me.Position + new Godot.Vector2(me.Hitbox.Size.X * 0.5f + 150f + slot.Index * 190f, -75f);
    }

    public static void TogglePosition(FmSlot slot)
    {
        if (slot.MonsterId == 0) return;
        if (slot.Position == FmPosition.Attack && slot.AttackedThisTurn) return; // attacked: can't go to Defense this turn
        slot.Position = slot.Position == FmPosition.Attack ? FmPosition.Defense : FmPosition.Attack;
        FmSfx.Play(FmSfx.Flip);
        NotifyChanged();
    }

    /// <summary>End of the player's turn: Defense-position monsters brace. (Attack-position monsters only attack when you order them to.)</summary>
    public static async Task ResolveEndOfTurn(PlayerChoiceContext ctx, Player player)
    {
        foreach (var slot in Living(player).ToList())
        {
            if (slot.Position == FmPosition.Defense)
                await FmRules.Raw(() => CreatureCmd.GainBlock(player.Creature, slot.Def, ValueProp.Unpowered, null));
        }
    }

    /// <summary>A monster's one attack this turn (clicked, or automatically at end of turn).</summary>
    public static async Task Attack(PlayerChoiceContext ctx, Player player, FmSlot slot, Creature? chosen)
    {
        if (!slot.Alive(player) || slot.Position != FmPosition.Attack || slot.AttackedThisTurn) return;
        if (FmRival.BlockFirstTurnAttack()) return; // FM: the player going first can't attack on turn 1
        var combat = player.Creature.CombatState;
        if (combat == null || slot.Atk <= 0 || slot.SourceCard == null) return;
        if (chosen != null) slot.TargetCombatId = chosen.CombatId;
        slot.AttackedThisTurn = true;
        NotifyChanged();
        Creature? target = combat.HittableEnemies.FirstOrDefault(e => e.CombatId == slot.TargetCombatId && e.IsAlive && (!FmRival.Active || FmRival.CanBeAttacked(e)))
            ?? combat.HittableEnemies.Where(e => e.IsAlive && (!FmRival.Active || FmRival.CanBeAttacked(e))).OrderBy(e => e.CurrentHp).FirstOrDefault();
        if (target == null) return;
        slot.TargetCombatId = target.CombatId;
        if (NCombatRoom.Instance?.GetCreatureNode(target) is { } tn)
        {
            // each monster attacks its own way (style from its type, name and card text; colors from its art)
            var at = tn.Hitbox.GetGlobalRect().GetCenter();
            var from = slot.Visual.CardCenter();
            if (FmAttackVfx.FliesIn(slot.MonsterId)) await slot.Visual.FlyTo(at);
            if (FmRival.SlotOf(target) is { } rs) at = rs.Visual.CardCenter();
            await FmAttackVfx.Play(slot.MonsterId, from, at);
        }
        if (FmRival.Active)
        {
            // boss duel: an FM battle instead of damage
            await FmRival.PlayerAttack(ctx, player, slot, target);
            slot.Visual.FlyHome();
            NotifyChanged();
            return;
        }
        await CreatureCmd.Damage(ctx, target, slot.Atk, ValueProp.Move, player.Creature, slot.SourceCard);
        slot.Visual.FlyHome();
    }

    public static async Task OnMonsterDestroyed(PlayerChoiceContext ctx, Player player, FmSlot slot)
    {
        if (slot.MonsterId == 0) return;
        int burn = slot.Atk * Relics.FmBonus.DeathBurnPercent(player) / 100;
        var foes = player.Creature.CombatState?.HittableEnemies.Where(e => e.IsAlive).ToList();
        var source = slot.SourceCard;
        slot.Clear();
        FmSfx.Play(FmSfx.Destroy);
        NotifyChanged();
        if (burn > 0 && foes is { Count: > 0 })
            await CreatureCmd.Damage(ctx, foes[Random.Shared.Next(foes.Count)], burn, ValueProp.Unpowered, player.Creature, source);
    }
}
