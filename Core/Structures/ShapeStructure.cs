using Godot;
using System;
using System.Collections.Generic;


public sealed class ShapeStructure : Structure {
    private const int MaxDrawSubdivisionDepth = 3;

    private static readonly TriangleGridPosition LocalCell =
        new(
            0,
            0,
            TriangleFacing.Down);


    public Shape Shape { get; set; }


    public ShapeStructure(
        Vector2I anchor,
        Shape shape,
        int gridRotation = 0)
        : base(anchor, gridRotation) {
        Shape = shape;
    }


    protected override IEnumerable<TriangleGridPosition>
        EnumerateLocalOccupiedCells() {
        yield return LocalCell;
    }


    /// <summary>
    /// 返回用于绘制该 ShapeStructure 的实心三角形集合。
    ///
    /// 特性：
    ///
    /// 1. 四个完整 child 会尽可能合并成 parent triangle；
    ///
    /// 2. Inline Shape 可以在最多 3 层 subdivision 内精确表示；
    ///
    /// 3. Complex Shape 最多展开 3 层，
    ///    因此最多生成 4^3 = 64 个三角形；
    ///
    /// 4. 到达最大深度后仍然是 Complex 的区域，
    ///    会作为一个完整 triangle 绘制，
    ///    即作为 rendering LOD approximation。
    /// </summary>
    public IEnumerable<TriangleVertices>
        EnumerateDrawTriangles(
            TriangleGrid grid,
            ShapeStore shapeStore) {
        ArgumentNullException.ThrowIfNull(grid);
        ArgumentNullException.ThrowIfNull(shapeStore);

        TriangleVertices rootTriangle =
            GetRootTriangle(grid);

        foreach (
            TriangleVertices triangle
            in EnumerateShapeTriangles(
                Shape,
                rootTriangle,
                shapeStore,
                depth: 0)) {
            yield return triangle;
        }
    }


    /// <summary>
    /// 获得 Structure transform 之后的 root triangle。
    ///
    /// 不直接使用 Grid.GetVertices(occupiedCell)，
    /// 因为这里需要保留 V0/V1/V2 在 GridRotation
    /// 之后的对应关系。
    /// </summary>
    private TriangleVertices GetRootTriangle(
        TriangleGrid grid) {
        var (localV0, localV1, localV2) =
            TriangleGrid.GetCellVertices(
                LocalCell);

        Vector2I worldV0 =
            TriangleGrid.TransformVertex(
                localV0,
                Anchor,
                GridRotation);

        Vector2I worldV1 =
            TriangleGrid.TransformVertex(
                localV1,
                Anchor,
                GridRotation);

        Vector2I worldV2 =
            TriangleGrid.TransformVertex(
                localV2,
                Anchor,
                GridRotation);

        return new TriangleVertices(
            grid.GetVertexPosition(worldV0),
            grid.GetVertexPosition(worldV1),
            grid.GetVertexPosition(worldV2));
    }


    private static IEnumerable<TriangleVertices>
        EnumerateShapeTriangles(
            Shape shape,
            TriangleVertices triangle,
            ShapeStore shapeStore,
            int depth) {
        /*
		 * Empty：
		 * 什么都不画。
		 */
        if (shape == ShapeStore.Empty)
            yield break;


        /*
		 * Full：
		 *
		 * 直接画当前大 triangle，
		 * 不继续拆成4个 child。
		 *
		 * 这就是最主要的 4 -> 1 merge。
		 */
        if (shape == ShapeStore.Full) {
            yield return triangle;
            yield break;
        }


        /*
		 * Inline 在 depth == 3 时一定已经落到 Full / Empty，
		 * 因此正常不会进入这里。
		 *
		 * 如果 Complex 到这里仍有更细结构，
		 * 就停止展开，把整个区域视作一个 occupied
		 * render pixel。
		 */
        if (depth >= MaxDrawSubdivisionDepth) {
            yield return triangle;
            yield break;
        }


        ComplexShapeKey children =
            shapeStore.Expand(shape);


        /*
		 * 理论上 ShapeStore 的 canonical invariant
		 * 已保证“四个 Full child”应该直接成为 Full。
		 *
		 * 这里仍显式检查一次，使 renderer 不依赖
		 * ComplexShapePool 是否始终 canonical。
		 */
        if (children.C0 == ShapeStore.Full &&
            children.C1 == ShapeStore.Full &&
            children.C2 == ShapeStore.Full &&
            children.C3 == ShapeStore.Full) {
            yield return triangle;
            yield break;
        }


        foreach (
            TriangleVertices childTriangle
            in EnumerateShapeTriangles(
                children.C0,
                triangle.GetChild(0),
                shapeStore,
                depth + 1)) {
            yield return childTriangle;
        }

        foreach (
            TriangleVertices childTriangle
            in EnumerateShapeTriangles(
                children.C1,
                triangle.GetChild(1),
                shapeStore,
                depth + 1)) {
            yield return childTriangle;
        }

        foreach (
            TriangleVertices childTriangle
            in EnumerateShapeTriangles(
                children.C2,
                triangle.GetChild(2),
                shapeStore,
                depth + 1)) {
            yield return childTriangle;
        }

        foreach (
            TriangleVertices childTriangle
            in EnumerateShapeTriangles(
                children.C3,
                triangle.GetChild(3),
                shapeStore,
                depth + 1)) {
            yield return childTriangle;
        }
    }
}