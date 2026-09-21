using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Gameplay policy for cursor-based world interaction.
///
/// Evaluate() snapshots all world/player facts required to describe the current
/// action. TryCommit() later consumes only that already-resolved ActionPlan.
///
/// Mutable services needed to execute plans are constructor dependencies rather
/// than TryCommit parameters.
/// </summary>
public sealed class InteractionPlanner {
	public const double DefaultCropHoldSeconds =
		0.5;

	private static readonly TriangleGridPosition
		ShapeLocalCell =
			new(
				0,
				0,
				TriangleFacing.Down);

	private readonly World _world;
	private readonly PlayerState _playerState;
	private readonly ShapeStore _shapeStore;
	private readonly double _cropHoldSeconds;

	public InteractionPlanner(
		World world,
		PlayerState playerState,
		ShapeStore shapeStore,
		double cropHoldSeconds = DefaultCropHoldSeconds) {

		_world =
			world
			?? throw new ArgumentNullException(
				nameof(world));

		_playerState =
			playerState
			?? throw new ArgumentNullException(
				nameof(playerState));

		_shapeStore =
			shapeStore
			?? throw new ArgumentNullException(
				nameof(shapeStore));

		if (cropHoldSeconds <= 0.0) {
			throw new ArgumentOutOfRangeException(
				nameof(cropHoldSeconds),
				"Crop hold duration must be greater than zero.");
		}

		_cropHoldSeconds =
			cropHoldSeconds;
	}

	/// <summary>
	/// Current interaction rules:
	///
	/// 1. ShapeStructure under cursor:
	///      -> CropPlan, regardless of HeldItem.
	///
	/// 2. EmbeddedBoxReference under cursor:
	///      -> TryEvaluateEmbedded(...).
	///
	/// 3. Empty cursor cell:
	///      -> disabled CropPlan.
	///
	/// 4. Everything else:
	///      -> NoActionPlan.
	///
	/// Placement and other interactions are intentionally not implemented yet.
	/// </summary>
	public ActionPlan Evaluate(
		InteractionContext context) {

		ArgumentNullException.ThrowIfNull(
			context);

		if (!ReferenceEquals(
				context.World,
				_world)) {

			throw new InvalidOperationException(
				"InteractionContext belongs to a different World.");
		}

		return context.StructureAtCursor switch {
			ShapeStructure shapeStructure =>
				EvaluateShape(
					context.CursorGridPosition,
					shapeStructure),

			EmbeddedBoxReference reference =>
				TryEvaluateEmbedded(
					context,
					reference),

			null =>
				BuildCropPlan(
					rootCell:
						context.CursorGridPosition,
					cropRegion:
						new[] {
							context.CursorGridPosition
						},
					resultShape:
						ShapeStore.Empty),

			_ =>
				new NoActionPlan(),
		};
	}

	/// <summary>
	/// Commit a previously evaluated ActionPlan.
	///
	/// Commit does not re-evaluate cursor/player intent. It consumes the
	/// already-resolved plan and only validates mutable state that may have
	/// changed since evaluation.
	/// </summary>
	public bool TryCommit(
		ActionPlan plan) {

		ArgumentNullException.ThrowIfNull(
			plan);

		return plan switch {
			CropPlan cropPlan =>
				TryCommitCrop(
					cropPlan),

			_ =>
				false,
		};
	}

	/// <summary>
	/// First-version Crop commit. Crop results go directly into Inventory.
	///
	/// The operation is staged as:
	/// 1. preflight inventory capacity;
	/// 2. validate the entire CropRegion;
	/// 3. remove all ShapeStructures;
	/// 4. add ItemStack(ResultShape, 1) to Inventory.
	///
	/// No world mutation occurs until every ordinary failure condition has
	/// been checked.
	/// </summary>
	private bool TryCommitCrop(
		CropPlan plan) {

		if (!plan.CanExecute ||
			plan.ResultShape == ShapeStore.Empty) {
			return false;
		}

		Item resultItem =
			_shapeStore.ToItem(
				plan.ResultShape);

		Inventory inventory =
			_playerState.Inventory;

		if (!inventory.CanAdd(
				resultItem,
				count: 1)) {
			return false;
		}

		var shapesToRemove =
			new HashSet<ShapeStructure>();

		foreach (
			TriangleGridPosition cell
			in plan.CropRegion) {

			Structure? structure =
				_world.GetStructure(
					cell);

			switch (structure) {
				case null:
					break;

				case ShapeStructure shapeStructure:
					shapesToRemove.Add(
						shapeStructure);
					break;

				default:
					/*
					 * The plan became stale: this region is no longer a pure
					 * empty/ShapeStructure crop region.
					 */
					return false;
			}
		}

		if (shapesToRemove.Count == 0)
			return false;

		foreach (
			ShapeStructure shapeStructure
			in shapesToRemove) {

			if (!_world.RemoveStructure(
					shapeStructure)) {

				throw new InvalidOperationException(
					"Crop region was validated, but a ShapeStructure " +
					"could not be removed.");
			}
		}

		int remaining =
			inventory.Add(
				resultItem,
				count: 1);

		if (remaining != 0) {
			throw new InvalidOperationException(
				"Inventory capacity changed during synchronous Crop commit.");
		}

		return true;
	}

