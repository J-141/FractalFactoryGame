using Godot;
using System;
using System.Collections.Generic;


public partial class BoxRendererManager : Node
{
	[Export]
	public PackedScene BoxRendererScene { get; set; } = null!;

	[Export(PropertyHint.Range, "0.01,8.0,0.01")]
	public float PixelsPerWorldUnit { get; set; } = 1.0f;

	[Export]
	public float CropPadding { get; set; } = 0.0f;


	private WorldRendererRoot? _worldRenderer;

	private bool _started;


	/*
	 * 当前 manager 已加载的 render chunks。
	 */
	private readonly HashSet<Vector2I>
		_loadedChunks =
			new();


	/*
	 * 一个 loaded chunk 当前与哪些 Boxes 相交。
	 *
	 * 只记录 loaded chunks，不为整个 World 建额外空间索引。
	 */
	private readonly Dictionary<
		Vector2I,
		HashSet<Box>>
		_boxesByLoadedChunk =
			new();


	/*
	 * 一个 Box 当前由哪些 loaded chunks 保持 residency。
	 *
	 * 跨 chunk Box 会在这里有多个坐标，
	 * 但仍然只有一个 BoxRenderer。
	 */
	private readonly Dictionary<
		Box,
		HashSet<Vector2I>>
		_loadedChunksByBox =
			new(ReferenceEqualityComparer.Instance);


	private readonly Dictionary<Box, BoxRenderer>
		_renderers =
			new(ReferenceEqualityComparer.Instance);


	private World World =>
		_worldRenderer!.World;


	public IEnumerable<BoxRenderer> Renderers =>
		_renderers.Values;


	public event Action<Box, BoxRenderer>? RendererAdded;

	public event Action<Box>? RendererRemoved;


