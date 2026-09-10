using Godot;
using System;
using System.Collections.Generic;


public partial class WorldRendererRoot : Node2D
{
	private sealed class ChunkPinDependency
	{
		public HashSet<Vector2I> SourceChunks { get; }

		public HashSet<Vector2I> TargetChunks { get; }


		public ChunkPinDependency(
			IEnumerable<Vector2I> sourceChunks,
			IEnumerable<Vector2I> targetChunks)
		{
			SourceChunks =
				new HashSet<Vector2I>(
					sourceChunks);

			TargetChunks =
				new HashSet<Vector2I>(
					targetChunks);
		}
	}


	[Export(PropertyHint.Range, "1,256,1")]
	public int ChunkSize { get; set; } = 32;


	private World? _world;

	private ShapeStore? _shapeStore;

	private bool _ready;

	private bool _childrenInitialized;


	/*
	 * Direct chunks 是外部观察者真正请求的 roots，
	 * 例如主 Camera / Sandbox。
	 *
	 * Reference 不调用 LoadChunk()；
	 * 它通过 SetChunkPins() 建立 dependency edges。
	 */
	private readonly HashSet<Vector2I>
		_directLoadedChunks =
			new();


	/*
	 * 实际已经向各 renderer LoadChunk() 的 closure。
	 *
	 * LoadedChunks =
	 *     transitive closure(
	 *         DirectLoadedChunks,
	 *         reference chunk dependencies)
	 *
	 * 因此 cyclic/self reference 不会形成 ref-count leak。
	 */
	private readonly HashSet<Vector2I>
		_loadedChunks =
			new();


	/*
	 * owner 使用 reference identity。
	 *
	 * 每个 EmbeddedBoxReference 注册：
	 *
	 *     source = reference footprint chunks
	 *     target = TargetBox footprint chunks
	 *
	 * 依赖只有在 source 至少一个 chunk 可达时才激活。
	 */
	private readonly Dictionary<object, ChunkPinDependency>
		_chunkPins =
			new(ReferenceEqualityComparer.Instance);


	private bool _isReconcilingChunks;

	private bool _chunkReconcileRequested;


	public World World =>
		_world
		?? throw new InvalidOperationException(
			"WorldRendererRoot has not been initialized.");


	public ShapeStore ShapeStore =>
		_shapeStore
		?? throw new InvalidOperationException(
			"WorldRendererRoot has not been initialized.");


	public IReadOnlySet<Vector2I> DirectLoadedChunks =>
		_directLoadedChunks;


	public IReadOnlySet<Vector2I> LoadedChunks =>
		_loadedChunks;


	public Node2D WorldContent =>
		GetNode<Node2D>("WorldContent");


	public Node2D WorldOverlay =>
		GetNode<Node2D>("WorldOverlay");


	public ShapeStructureRenderer ShapeStructureRenderer =>
		GetNode<ShapeStructureRenderer>(
			"WorldContent/ShapeStructureRenderer");


	public EmbeddedBoxReferenceRenderer EmbeddedBoxReferenceRenderer =>
		GetNode<EmbeddedBoxReferenceRenderer>(
			"WorldContent/EmbeddedBoxReferenceRenderer");


	public BoxRendererManager BoxRendererManager =>
		GetNode<BoxRendererManager>(
			"BoxRendererManager");


	public void Initialize(
		World world,
		ShapeStore shapeStore)
	{
		if (_world != null ||
			_shapeStore != null)
		{
			throw new InvalidOperationException(
				"WorldRendererRoot is already initialized.");
		}

		_world =
			world
			?? throw new ArgumentNullException(
				nameof(world));

		_shapeStore =
			shapeStore
			?? throw new ArgumentNullException(
				nameof(shapeStore));

		TryInitializeChildren();
	}


	public override void _Ready()
	{
		_ready =
			true;

		TryInitializeChildren();
	}


	/// <summary>
	/// 把 TriangleGrid cell 映射到统一 render chunk。
	///
	/// Facing 不参与 chunk coordinate。
	/// </summary>
	public Vector2I GetChunkForCell(
		TriangleGridPosition cell)
	{
		ValidateChunkSize();

		return new Vector2I(
			FloorDiv(
				cell.X,
				ChunkSize),

			FloorDiv(
				cell.Y,
				ChunkSize));
	}


