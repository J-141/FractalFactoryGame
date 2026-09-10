using System;

public enum ShapeKind : byte {
    Inline,
    Complex,
}

public readonly struct Shape : IEquatable<Shape> {
    private readonly ulong _value;

    public ShapeKind Kind { get; }

    private Shape(ShapeKind kind, ulong value) {
        Kind = kind;
        _value = value;
    }

    public static Shape Inline(ulong bits)
        => new(ShapeKind.Inline, bits);

    internal static Shape Complex(ulong id)
        => new(ShapeKind.Complex, id);

    public ulong InlineBits =>
        Kind == ShapeKind.Inline
            ? _value
            : throw new InvalidOperationException(
                "Shape is not inline.");

    public ulong ComplexId =>
        Kind == ShapeKind.Complex
            ? _value
            : throw new InvalidOperationException(
                "Shape is not complex.");

    public bool IsInline => Kind == ShapeKind.Inline;

    public bool IsComplex => Kind == ShapeKind.Complex;

    public bool IsEmpty =>
        Kind == ShapeKind.Inline &&
        _value == 0;

    public bool IsFull =>
        Kind == ShapeKind.Inline &&
        _value == ulong.MaxValue;

    public bool Equals(Shape other)
        => Kind == other.Kind &&
           _value == other._value;

    public override bool Equals(object? obj)
        => obj is Shape other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine((byte)Kind, _value);

    public static bool operator ==(Shape left, Shape right)
        => left.Equals(right);

    public static bool operator !=(Shape left, Shape right)
        => !left.Equals(right);

    public override string ToString()
        => Kind switch {
            ShapeKind.Inline =>
                $"Inline(0x{_value:X16})",

            ShapeKind.Complex =>
                $"Complex({_value})",

            _ => throw new InvalidOperationException()
        };
}
