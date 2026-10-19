using FMDuelist.Data;
using Godot;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace FMDuelist.Game;

/// <summary>
/// Choosing a monster zone the way FM does it: on the field itself. The five zones light up where they are, each
/// with its number and what's in it; click one to summon there (a monster already there is replaced, or fused
/// with). Right click or Esc backs out when that's allowed.
/// </summary>
public static class FmZonePicker
{
    public static bool Open { get; private set; }

    public static async Task<FmSlot?> Pick(Player owner, int monsterId, bool canCancel)
    {
        if (NGame.Instance is not { } game || NCombatRoom.Instance is not { } room || Engine.GetMainLoop() is not SceneTree tree) return null;
        var layer = new CanvasLayer { Layer = 94 };
        game.AddChild(layer);
        Open = true;
        FmSlot? picked = null;
        bool cancelled = false;
        try
        {
            var view = layer.GetViewport().GetVisibleRect().Size;
            var catcher = new Control { Size = view, MouseFilter = Control.MouseFilterEnum.Stop, FocusMode = Control.FocusModeEnum.All };
            catcher.GuiInput += e =>
            {
                if (canCancel && (e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right } || e is InputEventKey { Pressed: true, Keycode: Key.Escape })) cancelled = true;
            };
            layer.AddChild(catcher);
            catcher.GrabFocus();
            var canvas = room.GetViewport().GetCanvasTransform();
            float scale = canvas.Scale.Y;
            var cardSize = new Vector2(300, 422) * FmSlotVisual.CardScaleNow * scale;
            string what = monsterId > 0 ? FmData.Get(monsterId).Name : "this monster";
            var title = FmTextBox.Text($"Choose a zone for {what}", view.Y / 240f * 0.8f, new Color(1f, 0.86f, 0.35f));
            title.Position = new Vector2((view.X - FmTextBox.TextWidth($"Choose a zone for {what}", view.Y / 240f * 0.8f)) / 2, view.Y * 0.06f);
            layer.AddChild(title);
            if (canCancel)
            {
                var hint = FmTextBox.Text("Right click: back", view.Y / 240f * 0.55f, new Color(0.8f, 0.8f, 0.85f));
                hint.Position = new Vector2((view.X - FmTextBox.TextWidth("Right click: back", view.Y / 240f * 0.55f)) / 2, view.Y * 0.06f + view.Y / 240f * 12);
                layer.AddChild(hint);
            }
            foreach (var slot in FmField.Slots)
            {
                if (FmDuelLayout.SpotFor(owner, slot) is not { } world) continue;
                var at = canvas * world;
                var s = slot;
                bool taken = slot.Alive(owner);
                var box = new Panel { Position = at - cardSize / 2, Size = cardSize, MouseFilter = Control.MouseFilterEnum.Ignore };
                var idle = new StyleBoxFlat { BgColor = new Color(0.1f, 0.3f, 0.9f, taken ? 0.18f : 0.32f), BorderColor = new Color(0.55f, 0.75f, 1f), BorderWidthLeft = 4, BorderWidthRight = 4, BorderWidthTop = 4, BorderWidthBottom = 4, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6 };
                var lit = new StyleBoxFlat { BgColor = new Color(0.9f, 0.85f, 0.1f, 0.35f), BorderColor = new Color(1f, 0.95f, 0.2f), BorderWidthLeft = 7, BorderWidthRight = 7, BorderWidthTop = 7, BorderWidthBottom = 7, CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6, CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6 };
                box.AddThemeStyleboxOverride("panel", idle);
                layer.AddChild(box);
                float px = view.Y / 240f * 0.6f;
                var num = FmTextBox.Text($"ZONE {slot.Index + 1}", px);
                num.Position = new Vector2((cardSize.X - FmTextBox.TextWidth($"ZONE {slot.Index + 1}", px)) / 2, 8);
                box.AddChild(num);
                string note = taken ? FmData.Get(slot.MonsterId).Name : "EMPTY";
                if (note.Length > 12) note = note[..12];
                var occ = FmTextBox.Text(note, px * 0.8f, taken ? new Color(1f, 0.8f, 0.6f) : new Color(0.7f, 0.9f, 1f));
                occ.Position = new Vector2((cardSize.X - FmTextBox.TextWidth(note, px * 0.8f)) / 2, cardSize.Y - 12 * px);
                box.AddChild(occ);
                var hit = new Button { Flat = true, Position = box.Position, Size = box.Size, FocusMode = Control.FocusModeEnum.None, MouseDefaultCursorShape = Control.CursorShape.PointingHand };
                foreach (var st in new[] { "normal", "hover", "pressed" }) hit.AddThemeStyleboxOverride(st, new StyleBoxEmpty());
                hit.MouseEntered += () => { box.AddThemeStyleboxOverride("panel", lit); FmSfx.Play(FmSfx.Flip); };
                hit.MouseExited += () => box.AddThemeStyleboxOverride("panel", idle);
                hit.Pressed += () => picked = s;
                hit.GuiInput += e => { if (canCancel && e is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Right }) cancelled = true; };
                layer.AddChild(hit);
            }
            while (picked == null && !cancelled && GodotObject.IsInstanceValid(layer)) await layer.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            FmSfx.Play(picked != null ? FmSfx.Mark : FmSfx.Flip);
        }
        finally
        {
            Open = false;
            if (GodotObject.IsInstanceValid(layer)) layer.QueueFree();
        }
        if (picked == null && !canCancel) picked = FmField.Slots.FirstOrDefault(s => !s.Alive(owner)) ?? FmField.Slots[0];
        return picked;
    }
}
