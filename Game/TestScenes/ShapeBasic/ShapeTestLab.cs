using Godot;

public partial class ShapeTestLab : Node2D
{
	private ShapeStore _store = null!;

	private ShapeDebugView _source = null!;
	private ExpandedShapeDebugView _expanded = null!;

	private Button _expandButton = null!;
	private Button _contractButton = null!;
	private Button _rotateButton = null!;

	private Shape _current;

	public override void _Ready()
	{
		_store = new ShapeStore();

		_source =
			GetNode<ShapeDebugView>("SourceView");

		_expanded =
			GetNode<ExpandedShapeDebugView>(
				"ExpandedView");

		_expandButton =
			GetNode<Button>(
				"UI/HBoxContainer/ExpandButton");

		_contractButton =
			GetNode<Button>(
				"UI/HBoxContainer/ContractButton");

		_rotateButton =
			GetNode<Button>(
				"UI/HBoxContainer/RotateButton");

		_source.Store = _store;
		_expanded.Initialize(_store);

		_expandButton.Pressed += Expand;
		_contractButton.Pressed += Contract;
		_rotateButton.Pressed += Rotate;

		CreateTestShape();
	}

	private void CreateTestShape()
	{
		/*
		 * 故意选择不对称图案，
		 * 这样120°旋转很明显。
		 */
		_current =
			Shape.Inline(
				0x0000_0000_0000_00FFUL);

		Refresh();
	}

	private void Expand()
	{
		ComplexShapeKey children =
			_store.Expand(_current);

		_expanded.SetChildren(children);
	}

	private void Contract()
	{
		ComplexShapeKey children =
			_expanded.GetChildren();

		Shape before = _current;

		_current =
			_store.Contract(children);

		GD.Print(
			$"Contract: {before} -> {_current}");

		Refresh();
	}

	private void Rotate()
	{
		_current =
			_store.Rotate120(_current);

		Refresh();
	}

	private void Refresh()
	{
		_source.Shape = _current;

		_expanded.SetChildren(
			_store.Expand(_current));
	}
}
