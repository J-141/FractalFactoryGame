using Godot;
using System;
using System.Collections.Generic;


public partial class EmbeddedBoxReferenceRenderer : Node2D
{
	private WorldRendererRoot? _worldRenderer;

	private bool _started;


	/*
	 * Chunk 只表示 renderer-side residency。
	 *
	 * 一个 EmbeddedBoxReference 可以跨多个 chunk，
	 * 但无论覆盖多少 loaded chunks，都只有一个 visual。
	 *
	 * resident reference 会向 WorldRendererRoot 注册：
	 *
	 *     reference footprint chunks
	 *         -> TargetBox footprint chunks
	 *
	 * 的 chunk dependency。
	 */
	private readonly HashSet<Vector2I>
		_loadedChunks =
			new();


	private readonly Dictionary<
		Vector2I,
		HashSet<EmbeddedBoxReference>>
		_referencesByLoadedChunk =
			new();


	private readonly Dictionary<
		EmbeddedBoxReference,
		HashSet<Vector2I>>
		_loadedChunksByReference =
			new(ReferenceEqualityComparer.Instance);


	/*
	 * 方便在 TargetBox renderer 创建/销毁/变换时，
	 * 快速找到所有当前 resident references。
	 */
	private readonly Dictionary<
		Box,
		HashSet<EmbeddedBoxReference>>
		_referencesByTargetBox =
			new(ReferenceEqualityComparer.Instance);


	/*
	 * 每个 logical reference 永远只有一个 MeshInstance2D。
	 *
	 * 它永久采样 TargetBox.HistoryTexture，
	 * 不存在 buffer parity visual。
	 */
	private readonly Dictionary<
		EmbeddedBoxReference,
		MeshInstance2D>
		_visualByReference =
			new(ReferenceEqualityComparer.Instance);


	/*
	 * 相同 TargetBox 的所有 references：
	 *
	 * - local footprint 相同；
	 * - UV mapping 相同；
	 *
	 * 因此共享同一个 ArrayMesh。
	 */
	private readonly Dictionary<
		Box,
		ArrayMesh>
		_meshByTargetBox =
			new(ReferenceEqualityComparer.Instance);


	private World World =>
		_worldRenderer!.World;


	private BoxRendererManager BoxRendererManager =>
		_worldRenderer!.BoxRendererManager;