	private ActionPlan EvaluateShape(
		TriangleGridPosition cell,
		ShapeStructure shapeStructure) {

		return BuildCropPlan(
			rootCell:
				cell,
			cropRegion:
				new[] { cell },
			resultShape:
				shapeStructure.Shape);
	}

	/// <summary>
	/// Evaluate an interaction through EmbeddedBoxReference.
	///
	/// Embedded evaluation is deliberately split into two phases:
	///
	/// 1. GetRecursiveRegion:
	///      Resolve the corresponding 4-cell region in TargetBox.
	///      If every cell of that region belongs to the SAME
	///      EmbeddedBoxReference, resolve the whole region through that
	///      reference again.
	///
	///      Region size is therefore 4, 16, 64, ... .
	///
	/// 2. TryBuildCropPlan:
	///      Inspect only the final region:
	///      - all empty / ShapeStructure -> CropPlan;
	///      - all empty                  -> disabled CropPlan;
	///      - any other Structure        -> NoActionPlan.
	///
	/// This method returns ActionPlan because embedded interactions may later
	/// support operations other than crop.
	/// </summary>
	private ActionPlan TryEvaluateEmbedded(
		InteractionContext context,
		EmbeddedBoxReference reference) {

		RecursiveCropRegion? region =
			GetRecursiveRegion(
				context.World,
				context.CursorGridPosition,
				reference);

		if (region is null)
			return new NoActionPlan();

		return TryBuildCropPlan(
			context.World,
			context.CursorGridPosition,
			region);
	}

	/// <summary>
	/// Resolve the visible reference cell into a concrete region.
	///
	/// After each 2x expansion, continue ONLY when every cell in the current
	/// region is occupied by the same EmbeddedBoxReference instance.
	///
	/// This is intentionally region-wise rather than branch-wise recursion:
	/// mixed regions stop expanding immediately and are evaluated as one crop
	/// region by TryBuildCropPlan().
	///
	/// EmbeddedBoxReference footprints are finite. Each expansion multiplies
	/// region size by four, so eventually the region cannot remain entirely
	/// inside one finite reference footprint.
	/// </summary>
	private RecursiveCropRegion? GetRecursiveRegion(
		World world,
		TriangleGridPosition rootCell,
		EmbeddedBoxReference rootReference) {

		var levels =
			new List<RegionExpansionLevel>();

		TriangleGridPosition[] parentWorldCells =
			new[] { rootCell };

		EmbeddedBoxReference currentReference =
			rootReference;

		while (true) {
			RegionExpansionLevel? level =
				TryExpandRegion(
					world,
					parentWorldCells,
					currentReference);

			if (level is null)
				return null;

			levels.Add(
				level);

			TriangleGridPosition[] currentRegion =
				level.ChildWorldCells;

			if (!TryGetCommonEmbeddedReference(
					world,
					currentRegion,
					out EmbeddedBoxReference? nextReference)) {

				return new RecursiveCropRegion(
					currentRegion,
					levels);
			}

			parentWorldCells =
				currentRegion;

			currentReference =
				nextReference;
		}
	}

