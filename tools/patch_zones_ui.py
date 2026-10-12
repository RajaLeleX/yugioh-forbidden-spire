import os, re
H = os.path.expanduser('~/fmsts')
p = f'{H}/src/FMDuelist/Game/FmUi.cs'
s = open(p, encoding='utf-8').read()

# ---- input: per-zone right-click toggle and click-to-attack ----
s = s.replace('''        if (FmOstyVisual.IsMouseOver(mouse)) { FmField.TogglePosition(); return true; }
        return false;''', '''        if (ZoneUnderMouse(mouse) is { } z) { FmField.TogglePosition(z); return true; }
        return false;''', 1)

old = s[s.index('    static bool HandleLeftClick(Vector2 mouse)'):s.index('    static void Process()')]
s = s.replace(old, '''    static FmSlot? ZoneUnderMouse(Vector2 mouse)
    {
        var player = FmField.Duelist;
        return FmField.Slots.LastOrDefault(z => z.Alive(player) && z.Visual.IsMouseOver(mouse));
    }

    static bool HandleLeftClick(Vector2 mouse)
    {
        var player = FmField.Duelist;
        if (player == null || ZoneUnderMouse(mouse) is not { } zone) return false;
        if (zone.Position != FmPosition.Attack || zone.AttackedThisTurn) return false;
        _ = AimAndAttack(player, zone);
        return true;
    }

    /// <summary>The game's own targeting arrow from the monster card; the chosen enemy gets that monster's attack.</summary>
    static async Task AimAndAttack(MegaCrit.Sts2.Core.Entities.Players.Player player, FmSlot zone)
    {
        try
        {
            var tm = NTargetManager.Instance;
            tm.StartTargeting(MegaCrit.Sts2.Core.Entities.Cards.TargetType.AnyEnemy, zone.Visual.CardCenter(), TargetMode.ClickMouseToTarget,
                () => !zone.Alive(player), n => n is NCreature c && c.Entity.IsEnemy && c.Entity.IsAlive);
            FmField.Aiming = true;
            FmField.NotifyChanged();
            var node = await tm.SelectionFinished();
            FmField.Aiming = false;
            FmField.NotifyChanged();
            if (node is NCreature target && target.Entity.IsEnemy && target.Entity.IsAlive)
                FmAttackAction.Request(player, zone.Index, target.Entity);
        }
        catch (Exception ex) { FmField.Aiming = false; Log.Warn("[FMDuelist] aim: " + ex.Message); }
    }

''', 1)
s = s.replace("try { UpdateMarks(); FmOstyVisual.Tick();", "try { UpdateMarks(); foreach (var z in FmField.Slots) z.Visual.Tick();", 1)

# ---- visual: static class -> one instance per zone ----
start = s.index('/// <summary>\n/// The summoned monster stands in Osty')
vis = s[start:]
vis = vis.replace('public static class FmOstyVisual\n{', 'public sealed class FmSlotVisual\n{\n    readonly FmSlot _slot;\n    public FmSlotVisual(FmSlot slot) { _slot = slot; }\n', 1)
vis = re.sub(r'\n    static (?!readonly Vector2 Home)(?!NCard\? MakeCard)', '\n    ', vis)
vis = vis.replace('    public static ', '    public ')
vis = vis.replace('    static NCard? MakeCard(int id)', '    NCard? MakeCard(int id)')
vis = vis.replace('    bool Valid =>', '    bool Valid =>')
vis = vis.replace('FmField.MonsterId == 0) return false;', '_slot.MonsterId == 0) return false;')
vis = vis.replace('''        var osty = FmField.Duelist?.Osty;
        return osty != null && NCombatRoom.Instance?.GetCreatureNode(osty) is { } n && n.Hitbox.GetGlobalRect().HasPoint(screenMouse);''',
'''        var creature = FmField.Duelist is { } pl ? _slot.Creature(pl) : null;
        return creature != null && NCombatRoom.Instance?.GetCreatureNode(creature) is { } n && n.Hitbox.GetGlobalRect().HasPoint(screenMouse);''')
vis = vis.replace('''        if (!_subscribed) { FmField.Changed += Refresh; _subscribed = true; }
        var node = player.Osty != null ? NCombatRoom.Instance?.GetCreatureNode(player.Osty) : null;''',
'''        if (!_subscribed) { FmField.Changed += Refresh; _subscribed = true; }
        var creature = _slot.Creature(player);
        var node = creature != null ? NCombatRoom.Instance?.GetCreatureNode(creature) : null;''')
vis = vis.replace('_root = new Node2D { Name = "FMDuelistCard" };', '_root = new Node2D { Name = "FMDuelistCard" + _slot.Index };')
vis = vis.replace('        int id = FmField.MonsterId;', '        int id = _slot.MonsterId;')
vis = vis.replace('tween.TweenProperty(_holder, "rotation", FmField.Position == FmPosition.Defense', 'tween.TweenProperty(_holder, "rotation", _slot.Position == FmPosition.Defense')
vis = vis.replace('''        _stats.Text = $"{FmField.Atk} / {FmField.Def}";
        _stats.AddThemeColorOverride("font_color", FmField.Position == FmPosition.Attack''', '''        _stats.Text = $"{_slot.Atk} / {_slot.Def}";
        _stats.AddThemeColorOverride("font_color", _slot.Position == FmPosition.Attack''')
vis = vis.replace('''            : FmField.Position == FmPosition.Defense ? "DEF  (right-click: ATK)"
            : FmField.AttackedThisTurn ? "ATK  (attacked)" : "ATK  click: attack / right-click: DEF";''', '''            : _slot.Position == FmPosition.Defense ? "DEF  (right-click: ATK)"
            : _slot.AttackedThisTurn ? "ATK  (attacked)" : "ATK  click: attack / right-click: DEF";''')
vis = vis.replace('_holder, "rotation", FmField.Position == FmPosition.Defense ? -Mathf.Pi / 2 : 0f, 0.3);', '_holder, "rotation", _slot.Position == FmPosition.Defense ? -Mathf.Pi / 2 : 0f, 0.3);')
vis = vis.replace('_pos.Text = FmField.Aiming ? "Choose an enemy"', '_pos.Text = FmField.Aiming && !_slot.AttackedThisTurn ? "Choose an enemy"')
s = s[:start] + vis
open(p, 'w', encoding='utf-8').write(s)

# ---- attack action carries the zone ----
p = f'{H}/src/FMDuelist/Game/FmAttackAction.cs'
t = open(p, encoding='utf-8').read()
t = t.replace('    private readonly Creature _target;\n\n    public FmAttackAction(Player player, Creature target) { _player = player; _target = target; }',
              '    private readonly Creature _target;\n    private readonly int _zone;\n\n    public FmAttackAction(Player player, int zone, Creature target) { _player = player; _zone = zone; _target = target; }')
t = t.replace('await FmField.Attack(new GameActionPlayerChoiceContext(this), _player, _target);',
              'await FmField.Attack(new GameActionPlayerChoiceContext(this), _player, FmField.Slots[_zone], _target);')
t = t.replace('    public static void Request(Player player, Creature target) =>\n        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new FmAttackAction(player, target));',
              '    public static void Request(Player player, int zone, Creature target) =>\n        RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new FmAttackAction(player, zone, target));')
open(p, 'w', encoding='utf-8').write(t)
print('ok')
