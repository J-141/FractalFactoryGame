using Godot;

public sealed class InteractionInputTranslator {

    public InteractionCommand? Translate(
        InputEvent input,
        InteractionState state) {

        if (input is InputEventMouseMotion mouseMotion) {
            return new CursorMovedCommand(
                mouseMotion.Position);
        }

        if (input.IsActionPressed(
                "interaction_cancel")) {
            return new CancelInteractionCommand();
        }

        if (!input.IsActionPressed(
                "interaction_primary")) {
            return null;
        }

        return state.Mode switch {
            InteractionMode.Normal =>
                new PrimaryInteractCommand(),

            InteractionMode.HoldingItem =>
                new PlaceHeldItemCommand(),

            _ => null
        };
    }
}
