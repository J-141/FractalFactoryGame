using Godot;
using System;


public partial class BoxRenderer : Node
{
	private SubViewport _renderViewport = null!;
	private Camera2D _camera = null!;

	private SubViewport _historyViewport = null!;
	private TextureRect _historyCopy = null!;


	public Box Box { get; private set; } = null!;

	public Rect2 WorldCrop { get; private set; }

	public Vector2I TextureSize { get; private set; }


	public ViewportTexture RenderTexture =>
		_renderViewport.GetTexture();

	public ViewportTexture HistoryTexture =>
		_historyViewport.GetTexture();


	public override void _Ready()
	{
		_renderViewport =
			GetNode<SubViewport>("RenderViewport");

		_camera =
			GetNode<Camera2D>("RenderViewport/Camera2D");

		_historyViewport =
			GetNode<SubViewport>("HistoryViewport");

		_historyCopy =
			GetNode<TextureRect>("HistoryViewport/TextureRect");

		/*
		 * RenderViewport:
		 *     每帧从共享 World2D 中 crop Box region。
		 *
		 * HistoryViewport:
		 *     每帧把 RenderViewport.Texture 复制到另一张 texture。
		 *
		 * EmbeddedBoxReference 永远采样 HistoryTexture。
		 */
		_renderViewport.RenderTargetUpdateMode =
			SubViewport.UpdateMode.Always;

		_historyViewport.RenderTargetUpdateMode =
			SubViewport.UpdateMode.Always;

		_historyCopy.Texture =
			_renderViewport.GetTexture();
	}


	public void Initialize(
		Box box,
		World2D world2D,
		Rect2 worldCrop,
		Vector2I textureSize)
	{
		ArgumentNullException.ThrowIfNull(box);
		ArgumentNullException.ThrowIfNull(world2D);

		if (!IsNodeReady())
		{
			throw new InvalidOperationException(
				"BoxRenderer must be inside the SceneTree before Initialize().");
		}

		Box =
			box;

		/*
		 * 只有 RenderViewport 观察真实 World2D。
		 *
		 * HistoryViewport 不共享 World2D；
		 * 它只包含一个 TextureRect。
		 */
		_renderViewport.World2D =
			world2D;

		SetCrop(
			worldCrop,
			textureSize);
	}


	public void SetCrop(
		Rect2 worldCrop,
		Vector2I textureSize)
	{
		if (worldCrop.Size.X <= 0.0f ||
			worldCrop.Size.Y <= 0.0f)
		{
			throw new ArgumentOutOfRangeException(
				nameof(worldCrop));
		}

		if (textureSize.X <= 0 ||
			textureSize.Y <= 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(textureSize));
		}

		WorldCrop =
			worldCrop;

		TextureSize =
			textureSize;

		_renderViewport.Size =
			textureSize;

		_historyViewport.Size =
			textureSize;

		ConfigureCamera();
	}


	/// <summary>
	/// 将 world-space point 转换为 History/Render texture 的 UV。
	///
	/// Reference mesh 的 UV 计算可以直接使用该方法。
	/// </summary>
	public Vector2 WorldToUv(
		Vector2 worldPosition)
	{
		Vector2 viewportSize =
			new(
				TextureSize.X,
				TextureSize.Y);

		float zoom =
			GetUniformZoom();

		Vector2 pixel =
			(worldPosition -
			 WorldCrop.GetCenter())
			* zoom
			+ viewportSize * 0.5f;

		return new Vector2(
			pixel.X / viewportSize.X,
			pixel.Y / viewportSize.Y);
	}


	private void ConfigureCamera()
	{
		_camera.Position =
			WorldCrop.GetCenter();

		float zoom =
			GetUniformZoom();

		_camera.Zoom =
			new Vector2(
				zoom,
				zoom);
	}


	private float GetUniformZoom()
	{
		float zoomX =
			TextureSize.X /
			WorldCrop.Size.X;

		float zoomY =
			TextureSize.Y /
			WorldCrop.Size.Y;

		return Mathf.Min(
			zoomX,
			zoomY);
	}
}