	public void Initialize(
		WorldRendererRoot worldRenderer)
	{
		if (_worldRenderer != null)
		{
			throw new InvalidOperationException(
				"EmbeddedBoxReferenceRenderer is already initialized.");
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

			World.BoxGrid.Changed -=
				OnBoxGridChanged;

			BoxRendererManager.RendererAdded -=
				OnBoxRendererAdded;

			BoxRendererManager.RendererRemoved -=
				OnBoxRendererRemoved;
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

		if (!_referencesByLoadedChunk.Remove(
				chunkCoord,
				out HashSet<EmbeddedBoxReference>? references))
		{
			return;
		}

		foreach (
			EmbeddedBoxReference reference
			in references)
		{
			if (!_loadedChunksByReference.TryGetValue(
					reference,
					out HashSet<Vector2I>? referenceChunks))
			{
				continue;
			}

			referenceChunks.Remove(
				chunkCoord);

			/*
			 * 跨 chunk reference：
			 * 只要还有一个 footprint chunk loaded，
			 * 就保留同一个 visual。
			 */
			if (referenceChunks.Count > 0)
				continue;

			RemoveResidentReference(
				reference);
		}
	}


	/// <summary>
	/// 强制重建当前 loaded chunks 中所有
	/// EmbeddedBoxReference 的 residency 与 visuals。
	/// </summary>
	public void RebuildLoadedChunks()
	{
		EnsureStarted();

		ClearAll();

		foreach (
			Vector2I chunk
			in _loadedChunks)
		{
			LoadChunkFromWorld(
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

		World.StructureGrid.Changed +=
			OnStructureGridChanged;

		World.BoxGrid.Changed +=
			OnBoxGridChanged;

		BoxRendererManager.RendererAdded +=
			OnBoxRendererAdded;

		BoxRendererManager.RendererRemoved +=
			OnBoxRendererRemoved;

		_started =
			true;
	}


	private void LoadChunkFromWorld(
		Vector2I chunkCoord)
	{
		var references =
			new HashSet<EmbeddedBoxReference>(
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
				is EmbeddedBoxReference reference)
			{
				references.Add(
					reference);
			}
		}

		if (references.Count > 0)
		{
			_referencesByLoadedChunk[chunkCoord] =
				references;
		}

		foreach (
			EmbeddedBoxReference reference
			in references)
		{
			AddReferenceChunkResidency(
				reference,
				chunkCoord);
		}
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

		foreach (
			Structure structure
			in e.Structures)
		{
			if (structure
				is not EmbeddedBoxReference reference)
			{
				continue;
			}

			ReconcileReference(
				reference);
		}
	}


	/// <summary>
	/// TargetBox 的 transform/crop 改变时，
	/// reference mesh 中的 UV 也必须重新计算。
	///
	/// BoxRendererManager 比本 renderer 更早订阅 BoxGrid.Changed，
	/// 因此到这里时其 BoxRenderer crop 已经更新。
	/// </summary>
	private void OnBoxGridChanged(
		object? sender,
		TriangleGridChangedEventArgs e)
	{
		if (e.FullRedraw)
		{
			_meshByTargetBox.Clear();

			foreach (
				Box targetBox
				in new List<Box>(
					_referencesByTargetBox.Keys))
			{
				RebuildVisualsTargeting(
					targetBox);
			}

			return;
		}

		foreach (
			Structure structure
			in e.Structures)
		{
			if (structure is not Box box)
				continue;

			_meshByTargetBox.Remove(
				box);

			RebuildVisualsTargeting(
				box);
		}
	}


	private void OnBoxRendererAdded(
		Box box,
		BoxRenderer renderer)
	{
		/*
		 * 新 BoxRenderer 代表新的 HistoryViewport texture resource。
		 * 即使旧 mesh geometry 仍有效，也重新取当前 crop 生成 UV。
		 */
		_meshByTargetBox.Remove(
			box);

		RebuildVisualsTargeting(
			box);
	}


	private void OnBoxRendererRemoved(
		Box box)
	{
		_meshByTargetBox.Remove(
			box);

		if (!_referencesByTargetBox.TryGetValue(
				box,
				out HashSet<EmbeddedBoxReference>? references))
		{
			return;
		}

		foreach (
			EmbeddedBoxReference reference
			in references)
		{
			RemoveVisual(
				reference);
		}
	}


	/// <summary>
	/// Place/remove/move/rotate 一个 reference 后，
	/// 重新计算它与当前 loaded chunks 的精确交集。
	/// </summary>
	private void ReconcileReference(
		EmbeddedBoxReference reference)
	{
		HashSet<Vector2I> oldLoadedChunks;

		if (_loadedChunksByReference.TryGetValue(
				reference,
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

		if (World.StructureGrid.Contains(
				reference))
		{
			foreach (
				Vector2I chunk
				in _worldRenderer!
					.GetChunksForStructure(
						reference))
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
		 * 从旧 chunk membership 移除。
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

			if (_referencesByLoadedChunk.TryGetValue(
					chunk,
					out HashSet<EmbeddedBoxReference>? references))
			{
				references.Remove(
					reference);

				if (references.Count == 0)
				{
					_referencesByLoadedChunk.Remove(
						chunk);
				}
			}
		}

		/*
		 * 加入新的 loaded chunk membership。
		 */
		foreach (
			Vector2I chunk
			in newLoadedChunks)
		{
			if (!_referencesByLoadedChunk.TryGetValue(
					chunk,
					out HashSet<EmbeddedBoxReference>? references))
			{
				references =
					new HashSet<EmbeddedBoxReference>(
						ReferenceEqualityComparer.Instance);

				_referencesByLoadedChunk.Add(
					chunk,
					references);
			}

			references.Add(
				reference);
		}

		if (newLoadedChunks.Count == 0)
		{
			RemoveResidentReference(
				reference);

			return;
		}

		bool wasResident =
			oldLoadedChunks.Count > 0;

		_loadedChunksByReference[reference] =
			newLoadedChunks;

		if (!wasResident)
		{
			AddTargetIndex(
				reference);
		}

		/*
		 * Reference move/rotate 后 source footprint 可能跨入新的 chunk；
		 * TargetBox transform 也可能在别处被更新。
		 * 因此 resident reference 每次 reconcile 都刷新 dependency。
		 */
		UpdateChunkPins(
			reference);

		if (_visualByReference.TryGetValue(
				reference,
				out MeshInstance2D? visual))
		{
			UpdateVisualTransform(
				reference,
				visual);
		}
		else
		{
			TryCreateVisual(
				reference);
		}
	}


	private void AddReferenceChunkResidency(
		EmbeddedBoxReference reference,
		Vector2I chunkCoord)
	{
		bool alreadyResident =
			_loadedChunksByReference.TryGetValue(
				reference,
				out HashSet<Vector2I>? chunks);

		if (!alreadyResident)
		{
			chunks =
				new HashSet<Vector2I>();

			_loadedChunksByReference.Add(
				reference,
				chunks);

			AddTargetIndex(
				reference);
		}

		chunks!.Add(
			chunkCoord);

		/*
		 * Pin dependency 使用完整 source footprint，
		 * 而不是只记录当前这一格 loaded chunk。
		 *
		 * 只要 reference footprint 任一 chunk 可达，
		 * TargetBox 的全部 footprint chunks 就必须可达。
		 */
		UpdateChunkPins(
			reference);

		TryCreateVisual(
			reference);
	}

	private void RemoveResidentReference(
		EmbeddedBoxReference reference)
	{
		/*
		 * 先清 renderer-side residency，再撤销 dependency。
		 * ClearChunkPins() 可能同步触发更多 chunk unload；
		 * 此顺序保证 re-entrant callback 看见的是已清理状态。
		 */
		_loadedChunksByReference.Remove(
			reference);

		RemoveTargetIndex(
			reference);

		RemoveVisual(
			reference);

		_worldRenderer!.ClearChunkPins(
			reference);
	}

	private void UpdateChunkPins(
		EmbeddedBoxReference reference)
	{
		HashSet<Vector2I> sourceChunks =
			_worldRenderer!
				.GetChunksForStructure(
					reference);

		HashSet<Vector2I> targetChunks =
			_worldRenderer
				.GetChunksForStructure(
					reference.TargetBox);

		_worldRenderer.SetChunkPins(
			reference,
			sourceChunks,
			targetChunks);
	}


	private void AddTargetIndex(
		EmbeddedBoxReference reference)
	{
		Box targetBox =
			reference.TargetBox;

		if (!_referencesByTargetBox.TryGetValue(
				targetBox,
				out HashSet<EmbeddedBoxReference>? references))
		{
			references =
				new HashSet<EmbeddedBoxReference>(
					ReferenceEqualityComparer.Instance);

			_referencesByTargetBox.Add(
				targetBox,
				references);
		}

		references.Add(
			reference);
	}


	private void RemoveTargetIndex(
		EmbeddedBoxReference reference)
	{
		Box targetBox =
			reference.TargetBox;

		if (!_referencesByTargetBox.TryGetValue(
				targetBox,
				out HashSet<EmbeddedBoxReference>? references))
		{
			return;
		}

		references.Remove(
			reference);

		if (references.Count == 0)
		{
			_referencesByTargetBox.Remove(
				targetBox);
		}
	}


	private bool TryCreateVisual(
		EmbeddedBoxReference reference)
	{
		if (_visualByReference.ContainsKey(
				reference))
		{
			return true;
		}

		if (!_loadedChunksByReference.ContainsKey(
				reference))
		{
			return false;
		}

		if (!BoxRendererManager.TryGetRenderer(
				reference.TargetBox,
				out BoxRenderer? targetRenderer)
			|| targetRenderer == null)
		{
			/*
			 * TargetBox 当前没有 renderer/history texture。
			 *
			 * TargetBox chunks 已通过 reference dependency
			 * 注册到 WorldRendererRoot。
			 *
			 * 如果当前正处于 closure reconciliation，
			 * TargetBox renderer 可能会在稍后的 nested chunk load
			 * 中创建；RendererAdded 会自动补建 visual。
			 */
			return false;
		}

		ArrayMesh mesh =
			GetOrCreateMesh(
				reference.TargetBox,
				targetRenderer);

		var visual =
			new MeshInstance2D
			{
				Name =
					$"Reference_{reference.GetHashCode()}",

				Mesh =
					mesh,

				/*
				 * 永远采样稳定 history texture。
				 * 不采样 RenderTexture。
				 */
				Texture =
					targetRenderer.HistoryTexture,
			};

		AddChild(
			visual);

		_visualByReference.Add(
			reference,
			visual);

		UpdateVisualTransform(
			reference,
			visual);

		return true;
	}


	private void RemoveVisual(
		EmbeddedBoxReference reference)
	{
		if (!_visualByReference.Remove(
				reference,
				out MeshInstance2D? visual))
		{
			return;
		}

		visual.QueueFree();
	}


	private void RebuildVisualsTargeting(
		Box targetBox)
	{
		if (!_referencesByTargetBox.TryGetValue(
				targetBox,
				out HashSet<EmbeddedBoxReference>? references))
		{
			return;
		}

		foreach (
			EmbeddedBoxReference reference
			in references)
		{
			RemoveVisual(
				reference);

			TryCreateVisual(
				reference);
		}
	}


	private void UpdateVisualTransform(
		EmbeddedBoxReference reference,
		MeshInstance2D visual)
	{
		/*
		 * Mesh geometry 是 reference local lattice geometry，
		 * anchor 位于 local vertex (0,0)。
		 */
		visual.Position =
			World.StructureGrid
				.GetVertexPosition(
					reference.Anchor);

		/*
		 * 当前 lattice rotation 定义：
		 * (x,y) -> (-y, x+y)
		 *
		 * 在 Godot y-down 2D coordinates 中对应视觉 +60°。
		 */
		visual.Rotation =
			reference.GridRotation
			* Mathf.Pi / 3.0f;
	}


	private ArrayMesh GetOrCreateMesh(
		Box targetBox,
		BoxRenderer targetRenderer)
	{
		if (_meshByTargetBox.TryGetValue(
				targetBox,
				out ArrayMesh? existing))
		{
			return existing;
		}

		ArrayMesh mesh =
			BuildReferenceMesh(
				targetBox,
				targetRenderer);

		_meshByTargetBox.Add(
			targetBox,
			mesh);

		return mesh;
	}


	/// <summary>
	/// Reference 固定为 TargetBox 的 1/2 linear scale。
	///
	/// 对 reference local lattice vertex q：
	///
	///     reference local q
	///         -> target local 2q
	///         -> TargetBox world transform
	///         -> TargetBox HistoryTexture UV
	///
	/// 因此所有指向同一 TargetBox 的 references
	/// 可以共享同一 mesh。
	/// </summary>
	private ArrayMesh BuildReferenceMesh(
		Box targetBox,
		BoxRenderer targetRenderer)
	{
		if (!TriangleGrid.TryContractRegionBy2(
				targetBox.LocalRegion,
				out TriangleGridPosition[] referenceRegion))
		{
			throw new InvalidOperationException(
				"TargetBox region cannot be contracted exactly by 1/2.");
		}

		var vertices =
			new List<Vector2>(
				referenceRegion.Length * 3);

		var uvs =
			new List<Vector2>(
				referenceRegion.Length * 3);

		foreach (
			TriangleGridPosition cell
			in referenceRegion)
		{
			var (v0, v1, v2) =
				TriangleGrid.GetCellVertices(
					cell);

			AddVertex(
				v0);

			AddVertex(
				v1);

			AddVertex(
				v2);
		}

		var arrays =
			new Godot.Collections.Array();

		arrays.Resize(
			(int)Mesh.ArrayType.Max);

		arrays[
			(int)Mesh.ArrayType.Vertex] =
				vertices.ToArray();

		arrays[
			(int)Mesh.ArrayType.TexUV] =
				uvs.ToArray();

		var mesh =
			new ArrayMesh();

		mesh.AddSurfaceFromArrays(
			Mesh.PrimitiveType.Triangles,
			arrays);

		return mesh;


		void AddVertex(
			Vector2I referenceLocalVertex)
		{
			vertices.Add(
				GetReferenceLocalPosition(
					referenceLocalVertex));

			Vector2I targetLocalVertex =
				referenceLocalVertex * 2;

			Vector2I targetWorldVertex =
				TriangleGrid.TransformVertex(
					targetLocalVertex,
					targetBox.Anchor,
					targetBox.GridRotation);

			Vector2 targetWorldPosition =
				World.BoxGrid
					.GetVertexPosition(
						targetWorldVertex);

			uvs.Add(
				targetRenderer.WorldToUv(
					targetWorldPosition));
		}
	}


	/// <summary>
	/// 将 local lattice vertex 转换为相对于 local (0,0)
	/// 的 2D geometry。
	///
	/// 不重复写 SideLength/triangle-height 数学；
	/// 直接复用 TriangleGrid 的 lattice -> world transform，
	/// 再消去 Origin。
	/// </summary>
	private Vector2 GetReferenceLocalPosition(
		Vector2I localVertex)
	{
		Vector2 origin =
			World.StructureGrid
				.GetVertexPosition(
					Vector2I.Zero);

		return
			World.StructureGrid
				.GetVertexPosition(
					localVertex)
			- origin;
	}


	private void ClearAll()
	{
		/*
		 * 先快照所有 resident references，并清空本地索引。
		 * 随后的 ClearChunkPins() 可能让 WorldRendererRoot
		 * 立即卸载 dependency-only chunks，并回调本 renderer。
		 */
		var residentReferences =
			new List<EmbeddedBoxReference>(
				_loadedChunksByReference.Keys);

		foreach (
			MeshInstance2D visual
			in _visualByReference.Values)
		{
			visual.QueueFree();
		}

		_visualByReference.Clear();
		_referencesByLoadedChunk.Clear();
		_loadedChunksByReference.Clear();
		_referencesByTargetBox.Clear();
		_meshByTargetBox.Clear();

		foreach (
			EmbeddedBoxReference reference
			in residentReferences)
		{
			_worldRenderer!.ClearChunkPins(
				reference);
		}
	}

	private void EnsureStarted()
	{
		if (!_started)
		{
			throw new InvalidOperationException(
				"EmbeddedBoxReferenceRenderer has not been initialized.");
		}
	}
}