	/// <summary>
	/// Expand every parent cell through the same EmbeddedBoxReference.
	///
	/// For each parent:
	/// reference-world cell
	///   -> reference-local cell
	///   -> four scale-2 children
	///   -> TargetBox world cells
	///
	/// Child order is kept exactly as TriangleGrid.GetScale2Children(...), and
	/// parent order is preserved. This gives a stable 4-ary ordering for later
	/// ShapeStore.Contract().
	/// </summary>
	private RegionExpansionLevel? TryExpandRegion(
		World world,
		IReadOnlyList<TriangleGridPosition> parentWorldCells,
		EmbeddedBoxReference reference) {

		Box targetBox =
			reference.TargetBox;

		if (!world.BoxGrid.Contains(
				targetBox)) {
			return null;
		}

		var parentReferenceLocalCells =
			new TriangleGridPosition[
				parentWorldCells.Count];

		var childTargetLocalCells =
			new TriangleGridPosition[
				parentWorldCells.Count * 4];

		var childWorldCells =
			new TriangleGridPosition[
				parentWorldCells.Count * 4];

		int childIndex =
			0;

		for (int parentIndex = 0;
			 parentIndex < parentWorldCells.Count;
			 parentIndex++) {

			TriangleGridPosition parentWorldCell =
				parentWorldCells[
					parentIndex];

			/*
			 * This should be true by construction:
			 * - root: cursor is on reference;
			 * - recursive levels: every parent was verified to belong to the
			 *   same reference before expansion.
			 */
			if (!ReferenceEquals(
					world.GetStructure(
						parentWorldCell),
					reference)) {
				return null;
			}

			TriangleGridPosition referenceLocalCell =
				TriangleGrid.InverseTransformCell(
					parentWorldCell,
					reference.Anchor,
					reference.GridRotation);

			parentReferenceLocalCells[
				parentIndex] =
					referenceLocalCell;

			int childCount =
				0;

			foreach (
				TriangleGridPosition childTargetLocalCell
				in TriangleGrid.GetScale2Children(
					referenceLocalCell)) {

				if (!targetBox.LocalRegion.Contains(
						childTargetLocalCell)) {
					return null;
				}

				TriangleGridPosition childWorldCell =
					TriangleGrid.TransformCell(
						childTargetLocalCell,
						targetBox.Anchor,
						targetBox.GridRotation);

				if (!ReferenceEquals(
						world.GetBox(
							childWorldCell),
						targetBox)) {
					return null;
				}

				childTargetLocalCells[
					childIndex] =
						childTargetLocalCell;

				childWorldCells[
					childIndex] =
						childWorldCell;

				childIndex++;
				childCount++;
			}

			if (childCount != 4) {
				throw new InvalidOperationException(
					"Scale-2 triangle parent must contain exactly four children.");
			}
		}

		return new RegionExpansionLevel(
			reference,
			targetBox,
			parentWorldCells,
			parentReferenceLocalCells,
			childTargetLocalCells,
			childWorldCells);
	}

	/// <summary>
	/// Return true only when every cell is occupied by exactly the same
	/// EmbeddedBoxReference instance.
	/// </summary>
	private static bool TryGetCommonEmbeddedReference(
		World world,
		IReadOnlyList<TriangleGridPosition> region,
		out EmbeddedBoxReference? reference) {

		reference =
			null;

		if (region.Count == 0)
			return false;

		for (int i = 0;
			 i < region.Count;
			 i++) {

			if (world.GetStructure(
					region[i])
				is not EmbeddedBoxReference current) {

				reference =
					null;

				return false;
			}

			if (reference is null) {
				reference =
					current;

				continue;
			}

			if (!ReferenceEquals(
					reference,
					current)) {

				reference =
					null;

				return false;
			}
		}

		return reference is not null;
	}

