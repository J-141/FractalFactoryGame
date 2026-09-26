using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Screen-space inventory UI using densely tiled hexagonal slots.
///
/// - Hotbar: first HotbarSlotCount slots, one horizontal hex row,
///   always visible.
/// - Full inventory: all slots in multiple staggered hex rows,
///   toggleable.
/// - Held slot: one special always-visible hex slot rendering HeldItem with
///   HeldItemRotation.
///
/// Slot manipulation is intentionally not implemented yet.
/// </summary>
public partial class InventoryUI
	: CanvasLayer {

	[Export(PropertyHint.Range, "1,16,1")]
	public int HotbarSlotCount { get; set; } =
		9;

	[Export(PropertyHint.Range, "1,16,1")]
	public int FullInventoryColumns { get; set; } =
		9;

	/// <summary>
	/// Center-to-vertex radius of every slot hexagon.
	/// </summary>
	[Export(PropertyHint.Range, "16,64,1")]
	public float SlotRadius { get; set; } =
		32.0f;

	[Export]
	public StringName ToggleAction { get; set; } =
		"inventory_toggle";

	private PlayerState? _playerState;
	private ShapeStore? _shapeStore;

	private HexGridContainer? _hotbarSlots;
	private HexGridContainer? _fullSlots;
	private HexGridContainer? _heldSlots;
	private Control? _fullInventoryBlocker;

	private readonly List<InventorySlotView>
		_hotbarViews =
			new();

	private readonly List<InventorySlotView>
		_fullViews =
			new();

	private InventorySlotView? _heldView;

	private bool _started;

	public bool IsFullInventoryVisible =>
		_fullInventoryBlocker?.Visible
		?? false;

	public override void _Ready() {
		ResolveNodes();

		TryStart();
	}

	public override void _ExitTree() {
		Stop();
	}

	public void Initialize(
		PlayerState playerState,
		ShapeStore shapeStore) {

		if (_playerState is not null ||
			_shapeStore is not null) {

			throw new InvalidOperationException(
				"InventoryUI is already initialized.");
		}

		_playerState =
			playerState
			?? throw new ArgumentNullException(
				nameof(playerState));

		_shapeStore =
			shapeStore
			?? throw new ArgumentNullException(
				nameof(shapeStore));

		TryStart();
	}

	public override void _UnhandledInput(
		InputEvent @event) {

		if (!_started)
			return;

		bool toggleRequested;

		if (InputMap.HasAction(
				ToggleAction)) {

			toggleRequested =
				@event.IsActionPressed(
					ToggleAction);
		}
		else {
			toggleRequested =
				@event is InputEventKey key &&
				key.Pressed &&
				!key.Echo &&
				key.Keycode == Key.E;
		}

		if (!toggleRequested)
			return;

		ToggleFullInventory();

		GetViewport()
			.SetInputAsHandled();
	}

	public void ToggleFullInventory() {
		SetFullInventoryVisible(
			!IsFullInventoryVisible);
	}

	public void OpenFullInventory() {
		SetFullInventoryVisible(
			true);
	}

	public void CloseFullInventory() {
		SetFullInventoryVisible(
			false);
	}

	public void Refresh() {
		if (!_started)
			return;

		RefreshInventory();
		RefreshHolding();
	}

	private void ResolveNodes() {
		_hotbarSlots =
			GetNode<HexGridContainer>(
				"Root/BottomCenter/BottomBar/HotbarSlots");

		_heldSlots =
			GetNode<HexGridContainer>(
				"Root/BottomCenter/BottomBar/HeldSlots");

		_fullSlots =
			GetNode<HexGridContainer>(
				"Root/FullInventoryBlocker/FullCenter/" +
				"FullInventoryPanel/FullMargin/FullLayout/FullSlots");

		_fullInventoryBlocker =
			GetNode<Control>(
				"Root/FullInventoryBlocker");
	}

	private void TryStart() {
		if (_started ||
			!IsNodeReady() ||
			_playerState is null ||
			_shapeStore is null) {
			return;
		}

		if (_hotbarSlots is null ||
			_heldSlots is null ||
			_fullSlots is null ||
			_fullInventoryBlocker is null) {

			ResolveNodes();
		}

		int hotbarCount =
			Math.Min(
				HotbarSlotCount,
				_playerState.Inventory.SlotCount);

		_hotbarSlots!.Columns =
			Math.Max(
				hotbarCount,
				1);

		_hotbarSlots.HexRadius =
			SlotRadius;

		_heldSlots!.Columns =
			1;

		_heldSlots.HexRadius =
			SlotRadius;

		_fullSlots!.Columns =
			FullInventoryColumns;

		_fullSlots.HexRadius =
			SlotRadius;

		BuildSlotViews();

		_playerState.Inventory.Changed +=
			OnInventoryChanged;

		_playerState.HoldingChanged +=
			OnHoldingChanged;

		_started =
			true;

		Refresh();

		SetFullInventoryVisible(
			false);
	}

	private void Stop() {
		if (!_started ||
			_playerState is null) {
			return;
		}

		_playerState.Inventory.Changed -=
			OnInventoryChanged;

		_playerState.HoldingChanged -=
			OnHoldingChanged;

		_started =
			false;
	}

	private void BuildSlotViews() {
		ClearChildren(
			_hotbarSlots!);

		ClearChildren(
			_fullSlots!);

		ClearChildren(
			_heldSlots!);

		_hotbarViews.Clear();
		_fullViews.Clear();

		int hotbarCount =
			Math.Min(
				HotbarSlotCount,
				_playerState!
					.Inventory
					.SlotCount);

		for (int i = 0;
			 i < hotbarCount;
			 i++) {

			InventorySlotView view =
				CreateSlot(
					isHoldingSlot: false);

			_hotbarSlots!
				.AddChild(
					view);

			_hotbarViews.Add(
				view);
		}

		for (int i = 0;
			 i < _playerState!
				 .Inventory
				 .SlotCount;
			 i++) {

			InventorySlotView view =
				CreateSlot(
					isHoldingSlot: false);

			_fullSlots!
				.AddChild(
					view);

			_fullViews.Add(
				view);
		}

		_heldView =
			CreateSlot(
				isHoldingSlot: true);

		_heldSlots!
			.AddChild(
				_heldView);

		RefreshContainerLayout(
			_hotbarSlots);

		RefreshContainerLayout(
			_fullSlots);

		RefreshContainerLayout(
			_heldSlots);
	}

	private InventorySlotView CreateSlot(
		bool isHoldingSlot) {

		return InventorySlotView.Create(
			_shapeStore!,
			SlotRadius,
			isHoldingSlot);
	}

	private void RefreshInventory() {
		Inventory inventory =
			_playerState!
				.Inventory;

		for (int i = 0;
			 i < _hotbarViews.Count;
			 i++) {

			_hotbarViews[i]
				.SetStack(
					inventory.GetStack(
						i));
		}

		for (int i = 0;
			 i < _fullViews.Count;
			 i++) {

			_fullViews[i]
				.SetStack(
					inventory.GetStack(
						i));
		}
	}

	private void RefreshHolding() {
		_heldView?.SetStack(
			_playerState!.HeldItem,
			_playerState.HeldItemRotation);
	}

	private void SetFullInventoryVisible(
		bool visible) {

		if (_fullInventoryBlocker is null)
			return;

		_fullInventoryBlocker.Visible =
			visible;
	}

	private void OnInventoryChanged() {
		RefreshInventory();
	}

	private void OnHoldingChanged() {
		RefreshHolding();
	}

	private static void RefreshContainerLayout(
		HexGridContainer container) {

		container.UpdateMinimumSize();
		container.QueueSort();
	}

	private static void ClearChildren(
		Node parent) {

		foreach (
			Node child
			in parent.GetChildren()) {

			parent.RemoveChild(
				child);

			child.QueueFree();
		}
	}
}
