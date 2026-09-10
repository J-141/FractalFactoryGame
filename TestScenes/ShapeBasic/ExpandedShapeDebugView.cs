using Godot;

public partial class ExpandedShapeDebugView : Node2D
{
	[Export]
	public float ParentSideLength { get; set; } = 300.0f;

	private ShapeDebugView _c0 = null!;
	private ShapeDebugView _c1 = null!;
	private ShapeDebugView _c2 = null!;
	private ShapeDebugView _c3 = null!;

	public override void _Ready()
	{
		_c0 = GetNode<ShapeDebugView>("C0");
		_c1 = GetNode<ShapeDebugView>("C1");
		_c2 = GetNode<ShapeDebugView>("C2");
		_c3 = GetNode<ShapeDebugView>("C3");

		LayoutChildren();
	}

	public void Initialize(ShapeStore store)
	{
		_c0.Store = store;
		_c1.Store = store;
		_c2.Store = store;
		_c3.Store = store;
	}

	public void SetChildren(ComplexShapeKey key)
	{
		_c0.Shape = key.C0;
		_c1.Shape = key.C1;
		_c2.Shape = key.C2;
		_c3.Shape = key.C3;
	}

	public ComplexShapeKey GetChildren()
	{
		return new ComplexShapeKey(
			_c0.Shape,
			_c1.Shape,
			_c2.Shape,
			_c3.Shape);
	}

	private void LayoutChildren()
	{
		float childSide =
			ParentSideLength * 0.5f;

		float parentHeight =
			ParentSideLength *
			Mathf.Sqrt(3.0f) / 2.0f;

		float childHeight =
			childSide *
			Mathf.Sqrt(3.0f) / 2.0f;

		foreach (var child in new[]
				 {
					 _c0, _c1, _c2, _c3
				 })
		{
			child.SideLength = childSide;
		}

		/*
		 * Parent 是 Up triangle。
		 *
		 * 三个外侧 child 为 Up，
		 * 中央 child 为 Down。
		 */

		_c0.Facing = TriangleFacing.Up;
		_c1.Facing = TriangleFacing.Up;
		_c2.Facing = TriangleFacing.Up;
		_c3.Facing = TriangleFacing.Down;

		/*
		 * 下面用的是四个子三角形的质心位置。
		 */

		_c0.Position =
			new Vector2(
				0,
				-parentHeight / 3.0f);

		_c1.Position =
			new Vector2(
				-ParentSideLength / 4.0f,
				parentHeight / 6.0f);

		_c2.Position =
			new Vector2(
				ParentSideLength / 4.0f,
				parentHeight / 6.0f);

		_c3.Position =
			Vector2.Zero;
	}
}
