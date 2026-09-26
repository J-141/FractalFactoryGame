using System;

/// <summary>
/// How the current ActionPlan is activated by primary interaction.
/// </summary>
public abstract record ActionActivation;

/// <summary>
/// Commit immediately when primary interaction starts.
/// </summary>
public sealed record ImmediateActionActivation
    : ActionActivation {

    public static ImmediateActionActivation Instance { get; } =
        new();
}

/// <summary>
/// Primary interaction must remain held on the same logical region for
/// RequiredSeconds before the plan may be committed.
///
/// ContinuityKey is the world-grid cell on which the hold started.
///
/// Hold progress continues while the evaluated action remains on the same
/// TriangleGridPosition, and resets when the cursor moves to another cell.
///
/// InteractionController owns ElapsedSeconds; the planner only supplies the
/// required duration and continuity cell.
/// </summary>
public sealed record HoldActionActivation(
    double RequiredSeconds,
    TriangleGridPosition ContinuityKey,
    double ElapsedSeconds = 0.0
) : ActionActivation {

    public double Progress =>
        RequiredSeconds <= 0.0
            ? 1.0
            : Math.Clamp(
                ElapsedSeconds / RequiredSeconds,
                0.0,
                1.0);

    public bool IsComplete =>
        Progress >= 1.0;
}

/// <summary>
/// Fully evaluated description of the world interaction currently available.
///
/// ActionPlan is derived from InteractionContext. It is shared by overlay
/// presentation and commit. Concrete plans should contain already-resolved
/// targets/results so commit does not need to resolve the cursor again.
/// </summary>
public abstract record ActionPlan(
    bool CanExecute,
    string? FailureReason,
    ActionActivation Activation);

public sealed record NoActionPlan()
    : ActionPlan(
        CanExecute: false,
        FailureReason: null,
        Activation: ImmediateActionActivation.Instance);
