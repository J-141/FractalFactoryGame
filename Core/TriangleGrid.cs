using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;


public readonly record struct TriangleGridPosition(
    int X,
    int Y,
    TriangleFacing Facing);


public readonly record struct TriangleVertices(
    Vector2 V0,
    Vector2 V1,
    Vector2 V2) {

    public Vector2 Center =>
        (V0 + V1 + V2) / 3.0f;

    public Vector2 this[int index] => index switch {
        0 => V0,
        1 => V1,
        2 => V2,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public TriangleVertices GetChild(int child) {
        Vector2 m01 = (V0 + V1) * 0.5f;
        Vector2 m12 = (V1 + V2) * 0.5f;
        Vector2 m20 = (V2 + V0) * 0.5f;

        return child switch {
            0 => new TriangleVertices(
                V0,
                m01,
                m20),

            1 => new TriangleVertices(
                V1,
                m12,
                m01),

            2 => new TriangleVertices(
                V2,
                m20,
                m12),

            3 => new TriangleVertices(
                m12,
                m20,
                m01),

            _ => throw new ArgumentOutOfRangeException(nameof(child)),
        };
    }
}


public sealed class TriangleGrid {
    private readonly Dictionary<TriangleGridPosition, Structure>
        _structureByCell = new();

    private readonly HashSet<Structure>
        _structures = new(ReferenceEqualityComparer.Instance);


    public float SideLength { get; }

    public Vector2 Origin { get; set; }

    public float TriangleHeight =>
        SideLength * Mathf.Sqrt(3.0f) / 2.0f;

    public int StructureCount => _structures.Count;

    public int OccupiedCellCount => _structureByCell.Count;

    public IEnumerable<Structure> Structures =>
        _structures;

    public event EventHandler<TriangleGridChangedEventArgs>? Changed;


    public TriangleGrid(
        float sideLength,
        Vector2 origin = default) {

        if (sideLength <= 0)
            throw new ArgumentOutOfRangeException(nameof(sideLength));

        SideLength = sideLength;
        Origin = origin;
    }


    // ============================================================
    // Discrete lattice geometry
    // ============================================================

    /// <summary>
    /// 返回一个 TriangleGridPosition 对应的三个晶格顶点。
    ///
    /// 返回的是整数 lattice coordinates，而不是 Vector2 世界坐标。
    /// </summary>
    public static (
        Vector2I V0,
        Vector2I V1,
        Vector2I V2)
        GetCellVertices(
            TriangleGridPosition position) {

        int x = position.X;
        int y = position.Y;

        if (position.Facing == TriangleFacing.Down) {
            return (
                new Vector2I(x, y),
                new Vector2I(x + 1, y),
                new Vector2I(x, y + 1));
        }

        return (
            new Vector2I(x + 1, y + 1),
            new Vector2I(x, y + 1),
            new Vector2I(x + 1, y));
    }


    /// <summary>
    /// 根据一个单位三角格的三个晶格顶点，恢复 TriangleGridPosition。
    ///
    /// 三个顶点的顺序无关。
    /// 调用者必须保证三个顶点确实组成一个单位三角格。
    /// </summary>
    public static TriangleGridPosition GetCellFromVertices(
        Vector2I v0,
        Vector2I v1,
        Vector2I v2) {

        int x = Math.Min(
            v0.X,
            Math.Min(v1.X, v2.X));

        int y = Math.Min(
            v0.Y,
            Math.Min(v1.Y, v2.Y));

        /*
		 * Down(x,y):
		 *
		 * (x,y) 是三个顶点之一。
		 *
		 * Up(x,y):
		 *
		 * (x,y) 不是三个顶点之一。
		 */
        Vector2I lowerCorner =
            new Vector2I(x, y);

        TriangleFacing facing =
            v0 == lowerCorner ||
            v1 == lowerCorner ||
            v2 == lowerCorner
                ? TriangleFacing.Down
                : TriangleFacing.Up;

        return new TriangleGridPosition(
            x,
            y,
            facing);
    }


    /// <summary>
    /// 绕 lattice vertex (0,0) 旋转。
    ///
    /// rotation = 0..5
    /// 每一步在当前 Godot 坐标系中视觉上旋转 60°。
    ///
    /// lattice basis:
    ///
    /// ex = (1, 0)
    /// ey = (1/2, sqrt(3)/2)
    /// </summary>
    public static Vector2I RotateVertex(
        Vector2I vertex,
        int rotation) {

        ValidateGridRotation(rotation);

        int x = vertex.X;
        int y = vertex.Y;

        return rotation switch {
            0 => new Vector2I(
                x,
                y),

            1 => new Vector2I(
                -y,
                x + y),

            2 => new Vector2I(
                -x - y,
                x),

            3 => new Vector2I(
                -x,
                -y),

            4 => new Vector2I(
                y,
                -x - y),

            5 => new Vector2I(
                x + y,
                -x),

            _ => throw new UnreachableException(),
        };
    }


    /// <summary>
    /// 先绕 (0,0) 旋转 localVertex，
    /// 再平移到 anchor。
    /// </summary>
    public static Vector2I TransformVertex(
        Vector2I localVertex,
        Vector2I anchor,
        int gridRotation) {

        return anchor +
            RotateVertex(
                localVertex,
                gridRotation);
    }


    /// <summary>
    /// 对一个 TriangleGridPosition 应用：
    ///
    /// 1. 绕 lattice origin 旋转；
    /// 2. 平移到 anchor。
    ///
    /// 适合用于 Structure 的 local footprint -> actual footprint。
    /// </summary>
    public static TriangleGridPosition TransformCell(
        TriangleGridPosition localCell,
        Vector2I anchor,
        int gridRotation) {

        var (v0, v1, v2) =
            GetCellVertices(localCell);

        v0 = TransformVertex(
            v0,
            anchor,
            gridRotation);

        v1 = TransformVertex(
            v1,
            anchor,
            gridRotation);

        v2 = TransformVertex(
            v2,
            anchor,
            gridRotation);

        return GetCellFromVertices(
            v0,
            v1,
            v2);
    }


    /// <summary>
    /// 只旋转一个三角格，不进行平移。
    /// </summary>
    public static TriangleGridPosition RotateCell(
        TriangleGridPosition cell,
        int gridRotation) {

        return TransformCell(
            cell,
            Vector2I.Zero,
            gridRotation);
    }


    public static void ValidateGridRotation(
        int rotation) {

        if ((uint)rotation >= 6) {
            throw new ArgumentOutOfRangeException(
                nameof(rotation),
                "Grid rotation must be between 0 and 5.");
        }
    }


    // ============================================================
    // Structure queries
    // ============================================================

    public bool Contains(
        TriangleGridPosition position) {

        return _structureByCell.ContainsKey(position);
    }


    public bool Contains(
        Structure structure) {

        return _structures.Contains(structure);
    }


    public bool TryGetStructure(
        TriangleGridPosition position,
        out Structure? structure) {

        return _structureByCell.TryGetValue(
            position,
            out structure);
    }


    public Structure? GetStructure(
        TriangleGridPosition position) {

        return _structureByCell.GetValueOrDefault(position);
    }


    // ============================================================
    // Structure placement
    // ============================================================

    public bool CanPlaceStructure(
        Structure structure) {

        if (_structures.Contains(structure))
            return false;

        TriangleGridPosition[] cells =
            GetValidatedFootprint(structure);

        foreach (TriangleGridPosition cell in cells) {
            if (_structureByCell.ContainsKey(cell))
                return false;
        }

        return true;
    }


    public bool TryPlaceStructure(
        Structure structure) {

        if (_structures.Contains(structure))
            return false;

        TriangleGridPosition[] cells =
            GetValidatedFootprint(structure);

        foreach (TriangleGridPosition cell in cells) {
            if (_structureByCell.ContainsKey(cell))
                return false;
        }

        foreach (TriangleGridPosition cell in cells) {
            _structureByCell.Add(
                cell,
                structure);
        }

        _structures.Add(structure);

        OnChanged(
            new TriangleGridChangedEventArgs(
                structures: new[] { structure },
                cells: cells));

        return true;
    }


    public bool RemoveStructure(
        Structure structure) {

        if (!_structures.Contains(structure))
            return false;

        TriangleGridPosition[] cells =
            GetValidatedFootprint(structure);

        _structures.Remove(structure);

        foreach (TriangleGridPosition cell in cells) {
            if (_structureByCell.TryGetValue(
                    cell,
                    out Structure? occupant)
                && ReferenceEquals(
                    occupant,
                    structure)) {

                _structureByCell.Remove(cell);
            }
        }

        OnChanged(
            new TriangleGridChangedEventArgs(
                structures: new[] { structure },
                cells: cells));

        return true;
    }


    public bool RemoveStructureAt(
        TriangleGridPosition position) {

        if (!_structureByCell.TryGetValue(
                position,
                out Structure? structure)) {

            return false;
        }

        return RemoveStructure(structure);
    }


    public void Clear() {
        if (_structures.Count == 0)
            return;

        Structure[] affectedStructures =
            _structures.ToArray();

        TriangleGridPosition[] affectedCells =
            _structureByCell.Keys.ToArray();

        _structureByCell.Clear();
        _structures.Clear();

        OnChanged(
            new TriangleGridChangedEventArgs(
                structures: affectedStructures,
                cells: affectedCells,
                fullRedraw: true));
    }


    // ============================================================
    // Structure transform
    // ============================================================

    /// <summary>
    /// 只移动 Structure，不改变 rotation。
    /// </summary>
    public bool TryMoveStructure(
        Structure structure,
        Vector2I newAnchor) {

        return TryTransformStructure(
            structure,
            newAnchor,
            structure.GridRotation);
    }


    /// <summary>
    /// 只旋转 Structure，不改变 Anchor。
    /// </summary>
    public bool TryRotateStructure(
        Structure structure,
        int newGridRotation) {

        return TryTransformStructure(
            structure,
            structure.Anchor,
            newGridRotation);
    }


    /// <summary>
    /// 原子地改变 Structure 的 Anchor + GridRotation。
    ///
    /// 新 footprint 可以覆盖自己的旧 footprint，
    /// 但不能覆盖其他 Structure。
    /// </summary>
    public bool TryTransformStructure(
        Structure structure,
        Vector2I newAnchor,
        int newGridRotation) {

        if (!_structures.Contains(structure))
            return false;

        ValidateGridRotation(newGridRotation);

        Vector2I oldAnchor =
            structure.Anchor;

        int oldGridRotation =
            structure.GridRotation;

        TriangleGridPosition[] oldCells =
            GetValidatedFootprint(structure);

        structure.SetTransform(
            newAnchor,
            newGridRotation);

        TriangleGridPosition[] newCells;

        try {
            newCells =
                GetValidatedFootprint(structure);
        }
        catch {
            structure.SetTransform(
                oldAnchor,
                oldGridRotation);

            throw;
        }

        /*
		 * 允许新的 footprint 与自己旧 footprint 重叠。
		 */
        foreach (TriangleGridPosition cell in newCells) {
            if (_structureByCell.TryGetValue(
                    cell,
                    out Structure? occupant)
                && !ReferenceEquals(
                    occupant,
                    structure)) {

                structure.SetTransform(
                    oldAnchor,
                    oldGridRotation);

                return false;
            }
        }

        foreach (TriangleGridPosition cell in oldCells) {
            if (_structureByCell.TryGetValue(
                    cell,
                    out Structure? occupant)
                && ReferenceEquals(
                    occupant,
                    structure)) {

                _structureByCell.Remove(cell);
            }
        }

        foreach (TriangleGridPosition cell in newCells) {
            _structureByCell.Add(
                cell,
                structure);
        }

        TriangleGridPosition[] affectedCells =
            oldCells
                .Concat(newCells)
                .Distinct()
                .ToArray();

        OnChanged(
            new TriangleGridChangedEventArgs(
                structures: new[] { structure },
                cells: affectedCells));

        return true;
    }


    // ============================================================
    // Footprint validation
    // ============================================================

    private static TriangleGridPosition[]
        GetValidatedFootprint(
            Structure structure) {

        TriangleGridPosition[] cells =
            structure
                .EnumerateOccupiedCells()
                .ToArray();

        if (cells.Length == 0) {
            throw new InvalidOperationException(
                $"{structure.GetType().Name} " +
                "does not occupy any grid cells.");
        }

        var unique =
            new HashSet<TriangleGridPosition>();

        foreach (TriangleGridPosition cell in cells) {
            if (!unique.Add(cell)) {
                throw new InvalidOperationException(
                    $"{structure.GetType().Name} " +
                    $"contains duplicate cell {cell}.");
            }
        }

        return cells;
    }


    private void OnChanged(
        TriangleGridChangedEventArgs args) {

        Changed?.Invoke(this, args);
    }


    // ============================================================
    // Lattice <-> Vector2 geometry
    // ============================================================

    /// <summary>
    /// 把整数 lattice vertex 转换为实际 Vector2 坐标。
    /// </summary>
    public Vector2 GetVertexPosition(
        Vector2I vertex) {

        return GetVertexPosition(
            vertex.X,
            vertex.Y);
    }


    public Vector2 GetVertexPosition(
        int x,
        int y) {

        return Origin + new Vector2(
            SideLength * (x + y * 0.5f),
            TriangleHeight * y);
    }


    /// <summary>
    /// 将离散 TriangleGridPosition 转成实际绘制用三角形。
    /// </summary>
    public TriangleVertices GetVertices(
        TriangleGridPosition position) {

        var (v0, v1, v2) =
            GetCellVertices(position);

        return new TriangleVertices(
            GetVertexPosition(v0),
            GetVertexPosition(v1),
            GetVertexPosition(v2));
    }


    public TriangleGridPosition GetGridPosition(
        Vector2 worldPosition) {

        Vector2 p =
            worldPosition - Origin;

        float gy =
            p.Y / TriangleHeight;

        float gx =
            p.X / SideLength
            - gy * 0.5f;

        int x =
            Mathf.FloorToInt(gx);

        int y =
            Mathf.FloorToInt(gy);

        float localX =
            gx - x;

        float localY =
            gy - y;

        TriangleFacing facing =
            localX + localY <= 1.0f
                ? TriangleFacing.Down
                : TriangleFacing.Up;

        return new TriangleGridPosition(
            x,
            y,
            facing);
    }


    public Vector2 GetCenter(
        TriangleGridPosition position) {

        return GetVertices(position).Center;
    }


    // ============================================================
    // Neighbors
    // ============================================================

    public TriangleGridPosition GetNeighbor(
        TriangleGridPosition position,
        int edge) {

        if ((uint)edge >= 3)
            throw new ArgumentOutOfRangeException(nameof(edge));

        int x = position.X;
        int y = position.Y;

        if (position.Facing == TriangleFacing.Down) {
            return edge switch {
                0 => new(
                    x,
                    y - 1,
                    TriangleFacing.Up),

                1 => new(
                    x,
                    y,
                    TriangleFacing.Up),

                2 => new(
                    x - 1,
                    y,
                    TriangleFacing.Up),

                _ => throw new UnreachableException(),
            };
        }

        return edge switch {
            0 => new(
                x,
                y + 1,
                TriangleFacing.Down),

            1 => new(
                x,
                y,
                TriangleFacing.Down),

            2 => new(
                x + 1,
                y,
                TriangleFacing.Down),

            _ => throw new UnreachableException(),
        };
    }
    public static IEnumerable<TriangleGridPosition>
    GetScale2Children(
        TriangleGridPosition parent) {
        int x = parent.X * 2;
        int y = parent.Y * 2;

        if (parent.Facing == TriangleFacing.Down) {
            yield return new(
                x,
                y,
                TriangleFacing.Down);

            yield return new(
                x,
                y,
                TriangleFacing.Up);

            yield return new(
                x + 1,
                y,
                TriangleFacing.Down);

            yield return new(
                x,
                y + 1,
                TriangleFacing.Down);

            yield break;
        }

        yield return new(
            x + 1,
            y + 1,
            TriangleFacing.Up);

        yield return new(
            x + 1,
            y + 1,
            TriangleFacing.Down);

        yield return new(
            x,
            y + 1,
            TriangleFacing.Up);

        yield return new(
            x + 1,
            y,
            TriangleFacing.Up);
    }
    public static TriangleGridPosition GetScale2Parent(
    TriangleGridPosition child) {
        int x = child.X;
        int y = child.Y;

        bool xOdd = (x & 1) != 0;
        bool yOdd = (y & 1) != 0;

        int parentX =
            (x - (xOdd ? 1 : 0)) / 2;

        int parentY =
            (y - (yOdd ? 1 : 0)) / 2;

        if (child.Facing == TriangleFacing.Down) {
            /*
             * D(even,even)
             * D(odd, even)
             * D(even,odd)
             *
             * 都属于一个 Down parent。
             *
             * D(odd,odd)
             * 是 Up parent 中央的倒三角。
             */
            return new TriangleGridPosition(
                parentX,
                parentY,
                xOdd && yOdd
                    ? TriangleFacing.Up
                    : TriangleFacing.Down);
        }

        /*
         * U(even,even)
         * 是 Down parent 中央的正三角。
         *
         * 其他三个 parity 都属于 Up parent。
         */
        return new TriangleGridPosition(
            parentX,
            parentY,
            !xOdd && !yOdd
                ? TriangleFacing.Down
                : TriangleFacing.Up);
    }

    public static bool TryContractRegionBy2(
     IEnumerable<TriangleGridPosition> region,
     out TriangleGridPosition[] contracted) {
        ArgumentNullException.ThrowIfNull(region);

        var source =
            new HashSet<TriangleGridPosition>(
                region);

        var parents =
            new HashSet<TriangleGridPosition>();

        foreach (TriangleGridPosition cell in source) {
            parents.Add(
                GetScale2Parent(cell));
        }

        foreach (TriangleGridPosition parent in parents) {
            foreach (
                TriangleGridPosition child
                in GetScale2Children(parent)) {
                if (!source.Contains(child)) {
                    contracted =
                        Array.Empty<TriangleGridPosition>();

                    return false;
                }
            }
        }

        /*
         * 每个 source cell 都已经映射到某个 parent，
         * 每个 parent 的全部 children 又都存在，
         * 所以 source region 与这些 parent 的 expansion 完全相等。
         */
        contracted =
            parents.ToArray();

        return true;
    }
}


public sealed class TriangleGridChangedEventArgs : EventArgs {
    public IReadOnlyList<Structure> Structures { get; }

    public IReadOnlyList<TriangleGridPosition> Cells { get; }

    public bool FullRedraw { get; }


    public TriangleGridChangedEventArgs(
        IReadOnlyList<Structure>? structures = null,
        IReadOnlyList<TriangleGridPosition>? cells = null,
        bool fullRedraw = false) {

        Structures =
            structures ?? Array.Empty<Structure>();

        Cells =
            cells ?? Array.Empty<TriangleGridPosition>();

        FullRedraw = fullRedraw;
    }




}