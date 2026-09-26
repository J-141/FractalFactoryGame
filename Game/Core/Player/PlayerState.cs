using System;

/// <summary>
/// Persistent player-domain state.
///
/// Holding is a cursor/hand stack separate from Inventory storage.
/// </summary>
public sealed class PlayerState {
    public Inventory Inventory { get; }

    /// <summary>
    /// Raised whenever HeldItem or HeldItemRotation changes.
    /// </summary>
    public event Action? HoldingChanged;

    public ItemStack? HeldItem { get; private set; }

    /// <summary>
    /// 0 = representative
    /// 1 = +120 degrees
    /// 2 = +240 degrees
    /// </summary>
    public int HeldItemRotation { get; private set; }
    public bool IsHoldingItem =>
        HeldItem.HasValue;

    public PlayerState(
        Inventory? inventory = null) {

        Inventory =
            inventory
            ?? new Inventory();
    }

    public void Hold(
        ItemStack stack) {

        HeldItem =
            stack;

        HeldItemRotation =
            0;

        HoldingChanged?.Invoke();
    }

    public void Hold(
        Item item,
        int count = 1) {

        Hold(
            new ItemStack(
                item,
                count));
    }

    public void SetHeldItemRotation(
        int rotation) {

        if (!HeldItem.HasValue) {
            throw new InvalidOperationException(
                "Cannot rotate when no item stack is held.");
        }

        int normalized =
            ((rotation % 3) + 3) % 3;

        if (HeldItemRotation ==
            normalized) {
            return;
        }

        HeldItemRotation =
            normalized;

        HoldingChanged?.Invoke();
    }

    public ItemStack? ClearHeldItem() {
        ItemStack? previous =
            HeldItem;

        if (!previous.HasValue)
            return null;

        HeldItem =
            null;

        HeldItemRotation =
            0;

        HoldingChanged?.Invoke();

        return previous;
    }
}
