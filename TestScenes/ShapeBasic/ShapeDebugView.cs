using Godot;
using System;
using System.Numerics;
using Vector2 = Godot.Vector2;

public partial class ShapeDebugView : Node2D {

    [Export]
    public float SideLength { get; set; } = 160.0f;

    [Export]
    public TriangleFacing Facing { get; set; } = TriangleFacing.Up;

    [Export]
    public Color FillColor { get; set; } = Colors.White;

    [Export]
    public Color BorderColor { get; set; } = Colors.Gray;

    [Export]
    public float BorderWidth { get; set; } = 2.0f;

    [Export]
    public int MaxComplexDepth { get; set; } = 16;

    public ShapeStore Store { get; set; } = null!;

    private Shape _shape = ShapeStore.Empty;

    public Shape Shape {
        get => _shape;
        set {
            _shape = value;
            QueueRedraw();
        }
    }

    public override void _Draw() {
        Triangle root = CreateRootTriangle();

        DrawShape(
            Shape,
            root,
            complexDepth: 0);

        DrawTriangleBorder(root);
    }

    private void DrawShape(
        Shape shape,
        Triangle triangle,
        int complexDepth) {
        if (shape == ShapeStore.Empty)
            return;

        if (shape.IsInline) {
            DrawInline(
                shape.InlineBits,
                triangle);

            return;
        }

        if (complexDepth >= MaxComplexDepth) {
            // Debug guard：复杂度过高时只画边界。
            DrawTriangleBorder(triangle);
            return;
        }

        ComplexShapeKey children =
            Store.Expand(shape);

        DrawShape(
            children.C0,
            GetChildTriangle(triangle, 0),
            complexDepth + 1);

        DrawShape(
            children.C1,
            GetChildTriangle(triangle, 1),
            complexDepth + 1);

        DrawShape(
            children.C2,
            GetChildTriangle(triangle, 2),
            complexDepth + 1);

        DrawShape(
            children.C3,
            GetChildTriangle(triangle, 3),
            complexDepth + 1);
    }

    private void DrawInline(
        ulong bits,
        Triangle root) {
        while (bits != 0) {
            int bit =
                BitOperations.TrailingZeroCount(bits);

            bits &= bits - 1;

            /*
			 * Inline bit:
			 *
			 * index = c0 * 16 + c1 * 4 + c2
			 */
            int c0 = (bit >> 4) & 0b11;
            int c1 = (bit >> 2) & 0b11;
            int c2 = bit & 0b11;

            Triangle t = root;

            t = GetChildTriangle(t, c0);
            t = GetChildTriangle(t, c1);
            t = GetChildTriangle(t, c2);

            DrawColoredPolygon(
                new[]
                {
                    t.V0,
                    t.V1,
                    t.V2
                },
                FillColor);
        }
    }

    private Triangle CreateRootTriangle() {
        float h =
            SideLength * Mathf.Sqrt(3.0f) / 2.0f;

        /*
		 * 质心位于 local (0,0)。
		 *
		 * 顶点顺序同时决定：
		 * child0 = V0 corner
		 * child1 = V1 corner
		 * child2 = V2 corner
		 */
        if (Facing == TriangleFacing.Up) {
            return new Triangle(
                new Vector2(0, -2 * h / 3),
                new Vector2(-SideLength / 2, h / 3),
                new Vector2(SideLength / 2, h / 3));
        }

        /*
		 * Down 时保持相同的局部循环顺序。
		 */
        return new Triangle(
            new Vector2(0, 2 * h / 3),
            new Vector2(SideLength / 2, -h / 3),
            new Vector2(-SideLength / 2, -h / 3));
    }

    private static Triangle GetChildTriangle(
        Triangle t,
        int index) {
        Vector2 m01 = (t.V0 + t.V1) * 0.5f;
        Vector2 m12 = (t.V1 + t.V2) * 0.5f;
        Vector2 m20 = (t.V2 + t.V0) * 0.5f;

        return index switch {
            // V0 corner
            0 => new Triangle(
                t.V0,
                m01,
                m20),

            // V1 corner
            1 => new Triangle(
                t.V1,
                m12,
                m01),

            // V2 corner
            2 => new Triangle(
                t.V2,
                m20,
                m12),

            // inverted center
            3 => new Triangle(
                m12,
                m20,
                m01),

            _ => throw new ArgumentOutOfRangeException(
                nameof(index))
        };
    }

    private void DrawTriangleBorder(Triangle t) {
        DrawLine(
            t.V0,
            t.V1,
            BorderColor,
            BorderWidth);

        DrawLine(
            t.V1,
            t.V2,
            BorderColor,
            BorderWidth);

        DrawLine(
            t.V2,
            t.V0,
            BorderColor,
            BorderWidth);
    }

    private readonly record struct Triangle(
        Vector2 V0,
        Vector2 V1,
        Vector2 V2);

}
