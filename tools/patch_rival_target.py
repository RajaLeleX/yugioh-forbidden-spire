import os
os.chdir(os.path.expanduser('~/fmsts/src/FMDuelist'))

def patch(p, reps):
    s = open(p, encoding='utf-8').read()
    for a, b in reps:
        assert a in s, (p, a[:70]); s = s.replace(a, b, 1)
    open(p, 'w', encoding='utf-8').write(s)

patch('Game/FmUi.cs', [
('''        if (NTargetManager.Instance is { IsInSelection: true }) return false;
        if (FmField.Duelist == null && FmField.Marked.Count == 0 && NPlayerHand.Instance == null) return false;
        var mouse = vp.GetMousePosition();''', '''        if (NTargetManager.Instance is { IsInSelection: true } aiming)
        {
            // your monster's attack arrow: a click on one of the boss's monster cards picks it, wherever its hit area is
            if (mb.ButtonIndex == MouseButton.Left && FmField.Aiming && RivalTargetUnderMouse(vp.GetMousePosition()) is { } pick)
            {
                PickTarget(aiming, pick);
                return true;
            }
            return false;
        }
        if (FmField.Duelist == null && FmField.Marked.Count == 0 && NPlayerHand.Instance == null) return false;
        var mouse = vp.GetMousePosition();'''),
('''    static FmSlot? ZoneUnderMouse(Vector2 mouse)''', '''    static readonly System.Reflection.PropertyInfo? HoveredNodeProp = AccessTools.Property(typeof(NTargetManager), "HoveredNode");
    static readonly System.Reflection.MethodInfo? FinishTargetingM = AccessTools.Method(typeof(NTargetManager), "FinishTargeting", new[] { typeof(bool) });
    static readonly System.Reflection.MethodInfo? OnCreatureHoveredM = AccessTools.Method(typeof(NTargetManager), "OnCreatureHovered");

    /// <summary>The boss monster whose card is under the mouse, if your monster may attack it.</summary>
    static NCreature? RivalTargetUnderMouse(Vector2 mouse)
    {
        if (!FmRival.Active || NCombatRoom.Instance is not { } room) return null;
        foreach (var s in FmRival.Slots.Reverse())
            if (s.RivalCreature is { IsAlive: true } c && FmRival.CanBeAttacked(c) && s.Visual.IsMouseOverCard(mouse) && room.GetCreatureNode(c) is { } n)
                return n;
        return null;
    }

    static void PickTarget(NTargetManager tm, NCreature node)
    {
        try
        {
            HoveredNodeProp?.SetValue(tm, node);
            FinishTargetingM?.Invoke(tm, new object[] { false });
        }
        catch (Exception ex) { Log.Warn("[FMDuelist] pick target: " + ex.Message); }
    }

    /// <summary>While aiming, hovering a boss monster's card lights it up as the target (reticle and red arrow).</summary>
    static void SyncRivalHover()
    {
        if (!FmField.Aiming || NTargetManager.Instance is not { IsInSelection: true } tm || Engine.GetMainLoop() is not SceneTree t) return;
        if (RivalTargetUnderMouse(t.Root.GetMousePosition()) is not { } n) return;
        if (HoveredNodeProp?.GetValue(tm) == n) return;
        try { OnCreatureHoveredM?.Invoke(tm, new object[] { n }); } catch (Exception ex) { Log.Warn("[FMDuelist] rival hover: " + ex.Message); }
    }

    static FmSlot? ZoneUnderMouse(Vector2 mouse)'''),
('''        try { UpdateMarks(); foreach''', '''        try { UpdateMarks(); SyncRivalHover(); foreach'''),
('''    public bool IsMouseOver(Vector2 screenMouse)
    {''', '''    /// <summary>Is the mouse on this zone's card itself (not counting the creature's hit area)?</summary>
    public bool IsMouseOverCard(Vector2 screenMouse)
    {
        if (!Valid || _card == null || !GodotObject.IsInstanceValid(_card) || !_group!.Visible || _slot.MonsterId == 0 || FmZone.OverlayOpen) return false;
        var local = _holder!.GetGlobalTransformWithCanvas().AffineInverse() * screenMouse;
        var size = _slot.Position == FmPosition.Defense ? new Vector2(NCard.defaultSize.Y, NCard.defaultSize.X) : NCard.defaultSize;
        return new Rect2(-NCard.defaultSize / 2, NCard.defaultSize).HasPoint(local) || new Rect2(-size / 2, size).HasPoint(local);
    }

    public bool IsMouseOver(Vector2 screenMouse)
    {'''),
])
print('ok')
