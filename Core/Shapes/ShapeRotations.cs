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