using FMDuelist.Cards;
using FMDuelist.Data;
using FMDuelist.Game;
using FMDuelist.Powers;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Entities.Rewards;
using MegaCrit.Sts2.Core.Rewards;

namespace FMDuelist.Relics;

/// <summary>Starter relic of the FM Necrobinder. Carries every duel rule in sheets/systems.json.</summary>
public sealed class MillenniumPuzzle : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Starter;
    public override bool SpawnsPets => true;

    private readonly HashSet<Creature> _scaled = new();
    private int _storyStage;

    /// <summary>How far the Act 1 story ending has got (see FmAct1Story.Stage*), saved with the run.</summary>
    [MegaCrit.Sts2.Core.Saves.Runs.SavedProperty]
    public int StoryStage { get => _storyStage; set { AssertMutable(); _storyStage = value; } }

    private bool _storyMap;

    /// <summary>Runs started with the story Act 1 (older runs keep their own map).</summary>
    [MegaCrit.Sts2.Core.Saves.Runs.SavedProperty]
    public bool StoryMap { get => _storyMap; set { AssertMutable(); _storyMap = value; } }

    private int[] _beaten = Array.Empty<int>(), _restDuels = Array.Empty<int>();

    /// <summary>The duelists you've beaten this run (FM duelist numbers): the rest sites' Free Duels offer them.</summary>
    [MegaCrit.Sts2.Core.Saves.Runs.SavedProperty]
    public int[] BeatenDuelists { get => _beaten; set { AssertMutable(); _beaten = value ?? Array.Empty<int>(); } }

    private int[] _freeRecord = Array.Empty<int>();

    /// <summary>Free Duel record this run: [duelist*2] wins, [duelist*2+1] losses.</summary>
    [MegaCrit.Sts2.Core.Saves.Runs.SavedProperty]
    public int[] FreeRecord { get => _freeRecord; set { AssertMutable(); _freeRecord = value ?? Array.Empty<int>(); } }

    private int[] _deckSel = Array.Empty<int>(), _newIds = Array.Empty<int>(), _newAt = Array.Empty<int>();
    private int _combats;

    /// <summary>Your duel Deck (up to 40 FM card numbers chosen from everything you own).</summary>
    [MegaCrit.Sts2.Core.Saves.Runs.SavedProperty]
    public int[] DeckSel { get => _deckSel; set { AssertMutable(); _deckSel = value ?? Array.Empty<int>(); } }

    /// <summary>Cards won recently (marked NEW in Build Deck) and the combat count when each was won.</summary>
    [MegaCrit.Sts2.Core.Saves.Runs.SavedProperty]
    public int[] NewIds { get => _newIds; set { AssertMutable(); _newIds = value ?? Array.Empty<int>(); } }

    [MegaCrit.Sts2.Core.Saves.Runs.SavedProperty]
    public int[] NewAt { get => _newAt; set { AssertMutable(); _newAt = value ?? Array.Empty<int>(); } }

    /// <summary>Combats fought this run.</summary>
    [MegaCrit.Sts2.Core.Saves.Runs.SavedProperty]
    public int Combats { get => _combats; set { AssertMutable(); _combats = value; } }

    /// <summary>The rest site (floor) and the Free Duels left there.</summary>
    [MegaCrit.Sts2.Core.Saves.Runs.SavedProperty]
    public int[] RestDuels { get => _restDuels; set { AssertMutable(); _restDuels = value ?? Array.Empty<int>(); } }

    /// <summary>Act 1 is Forbidden Memories' story on one straight path.</summary>
    public override MegaCrit.Sts2.Core.Map.ActMap ModifyGeneratedMap(MegaCrit.Sts2.Core.Runs.IRunState runState, MegaCrit.Sts2.Core.Map.ActMap map, int actIndex)
        => actIndex == 0 && StoryMap && runState.Players.Contains(Owner) ? new FmStoryActMap() : map;

    public override bool TryModifyRewards(Player player, List<MegaCrit.Sts2.Core.Rewards.Reward> rewards, AbstractRoom? room)
        => Mine(player) && ((FmRestDuel.Rewards(player, rewards, room) || (FmAct1Story.HeishinRewards(player, rewards, room) | FmRelicDrops.Apply(player, rewards, room) | (room is CombatRoom && FmDuelEnd.AddDrop(player, rewards)) | FmWild.Rewards(player, rewards, room)))
            | rewards.RemoveAll(r => r is MegaCrit.Sts2.Core.Rewards.PotionReward) > 0); // no potions in an FM run
    private readonly List<(Creature attacker, int trapId, decimal hit)> _sprung = new();

    private bool Mine(Player? p) => p == Owner;
    private bool IsMineOrOsty(Creature? c) => c != null && (c == Owner.Creature || (c.PetOwner == Owner && (c.Monster is Osty || c.Monster is FmZonePet)));
    private static bool IsZone(Creature c) => c.Monster is Osty || c.Monster is FmZonePet;
    private static bool FromFm(CardModel? src) => src is FmCardBase;

    private void Track() { if (Owner?.RunState != null) FmRules.ActIndex = Owner.RunState.CurrentActIndex; }

    public override Task AfterObtained()
    {
        Track();
        if (Owner?.RunState is { CurrentActIndex: 0 } rs && rs.ActFloor <= 1 && !FmPractice.Active) StoryMap = true;
        return Task.CompletedTask;
    }
    public override Task AfterRoomEntered(AbstractRoom room)
    {
        Track(); FmInput.Ensure(); FmMusic.OnRoomEntered(room, Owner);
        // the start of the story: Simon scolds the prince (before the starting choice)
        if (room is EventRoom er && er.CanonicalEvent is MegaCrit.Sts2.Core.Models.Events.Neow && Owner.RunState.CurrentActIndex == 0 && !FmPractice.Active)
            MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(FmAct1Story.SimonScolds());
        _shopDragonPending = false;
        if (room.RoomType == RoomType.Shop && Owner.RunState.CurrentActIndex == 0 && Owner.PlayerRng.Rewards.NextFloat() < FmSheet.Act1ShopDragonChance)
            _shopDragonPending = true;
        return Task.CompletedTask;
    }
    public override Task AfterActEntered() { Track(); return Task.CompletedTask; }
    public override CardCreationOptions ModifyCardRewardCreationOptions(Player player, CardCreationOptions options)
    {
        Track();
        if (!Mine(player) || options.CustomCardPool != null || !options.CardPools.Any(p => p is FmCardPool)) return options;
        var prior = options.CardPoolFilter;
        int act = FmRules.ActIndex;
        return options.WithCardPools(options.CardPools.ToList(), c => (prior == null || prior(c)) && FmSheet.OfferedInAct(c, act));
    }

    // --- enemy scale x100 ---
    public override async Task BeforeCombatStart()
    {
        FmField.ResetCombat();
        { var me = Owner; Godot.Callable.From(() => FmDeathFx.DressPlayer(me)).CallDeferred(); } // Duel Master K's portrait
        await FmEnergyFilter.CleanBelt(Owner);
        FmInput.Ensure();
        _scaled.Clear();
        foreach (var e in Owner.Creature.CombatState!.Enemies) await ScaleEnemy(e);
        await FmStatRework.StripCaps(Owner.Creature.CombatState);
        // Act 1 boss: Forbidden Memories' plaza and duel ground scenes first
        if (FmAct1Story.Enabled(Owner) && Owner.Creature.CombatState!.Enemies.Any(e => e.Monster is MegaCrit.Sts2.Core.Models.Monsters.KinPriest))
        {
            FmAct1Story.Reset();
            try { await FmAct1Story.PreBoss(); } catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] pre-boss scene: " + ex.Message); }
            FmMusic.OnRoomEntered(Owner.RunState.CurrentRoom!, Owner);
        }
        // Act 1 story duels: the duelist's lines first
        var storyDuel = Owner.Creature.CombatState!.Enemies.Select(e => FmStoryAct1.ForMonster(e.Monster?.GetType())).FirstOrDefault(d => d != null);
        if (storyDuel != null && !FmPractice.Active && !FmRestDuel.Active)
        {
            try { await FmAct1Story.PreDuel(storyDuel); } catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] pre-duel: " + ex.Message); }
            FmMusic.OnRoomEntered(Owner.RunState.CurrentRoom!, Owner);
        }
        FmLifePoints.Show();
        // Build Deck: before a duel you may change your Deck; every fight uses only your Deck (up to 40 cards)
        if (!FmPractice.Active)
        {
            bool duel = Owner.Creature.CombatState!.Enemies.Any(e => e.Monster != null && FmRival.AllDuelists.Any(b => b.Monster == e.Monster.GetType()));
            if (duel) { try { await FmDeckBuild.BeforeDuel(Owner); } catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] build deck: " + ex.Message); } }
            await FmDeckBuild.ApplyToCombat(Owner);
            // whatever the scene or Build Deck played, the duel starts with its own theme
            if (Owner.RunState.CurrentRoom is { } here) { FmMusic.Stop(); FmMusic.OnRoomEntered(here, Owner); }
        }
        // boss duel: the Kin Priest duels by FM rules
        if (Owner.Creature.CombatState!.Enemies.Any(e => e.Monster != null && FmRival.AllDuelists.Any(b => b.Monster == e.Monster.GetType()))) await FmRival.Begin(Owner, Owner.Creature.CombatState!);
        // a rest site's Free Duel: 8000 LP
        await FmRestDuel.Begin(Owner);
        if (storyDuel != null && FmRival.Priest != null) FmStoryAct1.Dress(FmRival.Priest, storyDuel);
        // the story's boss and Heishin wear their FM pictures too
        else if (FmRival.Priest != null && (FmStoryAct1.StoryRun(Owner.RunState) || FmRestDuel.Active))
        {
            if (FmRival.Current == FmRival.Bosses[0]) FmStoryAct1.Dress(FmRival.Priest, FmStoryAct1.SetoLook);
            else if (FmAct1Story.IsHeishin(FmRival.Priest)) FmStoryAct1.Dress(FmRival.Priest, FmStoryAct1.HeishinLook);
        }
        // the secret level: Heishin's unwinnable duel
        if (!FmRestDuel.Active && Owner.Creature.CombatState!.Enemies.Any(FmAct1Story.IsHeishin))
            await FmAct1Story.StartHeishin(new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext(), Owner);
        // Doom items cast a Shadow Spell instead (see FmShadow)
        try { await FmShadow.CombatStart(Owner, new MegaCrit.Sts2.Core.GameActions.Multiplayer.BlockingPlayerChoiceContext()); }
        catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] shadow start: " + ex.Message); }
    }

    public override async Task AfterCreatureAddedToCombat(Creature creature)
    {
        if (creature.IsEnemy) { await ScaleEnemy(creature); await FmStatRework.StripCaps(creature.CombatState); }
    }

    private async Task ScaleEnemy(Creature e)
    {
        if (!_scaled.Add(e) || e.Monster is FmWildMonster) return; // wild monsters: 10x DEF already
        await CreatureCmd.SetMaxAndCurrentHp(e, (decimal)e.MaxHp * FmRules.EnemyScale);
    }

    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (FmRules.RawDepth > 0 || FromFm(cardSource)) return 1m;
        if (dealer != null && dealer.IsEnemy) return FmRules.EnemyScale;
        if (dealer != null && IsMineOrOsty(dealer) && (target == null || target.IsEnemy)) return FmRules.EnemyScale;
        if (dealer == null && target != null && (target.IsEnemy || IsMineOrOsty(target)) && amount < FmRules.FlatLimit) return FmRules.EnemyScale; // poison, status cards, event HP costs
        return 1m;
    }

    public override decimal ModifyBlockMultiplicative(Creature target, decimal block, ValueProp props, CardModel? cardSource, CardPlay? cardPlay)
    {
        if (FmRules.RawDepth > 0) return 1m; // already in FM numbers (e.g. an enemy's Defense Position Block = its hit)
        if (target.IsEnemy) return FmRules.EnemyScale;
        if (FromFm(cardSource)) return 1m;
        if (target == Owner.Creature) return FmRules.EnemyScale; // relics, powers, potions, colorless/base cards
        return 1m;
    }

    // --- Swords of Revealing Light, traps, and FM damage calculation (per hit) ---
    // While a monster is on the field the duelist takes no enemy damage. Each enemy hit is its own battle:
    // Attack position: a hit weaker than its ATK can't destroy it, and the enemy takes the difference back; an equal hit
    // destroys it as it hits back for its ATK; a stronger hit destroys it and the duelist takes what the hit exceeds ATK by.
    // Defense position: Block soaks the hit; an unbroken Block reflects what is left of it; a broken Block destroys the monster.
    // The monster only dies to enemy attacks. Once it is gone, later hits land on the duelist normally.
    private readonly Queue<(Creature dealer, FmSlot zone, FmPosition pos, int atk, int block, decimal amount)> _hits = new();

    // Enemy attacks on the duelist land on the monster the enemy picks (see FmField.EnemyTarget).
    /// <summary>Damage an enemy's Thorns deal back to whoever attacked it.</summary>
    private static bool IsThorns(Creature? dealer, ValueProp props) =>
        dealer != null && dealer.IsEnemy && !props.IsPoweredAttack() && dealer.GetPower<ThornsPower>() != null;

    public override Creature ModifyUnblockedDamageTarget(Creature target, decimal amount, ValueProp props, Creature? dealer)
    {
        // Thorns: when your monster attacks a thorny enemy, the thorns hurt you (your Life Points), not the monster
        if (IsThorns(dealer, props) && IsZone(target) && target.PetOwner == Owner) return Owner.Creature;
        if (dealer == null || !dealer.IsEnemy || !props.IsPoweredAttack()) return target;
        if (target != Owner.Creature && !(IsZone(target) && target.PetOwner == Owner)) return target;
        return FmField.EnemyTarget(Owner, (int)amount)?.Creature(Owner) ?? target;
    }

    public override decimal ModifyHpLostBeforeOsty(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (FmAct1Story.IsHeishin(target) && !FmRestDuel.Active) return Math.Min(amount, Math.Max(0, target.CurrentHp - 1));
        if (FmRival.IsZoneCreature(target)) return 0; // the boss's monster zones only lose monsters in FM battles
        if (amount <= 0 || !IsMineOrOsty(target)) return amount;
        bool enemyHit = dealer != null && dealer.IsEnemy;
        if (IsZone(target) && !(enemyHit && props.IsPoweredAttack())) return 0;
        // an enemy hit weaker than an Attack Position monster's ATK doesn't destroy it
        if (enemyHit && props.IsPoweredAttack() && FmField.SlotOf(target) is { Position: FmPosition.Attack } held && amount < held.Atk) return 0;
        if (enemyHit && props.IsPoweredAttack() && FmField.Traps.Count > 0)
        {
            for (int i = 0; i < FmField.Traps.Count; i++)
            {
                int id = FmField.Traps[i];
                if (!FmSheet.Traps.TryGetValue(id, out var t)) continue;
                if (t.kind == FmTrapKind.DestroyLe && amount > t.value) continue;
                FmField.Traps.RemoveAt(i);
                _sprung.Add((dealer!, id, amount));
                _hits.Clear();
                return 0;
            }
        }
        if (target == Owner.Creature && IsThorns(dealer, props)) return amount; // Thorns go straight to your Life Points
        if (target == Owner.Creature && enemyHit && FmField.HasMonster(Owner) && !props.IsPoweredAttack()) return 0;
        return amount;
    }

    // Overkill from the monster spills onto the duelist in the base game; in a duel the duelist only takes the ATK excess.
    public override decimal ModifyHpLostAfterOsty(Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (target == Owner.Creature && dealer != null && dealer.IsEnemy && _hits.Count > 0) return 0;
        return amount;
    }

    public override Task BeforeDamageReceived(PlayerChoiceContext ctx, Creature target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (dealer != null && dealer.IsEnemy && props.IsPoweredAttack() && IsMineOrOsty(target)
            && (FmField.Slots.FirstOrDefault(s => s.Alive(Owner) && s.Creature(Owner) == target) ?? FmField.EnemyTarget(Owner, (int)amount)) is { } front)
            _hits.Enqueue((dealer, front, front.Position, front.Position == FmPosition.Attack ? front.Atk : front.Def, Owner.Creature.Block, amount));
        return Task.CompletedTask;
    }

    public override async Task AfterDamageReceived(PlayerChoiceContext ctx, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
    {
        if (_sprung.Count > 0)
        {
            var list = _sprung.ToList();
            _sprung.Clear();
            foreach (var (attacker, id, sprungHit) in list)
            {
                FmSfx.Play(FmSfx.Trap);
                var trap = FmSheet.Traps[id];
                if (attacker.IsAlive)
                {
                    switch (trap.kind)
                    {
                        case FmTrapKind.DestroyLe: await FmRules.Raw(() => CreatureCmd.Damage(ctx, attacker, trap.value, ValueProp.Unpowered, Owner.Creature, null)); break;
                        case FmTrapKind.DestroyAny: await FmRules.Raw(() => CreatureCmd.Damage(ctx, attacker, 3000, ValueProp.Unpowered, Owner.Creature, null)); break;
                        case FmTrapKind.ReflectBurn: await FmRules.Raw(() => CreatureCmd.Damage(ctx, attacker, sprungHit, ValueProp.Unpowered, Owner.Creature, null)); break;
                        case FmTrapKind.Weak: await PowerCmd.Apply<WeakPower>(ctx, attacker, trap.value, Owner.Creature, null); break;
                        case FmTrapKind.ReverseBuffs:
                            foreach (var p in attacker.Powers.Where(p => p.Type == PowerType.Buff).ToList()) await PowerCmd.Remove(p);
                            break;
                    }
                }
                if (Owner.Creature.GetPower<FmTrapPower>() is { } tp) await PowerCmd.Decrement(tp);
                FmField.NotifyChanged();
            }
        }

        if (_hits.Count == 0 || dealer == null || !IsMineOrOsty(target)) return;
        var h = _hits.Dequeue();
        var foe = h.dealer;
        int hit = (int)h.amount;
        if (h.pos == FmPosition.Attack)
        {
            int excess = hit - h.atk;
            if (excess > 0)
            {
                // the enemy overpowers the monster: no hit-back, the duelist takes the difference
                await FmRules.Raw(() => CreatureCmd.Damage(ctx, Owner.Creature, excess, ValueProp.Unblockable | ValueProp.Unpowered, null, null));
            }
            else if (excess < 0)
            {
                // the monster holds: the enemy takes the difference back
                if (foe.IsAlive) await HitBack(ctx, foe, -excess * FmBonus.CounterMultiplier(Owner), h.zone);
            }
            else if (foe.IsAlive) await HitBack(ctx, foe, h.atk * FmBonus.CounterMultiplier(Owner), h.zone);
        }
        else if (hit <= h.block && h.block - hit > 0 && foe.IsAlive)
        {
            await HitBack(ctx, foe, h.block - hit, h.zone);
        }
    }

    private async Task HitBack(PlayerChoiceContext ctx, Creature foe, int amount, FmSlot zone)
    {
        if (zone.MonsterId == 0) FmSfx.Play(FmSfx.Slash);
        if (MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance?.GetCreatureNode(foe) is { } an)
        {
            var at = an.Hitbox.GetGlobalRect().GetCenter();
            if (zone.MonsterId != 0) _ = FmAttackVfx.Play(zone.MonsterId, zone.Visual.CardCenter(), at);
            else FmSlashVfx.Play(at);
        }
        var src = zone.SourceCard ?? FmField.Slots.Select(z => z.SourceCard).FirstOrDefault(c => c != null);
        if (src == null) await FmRules.Raw(() => CreatureCmd.Damage(ctx, foe, amount, ValueProp.Unpowered, Owner.Creature, null));
        else await CreatureCmd.Damage(ctx, foe, amount, ValueProp.Unpowered, Owner.Creature, src);
    }

    public override Task BeforeAttack(AttackCommand command) { _hits.Clear(); return Task.CompletedTask; }

    // --- Swords of Revealing Light / Spellbinding Circle: never in Act 1, very uncommon in Act 2, uncommon in Act 3 ---
    static readonly int[] KitCards = { 348, 349 };
    const float KitAct2KeepChance = 0.3f;

    static bool IsKitCard(CardModel? c) => c is FmCardBase f && KitCards.Contains(f.FmId);

    /// <summary>Whether a Swords/Spellbinding that the game rolled may stay (Act 3 keeps its normal Uncommon odds).</summary>
    static bool KitMayAppear(Player player) => player.RunState.CurrentActIndex switch
    {
        0 => false,
        1 => player.PlayerRng.Rewards.NextFloat() < KitAct2KeepChance,
        _ => true,
    };

    /// <summary>A different Uncommon FM card in its place.</summary>
    static CardModel KitReplacement(Player player)
    {
        var pool = FmSheet.PoolTypes.Select(t => ModelDb.GetById<CardModel>(ModelDb.GetId(t)))
            .Where(c => c.Rarity == CardRarity.Uncommon && !IsKitCard(c)).ToList();
        var pick = pool[player.PlayerRng.Rewards.NextInt(0, pool.Count)];
        return player.RunState.CreateCard(pick, player);
    }

    static bool GateKit(Player player, IEnumerable<CardCreationResult> options)
    {
        bool changed = false;
        foreach (var o in options)
            if (IsKitCard(o.Card) && !KitMayAppear(player)) { o.ModifyCard(KitReplacement(player)); changed = true; }
        return changed;
    }

    // --- Act 1: each card reward has a 20% chance to offer a weak Dragon ---
    public override bool TryModifyCardRewardOptions(Player player, List<CardCreationResult> options, CardCreationOptions creationOptions)
    {
        bool gated = Mine(player) && GateKit(player, options);
        return TryAct1Dragon(player, options, creationOptions) || gated;
    }

    private bool TryAct1Dragon(Player player, List<CardCreationResult> options, CardCreationOptions creationOptions)
    {
        if (!Mine(player) || player.RunState.CurrentActIndex != 0 || options.Count == 0 || creationOptions.CustomCardPool != null) return false;
        if (!creationOptions.CardPools.Any(p => p is FmCardPool) || creationOptions.Source != CardCreationSource.Encounter) return false;
        var rng = player.PlayerRng.Rewards;
        if (rng.NextFloat() >= FmSheet.Act1DragonChance) return false;
        int id = FmSheet.WeakDragons[rng.NextInt(0, FmSheet.WeakDragons.Length)];
        var dragon = player.RunState.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(id))), player);
        options[rng.NextInt(0, options.Count)].ModifyCard(dragon);
        return true;
    }

    // --- Act 1 shops: 40% chance one of the shop's cards is a weak Dragon (rolled once per shop visit) ---
    private bool _shopDragonPending;

    public override void ModifyMerchantCardCreationResults(Player player, List<CardCreationResult> cards)
    {
        if (Mine(player)) GateKit(player, cards);
        if (!Mine(player) || !_shopDragonPending) return;
        var entry = cards.FirstOrDefault(c => !c.HasBeenModified && c.Card is FmMonsterCard or FmSpellCard && !FmSheet.ColorlessIds.Contains(((FmCardBase)c.Card).FmId));
        if (entry == null) return;
        _shopDragonPending = false;
        int id = FmSheet.WeakDragons[player.PlayerRng.Rewards.NextInt(0, FmSheet.WeakDragons.Length)];
        entry.ModifyCard(player.RunState.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(id))), player));
    }

    // --- no upgrades in Forbidden Memories ---
    public override decimal ModifyCardRewardUpgradeOdds(Player player, CardModel card, decimal odds) => Mine(player) ? 0m : odds;

    // --- Skip really skips: the card reward is completed (gone) instead of waiting on the rewards screen ---
    public override bool TryModifyCardRewardAlternatives(Player player, CardReward cardReward, List<CardRewardAlternative> alternatives)
    {
        if (!Mine(player)) return false;
        int i = alternatives.FindIndex(a => a.AfterSelected == PostAlternateCardRewardAction.EndSelectionAndDoNotCompleteReward);
        if (i < 0) return false;
        alternatives[i] = new CardRewardAlternative(alternatives[i].OptionId, PostAlternateCardRewardAction.EndSelectionAndCompleteReward);
        return true;
    }

    // --- Rest sites: Cut (remove up to 5 cards) replaces Smith; FM cards don't upgrade ---
    public override bool TryModifyRestSiteOptions(Player player, ICollection<RestSiteOption> options)
    {
        if (!Mine(player)) return false;
        var smith = options.FirstOrDefault(o => o is SmithRestSiteOption);
        if (smith != null) options.Remove(smith);
        // Free Duels with the duelists you've beaten (in place of Cut), and Build Deck
        options.Add(new FmFreeDuelRestSiteOption(player));
        options.Add(new FmDeckRestSiteOption(player));
        return true;
    }

    // --- Exodia: five pieces in hand win the duel ---
    public override async Task AfterCardDrawn(PlayerChoiceContext ctx, CardModel card, bool fromHandDraw)
    {
        if (card.Owner == Owner && card is FmCardBase f && FmExodia.Pieces.Contains(f.FmId)) await FmExodia.CheckAndObliterate(Owner);
    }

    public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? clonedBy)
    {
        // a card joined your Chest: NEW for 3 combats
        if (card.Owner == Owner && oldPileType == PileType.None && card.Pile?.Type == PileType.Deck && card is FmCardBase won && !FmPractice.Active) FmDeckBuild.Acquired(Owner, won.FmId);
        if (card.Owner == Owner && card is FmCardBase f && FmExodia.Pieces.Contains(f.FmId) && card.Pile?.Type == PileType.Hand) await FmExodia.CheckAndObliterate(Owner);
    }

    // the shop's card removal also respects the minimum deck size
    public override bool ShouldAllowMerchantCardRemoval(Player player) => !Mine(player) || player.Deck.Cards.Count > FmRules.MinDeckFor(player);

    // Exodia pieces are never sold
    public override IEnumerable<CardModel> ModifyMerchantCardPool(Player player, IEnumerable<CardModel> options)
        => !Mine(player) ? options : options.Where(c => !(c is FmCardBase f && FmExodia.Pieces.Contains(f.FmId))).ToList();

    // --- FM hand: draw up to 5, never discard the hand ---
    public override decimal ModifyHandDraw(Player player, decimal count)
    {
        if (!Mine(player) || player.PlayerCombatState == null) return count;
        return Math.Max(0, FmBonus.HandSize(player) - player.PlayerCombatState.Hand.Cards.Count);
    }

    public override bool ShouldFlush(Player player) => !Mine(player);

    // --- Guardian Stars: Strength/Dexterity become +500 ATK/DEF for your monsters until your next turn ---
    public override async Task AfterPowerAmountChanged(PlayerChoiceContext choiceContext, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        await FmGuardianStars.OnPowerChanged(Owner, power, amount);
        await FmStatRework.OnPowerChanged(Owner, power, amount);
        // an enemy in Defense Position still carries out the rest of its move
        if (amount > 0 && applier != null && applier == EnemyDefensePatch.DefenderNow(applier.CombatState))
            EnemyDefensePatch.StillUses(applier, power.Title.GetFormattedText());
    }

    // --- Forbidden Memories duel bookkeeping and the end of a duel ---
    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (FmRival.Active && cardPlay.Card is FmCardBase fc && cardPlay.Card.Owner == Owner)
        {
            FmDuelStats.You.CardsUsed++;
            if (fc is FmSpellCard { Kind: FmSpellKind.Equip }) FmDuelStats.Equips++;
            else if (fc is FmSpellCard) FmDuelStats.Magic++;
        }
        return Task.CompletedTask;
    }

    public override bool ShouldDie(Creature creature)
    {
        // the unwinnable Heishin duel isn't a game over
        if (creature == Owner.Creature && FmAct1Story.State == FmAct1Story.Phase.Lost) return false;
        if (creature == Owner.Creature && FmRestDuel.Lost) return false;
        if (FmAct1Story.IsHeishin(creature) && FmAct1Story.State != FmAct1Story.Phase.None) return false;
        // the practice duel never ends the run: whoever falls, the results show and you go back to the main menu
        if (!FmPractice.Active || !FmRival.Active) return true;
        if (creature == Owner.Creature || creature == FmRival.Priest)
        {
            bool won = creature == FmRival.Priest;
            int turns = Owner.Creature.CombatState?.RoundNumber ?? 1;
            int comLp = Math.Max(0, FmRival.Priest?.CurrentHp ?? 0);
            FmRival.Active = false;
            MegaCrit.Sts2.Core.Helpers.TaskHelper.RunSafely(FmPractice.Finish(won, Owner, won ? 0 : comLp, turns));
            return false;
        }
        return true;
    }

    public override async Task BeforeDeath(Creature creature)
    {
        // one of your monsters: its card starts burning now (before its zone creature is removed)
        if (creature.PetOwner == Owner && FmField.SlotOf(creature) is { } dying) dying.Visual.BurnNow();
        // a game over outside a duel: Duel Master K burns away
        if (creature == Owner.Creature && !FmRival.Active && FmAct1Story.State != FmAct1Story.Phase.HeishinDuel && !FmPractice.Active)
            await FmDeathFx.Player(creature);
        // Act 1 story: losing to Heishin leads into the Puzzle
        if (FmAct1Story.State == FmAct1Story.Phase.HeishinDuel && creature == Owner.Creature)
        {
            await FmAct1Story.AfterHeishin(Owner);
            return;
        }
        if (!FmRival.Active || FmPractice.Active || FmRival.Current == null) return;
        if (creature != Owner.Creature && creature != FmRival.Priest) return;
        // a rest site's Free Duel lost: results, then back to the rest site (not a game over)
        if (FmRestDuel.Active && creature == Owner.Creature) { await FmRestDuel.OnLoss(Owner); return; }
        bool won = creature == FmRival.Priest;
        int turns = Owner.Creature.CombatState?.RoundNumber ?? 1;
        int comLp = won ? 0 : Math.Max(0, FmRival.Priest?.CurrentHp ?? 0);
        var duel = FmRival.Current;
        await FmDuelEnd.Show(won, Owner, comLp, duel.Name, turns, spoils: !FmRestDuel.Active, dropFrom: duel.DuelistIndex);
        if (FmRestDuel.Active) { FmRestDuel.Ranked(FmDuelEnd.LastKind, FmDuelEnd.LastLetter); return; }
        if (won) FmRestDuel.MarkBeaten(Owner, duel);
        // a story duelist's closing lines
        if (won && FmStoryAct1.ForMonster(duel.Monster) is { } sd) await FmAct1Story.PostDuel(sd);
    }

    public override Task AfterCardGeneratedForCombat(CardModel card, Player? creator)
    {
        if (creator == null && card.Owner == Owner && EnemyDefensePatch.DefenderNow(Owner.Creature.CombatState) is { } d)
            EnemyDefensePatch.StillUses(d, card.Title);
        return Task.CompletedTask;
    }

    public override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (side == CombatSide.Player && participants.Contains(Owner.Creature)) { FmGuardianStars.Reset(); FmField.NotifyChanged(); }
        return FmStatRework.StripCaps(combatState as MegaCrit.Sts2.Core.Combat.ICombatState ?? Owner.Creature.CombatState);
    }

    // --- turn flow ---
    public override async Task AfterPlayerTurnStartLate(PlayerChoiceContext ctx, Player player)
    {
        // FM: no reshuffle; not being able to draw a full hand is a loss (deck out)
        if (Mine(player)) await FmDeckBuild.CheckDeckOut(player, FmBonus.HandSize(player));
    }

    public override Task AfterPlayerTurnStart(PlayerChoiceContext ctx, Player player)
    {
        if (Mine(player))
        {
            _ = FmStatRework.StripCaps(player.Creature.CombatState);
            FmField.SummonedThisTurn = false;
            FmField.SummonsThisTurn = 0;
            FmField.MassSpellThisTurn = false;
            foreach (var z in FmField.Slots) { z.AttackedThisTurn = false; z.TempDef = 0; }
            FmField.Duelist = player;
            FmField.NotifyChanged();
            FmTutorial.EnsureButton();
            FmTutorial.MaybeShow(player);
        }
        return Task.CompletedTask;
    }

    public override async Task BeforeSideTurnEnd(PlayerChoiceContext ctx, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Player) return;
        await FmField.ResolveEndOfTurn(ctx, Owner);
        await HandLimit(ctx);
        await StunForSpells();
    }

    /// <summary>
    /// Swords of Revealing Light (3 turns) and Spellbinding Circle (1 turn) stun every enemy for its coming turn.
    /// A duelist is never stunned: Swords stops his attacks, Spellbinding takes 500 ATK/DEF off his monsters.
    /// </summary>
    private async Task StunForSpells()
    {
        bool swords = FmField.SwordsTurns > 0, bind = FmField.BindStun;
        FmField.BindStun = false;
        if (!swords && !bind) return;
        foreach (var e in Owner.Creature.CombatState!.Enemies.Where(e => e.IsAlive).ToList())
        {
            if (FmRival.IsZoneCreature(e)) continue;
            if (e == FmRival.Priest) continue; // a duelist is never stunned: Spellbinding only weakens his monsters
            try { await CreatureCmd.Stun(e); } catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] stun: " + ex.Message); }
        }
    }

    /// <summary>
    /// Hand limit: if you end your turn holding more than 5 cards, you choose which ones to discard down to 5.
    /// (The choice is only skipped when every card has to go anyway.)
    /// </summary>
    private async Task HandLimit(PlayerChoiceContext ctx)
    {
        var hand = Owner.PlayerCombatState?.Hand.Cards;
        if (hand == null) return;
        int excess = hand.Count - FmRules.HandSize;
        if (excess <= 0) return;
        var prefs = new MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs(MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs.DiscardSelectionPrompt, excess, excess) { RequireManualConfirmation = true };
        var picked = (await CardSelectCmd.FromHandForDiscard(ctx, Owner, prefs, null, this)).ToList();
        if (picked.Count > 0) await CardCmd.Discard(ctx, picked);
    }

    public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (side != CombatSide.Player || FmField.SwordsTurns <= 0) return;
        FmField.SwordsTurns--;
        if (Owner.Creature.GetPower<FmSwordsPower>() is { } sp) await PowerCmd.Decrement(sp);
    }

    public override async Task AfterDeath(PlayerChoiceContext ctx, Creature creature, bool wasRemovalPrevented, float deathAnimLength)
    {
        if (creature.PetOwner == Owner && FmField.SlotOf(creature) is { } zone) await FmField.OnMonsterDestroyed(ctx, Owner, zone);
        if (FmRival.Active && creature == FmRival.Priest)
        {
            foreach (var z in FmRival.Slots) if (z.RivalCreature is { IsAlive: true } zc) await CreatureCmd.Kill(zc);
            FmRival.Active = false;
        }
    }

    public override Task AfterCombatVictory(CombatRoom room)
    {
        // the boss duel's win jingle and results music play in FmDuelEnd; a wild monster beaten gets the victory theme too
        if (room.Encounter is FmWildEncounter) FmMusic.PlayOnce(FmDuelEnd.WinJingle);
        return Task.CompletedTask;
    }

    public override async Task AfterCombatEnd(CombatRoom room)
    {
        if (!FmPractice.Active) FmDeckBuild.CombatDone(Owner);
        FmLifePoints.Clear();
        await FmRestDuel.AfterCombat(Owner);
        FmField.ResetCombat();
        FmRival.Reset();
        FmAct1Story.Reset();
        FmStoryAct1.ClearBackground();
    }
}