	public IEnumerable<TriangleGridPosition>
		EnumerateCellsInChunk(
			Vector2I chunkCoord)
	{
		ValidateChunkSize();

		int startX =
			checked(
				chunkCoord.X *
				ChunkSize);

		int startY =
			checked(
				chunkCoord.Y *
				ChunkSize);

		int endX =
			checked(
				startX +
				ChunkSize);

		int endY =
			checked(
				startY +
				ChunkSize);

		for (
			int y = startY;
			y < endY;
			y++)
		{
			for (
				int x = startX;
				x < endX;
				x++)
			{
				yield return
					new TriangleGridPosition(
						x,
						y,
						TriangleFacing.Down);

				yield return
					new TriangleGridPosition(
						x,
						y,
						TriangleFacing.Up);
			}
		}
	}


	public HashSet<Vector2I> GetChunksForStructure(
		Structure structure)
	{
		ArgumentNullException.ThrowIfNull(
			structure);

		var chunks =
			new HashSet<Vector2I>();

		foreach (
			TriangleGridPosition cell
			in structure.EnumerateOccupiedCells())
		{
			chunks.Add(
				GetChunkForCell(
					cell));
		}

		return chunks;
	}


	public bool IsChunkLoaded(
		Vector2I chunkCoord)
	{
		return _loadedChunks.Contains(
			chunkCoord);
	}


	public bool IsChunkDirectlyLoaded(
		Vector2I chunkCoord)
	{
		return _directLoadedChunks.Contains(
			chunkCoord);
	}


	/// <summary>
	/// 增加一个 direct render root。
	///
	/// Reference dependency 不应调用这个方法。
	/// </summary>
	public bool LoadChunk(
		Vector2I chunkCoord)
	{
		if (!_directLoadedChunks.Add(
				chunkCoord))
		{
			return false;
		}

		RequestChunkReconcile();

		return true;
	}


	/// <summary>
	/// 移除一个 direct render root。
	///
	/// 如果该 chunk 仍由某条可达 reference dependency 需要，
	/// 它仍会保持实际 loaded。
	/// </summary>
	public bool UnloadChunk(
		Vector2I chunkCoord)
	{
		if (!_directLoadedChunks.Remove(
				chunkCoord))
		{
			return false;
		}

		RequestChunkReconcile();

		return true;
	}


	public void UnloadAllChunks()
	{
		if (_directLoadedChunks.Count == 0)
			return;

		_directLoadedChunks.Clear();

		RequestChunkReconcile();
	}


	/// <summary>
	/// 注册/更新一个 renderer-side chunk dependency。
	///
	/// 语义：
	///
	///     if any(SourceChunks) is reachable:
	///         all(TargetChunks) become reachable
	///
	/// owner 通常就是 EmbeddedBoxReference 实例。
	///
	/// 这里不是普通 ref-count：
	/// closure 每次都从 DirectLoadedChunks 重新求可达性，
	/// 因此 A->A、A->B->A 等 cycle 在失去 direct root 后
	/// 可以正确回收。
	/// </summary>
	public void SetChunkPins(
		object owner,
		IEnumerable<Vector2I> sourceChunks,
		IEnumerable<Vector2I> targetChunks)
	{
		ArgumentNullException.ThrowIfNull(
			owner);

		ArgumentNullException.ThrowIfNull(
			sourceChunks);

		ArgumentNullException.ThrowIfNull(
			targetChunks);

		var next =
			new ChunkPinDependency(
				sourceChunks,
				targetChunks);

		if (next.SourceChunks.Count == 0 ||
			next.TargetChunks.Count == 0)
		{
			ClearChunkPins(
				owner);

			return;
		}

		if (_chunkPins.TryGetValue(
				owner,
				out ChunkPinDependency? existing)
			&& existing.SourceChunks.SetEquals(
				next.SourceChunks)
			&& existing.TargetChunks.SetEquals(
				next.TargetChunks))
		{
			return;
		}

		_chunkPins[owner] =
			next;

		RequestChunkReconcile();
	}


	public bool ClearChunkPins(
		object owner)
	{
		ArgumentNullException.ThrowIfNull(
			owner);

		if (!_chunkPins.Remove(
				owner))
		{
			return false;
		}

		RequestChunkReconcile();

		return true;
	}


	private void TryInitializeChildren()
	{
		if (_childrenInitialized)
			return;

		if (!_ready ||
			_world == null ||
			_shapeStore == null)
		{
			return;
		}

		ValidateChunkSize();

		/*
		 * Manager 先初始化，因为 Reference visual
		 * 需要 TargetBox.HistoryTexture。
		 */
		BoxRendererManager.Initialize(
			this);

		ShapeStructureRenderer.Initialize(
			this);

		EmbeddedBoxReferenceRenderer.Initialize(
			this);

		_childrenInitialized =
			true;

		RequestChunkReconcile();
	}


