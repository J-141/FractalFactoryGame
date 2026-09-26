using Godot;
using System;
using System.Collections.Generic;


public partial class ShapeStructureRenderer : Node2D
{
	[Export]
	public Color ShapeColor { get; set; } = Colors.White;


	private WorldRendererRoot? _worldRenderer;

	private ArrayMesh? _unitTriangleMesh;

	private bool _started;


	/*
	 * 只为当前 loaded chunks 保存 Shape index。
	 * Unloaded world 不产生 renderer-side Shape cache。
	 */
	private readonly HashSet<Vector2I>
		_loadedChunks =
			new();


	private readonly Dictionary<
		ShapeStructure,
		Vector2I>
		_chunkByShape =
			new(ReferenceEqualityComparer.Instance);


	private readonly Dictionary<
		Vector2I,
		HashSet<ShapeStructure>>
		_shapesByChunk =
			new();


	/*
	 * 一个 loaded Shape chunk 对应一个 MultiMeshInstance2D batch。
	 * Chunk 本身不是 scene-tree architecture object；
	 * 这里只创建最低限度的 Godot render primitive。
	 */
	private readonly Dictionary<
		Vector2I,
		MultiMeshInstance2D>
		_visualByChunk =
			new();


	private World World =>
		_worldRenderer!.World;


	private ShapeStore ShapeStore =>
		_worldRenderer!.ShapeStore;


	public void Initialize(
		WorldRendererRoot worldRenderer)
	{
		if (_worldRenderer != null)
		{
			throw new InvalidOperationException(
				"ShapeStructureRenderer is already initialized.");
		}

		_worldRenderer =
			worldRenderer
			?? throw new ArgumentNullException(
				nameof(worldRenderer));

		TryStart();
	}


	public override void _Ready()
	{
		TryStart();
	}


	public override void _ExitTree()
	{
		if (_started)
		{
			World.StructureGrid.Changed -=
				OnStructureGridChanged;
		}

		_started =
			false;
	}


	public void LoadChunk(
		Vector2I chunkCoord)
	{
		EnsureStarted();

		if (!_loadedChunks.Add(
				chunkCoord))
		{
			return;
		}

		IndexChunkFromWorld(
			chunkCoord);

		RebuildChunk(
			chunkCoord);
	}


	public void UnloadChunk(
		Vector2I chunkCoord)
	{
		EnsureStarted();

		if (!_loadedChunks.Remove(
				chunkCoord))
		{
			return;
		}

		if (_shapesByChunk.Remove(
				chunkCoord,
				out HashSet<ShapeStructure>? shapes))
		{
			foreach (
				ShapeStructure shape
				in shapes)
			{
				_chunkByShape.Remove(
					shape);
			}
		}

		RemoveChunkVisual(
			chunkCoord);
	}


	/// <summary>
	/// 如果 ShapeStructure.Shape 被直接修改，
	/// 而没有触发 TriangleGrid.Changed，
	/// 可以显式刷新其当前 loaded chunk。
	/// </summary>
	public void RefreshShape(
		ShapeStructure shapeStructure)
	{
		EnsureStarted();

		if (!World.StructureGrid.Contains(
				shapeStructure))
		{
			return;
		}

		Vector2I chunk =
			GetChunkForShape(
				shapeStructure);

		if (!_loadedChunks.Contains(
				chunk))
		{
			return;
		}

		RebuildChunk(
			chunk);
	}


	public void RebuildLoadedChunks()
	{
		EnsureStarted();

		foreach (
			Vector2I chunk
			in new List<Vector2I>(
				_loadedChunks))
		{
			ReloadChunkFromWorld(
				chunk);
		}
	}


	private void TryStart()
	{
		if (_started)
			return;

		if (!IsNodeReady() ||
			_worldRenderer == null)
		{
			return;
		}

		_unitTriangleMesh =
			CreateUnitTriangleMesh();

		World.StructureGrid.Changed +=
			OnStructureGridChanged;

		_started =
			true;
	}


