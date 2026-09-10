using System;

public readonly record struct ComplexShapeKey(
    Shape C0,
    Shape C1,
    Shape C2,
    Shape C3) {
    public Shape this[int index] => index switch {
        0 => C0,
        1 => C1,
        2 => C2,
        3 => C3,
        _ => throw new ArgumentOutOfRangeException(nameof(index))
    };
}