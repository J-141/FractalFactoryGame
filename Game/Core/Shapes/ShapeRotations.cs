using System;

public readonly record struct ShapeRotations(
    Shape R0,
    Shape R120,
    Shape R240);

public readonly record struct Item {
    public Shape Shape { get; }

    internal Item(Shape representative) {
        Shape = representative;
    }
}

public readonly record struct ItemStack {
    public Item Item { get; }

    public int Count { get; }

    public ItemStack(
        Item item,
        int count) {

        if (count <= 0) {
            throw new ArgumentOutOfRangeException(
                nameof(count),
                "ItemStack count must be greater than zero.");
        }

        Item =
            item;

        Count =
            count;
    }

    public ItemStack WithCount(
        int count) {

        return new ItemStack(
            Item,
            count);
    }

    public override string ToString() {
        return $"{Item} x{Count}";
    }
}