	public void Initialize(
		WorldRendererRoot worldRenderer)
	{
		if (_worldRenderer != null)
		{
			throw new InvalidOperationException(
				"BoxRendererManager is already initialized.");
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
			World.BoxGrid.Changed -=
				OnBoxGridChanged;
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

		LoadChunkFromWorld(
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

		if (!_boxesByLoadedChunk.Remove(
				chunkCoord,
				out HashSet<Box>? boxes))
		{
			return;
		}

		foreach (
			Box box
			in boxes)
		{
			if (!_loadedChunksByBox.TryGetValue(
					box,
					out HashSet<Vector2I>? boxChunks))
			{
				continue;
			}

			boxChunks.Remove(
				chunkCoord);

			/*
			 * 跨 chunk Box:
			 * 只要还有至少一个 loaded chunk 与它相交，
			 * 就继续保留唯一 renderer。
			 */
			if (boxChunks.Count > 0)
				continue;

			_loadedChunksByBox.Remove(
				box);

			RemoveRenderer(
				box);
		}
	}


	public bool TryGetRenderer(
		Box box,
		out BoxRenderer? renderer)
	{
		return _renderers.TryGetValue(
			box,
			out renderer);
	}


	public BoxRenderer GetRenderer(
		Box box)
	{
		if (!_renderers.TryGetValue(
				box,
				out BoxRenderer? renderer))
		{
			throw new KeyNotFoundException(
				"Box does not currently have a loaded renderer.");
		}

		return renderer;
	}


	public ViewportTexture GetHistoryTexture(
		Box box)
	{
		return GetRenderer(
			box).HistoryTexture;
	}


	/// <summary>
	/// 返回这个 Box 当前与哪些 loaded chunks 相交。
	///
	/// 返回值是副本，调用方不能修改 manager 内部 residency。
	/// </summary>
	public HashSet<Vector2I> GetLoadedChunks(
		Box box)
	{
		if (!_loadedChunksByBox.TryGetValue(
				box,
				out HashSet<Vector2I>? chunks))
		{
			return new HashSet<Vector2I>();
		}

		return new HashSet<Vector2I>(
			chunks);
	}


	/// <summary>
	/// BoxRenderer 总是 crop 整个 Box，而不是某个 chunk 内的片段。
	/// 因此跨 chunk Box 仍然只有一个完整 texture。
	/// </summary>
	public Rect2 ComputeWorldCrop(
		Box box)
	{
		EnsureStarted();

		bool hasVertex =
			false;

		Vector2 min =
			Vector2.Zero;

		Vector2 max =
			Vector2.Zero;

		foreach (
			TriangleGridPosition cell
			in box.EnumerateOccupiedCells())
		{
			TriangleVertices triangle =
				World.BoxGrid.GetVertices(
					cell);

			for (int i = 0; i < 3; i++)
			{
				Vector2 vertex =
					triangle[i];

				if (!hasVertex)
				{
					min =
						vertex;

					max =
						vertex;

					hasVertex =
						true;

					continue;
				}

				min.X =
					Mathf.Min(
						min.X,
						vertex.X);

				min.Y =
					Mathf.Min(
						min.Y,
						vertex.Y);

				max.X =
					Mathf.Max(
						max.X,
						vertex.X);

				max.Y =
					Mathf.Max(
						max.Y,
						vertex.Y);
			}
		}

		if (!hasVertex)
		{
			throw new InvalidOperationException(
				"Box does not occupy any cells.");
		}

		Rect2 crop =
			new Rect2(
				min,
				max - min);

		if (CropPadding > 0.0f)
		{
			crop =
				crop.Grow(
					CropPadding);
		}

		return crop;
	}


	public Vector2I ComputeTextureSize(
		Rect2 worldCrop)
	{
		if (PixelsPerWorldUnit <= 0.0f)
		{
			throw new InvalidOperationException(
				"PixelsPerWorldUnit must be greater than zero.");
		}

		return new Vector2I(
			Math.Max(
				1,
				Mathf.CeilToInt(
					worldCrop.Size.X *
					PixelsPerWorldUnit)),

			Math.Max(
				1,
				Mathf.CeilToInt(
					worldCrop.Size.Y *
					PixelsPerWorldUnit)));
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

		if (BoxRendererScene == null)
		{
			throw new InvalidOperationException(
				"BoxRendererScene is not assigned.");
		}

		World.BoxGrid.Changed +=
			OnBoxGridChanged;

		_started =
			true;
	}


	private void LoadChunkFromWorld(
		Vector2I chunkCoord)
	{
		var boxes =
			new HashSet<Box>(
				ReferenceEqualityComparer.Instance);

		foreach (
			TriangleGridPosition cell
			in _worldRenderer!
				.EnumerateCellsInChunk(
					chunkCoord))
		{
			if (!World.BoxGrid.TryGetStructure(
					cell,
					out Structure? structure))
			{
				continue;
			}

			if (structure is Box box)
			{
				boxes.Add(
					box);
			}
		}

		if (boxes.Count > 0)
		{
			_boxesByLoadedChunk[chunkCoord] =
				boxes;
		}

		foreach (
			Box box
			in boxes)
		{
			if (!_loadedChunksByBox.TryGetValue(
					box,
					out HashSet<Vector2I>? boxChunks))
			{
				boxChunks =
					new HashSet<Vector2I>();

				_loadedChunksByBox.Add(
					box,
					boxChunks);
			}

			boxChunks.Add(
				chunkCoord);

			EnsureRenderer(
				box);
		}
	}


	private void OnBoxGridChanged(
		object? sender,
		TriangleGridChangedEventArgs e)
	{
		if (e.FullRedraw)
		{
			RebuildLoadedChunkResidency();
			return;
		}

		foreach (
			Structure structure
			in e.Structures)
		{
			if (structure is not Box box)
				continue;

			ReconcileBox(
				box);
		}
	}


	/// <summary>
	/// Box place/remove/move/rotate 后重新计算：
	/// footprint 与当前 loaded chunks 的精确交集。
	///
	/// 这是跨 chunk Box 的核心逻辑。
	/// </summary>
	private void ReconcileBox(
		Box box)
	{
		HashSet<Vector2I> oldLoadedChunks;

		if (_loadedChunksByBox.TryGetValue(
				box,
				out HashSet<Vector2I>? existing))
		{
			oldLoadedChunks =
				new HashSet<Vector2I>(
					existing);
		}
		else
		{
			oldLoadedChunks =
				new HashSet<Vector2I>();
		}

		var newLoadedChunks =
			new HashSet<Vector2I>();

		if (World.BoxGrid.Contains(
				box))
		{
			foreach (
				Vector2I chunk
				in _worldRenderer!
					.GetChunksForStructure(
						box))
			{
				if (_loadedChunks.Contains(
						chunk))
				{
					newLoadedChunks.Add(
						chunk);
				}
			}
		}

		/*
		 * 从不再相交的 loaded chunks 中移除。
		 */
		foreach (
			Vector2I chunk
			in oldLoadedChunks)
		{
			if (newLoadedChunks.Contains(
					chunk))
			{
				continue;
			}

			if (_boxesByLoadedChunk.TryGetValue(
					chunk,
					out HashSet<Box>? boxes))
			{
				boxes.Remove(
					box);

				if (boxes.Count == 0)
				{
					_boxesByLoadedChunk.Remove(
						chunk);
				}
			}
		}

		/*
		 * 加入新相交的 loaded chunks。
		 */
		foreach (
			Vector2I chunk
			in newLoadedChunks)
		{
			if (!_boxesByLoadedChunk.TryGetValue(
					chunk,
					out HashSet<Box>? boxes))
			{
				boxes =
					new HashSet<Box>(
						ReferenceEqualityComparer.Instance);

				_boxesByLoadedChunk.Add(
					chunk,
					boxes);
			}

			boxes.Add(
				box);
		}

		if (newLoadedChunks.Count == 0)
		{
			_loadedChunksByBox.Remove(
				box);

			RemoveRenderer(
				box);

			return;
		}

		_loadedChunksByBox[box] =
			newLoadedChunks;

		/*
		 * Box 仍然 resident：
		 * 跨 chunk 情况也只 update/create 同一个 renderer。
		 */
		if (_renderers.TryGetValue(
				box,
				out BoxRenderer? renderer))
		{
			UpdateRendererCrop(
				box,
				renderer);
		}
		else
		{
			AddRenderer(
				box);
		}
	}


	private void RebuildLoadedChunkResidency()
	{
		foreach (
			BoxRenderer renderer
			in _renderers.Values)
		{
			renderer.QueueFree();
		}

		foreach (
			Box box
			in _renderers.Keys)
		{
			RendererRemoved?.Invoke(
				box);
		}

		_renderers.Clear();
		_boxesByLoadedChunk.Clear();
		_loadedChunksByBox.Clear();

		foreach (
			Vector2I chunk
			in _loadedChunks)
		{
			LoadChunkFromWorld(
				chunk);
		}
	}


	private void EnsureRenderer(
		Box box)
	{
		if (_renderers.ContainsKey(
				box))
		{
			return;
		}

		AddRenderer(
			box);
	}


	private void AddRenderer(
		Box box)
	{
		BoxRenderer renderer =
			BoxRendererScene
				.Instantiate<BoxRenderer>();

		AddChild(
			renderer);

		Rect2 crop =
			ComputeWorldCrop(
				box);

		Vector2I textureSize =
			ComputeTextureSize(
				crop);

		renderer.Initialize(
			box,
			_worldRenderer!
				.GetViewport()
				.World2D,
			crop,
			textureSize);

		_renderers.Add(
			box,
			renderer);

		RendererAdded?.Invoke(
			box,
			renderer);
	}


	private void RemoveRenderer(
		Box box)
	{
		if (!_renderers.Remove(
				box,
				out BoxRenderer? renderer))
		{
			return;
		}

		RendererRemoved?.Invoke(
			box);

		renderer.QueueFree();
	}


	private void UpdateRendererCrop(
		Box box,
		BoxRenderer renderer)
	{
		Rect2 crop =
			ComputeWorldCrop(
				box);

		Vector2I textureSize =
			ComputeTextureSize(
				crop);

		renderer.SetCrop(
			crop,
			textureSize);
	}


	private void EnsureStarted()
	{
		if (!_started)
		{
			throw new InvalidOperationException(
				"BoxRendererManager has not been initialized.");
		}
	}
}
