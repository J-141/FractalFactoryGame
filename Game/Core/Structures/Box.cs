using Godot;
using System;
using System.Collections.Generic;

public sealed class Box : Structure {
    private readonly HashSet<TriangleGridPosition> _localRegion;

    public IReadOnlySet<TriangleGridPosition> LocalRegion =>
        _localRegion;

    public Box(
        IEnumerable<TriangleGridPosition> region,
        Vector2I anchor,
        int gridRotation = 0)
        : base(anchor, gridRotation) {
        _localRegion =
            new HashSet<TriangleGridPosition>(region);

        if (_localRegion.Count == 0)
            throw new ArgumentException(
                "Box region cannot be empty.",
                nameof(region));
    }

    protected override IEnumerable<TriangleGridPosition>
        EnumerateLocalOccupiedCells() {
        return _localRegion;
    }
}