using Godot;
using System;
using System.Collections.Generic;

public sealed class EmbeddedBoxReference : Structure {
    private readonly TriangleGridPosition[]
        _localOccupiedCells;


    public Box TargetBox { get; }


    public EmbeddedBoxReference(
        Box targetBox,
        Vector2I anchor,
        int gridRotation = 0)
        : base(anchor, gridRotation) {
        TargetBox =
            targetBox
            ?? throw new ArgumentNullException(
                nameof(targetBox));

        if (!TriangleGrid.TryContractRegionBy2(
                TargetBox.LocalRegion,
                out _localOccupiedCells)) {
            throw new ArgumentException(
                "TargetBox region cannot be scaled down " +
                "by exactly 1/2.",
                nameof(targetBox));
        }
    }


    protected override IEnumerable<TriangleGridPosition>
        EnumerateLocalOccupiedCells() {
        return _localOccupiedCells;
    }
}