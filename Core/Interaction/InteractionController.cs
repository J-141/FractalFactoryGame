using Godot;
using System;

/// <summary>
/// Controls cursor-based world interaction.
///
/// Responsibilities:
///
/// - track cursor screen position;
/// - track primary press/hold/release gesture;
/// - evaluate the current ActionPlan;
/// - advance HoldActionActivation;
/// - commit the current resolved plan.
///
/// It does NOT own player-domain state such as holding or inventory.
/// PlayerState is injected and treated as an external source of truth.
/// </summary>
public sealed class InteractionController {
    private readonly WorldRendererRoot _worldRenderer;
    private readonly PlayerState _playerState;
    private readonly InteractionPlanner _planner;

    /*
	 * Transient cursor/gesture state.
	 */
    private Vector2 _cursorScreenPosition;
    private bool _primaryHeld;
    private TriangleGridPosition? _activeHoldContinuityKey;
    private double _holdElapsedSeconds;

    public Vector2 CursorScreenPosition =>
        _cursorScreenPosition;

    public ActionPlan CurrentPlan { get; private set; } =
        new NoActionPlan();

    /// <summary>
    /// Presentation boundary for world overlay, screen UI, debug views, etc.
    /// </summary>
    public event Action<ActionPlan>? ActionPlanChanged;

    public InteractionController(
        WorldRendererRoot worldRenderer,
        PlayerState playerState,
        InteractionPlanner planner) {

        _worldRenderer =
            worldRenderer
            ?? throw new ArgumentNullException(
                nameof(worldRenderer));

        _playerState =
            playerState
            ?? throw new ArgumentNullException(
                nameof(playerState));

        _planner =
            planner
            ?? throw new ArgumentNullException(
                nameof(planner));

        /*
		 * Initialize from the actual viewport cursor so the first preview is
		 * correct even before a MouseMotion event is received.
		 */
        _cursorScreenPosition =
            worldRenderer
                .GetViewport()
                .GetMousePosition();
    }

    /// <summary>
    /// Handle only cursor-based world interaction input.
    ///
    /// Currently:
    /// - mouse motion
    /// - interaction_primary press/release
    ///
    /// UI, player movement, camera controls, etc. are handled elsewhere.
    /// </summary>
    public bool HandleInput(
        InputEvent input) {

        ArgumentNullException.ThrowIfNull(
            input);

        if (input is InputEventMouseMotion mouseMotion) {
            _cursorScreenPosition =
                mouseMotion.Position;

            RefreshPlan();
            return true;
        }

        if (input.IsActionPressed(
                "interaction_primary")) {

            BeginPrimaryInteraction();
            return true;
        }

        if (input.IsActionReleased(
                "interaction_primary")) {

            EndPrimaryInteraction();
            return true;
        }

        return false;
    }

    /// <summary>
    /// Advance hold-based interaction activation.
    ///
    /// The hosting Node should call this from _Process(delta).
    /// </summary>
    public void Update(
        double deltaSeconds) {

        if (deltaSeconds < 0.0) {
            throw new ArgumentOutOfRangeException(
                nameof(deltaSeconds));
        }

        if (!_primaryHeld)
            return;

        ActionPlan rawPlan =
            EvaluateCurrentPlan();

        if (!rawPlan.CanExecute ||
            rawPlan.Activation
                is not HoldActionActivation hold) {

            /*
			 * The button remains physically held, but there is currently no
			 * valid hold action under the cursor.
			 */
            ResetHoldProgress();

            SetCurrentPlan(
                rawPlan);

            return;
        }

        if (!Equals(
                _activeHoldContinuityKey,
                hold.ContinuityKey)) {

            /*
			 * The cursor entered a different logical interaction region.
			 * Hold progress starts again from zero for that region.
			 */
            _activeHoldContinuityKey =
                hold.ContinuityKey;

            _holdElapsedSeconds =
                0.0;
        }

        _holdElapsedSeconds +=
            deltaSeconds;
        HoldActionActivation progressedActivation =
            hold with {
                ElapsedSeconds =
                    _holdElapsedSeconds
            };

        ActionPlan progressedPlan =
            rawPlan with {
                Activation =
                    progressedActivation
            };

        SetCurrentPlan(
            progressedPlan);

        if (!progressedActivation.IsComplete)
            return;

        /*
		 * Commit the semantic plan that was resolved this frame.
		 * Hold elapsed time itself is controller/presentation state.
		 */
        _planner.TryCommit(
            rawPlan);

        /*
		 * One activation per physical primary press.
		 *
		 * Crop normally spawns a world item drop rather than modifying
		 * PlayerState.HeldItem. RefreshPlan still re-evaluates against the
		 * post-commit world state.
		 */
        ResetPrimaryGesture();

        RefreshPlan();
    }

