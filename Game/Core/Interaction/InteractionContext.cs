using Godot;
using System;

/// <summary>
/// Read-only snapshot used to evaluate the currently available world
/// interaction.
///
/// Sources:
///
/// - WorldRendererRoot / World:
///     cursor position, grid cell, structure, box
///
/// - PlayerState:
///     currently held ItemStack and placement rotation
///
/// InteractionContext owns none of those states and performs no gameplay
/// mutation.
/// </summary>
public sealed class InteractionContext {
    public World World { get; }

    public Vector2 CursorWorldPosition { get; }

    public TriangleGridPosition CursorGridPosition { get; }

    public Structure? StructureAtCursor { get; }

    public Box? BoxAtCursor { get; }

    /// <summary>
    /// Snapshot of the player's currently held stack.
    /// </summary>
    public ItemStack? HeldItem { get; }

    public int HeldItemRotation { get; }

    public bool IsHoldingItem =>
        HeldItem.HasValue;

    public InteractionContext(
        WorldRendererRoot worldRenderer,
        PlayerState playerState,
        Vector2 cursorScreenPosition) {

        ArgumentNullException.ThrowIfNull(
            worldRenderer);

        ArgumentNullException.ThrowIfNull(
            playerState);

        World =
            worldRenderer.World;

        CursorWorldPosition =
            worldRenderer.MakeCanvasPositionLocal(
                cursorScreenPosition);

        CursorGridPosition =
            World.StructureGrid.GetGridPosition(
                CursorWorldPosition);

        StructureAtCursor =
            World.GetStructure(
                CursorGridPosition);

        BoxAtCursor =
            World.GetBox(
                CursorGridPosition);

        HeldItem =
            playerState.HeldItem;

        HeldItemRotation =
            playerState.HeldItemRotation;
    }
}
