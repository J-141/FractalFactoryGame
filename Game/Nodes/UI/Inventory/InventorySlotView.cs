using Godot;
using System;

/// <summary>
/// Pointy-top hexagonal inventory slot.
///
/// Regular inventory slots render the canonical Item orientation.
/// The special holding slot renders PlayerState.HeldItemRotation.
///
/// ShapeItemPreviewControl renders an upward-facing root triangle with V0 at
/// the top, and draws that root triangle's border.
/// </summary>
public partial class InventorySlotView
	: Control {
	public const string ScenePath =
	   "res://Nodes/UI/Inventory/InventorySlotView.tscn";
	private static PackedScene? _packedScene;

	/// <summary>
	/// Instantiate an InventorySlotView from its scene and initialize it.
	///
	/// InventorySlotView should be created through this method instead of
	/// calling new InventorySlotView(), so the .tscn remains the authoritative
	/// source for its node structure and editor-configured properties.
	/// </summary>
	public static InventorySlotView Create(
		ShapeStore shapeStore,
		float hexRadius,
		bool isHoldingSlot = false) {

		PackedScene scene =
			_packedScene
			??= GD.Load<PackedScene>(
					ScenePath)
				?? throw new InvalidOperationException(
					$"Could not load InventorySlotView scene: {ScenePath}");

		InventorySlotView view =
			scene.Instantiate<InventorySlotView>();

		view.Initialize(
			shapeStore,
			hexRadius,
			isHoldingSlot);

		return view;
	}



	[Export]
	public Color BackgroundColor { get; set; } =
		new(
			0.10f,
			0.10f,
			0.10f,
			0.96f);

	[Export]
	public Color BorderColor { get; set; } =
		new(
			0.58f,
			0.58f,
			0.58f,
			1.0f);

	[Export]
	public Color HoldingBorderColor { get; set; } =
		new(
			0.95f,
			0.82f,
			0.25f,
			1.0f);

	[Export(PropertyHint.Range, "1,6,0.5")]
	public float BorderWidth { get; set; } =
		2.0f;

	private ShapeItemPreviewControl? _preview;
	private Label? _countLabel;
	private bool _isHoldingSlot;
	private float _hexRadius =
		32.0f;

	public override void _Ready() {
		MouseFilter =
			MouseFilterEnum.Stop;

		FocusMode =
			FocusModeEnum.None;

		EnsureChildren();

		QueueRedraw();
	}

	public void Initialize(
		ShapeStore shapeStore,
		float hexRadius,
		bool isHoldingSlot = false) {

		if (hexRadius <= 0.0f) {
			throw new ArgumentOutOfRangeException(
				nameof(hexRadius));
		}

		_hexRadius =
			hexRadius;

		_isHoldingSlot =
			isHoldingSlot;

		CustomMinimumSize =
			HexGridGeometry.GetHexSize(
				_hexRadius);

		EnsureChildren();

		_preview!.Padding =
			Mathf.Max(
				5.0f,
				_hexRadius * 0.30f);

		_preview.ShapeBorderWidth =
			Mathf.Max(
				1.0f,
				_hexRadius * 0.055f);

		_preview.Initialize(
			shapeStore);

		_countLabel!
			.AddThemeFontSizeOverride(
				"font_size",
				Math.Max(
					12,
					Mathf.RoundToInt(
						_hexRadius * 0.48f)));

		QueueRedraw();
	}

	public void SetStack(
		ItemStack? stack,
		int rotation = 0) {

		EnsureChildren();

		_preview!.SetStack(
			stack,
			rotation);

		_countLabel!.Text =
			stack.HasValue &&
			stack.Value.Count > 1
				? stack.Value.Count.ToString()
				: string.Empty;
	}

	public override void _Draw() {
		Vector2[] vertices =
			HexGridGeometry.GetVertices(
				Size);

		DrawColoredPolygon(
			vertices,
			BackgroundColor);

		Color outlineColor =
			_isHoldingSlot
				? HoldingBorderColor
				: BorderColor;

		for (int i = 0;
			 i < vertices.Length;
			 i++) {

			DrawLine(
				vertices[i],
				vertices[
					(i + 1) %
					vertices.Length],
				outlineColor,
				BorderWidth,
				antialiased: true);
		}
	}

	public override bool _HasPoint(
		Vector2 point) {

		return HexGridGeometry.ContainsPoint(
			point,
			Size);
	}

	public override void _GuiInput(
		InputEvent @event) {

		if (@event is InputEventMouse) {
			AcceptEvent();
		}
	}

	private void EnsureChildren() {
		_countLabel = GetNode<Label>("Count");

		if (_preview is null) {
			_preview =
				new ShapeItemPreviewControl {
					Name = "ShapePreview",
					MouseFilter =
						MouseFilterEnum.Ignore,
				};

			AddChild(
				_preview);

			_preview.AnchorLeft =
				0.0f;

			_preview.AnchorTop =
				0.0f;

			_preview.AnchorRight =
				1.0f;

			_preview.AnchorBottom =
				1.0f;

			_preview.OffsetLeft =
				0.0f;

			_preview.OffsetTop =
				0.0f;

			_preview.OffsetRight =
				0.0f;

			_preview.OffsetBottom =
				0.0f;
		}


	}
}
