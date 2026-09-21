using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// World-space interaction overlay.
///
/// This node is the existing WorldRendererRoot/WorldOverlay node, so its
/// visuals live in the same World2D as normal world content and therefore also
/// participate in BoxRenderer recursive capture.
///
/// Current behavior:
/// - CropPlan -> draw the outer boundary of CropRegion.
/// - any other ActionPlan -> draw nothing.
///
/// Disabled CropPlans are intentionally still rendered.
/// </summary>
public partial class InteractionOverlayRenderer
    : Node2D {

    [Export]
    public Color CropOutlineColor { get; set; } =
        Colors.White;

    [Export(PropertyHint.Range, "0.5,16,0.5")]
    public float CropOutlineWidth { get; set; } =
        2.0f;

    private WorldRendererRoot? _worldRenderer;
    private InteractionController? _interactionController;
    private CropPlan? _cropPlan;

    public override void _Ready() {
        _worldRenderer =
            GetParent()
            as WorldRendererRoot
            ?? throw new InvalidOperationException(
                "InteractionOverlayRenderer must be the WorldOverlay " +
                "child of WorldRendererRoot.");
    }

    public override void _ExitTree() {
        if (_interactionController is not null) {
            _interactionController.ActionPlanChanged -=
                OnActionPlanChanged;
        }

        _interactionController =
            null;

        _worldRenderer =
            null;

        _cropPlan =
            null;
    }

    /// <summary>
    /// Bind the overlay to the world interaction controller.
    ///
    /// Binding is presentation wiring only. InteractionController itself does
    /// not depend on this renderer.
    /// </summary>
    public void Bind(
        InteractionController interactionController) {

        ArgumentNullException.ThrowIfNull(
            interactionController);

        if (ReferenceEquals(
                _interactionController,
                interactionController)) {
            return;
        }

        if (_interactionController is not null) {
            _interactionController.ActionPlanChanged -=
                OnActionPlanChanged;
        }

        _interactionController =
            interactionController;

        _interactionController.ActionPlanChanged +=
            OnActionPlanChanged;

        SetPlan(
            interactionController.CurrentPlan);
    }

    public override void _Draw() {
        if (_worldRenderer is null ||
            _cropPlan is null) {
            return;
        }

        DrawCropRegionOutline(
            _cropPlan);
    }

    private void OnActionPlanChanged(
        ActionPlan plan) {

        SetPlan(
            plan);
    }

    private void SetPlan(
        ActionPlan plan) {
        _cropPlan =
            plan as CropPlan;

        QueueRedraw();
    }

    /// <summary>
    /// Draw only the boundary of CropRegion.
    ///
    /// Every triangle contributes three lattice edges. An edge shared by two
    /// crop cells is internal and appears twice, so it is omitted. Remaining
    /// edges form the outer triangular boundary.
    ///
    /// This means a recursively resolved region of 4 / 16 / 64 / ... cells is
    /// still rendered as one large triangle outline rather than a grid of
    /// individually outlined cells.
    /// </summary>
    private void DrawCropRegionOutline(
        CropPlan cropPlan) {

        TriangleGrid grid =
            _worldRenderer!
                .World
                .StructureGrid;

        var edgeCounts =
            new Dictionary<GridEdge, int>();

        foreach (
            TriangleGridPosition cell
            in cropPlan.CropRegion) {

            var (
                v0,
                v1,
                v2) =
                    TriangleGrid.GetCellVertices(
                        cell);

            AddEdge(
                v0,
                v1);

            AddEdge(
                v1,
                v2);

            AddEdge(
                v2,
                v0);
        }

        foreach (
            KeyValuePair<GridEdge, int> pair
            in edgeCounts) {

            if (pair.Value != 1)
                continue;

            GridEdge edge =
                pair.Key;

            DrawLine(
                grid.GetVertexPosition(
                    edge.A),
                grid.GetVertexPosition(
                    edge.B),
                CropOutlineColor,
                CropOutlineWidth,
                antialiased: true);
        }

        void AddEdge(
            Vector2I a,
            Vector2I b) {

            GridEdge edge =
                GridEdge.Create(
                    a,
                    b);

            edgeCounts.TryGetValue(
                edge,
                out int count);

            edgeCounts[edge] =
                count + 1;
        }
    }

    /// <summary>
    /// Undirected lattice edge.
    /// </summary>
    private readonly record struct GridEdge(
        Vector2I A,
        Vector2I B) {

        public static GridEdge Create(
            Vector2I a,
            Vector2I b) {

            return ComesBefore(
                    a,
                    b)
                ? new GridEdge(
                    a,
                    b)
                : new GridEdge(
                    b,
                    a);
        }

        private static bool ComesBefore(
            Vector2I a,
            Vector2I b) {

            return a.X < b.X ||
                (a.X == b.X &&
                 a.Y <= b.Y);
        }
    }
}
