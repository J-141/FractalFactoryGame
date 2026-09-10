using Godot;
using System;
using System.Collections.Generic;


public partial class Sandbox : Node2D {
	private enum PlacementMode {
		None,
		Box,
		FullShape,
		EmbeddedReference,
	}


	private const int HexBoxSideLength = 4;
	private const float GridSideLength = 48.0f;
	private const int InitialChunkRadius = 1;
	private const int DebugGridRadius = 14;


	private static readonly TriangleGridPosition
		LocalShapeCell =
			new(
				0,
				0,
				TriangleFacing.Down);


	private WorldRendererRoot _worldRenderer = null!;

	private VBoxContainer _boxList = null!;

	private Label _statusLabel = null!;


	private readonly Dictionary<Box, Control>
		_boxRows =
			new(ReferenceEqualityComparer.Instance);


	private PlacementMode _placementMode;

	private Box? _referenceTarget;

	private int _nextBoxNumber = 1;


	public World World { get; private set; } = null!;

	public ShapeStore ShapeStore { get; private set; } = null!;


	public override void _Ready() {
		_worldRenderer =
			GetNode<WorldRendererRoot>(
				"WorldRendererRoot");

		_boxList =
			GetNode<VBoxContainer>(
				"UI/Panel/Margin/VBox/BoxList");

		_statusLabel =
			GetNode<Label>(
				"UI/Panel/Margin/VBox/Status");

		GetNode<Button>(
			"UI/Panel/Margin/VBox/PlaceBox")
			.Pressed +=
			BeginPlaceBox;

		GetNode<Button>(
			"UI/Panel/Margin/VBox/PlaceFullShape")
			.Pressed +=
			BeginPlaceFullShape;


		/*
		 * Sandbox 是当前唯一创建 model 的地方。
		 *
		 * 如果项目中的构造函数签名之后变化，
		 * 只需要调整 CreateWorld()/CreateShapeStore()。
		 */
		ShapeStore =
			CreateShapeStore();

		World =
			CreateWorld();

		World.StructureGrid.Changed +=
			OnWorldChanged;

		World.BoxGrid.Changed +=
			OnWorldChanged;

		_worldRenderer.Initialize(
			World,
			ShapeStore);

		LoadInitialChunks();

		SetStatus(
			"选择一个放置按钮，然后点击三角格。");

		QueueRedraw();
	}


	public override void _ExitTree() {
		if (World != null) {
			World.StructureGrid.Changed -=
				OnWorldChanged;

			World.BoxGrid.Changed -=
				OnWorldChanged;
		}
	}


	public override void _UnhandledInput(
		InputEvent @event) {
		if (@event
			is not InputEventMouseButton mouse) {
			return;
		}

		if (!mouse.Pressed ||
			mouse.ButtonIndex != MouseButton.Left) {
			return;
		}

		if (_placementMode ==
			PlacementMode.None) {
			return;
		}

		Vector2 worldPosition =
			GetGlobalMousePosition();

		TriangleGridPosition cell =
			World.StructureGrid
				.GetGridPosition(
					worldPosition);

		switch (_placementMode) {
			case PlacementMode.Box:
				PlaceHexBox(
					cell,
					worldPosition);
				break;

			case PlacementMode.FullShape:
				PlaceFullShape(
					cell);
				break;

			case PlacementMode.EmbeddedReference:
				PlaceEmbeddedReference(
					cell,
					worldPosition);
				break;
		}

		GetViewport()
			.SetInputAsHandled();
	}


	public override void _Draw() {
		DrawDebugGrid();
		DrawBoxCells();
		DrawEmbeddedReferenceCells();
	}


	private static ShapeStore CreateShapeStore() {
		return new ShapeStore(
			new ComplexShapePool());
	}


	private static World CreateWorld() {

		return new World(
			32);
	}


	private void BeginPlaceBox() {
		_placementMode =
			PlacementMode.Box;

		_referenceTarget =
			null;

		SetStatus(
			"放置 Box：点击格子附近的 lattice vertex。");
	}


	private void BeginPlaceFullShape() {
		_placementMode =
			PlacementMode.FullShape;

		_referenceTarget =
			null;

		SetStatus(
			"放置 Full Shape：点击目标 triangle cell。");
	}


