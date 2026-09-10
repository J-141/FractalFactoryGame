using Godot;
using System;
using System.Collections.Generic;
using System.Linq;


public sealed class World {
    public TriangleGrid StructureGrid { get; }

    public TriangleGrid BoxGrid { get; }


    public World(
        float cellSideLength) {
        if (cellSideLength <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(cellSideLength));

        StructureGrid =
            new TriangleGrid(
                cellSideLength);

        BoxGrid =
            new TriangleGrid(
                cellSideLength);
    }


    // ============================================================
    // Structure queries
    // ============================================================

    public Structure? GetStructure(
        TriangleGridPosition position) {
        return StructureGrid.GetStructure(
            position);
    }


    public Box? GetBox(
        TriangleGridPosition position) {
        return BoxGrid.GetStructure(
            position) as Box;
    }


    /// <summary>
    /// 返回 Structure 当前完整所在的 Box。
    ///
    /// null 表示整个 Structure 都在所有 Box 外部。
    ///
    /// World 的 invariant 保证一个已放置的 Structure
    /// 不可能同时属于多个 Box。
    /// </summary>
    public Box? GetContainingBox(
        Structure structure) {
        foreach (
            TriangleGridPosition cell
            in structure.EnumerateOccupiedCells()) {
            return GetBox(cell);
        }

        throw new InvalidOperationException(
            "Structure does not occupy any cells.");
    }


    // ============================================================
    // Structure placement
    // ============================================================

    public bool CanPlaceStructure(
        Structure structure) {
        TriangleGridPosition[] footprint =
            structure
                .EnumerateOccupiedCells()
                .ToArray();

        if (!IsValidStructureFootprint(
                footprint)) {
            return false;
        }

        return StructureGrid.CanPlaceStructure(
            structure);
    }


    public bool TryPlaceStructure(
        Structure structure) {
        TriangleGridPosition[] footprint =
            structure
                .EnumerateOccupiedCells()
                .ToArray();

        /*
		 * Structure 不能跨越 Box boundary。
		 */
        if (!IsValidStructureFootprint(
                footprint)) {
            return false;
        }

        return StructureGrid.TryPlaceStructure(
            structure);
    }


    public bool RemoveStructure(
        Structure structure) {
        return StructureGrid.RemoveStructure(
            structure);
    }


    public bool RemoveStructureAt(
        TriangleGridPosition position) {
        return StructureGrid.RemoveStructureAt(
            position);
    }


    // ============================================================
    // Structure transform
    // ============================================================

    public bool TryMoveStructure(
        Structure structure,
        Vector2I newAnchor) {
        return TryTransformStructure(
            structure,
            newAnchor,
            structure.GridRotation);
    }


    public bool TryRotateStructure(
        Structure structure,
        int newGridRotation) {
        return TryTransformStructure(
            structure,
            structure.Anchor,
            newGridRotation);
    }


    public bool TryTransformStructure(
        Structure structure,
        Vector2I newAnchor,
        int newGridRotation) {
        if (!StructureGrid.Contains(structure))
            return false;

        TriangleGrid.ValidateGridRotation(
            newGridRotation);

        TriangleGridPosition[] newFootprint =
            structure
                .EnumerateOccupiedCells(
                    newAnchor,
                    newGridRotation)
                .ToArray();

        if (!IsValidStructureFootprint(
                newFootprint)) {
            return false;
        }

        return StructureGrid.TryTransformStructure(
            structure,
            newAnchor,
            newGridRotation);
    }


    // ============================================================
    // Box placement
    // ============================================================

    public bool CanPlaceBox(
        Box box) {
        TriangleGridPosition[] footprint =
            box
                .EnumerateOccupiedCells()
                .ToArray();

        /*
         * 新 Box 覆盖区域内必须完全没有 Structure。
         */
        if (!IsStructureRegionEmpty(footprint))
            return false;

        /*
         * BoxGrid 自己负责 Box-vs-Box collision。
         */
        return BoxGrid.CanPlaceStructure(box);
    }


    public bool TryPlaceBox(
        Box box) {
        TriangleGridPosition[] footprint =
            box
                .EnumerateOccupiedCells()
                .ToArray();

        if (!IsStructureRegionEmpty(footprint))
            return false;

        return BoxGrid.TryPlaceStructure(box);
    }


    // ============================================================
    // Box removal
    // ============================================================

    public bool RemoveBox(
        Box box) {
        if (!BoxGrid.Contains(box))
            return false;

        TriangleGridPosition[] footprint =
            box
                .EnumerateOccupiedCells()
                .ToArray();

        /*
         * 删除 Box 时，原 Box 区域内也必须为空。
         */
        if (!IsStructureRegionEmpty(footprint))
            return false;

        return BoxGrid.RemoveStructure(box);
    }


    // ============================================================
    // Box transform
    // ============================================================

    public bool TryMoveBox(
        Box box,
        Vector2I newAnchor) {
        return TryTransformBox(
            box,
            newAnchor,
            box.GridRotation);
    }


    public bool TryRotateBox(
        Box box,
        int newGridRotation) {
        return TryTransformBox(
            box,
            box.Anchor,
            newGridRotation);
    }


    public bool TryTransformBox(
        Box box,
        Vector2I newAnchor,
        int newGridRotation) {
        if (!BoxGrid.Contains(box))
            return false;

        TriangleGrid.ValidateGridRotation(
            newGridRotation);

        TriangleGridPosition[] oldFootprint =
            box
                .EnumerateOccupiedCells()
                .ToArray();

        TriangleGridPosition[] newFootprint =
            box
                .EnumerateOccupiedCells(
                    newAnchor,
                    newGridRotation)
                .ToArray();

        /*
         * 旧区域必须为空。
         */
        if (!IsStructureRegionEmpty(oldFootprint))
            return false;

        /*
         * 新区域也必须为空。
         */
        if (!IsStructureRegionEmpty(newFootprint))
            return false;

        /*
         * BoxGrid 再负责：
         *
         * - 是否撞到其他 Box
         * - 更新 occupancy
         * - 更新 Anchor / GridRotation
         * - Changed event
         */
        return BoxGrid.TryTransformStructure(
            box,
            newAnchor,
            newGridRotation);
    }

    // ============================================================
    // Cross-layer validation
    // ============================================================

    /// <summary>
    /// Structure footprint 合法，当且仅当：
    ///
    /// 所有 cells 在 BoxGrid 上对应同一个 occupant。
    ///
    /// 即：
    ///
    /// all null
    ///     -> 整个 Structure 在 Box 外
    ///
    /// all same Box
    ///     -> 整个 Structure 在同一个 Box 内
    ///
    /// null + Box
    ///     -> 跨 Box boundary，非法
    ///
    /// Box A + Box B
    ///     -> 跨两个 Box，非法
    /// </summary>
    private bool IsValidStructureFootprint(
        IReadOnlyList<TriangleGridPosition>
            footprint) {
        if (footprint.Count == 0)
            return false;

        Structure? expectedBox =
            BoxGrid.GetStructure(
                footprint[0]);

        for (int i = 1; i < footprint.Count; i++) {
            Structure? box =
                BoxGrid.GetStructure(
                    footprint[i]);

            if (!ReferenceEquals(
                    box,
                    expectedBox)) {
                return false;
            }
        }

        return true;
    }


    private bool IsStructureRegionEmpty(
      IEnumerable<TriangleGridPosition> region) {
        foreach (TriangleGridPosition cell in region) {
            if (StructureGrid.Contains(cell))
                return false;
        }

        return true;
    }
}