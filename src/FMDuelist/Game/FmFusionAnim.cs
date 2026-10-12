using Godot;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace FMDuelist.Game;

/// <summary>One step of a fusion chain: current + next -> result (null = failed, next replaces current).</summary>
public enum FmStepKind { Fuse, JunkFail, Sacrifice, Start }

/// <summary>One step of a fusion chain: current + next -> result (null = failed, next replaces current). Junk steps carry the junk card.</summary>
public record FmFusionStep(int Current, int Next, int? Result, bool IsEquip = false, bool EquipOk = false,
    MegaCrit.Sts2.Core.Models.CardModel? Junk = null, FmStepKind Kind = FmStepKind.Fuse);

/// <summary>FM-style chain playback: successes spin together into the result; failures knock the old card away.</summary>
public static class FmFusionAnim
{
    const float Scale = 1.15f;

    public static async Task Play(IReadOnlyList<FmFusionStep> steps, int firstId)
    {
        if (steps.Count == 0 || NCombatRoom.Instance is not { } room || Engine.GetMainLoop() is not SceneTree tree) return;
        var layer = new CanvasLayer { Layer = 90 };
        tree.Root.AddChild(layer);
        var view = room.GetViewportRect().Size;
        var center = view / 2 + new Vector2(0, -60);
        var dim = new ColorRect { Color = new Color(0, 0, 0, 0), Size = view, MouseFilter = Control.MouseFilterEnum.Ignore };
        layer.AddChild(dim);
        await Run(Tw(dim, "color:a", 0.45f, 0.15));

        Node2D? cur = Holder(layer, firstId, center + new Vector2(-170, 0));
        await Run(Tw(cur, "position", center, 0.2));

        foreach (var s in steps)
        {
            if (s.Kind == FmStepKind.Start)
            {
                cur = Holder(layer, s.Next, center + new Vector2(view.X * 0.6f, 0));
                await Run(Tw(cur, "position", center, 0.25));
                continue;
            }
            if (cur == null) continue;
            var next = s.Junk != null ? JunkHolder(layer, s.Junk, center + new Vector2(view.X * 0.6f, 0)) : Holder(layer, s.Next, center + new Vector2(view.X * 0.6f, 0));
            var t = next.CreateTween();
            t.TweenProperty(next, "position", center + new Vector2(150, 0), 0.22).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.Out);
            await Run(t);

            if (s.Kind == FmStepKind.JunkFail)
            {
                // junk never fuses: it bounces off the monster and is knocked away
                FmSfx.Play(FmSfx.Fail);
                var bump = next.CreateTween();
                bump.TweenProperty(next, "position", center + new Vector2(40, 0), 0.08);
                bump.TweenProperty(next, "position", center + new Vector2(view.X * 0.7f, -260), 0.35).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
                bump.Parallel().TweenProperty(next, "rotation", 2.5f, 0.35);
                await Run(bump);
                next.QueueFree();
                continue;
            }
            if (s.Kind == FmStepKind.Sacrifice)
            {
                // the cursed card drags the monster down: both burn away
                var hit = next.CreateTween();
                hit.TweenProperty(next, "position", center + new Vector2(40, 0), 0.12);
                await Run(hit);
                FmSfx.Play(FmSfx.Destroy);
                await Burn(layer, cur, next);
                cur = null;
                continue;
            }
            if (s.IsEquip)
            {
                FmSfx.Play(s.EquipOk ? FmSfx.PowerUp : FmSfx.Fail);
                var e = next.CreateTween();
                e.TweenProperty(next, "position", center, 0.18);
                e.Parallel().TweenProperty(next, "scale", Vector2.One * 0.2f, 0.18);
                e.Parallel().TweenProperty(next, "modulate:a", 0f, 0.18);
                await Run(e);
                next.QueueFree();
                if (s.EquipOk) await Flash(layer, view, cur);
                continue;
            }

            if (s.Result is int res)
            {
                // both cards slide together while spinning, flash, then the fused card appears
                FmSfx.Play(FmSfx.Fusion);
                var spin = cur.CreateTween().SetParallel();
                spin.TweenProperty(cur, "position", center, 0.45).SetTrans(Tween.TransitionType.Sine);
                spin.TweenProperty(next, "position", center, 0.45).SetTrans(Tween.TransitionType.Sine);
                spin.TweenProperty(cur, "rotation", Mathf.Tau * 2, 0.45);
                spin.TweenProperty(next, "rotation", -Mathf.Tau * 2, 0.45);
                spin.TweenProperty(cur, "scale", Vector2.One * 0.4f, 0.45);
                spin.TweenProperty(next, "scale", Vector2.One * 0.4f, 0.45);
                await Run(spin);
                cur.QueueFree(); next.QueueFree();
                var white = new ColorRect { Color = new Color(1, 1, 0.85f, 0.9f), Size = view, MouseFilter = Control.MouseFilterEnum.Ignore };
                layer.AddChild(white);
                cur = Holder(layer, res, center);
                cur.Scale = Vector2.One * 0.3f;
                var pop = cur.CreateTween().SetParallel();
                pop.TweenProperty(white, "color:a", 0f, 0.35);
                pop.TweenProperty(cur, "scale", Vector2.One * Scale, 0.3).SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
                await Run(pop);
                white.QueueFree();
                await Wait(tree, 0.25);
            }
            else
            {
                // the new card rams the old one, which flies off screen
                FmSfx.Play(FmSfx.Fail);
                var ram = next.CreateTween();
                ram.TweenProperty(next, "position", center + new Vector2(40, 0), 0.08);
                await Run(ram);
                var knock = cur.CreateTween().SetParallel();
                knock.TweenProperty(cur, "position", center + new Vector2(-view.X * 0.7f, -260), 0.35).SetTrans(Tween.TransitionType.Quad).SetEase(Tween.EaseType.In);
                knock.TweenProperty(cur, "rotation", -2.5f, 0.35);
                knock.TweenProperty(next, "position", center, 0.2);
                await Run(knock);
                cur.QueueFree();
                cur = next;
            }
        }
        await Wait(tree, 0.35);
        var fade = dim.CreateTween().SetParallel();
        fade.TweenProperty(dim, "color:a", 0f, 0.2);
        if (cur != null) fade.TweenProperty(cur, "modulate:a", 0f, 0.2);
        await Run(fade);
        layer.QueueFree();
    }

    /// <summary>A base-game card (junk) shown with the game's own card visuals.</summary>
    static Node2D JunkHolder(Node parent, MegaCrit.Sts2.Core.Models.CardModel junk, Vector2 pos)
    {
        var n = new Node2D { Position = pos, Scale = Vector2.One * Scale };
        try
        {
            var model = junk.CardScope?.CreateCard(MegaCrit.Sts2.Core.Models.ModelDb.GetById<MegaCrit.Sts2.Core.Models.CardModel>(junk.Id), junk.Owner!);
            if (model != null && MegaCrit.Sts2.Core.Nodes.Cards.NCard.Create(model) is { } card)
            {
                card.MouseFilter = Control.MouseFilterEnum.Ignore;
                card.Scale = Vector2.One * 0.5f;
                n.AddChild(card);
            }
        }
        catch (Exception ex) { MegaCrit.Sts2.Core.Logging.Log.Warn("[FMDuelist] junk card visual: " + ex.Message); }
        parent.AddChild(n);
        return n;
    }

    /// <summary>Both cards flare, char and crumble away in sparks.</summary>
    static async Task Burn(Node layer, params Node2D[] cards)
    {
        foreach (var c in cards)
        {
            var sparks = new CpuParticles2D
            {
                Position = c.Position, Amount = 70, Lifetime = 0.9, OneShot = true, Explosiveness = 0.6f,
                EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle, EmissionRectExtents = new Vector2(80, 115),
                Direction = new Vector2(0, -1), Spread = 35, InitialVelocityMin = 60, InitialVelocityMax = 180, Gravity = new Vector2(0, -60),
                ScaleAmountMin = 3, ScaleAmountMax = 7, Color = new Color(1f, 0.55f, 0.1f),
            };
            layer.AddChild(sparks);
            sparks.Emitting = true;
        }
        var t = cards[0].CreateTween().SetParallel();
        foreach (var c in cards) t.TweenProperty(c, "modulate", new Color(2.2f, 1.2f, 0.35f, 1f), 0.15);
        await Run(t);
        var t2 = cards[0].CreateTween().SetParallel();
        foreach (var c in cards) t2.TweenProperty(c, "modulate", new Color(0.25f, 0.08f, 0.02f, 0f), 0.6);
        await Run(t2);
        foreach (var c in cards) c.QueueFree();
    }

    /// <summary>Step Back: the Frantic Escape cards spin together into one, then it hits the field and vanishes.</summary>
    public static async Task PlayStepBack(MegaCrit.Sts2.Core.Models.CardModel card, int steps)
    {
        if (NCombatRoom.Instance is not { } room || Engine.GetMainLoop() is not SceneTree tree) return;
        var layer = new CanvasLayer { Layer = 90 };
        tree.Root.AddChild(layer);
        var view = room.GetViewportRect().Size;
        var center = view / 2 + new Vector2(0, -60);
        Node2D cur = JunkHolder(layer, card, center + new Vector2(-170, 0));
        await Run(Tw(cur, "position", center, 0.2));
        for (int i = 1; i < steps; i++)
        {
            var next = JunkHolder(layer, card, center + new Vector2(view.X * 0.6f, 0));
            await Run(Tw(next, "position", center + new Vector2(150, 0), 0.22));
            FmSfx.Play(FmSfx.Fusion);
            var spin = cur.CreateTween().SetParallel();
            spin.TweenProperty(cur, "position", center, 0.35);
            spin.TweenProperty(next, "position", center, 0.35);
            spin.TweenProperty(cur, "rotation", Mathf.Tau, 0.35);
            spin.TweenProperty(next, "rotation", -Mathf.Tau, 0.35);
            await Run(spin);
            next.QueueFree();
            cur.Rotation = 0;
        }
        var label = new Label { Text = $"STEP BACK x{steps}", Position = center + new Vector2(-150, 150), Size = new Vector2(300, 60), HorizontalAlignment = HorizontalAlignment.Center };
        label.AddThemeFontSizeOverride("font_size", 40);
        label.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.3f));
        label.AddThemeConstantOverride("outline_size", 8);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        layer.AddChild(label);
        FmSfx.Play(FmSfx.PowerUp);
        await Wait(tree, 0.6);
        var fade = cur.CreateTween().SetParallel();
        fade.TweenProperty(cur, "scale", Vector2.One * 0.2f, 0.3);
        fade.TweenProperty(cur, "modulate:a", 0f, 0.3);
        fade.TweenProperty(label, "modulate:a", 0f, 0.4);
        await Run(fade);
        layer.QueueFree();
    }

    static Node2D Holder(Node parent, int id, Vector2 pos)
    {
        var n = new Node2D { Position = pos, Scale = Vector2.One * Scale };
        n.AddChild(FmCardFace.Create(id));
        parent.AddChild(n);
        return n;
    }

    static async Task Flash(Node layer, Vector2 view, Node2D card)
    {
        var t = card.CreateTween();
        t.TweenProperty(card, "modulate", new Color(1.8f, 1.8f, 1.2f), 0.1);
        t.TweenProperty(card, "modulate", Colors.White, 0.2);
        await Run(t);
    }

    static async Task Run(Tween t) => await t.ToSignal(t, Tween.SignalName.Finished);
    static Tween Tw(Node n, string prop, Variant v, double d) { var t = n.CreateTween(); t.TweenProperty(n, prop, v, d); return t; }

    static async Task Wait(SceneTree tree, double s)
    {
        var timer = tree.CreateTimer(s);
        await timer.ToSignal(timer, SceneTreeTimer.SignalName.Timeout);
    }
}