	private void RequestChunkReconcile()
	{
		_chunkReconcileRequested =
			true;

		if (!_childrenInitialized ||
			_isReconcilingChunks)
		{
			return;
		}

		ReconcileChunkClosure();
	}


	/// <summary>
	/// 从 direct roots 出发，对 reference dependencies 求固定点，
	/// 然后只把实际差异传给各具体 renderer。
	///
	/// LoadChunkIntoChildren() 期间可能发现新的 references，
	/// 从而调用 SetChunkPins()。因此这里允许 reconciliation
	/// 被重新请求，并持续迭代到稳定。
	/// </summary>
	private void ReconcileChunkClosure()
	{
		if (_isReconcilingChunks)
		{
			_chunkReconcileRequested =
				true;

			return;
		}

		_isReconcilingChunks =
			true;

		try
		{
			while (true)
			{
				_chunkReconcileRequested =
					false;

				HashSet<Vector2I> desired =
					ComputeDesiredLoadedChunks();

				var toLoad =
					new List<Vector2I>();

				foreach (
					Vector2I chunk
					in desired)
				{
					if (!_loadedChunks.Contains(
							chunk))
					{
						toLoad.Add(
							chunk);
					}
				}

				/*
				 * Loading 新 chunk 可能发现新的 reference edge。
				 * 先完成 additions，然后重新计算 closure，
				 * 不使用旧 desired 做 unload。
				 */
				if (toLoad.Count > 0)
				{
					foreach (
						Vector2I chunk
						in toLoad)
					{
						if (!_loadedChunks.Add(
								chunk))
						{
							continue;
						}

						LoadChunkIntoChildren(
							chunk);
					}

					continue;
				}

				var toUnload =
					new List<Vector2I>();

				foreach (
					Vector2I chunk
					in _loadedChunks)
				{
					if (!desired.Contains(
							chunk))
					{
						toUnload.Add(
							chunk);
					}
				}

				if (toUnload.Count > 0)
				{
					foreach (
						Vector2I chunk
						in toUnload)
					{
						/*
						 * 先从 actual set 删除，再通知 children。
						 *
						 * 这样 child teardown 中 ClearChunkPins()
						 * 不会对同一个 chunk 触发递归 unload。
						 */
						if (!_loadedChunks.Remove(
								chunk))
						{
							continue;
						}

						UnloadChunkFromChildren(
							chunk);
					}

					continue;
				}

				if (!_chunkReconcileRequested)
					break;
			}
		}
		finally
		{
			_isReconcilingChunks =
				false;
		}

		/*
		 * finally 之后若又有请求，继续完成。
		 */
		if (_chunkReconcileRequested)
		{
			RequestChunkReconcile();
		}
	}


	private HashSet<Vector2I>
		ComputeDesiredLoadedChunks()
	{
		var reachable =
			new HashSet<Vector2I>(
				_directLoadedChunks);

		bool changed;

		do
		{
			changed =
				false;

			foreach (
				ChunkPinDependency dependency
				in _chunkPins.Values)
			{
				if (!dependency.SourceChunks.Overlaps(
						reachable))
				{
					continue;
				}

				foreach (
					Vector2I target
					in dependency.TargetChunks)
				{
					if (reachable.Add(
							target))
					{
						changed =
							true;
					}
				}
			}
		}
		while (changed);

		return reachable;
	}


	private void LoadChunkIntoChildren(
		Vector2I chunkCoord)
	{
		ShapeStructureRenderer.LoadChunk(
			chunkCoord);

		/*
		 * 先建立 Box history texture，
		 * 再扫描 reference visual。
		 */
		BoxRendererManager.LoadChunk(
			chunkCoord);

		EmbeddedBoxReferenceRenderer.LoadChunk(
			chunkCoord);
	}


	private void UnloadChunkFromChildren(
		Vector2I chunkCoord)
	{
		EmbeddedBoxReferenceRenderer.UnloadChunk(
			chunkCoord);

		BoxRendererManager.UnloadChunk(
			chunkCoord);

		ShapeStructureRenderer.UnloadChunk(
			chunkCoord);
	}


	private void ValidateChunkSize()
	{
		if (ChunkSize <= 0)
		{
			throw new InvalidOperationException(
				"ChunkSize must be greater than zero.");
		}
	}


	private static int FloorDiv(
		int value,
		int divisor)
	{
		int quotient =
			value / divisor;

		int remainder =
			value % divisor;

		if (remainder < 0)
			quotient--;

		return quotient;
	}
}