	/// <summary>
	/// Build a crop plan from the FINAL resolved region.
	///
	/// The final region is accepted only when every cell is either:
	/// - empty; or
	/// - ShapeStructure.
	///
	/// Any other Structure makes the current embedded interaction unavailable.
	///
	/// The region expansion history is planner-internal only. It is used to
	/// contract leaf shapes through the same sequence of reference/box frames
	/// that produced the region. CropPlan itself stores only the flat region
	/// and final ResultShape.
	/// </summary>
	private ActionPlan TryBuildCropPlan(
		World world,
		TriangleGridPosition rootCell,
		RecursiveCropRegion region) {

		if (region.Levels.Count == 0)
			return new NoActionPlan();

		RegionExpansionLevel finalLevel =
			region.Levels[
				region.Levels.Count - 1];

		if (finalLevel.ChildWorldCells.Length !=
			region.Cells.Count) {
			return new NoActionPlan();
		}

		var currentShapes =
			new Shape[
				region.Cells.Count];

		for (int i = 0;
			 i < region.Cells.Count;
			 i++) {

			TriangleGridPosition cell =
				region.Cells[i];

			Structure? structure =
				world.GetStructure(
					cell);

			if (structure is null) {
				currentShapes[i] =
					ShapeStore.Empty;

				continue;
			}

			if (structure
				is not ShapeStructure shapeStructure) {

				return new NoActionPlan();
			}

			if (!TryExpressShapeInFrame(
					shape:
						shapeStructure.Shape,
					sourceLocalCell:
						ShapeLocalCell,
					sourceAnchor:
						shapeStructure.Anchor,
					sourceRotation:
						shapeStructure.GridRotation,
					targetLocalCell:
						finalLevel.ChildTargetLocalCells[i],
					targetAnchor:
						finalLevel.TargetBox.Anchor,
					targetRotation:
						finalLevel.TargetBox.GridRotation,
					out Shape localShape)) {

				return new NoActionPlan();
			}

			currentShapes[i] =
				localShape;
		}

		/*
		 * Collapse from the deepest target box back toward the visible root
		 * reference one expansion level at a time.
		 */
		for (int levelIndex =
				 region.Levels.Count - 1;
			 levelIndex >= 0;
			 levelIndex--) {

			RegionExpansionLevel level =
				region.Levels[
					levelIndex];

			int parentCount =
				level.ParentWorldCells.Count;

			if (currentShapes.Length !=
				parentCount * 4) {
				return new NoActionPlan();
			}

			var parentShapesInReferenceFrame =
				new Shape[
					parentCount];

			for (int parentIndex = 0;
				 parentIndex < parentCount;
				 parentIndex++) {

				int childIndex =
					parentIndex * 4;

				Shape contracted =
					_shapeStore.Contract(
						currentShapes[
							childIndex],
						currentShapes[
							childIndex + 1],
						currentShapes[
							childIndex + 2],
						currentShapes[
							childIndex + 3]);

				/*
				 * Important:
				 *
				 * EmbeddedBoxReference mapping is defined in LOCAL lattice
				 * coordinates as:
				 *
				 *     reference local q
				 *         -> target local 2q
				 *
				 * The four children passed to Contract() are therefore the
				 * scale-2 children of this exact reference-local parent cell.
				 * Contract() already returns the Shape in that parent cell's
				 * local frame.
				 *
				 * TargetBox.Anchor/GridRotation and
				 * Reference.Anchor/GridRotation place those two structures at
				 * unrelated world locations; comparing their world vertices
				 * here is invalid and was the reason this path returned
				 * NoActionPlan.
				 */
				parentShapesInReferenceFrame[
					parentIndex] =
						contracted;
			}

			if (levelIndex == 0) {
				if (parentShapesInReferenceFrame.Length != 1)
					return new NoActionPlan();

				return BuildCropPlan(
					rootCell:
						rootCell,
					cropRegion:
						region.Cells,
					resultShape:
						parentShapesInReferenceFrame[0]);
			}

			/*
			 * This level's parent cells are exactly the previous level's child
			 * cells. Convert each collapsed Shape from the nested reference
			 * frame into the previous TargetBox-local child frame, then the
			 * next loop iteration can Contract them in groups of four.
			 */
			RegionExpansionLevel previousLevel =
				region.Levels[
					levelIndex - 1];

			if (previousLevel.ChildWorldCells.Length !=
				parentShapesInReferenceFrame.Length) {
				return new NoActionPlan();
			}

			var previousTargetFrameShapes =
				new Shape[
					parentShapesInReferenceFrame.Length];

			for (int i = 0;
				 i < parentShapesInReferenceFrame.Length;
				 i++) {

				if (level.ParentWorldCells[i] !=
					previousLevel.ChildWorldCells[i]) {
					return new NoActionPlan();
				}

				if (!TryExpressShapeInFrame(
						shape:
							parentShapesInReferenceFrame[i],
						sourceLocalCell:
							level.ParentReferenceLocalCells[i],
						sourceAnchor:
							level.Reference.Anchor,
						sourceRotation:
							level.Reference.GridRotation,
						targetLocalCell:
							previousLevel.ChildTargetLocalCells[i],
						targetAnchor:
							previousLevel.TargetBox.Anchor,
						targetRotation:
							previousLevel.TargetBox.GridRotation,
						out Shape previousFrameShape)) {

					return new NoActionPlan();
				}

				previousTargetFrameShapes[i] =
					previousFrameShape;
			}

			currentShapes =
				previousTargetFrameShapes;
		}

		throw new InvalidOperationException(
			"Embedded crop contraction did not produce a root Shape.");
	}