	private void OnStructureGridChanged(
		object? sender,
		TriangleGridChangedEventArgs e)
	{
		if (e.FullRedraw)
		{
			RebuildLoadedChunks();
			return;
		}

		var dirtyChunks =
			new HashSet<Vector2I>();

		foreach (
			Structure structure
			in e.Structures)
		{
			if (structure
				is not ShapeStructure shape)
			{
				continue;
			}

			/*
			 * Remove old renderer-side membership first.
			 * 对 move/rotate，_chunkByShape 保存的是 change 前的
			 * loaded chunk ownership。
			 */
			if (_chunkByShape.Remove(
					shape,
					out Vector2I oldChunk))
			{
				if (_shapesByChunk.TryGetValue(
						oldChunk,
						out HashSet<ShapeStructure>? oldSet))
				{
					oldSet.Remove(
						shape);

					if (oldSet.Count == 0)
					{
						_shapesByChunk.Remove(
							oldChunk);
					}
				}

				dirtyChunks.Add(
					oldChunk);
			}

			/*
			 * Event 发出后，以当前 model transform 为准。
			 */
			if (!World.StructureGrid.Contains(
					shape))
			{
				continue;
			}

			Vector2I newChunk =
				GetChunkForShape(
					shape);

			if (!_loadedChunks.Contains(
					newChunk))
			{
				continue;
			}

			AddShapeToLoadedChunk(
				shape,
				newChunk);

			dirtyChunks.Add(
				newChunk);
		}

		foreach (
			Vector2I chunk
			in dirtyChunks)
		{
			if (_loadedChunks.Contains(
					chunk))
			{
				RebuildChunk(
					chunk);
			}
		}
	}


	private void IndexChunkFromWorld(
		Vector2I chunkCoord)
	{
		if (_shapesByChunk.Remove(
				chunkCoord,
				out HashSet<ShapeStructure>? previous))
		{
			foreach (
				ShapeStructure shape
				in previous)
			{
				_chunkByShape.Remove(
					shape);
			}
		}

		var shapes =
			new HashSet<ShapeStructure>(
				ReferenceEqualityComparer.Instance);

		foreach (
			TriangleGridPosition cell
			in _worldRenderer!
				.EnumerateCellsInChunk(
					chunkCoord))
		{
			if (!World.StructureGrid.TryGetStructure(
					cell,
					out Structure? structure))
			{
				continue;
			}

			if (structure
				is not ShapeStructure shape)
			{
				continue;
			}

			if (!shapes.Add(
					shape))
			{
				continue;
			}

			_chunkByShape[shape] =
				chunkCoord;
		}

		if (shapes.Count > 0)
		{
			_shapesByChunk[chunkCoord] =
				shapes;
		}
	}


	private void ReloadChunkFromWorld(
		Vector2I chunkCoord)
	{
		IndexChunkFromWorld(
			chunkCoord);

		RebuildChunk(
			chunkCoord);
	}


	private void AddShapeToLoadedChunk(
		ShapeStructure shape,
		Vector2I chunkCoord)
	{
		if (!_shapesByChunk.TryGetValue(
				chunkCoord,
				out HashSet<ShapeStructure>? set))
		{
			set =
				new HashSet<ShapeStructure>(
					ReferenceEqualityComparer.Instance);

			_shapesByChunk.Add(
				chunkCoord,
				set);
		}

		set.Add(
			shape);

		_chunkByShape[shape] =
			chunkCoord;
	}


	private Vector2I GetChunkForShape(
		ShapeStructure shape)
	{
		using IEnumerator<TriangleGridPosition> enumerator =
			shape
				.EnumerateOccupiedCells()
				.GetEnumerator();

		if (!enumerator.MoveNext())
		{
			throw new InvalidOperationException(
				"ShapeStructure does not occupy a cell.");
		}

		TriangleGridPosition cell =
			enumerator.Current;

		if (enumerator.MoveNext())
		{
			throw new InvalidOperationException(
				"ShapeStructure must occupy exactly one cell.");
		}

		return _worldRenderer!
			.GetChunkForCell(
				cell);
	}