	private void BeginPlaceReference(
		Box targetBox) {
		if (!World.BoxGrid.Contains(
				targetBox)) {
			SetStatus(
				"该 Box 已不存在。");

			return;
		}

		_placementMode =
			PlacementMode.EmbeddedReference;

		_referenceTarget =
			targetBox;

		SetStatus(
			"放置 Embedded Reference：点击其 anchor vertex。");
	}


	private void PlaceHexBox(
		TriangleGridPosition clickedCell,
		Vector2 mouseWorldPosition) {
		Vector2I anchor =
			GetNearestVertex(
				clickedCell,
				mouseWorldPosition);

		var box =
			new Box(
				TriangleGridRegions.Hexagon(
					HexBoxSideLength),
				anchor);

		/*
		 * Box place:
		 * 1. BoxGrid 内不能与另一个 Box 重叠。
		 * 2. 新 Box footprint 必须完全没有 Structure。
		 */
		if (!World.BoxGrid.CanPlaceStructure(
				box)) {
			SetStatus(
				"无法放置 Box：与已有 Box 重叠。");

			return;
		}

		if (!IsStructureRegionEmpty(
				box.EnumerateOccupiedCells())) {
			SetStatus(
				"无法放置 Box：目标区域中已有 Structure。");

			return;
		}

		if (!World.BoxGrid.TryPlaceStructure(
				box)) {
			SetStatus(
				"BoxGrid 拒绝了该 Box。");

			return;
		}

		EnsureStructureChunksLoaded(
			box);

		AddBoxRow(
			box);

		_placementMode =
			PlacementMode.None;

		SetStatus(
			"已放置边长为 4 的 hex Box。");
	}


	private void PlaceFullShape(
		TriangleGridPosition targetCell) {
		if (!TryGetTransformForCell(
				targetCell,
				out Vector2I anchor,
				out int rotation)) {
			SetStatus(
				"无法得到该 triangle cell 的 Structure transform。");

			return;
		}

		var shape =
			new ShapeStructure(
				anchor,
				Shape.Inline(
					ulong.MaxValue),
				rotation);

		if (!CanPlaceNormalStructure(
				shape,
				out string error)) {
			SetStatus(
				error);

			return;
		}

		if (!World.StructureGrid.TryPlaceStructure(
				shape)) {
			SetStatus(
				"StructureGrid 拒绝了 Full Shape。");

			return;
		}

		EnsureStructureChunksLoaded(
			shape);

		_placementMode =
			PlacementMode.None;

		SetStatus(
			"已放置 Full Shape。");
	}


	private void PlaceEmbeddedReference(
		TriangleGridPosition clickedCell,
		Vector2 mouseWorldPosition) {
		Box? targetBox =
			_referenceTarget;

		if (targetBox == null ||
			!World.BoxGrid.Contains(
				targetBox)) {
			_placementMode =
				PlacementMode.None;

			_referenceTarget =
				null;

			SetStatus(
				"目标 Box 已不存在。");

			return;
		}

		Vector2I anchor =
			GetNearestVertex(
				clickedCell,
				mouseWorldPosition);

		EmbeddedBoxReference reference;

		try {
			reference =
				new EmbeddedBoxReference(
					targetBox,
					anchor);
		}
		catch (Exception exception) {
			SetStatus(
				$"无法创建 Reference：{exception.Message}");

			return;
		}

		if (!CanPlaceNormalStructure(
				reference,
				out string error)) {
			SetStatus(
				error);

			return;
		}

		if (!World.StructureGrid.TryPlaceStructure(
				reference)) {
			SetStatus(
				"StructureGrid 拒绝了 Embedded Reference。");

			return;
		}

		/*
		 * Reference 自己涉及的 chunks 要加载。
		 */
		EnsureStructureChunksLoaded(
			reference);

		/*
		 * TargetBox history texture 要完整，
		 * 所以 Sandbox 同时确保 TargetBox footprint
		 * 的所有 chunks 都 loaded。
		 */
		EnsureStructureChunksLoaded(
			targetBox);

		_placementMode =
			PlacementMode.None;

		_referenceTarget =
			null;

		SetStatus(
			"已放置 Embedded Reference。");
	}


