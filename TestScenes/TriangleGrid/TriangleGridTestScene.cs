using Godot;
using System;

public partial class TriangleGridTestScene : Node2D
{
	[Export]
	public float SideLength { get; set; } = 64.0f;

	[Export]
	public int GridRadius { get; set; } = 10;

	[Export]
	public Color GridColor { get; set; } = Colors.Gray;

	[Export]
	public Color SelectedColor { get; set; } = Colors.Yellow;

	[Export]
	public float GridLineWidth { get; set; } = 1.0f;

	[Export]
	public float SelectedLineWidth { get; set; } = 4.0f;

	private TriangleGrid _grid = null!;
	private Label _label = null!;

	private TriangleGridPosition _mouseGridPosition;
	private bool _hasMouseGridPosition;

	public override void _Ready()
	{
		_label = GetNode<Label>("Label");

		/*
		 * TriangleGrid 使用本 Node2D 的 local coordinate。
		 *
		 * (0, 0) 就是 grid origin。
		 */
		_grid = new TriangleGrid(
			SideLength,
			Vector2.Zero);

		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		/*
		 * GetGlobalMousePosition() 是 global coordinate，
		 * TriangleGrid 使用当前 Node2D 的 local coordinate，
		 * 因此先 ToLocal。
		 */
		Vector2 mousePosition =
			ToLocal(GetGlobalMousePosition());

		TriangleGridPosition position =
			_grid.GetGridPosition(mousePosition);

		_label.Text =
			$"Mouse: ({mousePosition.X:F1}, {mousePosition.Y:F1})\n" +
			$"Grid: ({position.X}, {position.Y})\n" +
			$"Facing: {position.Facing}";

		/*
		 * 只有鼠标进入其他 triangle 时才重绘。
		 */
		if (!_hasMouseGridPosition ||
			position != _mouseGridPosition)
		{
			_mouseGridPosition = position;
			_hasMouseGridPosition = true;

			QueueRedraw();
		}
	}

	public override void _Draw()
	{
		DrawGrid();

		if (_hasMouseGridPosition)
		{
			DrawSelectedTriangle(
				_mouseGridPosition);
		}
	}

	private void DrawGrid()
	{
		for (int y = -GridRadius; y <= GridRadius; y++)
		{
			for (int x = -GridRadius; x <= GridRadius; x++)
			{
				DrawGridTriangle(
					new TriangleGridPosition(
						x,
						y,
						TriangleFacing.Down));

				DrawGridTriangle(
					new TriangleGridPosition(
						x,
						y,
						TriangleFacing.Up));
			}
		}
	}

	private void DrawGridTriangle(
		TriangleGridPosition position)
	{
		TriangleVertices vertices =
			_grid.GetVertices(position);

		DrawTriangleOutline(
			vertices,
			GridColor,
			GridLineWidth);
	}

	private void DrawSelectedTriangle(
		TriangleGridPosition position)
	{
		TriangleVertices vertices =
			_grid.GetVertices(position);

		/*
		 * 半透明填充，方便确认 GetGridPosition()
		 * 返回的 triangle 是否正确。
		 */
		Color fillColor = SelectedColor;
		fillColor.A = 0.25f;

		DrawColoredPolygon(
			new[]
			{
				vertices.V0,
				vertices.V1,
				vertices.V2
			},
			fillColor);

		DrawTriangleOutline(
			vertices,
			SelectedColor,
			SelectedLineWidth);
	}

	private void DrawTriangleOutline(
		TriangleVertices vertices,
		Color color,
		float width)
	{
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
