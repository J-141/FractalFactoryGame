using Godot;

public enum InteractionMode {
    Normal,
    HoldingItem
}
public sealed class InteractionState {
    public InteractionMode Mode { get; private set; } = InteractionMode.Normal;

    public Item? HeldItem { get; private set; }

    public Vector2 CursorScreenPosition { get; internal set; }

    public int HoldItemRotation { get; private set; }

    public void EnterNormalMode() {
        Mode = InteractionMode.Normal;
        HeldItem = null;
        HoldItemRotation = 0;
    }

    public void EnterHoldingItemMode(Item item) {
        Mode = InteractionMode.HoldingItem;
        HeldItem = item;
        HoldItemRotation = 0;
    }

}