    /// <summary>
    /// Re-evaluate the currently available action from current player/world
    /// state.
    ///
    /// Call this after external changes to PlayerState or World that can affect
    /// interaction preview.
    /// </summary>
    public void RefreshPlan() {
        ActionPlan plan =
            EvaluateCurrentPlan();

        /*
		 * Preserve visible hold progress only while the newly evaluated plan
		 * represents the same logical hold region.
		 */
        if (_primaryHeld &&
            plan.CanExecute &&
            plan.Activation
                is HoldActionActivation hold) {

            if (Equals(
                    _activeHoldContinuityKey,
                    hold.ContinuityKey)) {

                plan =
                    plan with {
                        Activation =
                            hold with {
                                ElapsedSeconds =
                                    _holdElapsedSeconds
                            }
                    };
            }
            else {
                ResetHoldProgress();
            }
        }

        SetCurrentPlan(
            plan);
    }

    private void BeginPrimaryInteraction() {
        _primaryHeld =
            true;

        ResetHoldProgress();

        ActionPlan plan =
            EvaluateCurrentPlan();

        SetCurrentPlan(
            plan);

        if (!plan.CanExecute)
            return;

        switch (plan.Activation) {
            case ImmediateActionActivation:
                CommitImmediatePlan(
                    plan);
                return;

            case HoldActionActivation hold:
                _activeHoldContinuityKey =
                    hold.ContinuityKey;

                _holdElapsedSeconds =
                    0.0;

                SetCurrentPlan(
                    plan with {
                        Activation =
                            hold with {
                                ElapsedSeconds = 0.0
                            }
                    });

                return;

            default:
                throw new InvalidOperationException(
                    $"Unsupported action activation: " +
                    $"{plan.Activation.GetType().Name}");
        }
    }

    private void EndPrimaryInteraction() {
        /*
		 * Releasing primary is the cancellation rule for incomplete hold
		 * actions such as Crop.
		 */
        ResetPrimaryGesture();

        RefreshPlan();
    }

    private void CommitImmediatePlan(
        ActionPlan plan) {

        _planner.TryCommit(
            plan);

        /*
		 * Immediate interactions also activate at most once per physical
		 * primary press.
		 */
        ResetPrimaryGesture();

        RefreshPlan();
    }

    private ActionPlan EvaluateCurrentPlan() {
        InteractionContext context =
            new(
                _worldRenderer,
                _playerState,
                _cursorScreenPosition);

        return _planner.Evaluate(
            context);
    }

    private void ResetPrimaryGesture() {
        _primaryHeld =
            false;

        ResetHoldProgress();
    }

    private void ResetHoldProgress() {
        _activeHoldContinuityKey =
            null;

        _holdElapsedSeconds =
            0.0;
    }

    private void SetCurrentPlan(
        ActionPlan plan) {

        ArgumentNullException.ThrowIfNull(
            plan);

        /*
		 * Always publish. Hold progress changes continuously, and recursive
		 * world-overlay capture may require redraw even for semantically equal
		 * plans.
		 */
        CurrentPlan =
            plan;

        ActionPlanChanged?.Invoke(
            plan);
    }
}