	private void TryRemoveBox(
		Box box) {
		if (!World.BoxGrid.Contains(
				box)) {
			RemoveBoxRow(
				box);

			SetStatus(
				"该 Box 已不存在。");

			return;
		}

		/*
		 * 当前 World rule：
		 * Box 删除前，其整个 footprint 必须没有 Structure。
		 */
		if (!IsStructureRegionEmpty(
				box.EnumerateOccupiedCells())) {
			SetStatus(
				"无法删除 Box：Box 区域中仍有 Structure。");

			return;
		}

		if (!World.BoxGrid.RemoveStructure(
				box)) {
			SetStatus(
				"BoxGrid 拒绝删除该 Box。");

			return;
		}

		if (ReferenceEquals(
				_referenceTarget,
				box)) {
			_referenceTarget =
				null;

			_placementMode =
				PlacementMode.None;
		}

		RemoveBoxRow(
			box);

		SetStatus(
			"Box 已删除。");
	}


	private void AddBoxRow(
		Box box) {
		if (_boxRows.ContainsKey(
				box)) {
			return;
		}

		var row =
			new HBoxContainer();

		var label =
			new Label {
				Text =
					$"Box {_nextBoxNumber++}",
			};

		var remove =
			new Button {
				Text =
					"Remove Box",
			};

		var addReference =
			new Button {
				Text =
					"Add Embedded Reference",
			};

		remove.Pressed +=
			() =>
				TryRemoveBox(
					box);

		addReference.Pressed +=
			() =>
				BeginPlaceReference(
					box);

		row.AddChild(
			label);

		row.AddChild(
			remove);

		row.AddChild(
			addReference);

		_boxList.AddChild(
			row);

		_boxRows.Add(
			box,
			row);
	}


	private void RemoveBoxRow(
		Box box) {
		if (!_boxRows.Remove(
				box,
				out Control? row)) {
			return;
		}

		row.QueueFree();
	}


	private bool CanPlaceNormalStructure(
		Structure structure,
		out string error) {
		if (!World.StructureGrid.CanPlaceStructure(
				structure)) {
			error =
				"无法放置：Structure footprint 与已有 Structure 重叠。";

			return false;
		}

		/*
		 * 普通 Structure 不能跨 Box boundary：
		 *
		 * - 所有 cells 都 outside，合法；
		 * - 所有 cells 都在同一个 Box，合法；
		 * - null/Box 混合或 BoxA/BoxB 混合，非法。
		 */
		Box? container =
			null;

		bool first =
			true;

		foreach (
			TriangleGridPosition cell
			in structure.EnumerateOccupiedCells()) {
			Box? cellBox =
				null;

			if (World.BoxGrid.TryGetStructure(
					cell,
					out Structure? boxStructure)) {
				cellBox =
					boxStructure as Box;
			}

			if (first) {
				container =
					cellBox;

				first =
					false;

				continue;
			}

			if (!ReferenceEquals(
					container,
					cellBox)) {
				error =
					"无法放置：Structure 不能跨越 Box boundary。";

				return false;
			}
		}

		error =
			string.Empty;

		return true;
	}


	private bool IsStructureRegionEmpty(
		IEnumerable<TriangleGridPosition> region) {
		foreach (
			TriangleGridPosition cell
			in region) {
			if (World.StructureGrid.Contains(
					cell)) {
				return false;
			}
		}

		return true;
	}


	private void EnsureStructureChunksLoaded(
		Structure structure) {
		foreach (
			Vector2I chunk
			in _worldRenderer
				.GetChunksForStructure(
					structure)) {
			_worldRenderer.LoadChunk(
				chunk);
		}
	}


	private void LoadInitialChunks() {
		for (
			int y = -InitialChunkRadius;
			y <= InitialChunkRadius;
			y++) {
			for (
				int x = -InitialChunkRadius;
				x <= InitialChunkRadius;
				x++) {
				_worldRenderer.LoadChunk(
					new Vector2I(
						x,
						y));
			}
		}
	}