	private void RebuildChunk(
		Vector2I chunkCoord)
	{
		if (!_loadedChunks.Contains(
				chunkCoord))
		{
			return;
		}

		if (!_shapesByChunk.TryGetValue(
				chunkCoord,
				out HashSet<ShapeStructure>? shapes)
			|| shapes.Count == 0)
		{
			RemoveChunkVisual(
				chunkCoord);

			return;
		}

		var transforms =
			new List<Transform2D>();

		bool hasBounds =
			false;

		Vector2 min =
			Vector2.Zero;

		Vector2 max =
			Vector2.Zero;

		foreach (
			ShapeStructure shape
			in shapes)
		{
			foreach (
				TriangleVertices triangle
				in shape.EnumerateDrawTriangles(
					World.StructureGrid,
					ShapeStore))
			{
				transforms.Add(
					TriangleToTransform(
						triangle));

				AddBoundsPoint(
					triangle.V0);

				AddBoundsPoint(
					triangle.V1);

				AddBoundsPoint(
					triangle.V2);
			}
		}

		if (transforms.Count == 0)
		{
			RemoveChunkVisual(
				chunkCoord);

			return;
		}

		MultiMeshInstance2D visual =
			GetOrCreateChunkVisual(
				chunkCoord);

		var multiMesh =
			new MultiMesh
			{
				TransformFormat =
					MultiMesh.TransformFormatEnum.Transform2D,

				Mesh =
					_unitTriangleMesh,
			};

		multiMesh.InstanceCount =
			transforms.Count;

		multiMesh.VisibleInstanceCount =
			transforms.Count;

		for (
			int i = 0;
			i < transforms.Count;
			i++)
		{
			multiMesh.SetInstanceTransform2D(
				i,
				transforms[i]);
		}

		if (hasBounds)
		{
			multiMesh.CustomAabb =
				new Aabb(
					new Vector3(
						min.X,
						min.Y,
						-1.0f),

					new Vector3(
						Mathf.Max(
							max.X - min.X,
							0.001f),

						Mathf.Max(
							max.Y - min.Y,
							0.001f),

						2.0f));
		}

		visual.Multimesh =
			multiMesh;

		visual.Modulate =
			ShapeColor;


		void AddBoundsPoint(
			Vector2 point)
		{
			if (!hasBounds)
			{
				min =
					point;

				max =
					point;

				hasBounds =
					true;

				return;
			}

			min.X =
				Mathf.Min(
					min.X,
					point.X);

			min.Y =
				Mathf.Min(
					min.Y,
					point.Y);

			max.X =
				Mathf.Max(
					max.X,
					point.X);

			max.Y =
				Mathf.Max(
					max.Y,
					point.Y);
		}
	}


	private MultiMeshInstance2D
		GetOrCreateChunkVisual(
			Vector2I chunkCoord)
	{
		if (_visualByChunk.TryGetValue(
				chunkCoord,
				out MultiMeshInstance2D? existing))
		{
			return existing;
		}

		var visual =
			new MultiMeshInstance2D
			{
				Name =
					$"Chunk_{chunkCoord.X}_{chunkCoord.Y}",
			};

		AddChild(
			visual);

		_visualByChunk.Add(
			chunkCoord,
			visual);

		return visual;
	}


	private void RemoveChunkVisual(
		Vector2I chunkCoord)
	{
		if (!_visualByChunk.Remove(
				chunkCoord,
				out MultiMeshInstance2D? visual))
		{
			return;
		}

		visual.QueueFree();
	}


	private static Transform2D TriangleToTransform(
		TriangleVertices triangle)
	{
		return new Transform2D(
			triangle.V1 - triangle.V0,
			triangle.V2 - triangle.V0,
			triangle.V0);
	}


	private static ArrayMesh CreateUnitTriangleMesh()
	{
		var arrays =
			new Godot.Collections.Array();

		arrays.Resize(
			(int)Mesh.ArrayType.Max);

		arrays[
			(int)Mesh.ArrayType.Vertex] =
				new Vector2[]
				{
					new(0.0f, 0.0f),
					new(1.0f, 0.0f),
					new(0.0f, 1.0f),
				};

		var mesh =
			new ArrayMesh();

		mesh.AddSurfaceFromArrays(
			Mesh.PrimitiveType.Triangles,
			arrays);

		return mesh;
	}


	private void EnsureStarted()
	{
		if (!_started)
		{
			throw new InvalidOperationException(
				"ShapeStructureRenderer has not been initialized.");
		}
	}
}
