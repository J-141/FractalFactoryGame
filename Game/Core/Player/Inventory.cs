using System;

/// <summary>
/// Fixed-slot inventory with Minecraft/Factorio-style item stacking.
///
/// Quantity belongs to ItemStack/Inventory, not Item.
///
/// Add behavior:
/// 1. fill existing stacks of the same Item;
/// 2. then consume empty slots;
/// 3. return the amount that could not fit.
///
/// Stack size policy is intentionally simple for now. All Items share the same
/// DefaultMaxStackSize; this can later be replaced by item-specific rules.
/// </summary>
public sealed class Inventory {
    public const int DefaultMaxStackSize =
        100;

    private readonly ItemStack?[] _slots;

    /// <summary>
    /// Raised once after an operation changes one or more slots.
    /// Intended for UI/presentation refresh.
    /// </summary>
    public event Action? Changed;

    public int SlotCount =>
        _slots.Length;

    public Inventory(
        int slotCount = 30) {

        if (slotCount <= 0) {
            throw new ArgumentOutOfRangeException(
                nameof(slotCount),
                "Inventory must contain at least one slot.");
        }

        _slots =
            new ItemStack?[slotCount];
    }

    public ItemStack? GetStack(
        int slotIndex) {

        ValidateSlotIndex(
            slotIndex);

        return _slots[
            slotIndex];
    }

    /// <summary>
    /// Add count items to the inventory.
    ///
    /// Returns the number of items that could not fit.
    /// </summary>
    public int Add(
        Item item,
        int count) {

        if (count < 0) {
            throw new ArgumentOutOfRangeException(
                nameof(count));
        }

        if (count == 0)
            return 0;

        int remaining =
            count;

        int maxStackSize =
            GetMaxStackSize(
                item);

        for (int i = 0;
             i < _slots.Length &&
             remaining > 0;
             i++) {

            ItemStack? stack =
                _slots[i];

            if (!stack.HasValue ||
                stack.Value.Item != item) {
                continue;
            }

            int available =
                maxStackSize -
                stack.Value.Count;

            if (available <= 0)
                continue;

            int moved =
                Math.Min(
                    available,
                    remaining);

            _slots[i] =
                new ItemStack(
                    item,
                    stack.Value.Count + moved);

            remaining -=
                moved;
        }

        for (int i = 0;
             i < _slots.Length &&
             remaining > 0;
             i++) {

            if (_slots[i].HasValue)
                continue;

            int moved =
                Math.Min(
                    maxStackSize,
                    remaining);

            _slots[i] =
                new ItemStack(
                    item,
                    moved);

            remaining -=
                moved;
        }

        if (remaining != count) {
            Changed?.Invoke();
        }

        return remaining;
    }

    /// <summary>
    /// Remove up to count instances of item.
    ///
    /// Returns the number actually removed.
    /// </summary>
    public int Remove(
        Item item,
        int count) {

        if (count < 0) {
            throw new ArgumentOutOfRangeException(
                nameof(count));
        }

        if (count == 0)
            return 0;

        int remainingToRemove =
            count;

        int removed =
            0;

        for (int i = 0;
             i < _slots.Length &&
             remainingToRemove > 0;
             i++) {

            ItemStack? stack =
                _slots[i];

            if (!stack.HasValue ||
                stack.Value.Item != item) {
                continue;
            }

            int moved =
                Math.Min(
                    stack.Value.Count,
                    remainingToRemove);

            int newCount =
                stack.Value.Count -
                moved;

            _slots[i] =
                newCount == 0
                    ? null
                    : new ItemStack(
                        item,
                        newCount);

            remainingToRemove -=
                moved;

            removed +=
                moved;
        }

        if (removed > 0) {
            Changed?.Invoke();
        }

        return removed;
    }

    public int GetCount(
        Item item) {

        int total =
            0;

        foreach (ItemStack? stack in _slots) {
            if (stack.HasValue &&
                stack.Value.Item == item) {

                total +=
                    stack.Value.Count;
            }
        }

        return total;
    }

    public ItemStack? TakeStack(
        int slotIndex) {

        ValidateSlotIndex(
            slotIndex);

        ItemStack? result =
            _slots[
                slotIndex];

        if (!result.HasValue)
            return null;

        _slots[
            slotIndex] =
            null;

        Changed?.Invoke();

        return result;
    }

    public void SetStack(
        int slotIndex,
        ItemStack? stack) {

        ValidateSlotIndex(
            slotIndex);

        if (stack.HasValue) {
            int maxStackSize =
                GetMaxStackSize(
                    stack.Value.Item);

            if (stack.Value.Count >
                maxStackSize) {

                throw new ArgumentOutOfRangeException(
                    nameof(stack),
                    $"Stack exceeds max size {maxStackSize}.");
            }
        }

        if (Nullable.Equals(
                _slots[slotIndex],
                stack)) {
            return;
        }

        _slots[
            slotIndex] =
            stack;

        Changed?.Invoke();
    }

    /// <summary>
    /// Return whether the complete amount can be added without modifying the
    /// inventory.
    ///
    /// Gameplay systems use this as a preflight before performing mutations
    /// whose result must be delivered into this inventory.
    /// </summary>
    public bool CanAdd(
        Item item,
        int count) {

        if (count < 0) {
            throw new ArgumentOutOfRangeException(
                nameof(count));
        }

        if (count == 0)
            return true;

        int remaining =
            count;

        int maxStackSize =
            GetMaxStackSize(
                item);

        foreach (ItemStack? stack in _slots) {
            if (!stack.HasValue) {
                remaining -=
                    maxStackSize;
            }
            else if (stack.Value.Item == item) {
                remaining -=
                    Math.Max(
                        maxStackSize -
                        stack.Value.Count,
                        0);
            }

            if (remaining <= 0)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Placeholder stack-size policy.
    /// </summary>
    public int GetMaxStackSize(
        Item item) {

        return DefaultMaxStackSize;
    }

    private void ValidateSlotIndex(
        int slotIndex) {

        if ((uint)slotIndex >=
            (uint)_slots.Length) {

            throw new ArgumentOutOfRangeException(
                nameof(slotIndex));
        }
    }
}
