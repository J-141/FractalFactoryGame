using Godot;
using System;

/// <summary>
/// Standalone sandbox for InventoryUI.
///
/// Tests:
/// - one-row hex hotbar;
/// - multi-row hex full inventory;
/// - stack counts;
/// - several Shape complexities;
/// - Inventory.Changed refresh;
/// - PlayerState.HoldingChanged refresh;
/// - held-shape 120-degree rotation.
/// </summary>
public partial class InventoryUISandbox
	: Node {

	private InventoryUI _inventoryUi = null!;
	private Label _status = null!;

	private ShapeStore _shapeStore = null!;
	private Inventory _inventory = null!;
	private PlayerState _playerState = null!;

	private Item[] _sampleItems =
		Array.Empty<Item>();

	public override void _Ready() {
		_inventoryUi =
			GetNode<InventoryUI>(
				"InventoryUI");

		_status =
			GetNode<Label>(
				"TestControls/Panel/Margin/VBox/Status");

		_shapeStore =
			new ShapeStore(
				new ComplexShapePool());

		_inventory =
			new Inventory(
				slotCount: 30);

		_playerState =
			new PlayerState(
				_inventory);

		_sampleItems =
			CreateSampleItems();

		_inventoryUi.Initialize(
			_playerState,
			_shapeStore);

		GetNode<Button>(
			"TestControls/Panel/Margin/VBox/ToggleFull")
			.Pressed +=
			ToggleFullInventory;

		GetNode<Button>(
			"TestControls/Panel/Margin/VBox/Populate")
			.Pressed +=
			PopulateSamples;

		GetNode<Button>(
			"TestControls/Panel/Margin/VBox/ClearInventory")
			.Pressed +=
			ClearInventory;

		GetNode<Button>(
			"TestControls/Panel/Margin/VBox/AddOne")
			.Pressed +=
			AddOneSample;

		GetNode<Button>(
			"TestControls/Panel/Margin/VBox/HoldSample")
			.Pressed +=
			HoldSample;

		GetNode<Button>(
			"TestControls/Panel/Margin/VBox/RotateHeld")
			.Pressed +=
			RotateHeld;

		GetNode<Button>(
			"TestControls/Panel/Margin/VBox/ClearHeld")
			.Pressed +=
			ClearHeld;

		_inventory.Changed +=
			UpdateStatus;

		_playerState.HoldingChanged +=
			UpdateStatus;

		PopulateSamples();

		UpdateStatus();
	}

	public override void _ExitTree() {
		if (_inventory is not null) {
			_inventory.Changed -=
				UpdateStatus;
		}

		if (_playerState is not null) {
			_playerState.HoldingChanged -=
				UpdateStatus;
		}
	}

	private Item[] CreateSampleItems() {
		Shape empty =
			ShapeStore.Empty;

		Shape full =
			ShapeStore.Full;

		Shape corner =
			_shapeStore.Contract(
				full,
				empty,
				empty,
				empty);

		Shape twoCorners =
			_shapeStore.Contract(
				full,
				full,
				empty,
				empty);

		Shape center =
			_shapeStore.Contract(
				empty,
				empty,
				empty,
				full);

		Shape threeCorners =
			_shapeStore.Contract(
				full,
				full,
				full,
				empty);

		Shape complex =
			_shapeStore.Contract(
				corner,
				twoCorners,
				center,
				empty);

		return new[] {
			_shapeStore.ToItem(
				full),

			_shapeStore.ToItem(
				corner),

			_shapeStore.ToItem(
				twoCorners),

			_shapeStore.ToItem(
				center),

			_shapeStore.ToItem(
				threeCorners),

			_shapeStore.ToItem(
				complex),
		};
	}

	private void PopulateSamples() {
		ClearInventory();

		SetSampleStack(
			slot: 0,
			sample: 0,
			count: 64);

		SetSampleStack(
			slot: 1,
			sample: 1,
			count: 12);

		SetSampleStack(
			slot: 2,
			sample: 2,
			count: 3);

		SetSampleStack(
			slot: 3,
			sample: 3,
			count: 27);

		SetSampleStack(
			slot: 4,
			sample: 4,
			count: 1);

		SetSampleStack(
			slot: 5,
			sample: 5,
			count: 99);

		SetSampleStack(
			slot: 8,
			sample: 2,
			count: 40);

		SetSampleStack(
			slot: 9,
			sample: 3,
			count: 7);

		SetSampleStack(
			slot: 12,
			sample: 5,
			count: 22);

		SetSampleStack(
			slot: 17,
			sample: 1,
			count: 50);

		SetSampleStack(
			slot: 18,
			sample: 4,
			count: 2);

		SetSampleStack(
			slot: 23,
			sample: 0,
			count: 8);

		SetSampleStack(
			slot: 29,
			sample: 5,
			count: 1);

		UpdateStatus();
	}

	private void ClearInventory() {
		for (int i = 0;
			 i < _inventory.SlotCount;
			 i++) {

			_inventory.SetStack(
				i,
				null);
		}

		UpdateStatus();
	}

	private void AddOneSample() {
		_inventory.Add(
			_sampleItems[5],
			count: 1);

		UpdateStatus();
	}

	private void HoldSample() {
		_playerState.Hold(
			new ItemStack(
				_sampleItems[5],
				count: 17));
	}

	private void RotateHeld() {
		if (!_playerState.HeldItem.HasValue) {
			HoldSample();
			return;
		}

		_playerState.SetHeldItemRotation(
			_playerState.HeldItemRotation +
			1);
	}

	private void ClearHeld() {
		_playerState.ClearHeldItem();
	}

	private void ToggleFullInventory() {
		_inventoryUi.ToggleFullInventory();

		UpdateStatus();
	}

	private void SetSampleStack(
		int slot,
		int sample,
		int count) {

		_inventory.SetStack(
			slot,
			new ItemStack(
				_sampleItems[sample],
				count));
	}

	private void UpdateStatus() {
		if (_status is null)
			return;

		string holding =
			_playerState.HeldItem.HasValue
				? $"{_playerState.HeldItem.Value} " +
				  $"rotation={_playerState.HeldItemRotation * 120}°"
				: "empty";

		_status.Text =
			$"Full UI: " +
			$"{(_inventoryUi.IsFullInventoryVisible ? "open" : "closed")}\n" +
			$"Holding: {holding}\n" +
			$"Press E to toggle inventory.";
	}
}