	private Vector2I GetNearestVertex(
		TriangleGridPosition cell,
		Vector2 worldPosition) {
		var (v0, v1, v2) =
			TriangleGrid.GetCellVertices(
				cell);

		Vector2I best =
			v0;

		float bestDistanceSquared =
			World.StructureGrid
				.GetVertexPosition(v0)
				.DistanceSquaredTo(
					worldPosition);

		Test(
			v1);

		Test(
			v2);

		return best;


		void Test(
			Vector2I candidate) {
			float distanceSquared =
				World.StructureGrid
					.GetVertexPosition(
						candidate)
					.DistanceSquaredTo(
						worldPosition);

			if (distanceSquared >=
				bestDistanceSquared) {
				return;
			}

			best =
				candidate;

			bestDistanceSquared =
				distanceSquared;
		}
	}


	/// <summary>
	/// 求一个 Structure local Down(0,0) cell
	/// 精确变换到 targetCell 时所需的 anchor/rotation。
	///
	/// 用 6 rotations × target 3 vertices 穷举，
	/// 避免把 Up/Down 的 anchor 规则硬编码两遍。
	/// </summary>
	private static bool TryGetTransformForCell(
		TriangleGridPosition targetCell,
		out Vector2I anchor,
		out int rotation) {
		var (v0, v1, v2) =
			TriangleGrid.GetCellVertices(
				targetCell);

		Vector2I[] anchors =
		{
			v0,
			v1,
			v2,
		};

		for (
			int candidateRotation = 0;
			candidateRotation < 6;
			candidateRotation++) {
			foreach (
				Vector2I candidateAnchor
				in anchors) {
				TriangleGridPosition transformed =
					TriangleGrid.TransformCell(
						LocalShapeCell,
						candidateAnchor,
						candidateRotation);

				if (transformed !=
					targetCell) {
					continue;
				}

				anchor =
					candidateAnchor;

				rotation =
					candidateRotation;

				return true;
			}
		}

		anchor =
			default;

		rotation =
			0;

		return false;
	}


	private void OnWorldChanged(
		object? sender,
		TriangleGridChangedEventArgs e) {
		QueueRedraw();
	}


	private void SetStatus(
		string text) {
		_statusLabel.Text =
			text;
	}


	/*
	 * Test-only drawing.
	 *
	 * 正式 renderer 中 grid / Box frame / Reference visual
	 * 应由各自 renderer 负责。
	 * 这里仅用于让 Sandbox 在这些 renderer 尚未全部实现时
	 * 仍然可以看见交互结果。
	 */
	private void DrawDebugGrid() {
		for (
			int y = -DebugGridRadius;
			y <= DebugGridRadius;
			y++) {
			for (
				int x = -DebugGridRadius;
				x <= DebugGridRadius;
				x++) {
				DrawTriangleOutline(
					new TriangleGridPosition(
						x,
						y,
						TriangleFacing.Down),
					new Color(
						0.22f,
						0.22f,
						0.25f,
						0.8f));

				DrawTriangleOutline(
					new TriangleGridPosition(
						x,
						y,
						TriangleFacing.Up),
					new Color(
						0.22f,
						0.22f,
						0.25f,
						0.8f));
			}
		}
	}


	private void DrawBoxCells() {
		foreach (
			Structure structure
			in World.BoxGrid.Structures) {
			if (structure is not Box box)
				continue;

			foreach (
				TriangleGridPosition cell
				in box.EnumerateOccupiedCells()) {
				DrawTriangleOutline(
					cell,
					new Color(
						0.2f,
						0.75f,
						1.0f,
						0.7f),
					2.0f);
			}
		}
	}


	private void DrawEmbeddedReferenceCells() {
		foreach (
			Structure structure
			in World.StructureGrid.Structures) {
			if (structure
				is not EmbeddedBoxReference reference) {
				continue;
			}

			foreach (
				TriangleGridPosition cell
				in reference.EnumerateOccupiedCells()) {
				DrawTriangleOutline(
					cell,
					new Color(
						1.0f,
						0.75f,
						0.2f,
						0.9f),
					2.0f);
			}
		}
	}


	private void DrawTriangleOutline(
		TriangleGridPosition cell,
		Color color,
		float width = 1.0f) {
		TriangleVertices vertices =
			World.StructureGrid.GetVertices(
				cell);

		DrawLine(
			vertices.V0,
			vertices.V1,
			color,
			width);

		DrawLine(
			vertices.V1,
			vertices.V2,
			color,
			width);

		DrawLine(
			vertices.V2,
			vertices.V0,
			color,
			width);
	}
}
