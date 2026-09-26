using Godot;
using System;

/// <summary>
/// UI-only renderer for an ItemStack's Shape.
///
/// The preview root triangle faces up and uses:
///
///                 V0
///                /  \
///               /    \
///             V2------V1
///
/// Shape content is recursively filled inside that root triangle.
/// ShapeBorderColor / ShapeBorderWidth draw the border of the ROOT triangle,
/// not the occupied-region outline.
/// </summary>
public partial class ShapeItemPreviewControl
    : Control {

    [Export]
    public Color ShapeColor { get; set; } =
        Colors.White;

    [Export]
    public Color ShapeBorderColor { get; set; } =
        Colors.White;

    [Export(PropertyHint.Range, "0.5,8,0.5")]
    public float ShapeBorderWidth { get; set; } =
        2.0f;

    [Export(PropertyHint.Range, "0,8,1")]
    public int MaxSubdivisionDepth { get; set; } =
        3;

    [Export(PropertyHint.Range, "0,24,1")]
    public float Padding { get; set; } =
        6.0f;

    private ShapeStore? _shapeStore;
    private ItemStack? _stack;
    private int _rotation;

    public override void _Ready() {
        MouseFilter =
            MouseFilterEnum.Ignore;
    }

    public void Initialize(
        ShapeStore shapeStore) {

        _shapeStore =
            shapeStore
            ?? throw new ArgumentNullException(
                nameof(shapeStore));

        QueueRedraw();
    }

    public void SetStack(
        ItemStack? stack,
        int rotation = 0) {

        _stack =
            stack;

        _rotation =
            ((rotation % 3) + 3) % 3;

        QueueRedraw();
    }

    public override void _Draw() {
        if (_shapeStore is null ||
            !_stack.HasValue) {
            return;
        }

        Shape shape =
            GetDisplayedShape(
                _stack.Value.Item.Shape,
                _rotation);

        TriangleVertices root =
            GetRootTriangle();

        if (shape != ShapeStore.Empty) {
            DrawShape(
                shape,
                root,
                depth: 0);
        }

        DrawRootBorder(
            root);
    }

    private Shape GetDisplayedShape(
        Shape canonicalShape,
        int rotation) {

        return rotation switch {
            0 =>
                canonicalShape,

            1 =>
                _shapeStore!.Rotate120(
                    canonicalShape),

            2 =>
                _shapeStore!.Rotate240(
                    canonicalShape),

            _ =>
                throw new InvalidOperationException(),
        };
    }

    private TriangleVertices GetRootTriangle() {
        float availableWidth =
            Mathf.Max(
                Size.X - Padding * 2.0f,
                0.0f);

        float availableHeight =
            Mathf.Max(
                Size.Y - Padding * 2.0f,
                0.0f);

        const float heightRatio =
            0.8660254037844386f;

        float width =
            Mathf.Min(
                availableWidth,
                availableHeight /
                    heightRatio);

        float height =
            width *
            heightRatio;

        float left =
            (Size.X - width) *
            0.5f;

        float top =
            (Size.Y - height) *
            0.5f;

        /*
         * Facing-up preview frame:
         *
         *                 V0
         *                /  \
         *               /    \
         *             V2------V1
         *
         * V0 = top
         * V1 = bottom-right
         * V2 = bottom-left
         */
        return new TriangleVertices(
            new Vector2(
                left + width * 0.5f,
                top),

            new Vector2(
                left + width,
                top + height),

            new Vector2(
                left,
                top + height));
    }

    private void DrawShape(
        Shape shape,
        TriangleVertices triangle,
        int depth) {

        if (shape == ShapeStore.Empty)
            return;

        if (shape == ShapeStore.Full ||
            depth >= MaxSubdivisionDepth) {

            DrawColoredPolygon(
                new[] {
                    triangle.V0,
                    triangle.V1,
                    triangle.V2,
                },
                ShapeColor);

            return;
        }

        ComplexShapeKey children =
            _shapeStore!.Expand(
                shape);

        if (children.C0 == ShapeStore.Full &&
            children.C1 == ShapeStore.Full &&
            children.C2 == ShapeStore.Full &&
            children.C3 == ShapeStore.Full) {

            DrawColoredPolygon(
                new[] {
                    triangle.V0,
                    triangle.V1,
                    triangle.V2,
                },
                ShapeColor);

            return;
        }

        DrawShape(
            children.C0,
            triangle.GetChild(0),
            depth + 1);

        DrawShape(
            children.C1,
            triangle.GetChild(1),
            depth + 1);

        DrawShape(
            children.C2,
            triangle.GetChild(2),
            depth + 1);

        DrawShape(
            children.C3,
            triangle.GetChild(3),
            depth + 1);
    }

    private void DrawRootBorder(
        TriangleVertices root) {

        if (ShapeBorderWidth <= 0.0f)
            return;

        DrawLine(
            root.V0,
            root.V1,
            ShapeBorderColor,
            ShapeBorderWidth,
            antialiased: true);

        DrawLine(
            root.V1,
            root.V2,
            ShapeBorderColor,
            ShapeBorderWidth,
            antialiased: true);

        DrawLine(
            root.V2,
            root.V0,
            ShapeBorderColor,
            ShapeBorderWidth,
            antialiased: true);
    }
}
