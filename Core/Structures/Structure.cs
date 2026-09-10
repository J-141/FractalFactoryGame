using Godot;
using System.Collections.Generic;

public abstract class Structure {
    public Vector2I Anchor { get; private set; }

    public int GridRotation { get; private set; }


    protected Structure(
        Vector2I anchor,
        int gridRotation = 0) {
        TriangleGrid.ValidateGridRotation(gridRotation);

        Anchor = anchor;
        GridRotation = gridRotation;
    }


    protected abstract IEnumerable<TriangleGridPosition>
        EnumerateLocalOccupiedCells();


    public IEnumerable<TriangleGridPosition>
        EnumerateOccupiedCells() {
        return EnumerateOccupiedCells(
            Anchor,
            GridRotation);
    }


    internal IEnumerable<TriangleGridPosition>
        EnumerateOccupiedCells(
            Vector2I anchor,
            int gridRotation) {
        TriangleGrid.ValidateGridRotation(gridRotation);

        foreach (TriangleGridPosition local
                 in EnumerateLocalOccupiedCells()) {
            yield return TriangleGrid.TransformCell(
                local,
                anchor,
                gridRotation);
        }
    }


    internal void SetAnchor(
        Vector2I anchor) {
        Anchor = anchor;
    }


    internal void SetGridRotation(
        int gridRotation) {
        TriangleGrid.ValidateGridRotation(gridRotation);

        GridRotation = gridRotation;
    }


    internal void SetTransform(
        Vector2I anchor,
        int gridRotation) {
        TriangleGrid.ValidateGridRotation(gridRotation);

        Anchor = anchor;
        GridRotation = gridRotation;
    }
}