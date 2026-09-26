using System.Collections.Generic;

/// <summary>
/// Crop preview / commit snapshot.
///
/// CropRegion contains the concrete world grid cells reached by crop
/// resolution. For an EmbeddedBoxReference this is the flattened region in
/// the referenced box (and recursively referenced boxes), not the visible
/// reference cell itself.
///
/// ResultShape is the shape produced by cropping that region.
///
/// A CropPlan may be non-executable. In particular, an empty cell or an
/// EmbeddedBoxReference that resolves recursively to only empty cells produces
/// a CropPlan with CanExecute = false. This allows overlay code to render the
/// crop region even when there is currently nothing to crop.
/// </summary>
public sealed record CropPlan(
    bool CanExecute,
    string? FailureReason,
    IReadOnlyList<TriangleGridPosition> CropRegion,
    Shape ResultShape,
    ActionActivation Activation
) : ActionPlan(
    CanExecute,
    FailureReason,
    Activation);
