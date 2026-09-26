using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Reusable dense honeycomb Container for pointy-top hexagonal children.
///
/// Children are placed row-major:
///
///     0  1  2  3
///       4  5  6  7
///     8  9 ...
///
/// Odd rows are shifted by half a hex width. Neighboring rows overlap their
/// rectangular child bounds by radius / 2, while the actual hexagons share
/// edges exactly.
///
/// The container only lays out children. Hex drawing and hit testing belong to
/// the child Controls.
/// </summary>
public partial class HexGridContainer
	: Container {

	private int _columns =
		9;

	private float _hexRadius =
		32.0f;

	[Export(PropertyHint.Range, "1,32,1")]
	public int Columns {
		get =>
			_columns;

		set {
			int normalized =
				Math.Max(
					value,
					1);

			if (_columns ==
				normalized) {
				return;
			}

			_columns =
				normalized;

			InvalidateLayout();
		}
	}

	/// <summary>
	/// Center-to-vertex distance for every child hexagon.
	/// </summary>
	[Export(PropertyHint.Range, "8,128,1")]
	public float HexRadius {
		get =>
			_hexRadius;

		set {
			float normalized =
				Mathf.Max(
					value,
					1.0f);

			if (Mathf.IsEqualApprox(
					_hexRadius,
					normalized)) {
				return;
			}

			_hexRadius =
				normalized;

			InvalidateLayout();
		}
	}

	public Vector2 HexSize =>
		HexGridGeometry.GetHexSize(
			_hexRadius);

	public override Vector2 _GetMinimumSize() {
		int childCount =
			GetLayoutChildren().Count;

		if (childCount == 0)
			return Vector2.Zero;

		int rows =
			(childCount +
			 _columns - 1) /
			_columns;

		float hexWidth =
			HexSize.X;

		float maxWidth =
			0.0f;

		for (int row = 0;
			 row < rows;
			 row++) {

			int firstIndex =
				row * _columns;

			int rowCount =
				Math.Min(
					_columns,
					childCount -
					firstIndex);

			float rowOffset =
				(row & 1) == 1
					? hexWidth * 0.5f
					: 0.0f;

			maxWidth =
				Mathf.Max(
					maxWidth,
					rowOffset +
					rowCount * hexWidth);
		}

		float height =
			HexSize.Y +
			(rows - 1) *
			HexGridGeometry.GetRowStep(
				_hexRadius);

		return new Vector2(
			maxWidth,
			height);
	}

	public override void _Notification(
		int what) {

		if (what ==
			NotificationSortChildren) {

			LayoutChildren();
		}
	}

	private void LayoutChildren() {
		List<Control> children =
			GetLayoutChildren();

		Vector2 hexSize =
			HexSize;

		float rowStep =
			HexGridGeometry.GetRowStep(
				_hexRadius);

		for (int i = 0;
			 i < children.Count;
			 i++) {

			int row =
				i / _columns;

			int column =
				i % _columns;

			float rowOffset =
				(row & 1) == 1
					? hexSize.X * 0.5f
					: 0.0f;

			Vector2 position =
				new(
					rowOffset +
					column * hexSize.X,
					row * rowStep);

			FitChildInRect(
				children[i],
				new Rect2(
					position,
					hexSize));
		}
	}

	private List<Control> GetLayoutChildren() {
		var result =
			new List<Control>();

		foreach (
			Node child
			in GetChildren()) {

			if (child is Control control &&
				control.Visible) {

				result.Add(
					control);
			}
		}

		return result;
	}

	private void InvalidateLayout() {
		UpdateMinimumSize();

		if (IsInsideTree()) {
			QueueSort();
		}
	}
}