/// <summary>Run-start option: adds all five pieces of Exodia the Forbidden One to the deck.</summary>
public sealed class ExodiaCardBox : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Ancient;
    public override bool IsAllowedInShops => false;

    public override async Task AfterObtained()
    {
        foreach (var id in FmExodia.Pieces)
        {
            var card = Owner.RunState.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(id))), Owner);
            await CardPileCmd.Add(card, PileType.Deck);
        }
        var pool = ModelDb.CardPool<FmCardPool>().AllCards.Where(c => FmSheet.OfferedInAct(c, 0)).ToList();
        var rng = new Random();
        while (Owner.Deck.Cards.Count < FmRules.MinDeck && pool.Count > 0)
            await CardPileCmd.Add(Owner.RunState.CreateCard(pool[rng.Next(pool.Count)], Owner), PileType.Deck);
    }
}

/// <summary>Run-start blessing: dragon fusions of level 5+ are allowed; adds Baby Dragon and Petit Dragon.</summary>
/// <summary>Neow starter pack: Swords of Revealing Light and Spellbinding Circle.</summary>
public sealed class MagicProtectionKit : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Ancient;
    public override bool IsAllowedInShops => false;

    public override async Task AfterObtained()
    {
        foreach (int id in new[] { 348, 349 })
        {
            var card = Owner.RunState.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(id))), Owner);
            await CardPileCmd.Add(card, PileType.Deck);
        }
    }
}

public sealed class DragonTreasure : RelicModel
{
    public override RelicRarity Rarity => RelicRarity.Ancient;
    public override bool IsAllowedInShops => false;

    public override async Task AfterObtained()
    {
        // Joey's Trump Card: Baby Dragon and Time Wizard (fuse them for Thousand Dragon)
        foreach (int id in new[] { 4, FmWild.Id("Time Wizard") })
        {
            if (id <= 0 || FmSheet.TypeFor(id) == null) continue;
            var card = Owner.RunState.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(FmSheet.TypeFor(id))), Owner);
            await CardPileCmd.Add(card, PileType.Deck);
        }
    }
}