	private CropPlan BuildCropPlan(
		TriangleGridPosition rootCell,
		IReadOnlyList<TriangleGridPosition> cropRegion,
		Shape resultShape) {

		if (resultShape == ShapeStore.Empty) {
			return new CropPlan(
				CanExecute: false,
				FailureReason: "Crop region is empty.",
				CropRegion: cropRegion,
				ResultShape: resultShape,
				Activation:
					new HoldActionActivation(
						_cropHoldSeconds,
						rootCell));
		}

		Item resultItem =
			_shapeStore.ToItem(
				resultShape);

		bool inventoryHasSpace =
			_playerState.Inventory.CanAdd(
				resultItem,
				count: 1);

		return new CropPlan(
			CanExecute:
				inventoryHasSpace,
			FailureReason:
				inventoryHasSpace
					? null
					: "Inventory is full.",
			CropRegion:
				cropRegion,
			ResultShape:
				resultShape,
			Activation:
				new HoldActionActivation(
					_cropHoldSeconds,
					rootCell));
	}

	/// <summary>
	/// Re-express a Shape between two ordered triangle coordinate frames that
	/// describe the same physical world triangle.
	/// </summary>
	private bool TryExpressShapeInFrame(
		Shape shape,
		TriangleGridPosition sourceLocalCell,
		Vector2I sourceAnchor,
		int sourceRotation,
		TriangleGridPosition targetLocalCell,
		Vector2I targetAnchor,
		int targetRotation,
		out Shape transformedShape) {

		var (
			sourceLocalV0,
			sourceLocalV1,
			sourceLocalV2) =
				TriangleGrid.GetCellVertices(
					sourceLocalCell);

		Vector2I sourceWorldV0 =
			TriangleGrid.TransformVertex(
				sourceLocalV0,
				sourceAnchor,
				sourceRotation);

		Vector2I sourceWorldV1 =
			TriangleGrid.TransformVertex(
				sourceLocalV1,
				sourceAnchor,
				sourceRotation);

		Vector2I sourceWorldV2 =
			TriangleGrid.TransformVertex(
				sourceLocalV2,
				sourceAnchor,
				sourceRotation);

		var (
			targetLocalV0,
			targetLocalV1,
			targetLocalV2) =
				TriangleGrid.GetCellVertices(
					targetLocalCell);

		Vector2I targetWorldV0 =
			TriangleGrid.TransformVertex(
				targetLocalV0,
				targetAnchor,
				targetRotation);

		Vector2I targetWorldV1 =
			TriangleGrid.TransformVertex(
				targetLocalV1,
				targetAnchor,
				targetRotation);

		Vector2I targetWorldV2 =
			TriangleGrid.TransformVertex(
				targetLocalV2,
				targetAnchor,
				targetRotation);

		if (sourceWorldV0 == targetWorldV0 &&
			sourceWorldV1 == targetWorldV1 &&
			sourceWorldV2 == targetWorldV2) {

			transformedShape =
				shape;

			return true;
		}

		if (sourceWorldV0 == targetWorldV1 &&
			sourceWorldV1 == targetWorldV2 &&
			sourceWorldV2 == targetWorldV0) {

			transformedShape =
				_shapeStore.Rotate120(
					shape);

			return true;
		}

		if (sourceWorldV0 == targetWorldV2 &&
			sourceWorldV1 == targetWorldV0 &&
			sourceWorldV2 == targetWorldV1) {

			transformedShape =
				_shapeStore.Rotate240(
					shape);

			return true;
		}

		transformedShape =
			default;

		return false;
	}

	/// <summary>
	/// One whole-region upscale step through a single EmbeddedBoxReference.
	///
	/// This is planner-internal bookkeeping only. CropPlan deliberately does
	/// not expose this hierarchy.
	/// </summary>
	private sealed record RegionExpansionLevel(
		EmbeddedBoxReference Reference,
		Box TargetBox,
		IReadOnlyList<TriangleGridPosition> ParentWorldCells,
		IReadOnlyList<TriangleGridPosition> ParentReferenceLocalCells,
		TriangleGridPosition[] ChildTargetLocalCells,
		TriangleGridPosition[] ChildWorldCells);

	private sealed record RecursiveCropRegion(
		IReadOnlyList<TriangleGridPosition> Cells,
		IReadOnlyList<RegionExpansionLevel> Levels);
}
