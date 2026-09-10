using Godot;

public abstract record InteractionCommand;

/// <summary>
/// Cursor position changed.
///
/// This is not an InputMap action. It represents raw mouse motion translated
/// into interaction-space input so the controller can update cursor state and
/// refresh the current ActionPlan/overlay.
/// </summary>
public sealed record CursorMovedCommand(
    Vector2 ScreenPosition
) : InteractionCommand;

/// <summary>
/// Primary interaction while in Normal mode.
///
/// This command deliberately does not decide whether the world operation is
/// PickupShape, EditBuilding, etc. That requires InteractionContext and is
/// resolved in the next interaction stage.
/// </summary>
public sealed record PrimaryInteractCommand
    : InteractionCommand;

/// <summary>
/// Primary interaction while holding an Item.
///
/// The exact placement operation still requires InteractionContext and will be
/// resolved into an ActionPlan later.
/// </summary>
public sealed record PlaceHeldItemCommand
    : InteractionCommand;

/// <summary>
/// interaction_cancel was pressed.
///
/// State transition semantics are handled by InteractionController.
/// For example, while HoldingItem this can return to Normal mode.
/// </summary>
public sealed record CancelInteractionCommand
    : InteractionCommand;
