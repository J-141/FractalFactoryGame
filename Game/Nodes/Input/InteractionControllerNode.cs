using Godot;
using System;

/// <summary>
/// Thin Godot host for InteractionController.
///
/// _UnhandledInput allows Control-based screen UI to consume input before
/// cursor/world interaction sees it.
/// </summary>
public partial class InteractionControllerNode
    : Node {

    private InteractionController? _controller;

    public InteractionController Controller =>
        _controller
        ?? throw new InvalidOperationException(
            "InteractionControllerNode has not been initialized.");

    public void Initialize(
        WorldRendererRoot worldRenderer,
        PlayerState playerState,
        InteractionPlanner planner) {

        if (_controller is not null) {
            throw new InvalidOperationException(
                "InteractionControllerNode is already initialized.");
        }

        _controller =
            new InteractionController(
                worldRenderer,
                playerState,
                planner);

        if (worldRenderer.WorldOverlay
            is not InteractionOverlayRenderer overlayRenderer) {

            throw new InvalidOperationException(
                "WorldRendererRoot.WorldOverlay must use " +
                "InteractionOverlayRenderer.");
        }

        overlayRenderer.Bind(
            _controller);

        _controller.RefreshPlan();
    }

    public override void _Process(
        double delta) {

        _controller?.Update(
            delta);
    }

    public override void _UnhandledInput(
        InputEvent input) {

        _controller?.HandleInput(
            input);
    }
}
