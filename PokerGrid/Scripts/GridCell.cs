using System;
using Godot;

namespace Car.Scripts;

public partial class GridCell : Node2D
{
	public event Action<GridCell> FaceUpChanged;

	[Export]
	public Texture2D CellTexture { get; set; }

	[Export]
	public Vector2 CellSize { get; set; } = Vector2.Zero;

	[Export]
	public NodePath SpritePath { get; set; } = new("Sprite2D");

	[Export]
	public NodePath SuitSpritePath { get; set; } = new("SuitSprite2D");

	[Export]
	public NodePath RankLabelPath { get; set; } = new("RankLabel");

	[Export]
	public NodePath BackSpritePath { get; set; } = new("back");

	[Export]
	public Color NormalModulate { get; set; } = Colors.White;

	[Export]
	public Color HighlightModulate { get; set; } = new(1.55f, 1.45f, 0.65f);

	public bool IsFaceUp { get; private set; }

	public override void _Ready()
	{
		SetProcessUnhandledInput(true);
		ApplyVisual();
		UpdateFaceVisibility();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
		{
			return;
		}

		var localMousePosition = ToLocal(GetGlobalMousePosition());
		var halfCellSize = GetCellSize() * 0.5f;
		if (Mathf.Abs(localMousePosition.X) > halfCellSize.X ||
			Mathf.Abs(localMousePosition.Y) > halfCellSize.Y)
		{
			return;
		}

		Flip();
		GetViewport()?.SetInputAsHandled();
	}

	public void SetHighlighted(bool highlighted)
	{
		var modulate = highlighted ? HighlightModulate : NormalModulate;

		var sprite = GetSprite();
		if (sprite != null)
		{
			sprite.Modulate = modulate;
		}

		var suitSprite = GetSuitSprite();
		if (suitSprite != null)
		{
			suitSprite.Modulate = modulate;
		}

		var backSprite = GetBackSprite();
		if (backSprite != null)
		{
			backSprite.Modulate = modulate;
		}
	}

	public void Flip()
	{
		SetFaceUp(!IsFaceUp);
	}

	public void SetFaceUp(bool faceUp, bool emitChanged = true)
	{
		if (IsFaceUp == faceUp)
		{
			return;
		}

		IsFaceUp = faceUp;
		UpdateFaceVisibility();

		if (emitChanged)
		{
			FaceUpChanged?.Invoke(this);
		}
	}

	public void SetCard(Texture2D suitTexture, string rankText, Color rankColor)
	{
		var suitSprite = GetSuitSprite();
		if (suitSprite != null)
		{
			suitSprite.Texture = suitTexture;
		}

		var rankLabel = GetRankLabel();
		if (rankLabel != null)
		{
			rankLabel.Text = rankText;
			rankLabel.Modulate = rankColor;
		}

		ApplyVisual();
		SetFaceUp(false, emitChanged: false);
	}

	public void ApplyVisual()
	{
		var sprite = GetSprite();
		if (sprite == null)
		{
			return;
		}

		var texture = GetCellTexture();
		if (texture == null)
		{
			return;
		}

		sprite.Texture = texture;
		sprite.Centered = true;
		sprite.TextureFilter = TextureFilterEnum.Nearest;

		var textureSize = texture.GetSize();
		var cellSize = GetCellSize();
		if (textureSize.X <= 0.0f || textureSize.Y <= 0.0f || cellSize.X <= 0.0f || cellSize.Y <= 0.0f)
		{
			return;
		}

		sprite.Scale = new Vector2(cellSize.X / textureSize.X, cellSize.Y / textureSize.Y);
		ApplyCardLayout(cellSize);
		UpdateFaceVisibility();
	}

	public Vector2 GetCellSize()
	{
		var textureSize = GetCellTexture()?.GetSize() ?? Vector2.Zero;

		return new Vector2(
			CellSize.X > 0.0f ? CellSize.X : textureSize.X,
			CellSize.Y > 0.0f ? CellSize.Y : textureSize.Y);
	}

	private Sprite2D GetSprite()
	{
		return GetNodeOrNull<Sprite2D>(SpritePath);
	}

	private Sprite2D GetSuitSprite()
	{
		return GetNodeOrNull<Sprite2D>(SuitSpritePath);
	}

	private Label GetRankLabel()
	{
		return GetNodeOrNull<Label>(RankLabelPath);
	}

	private Sprite2D GetBackSprite()
	{
		return GetNodeOrNull<Sprite2D>(BackSpritePath);
	}

	private Texture2D GetCellTexture()
	{
		return CellTexture ?? GetSprite()?.Texture;
	}

	private void ApplyCardLayout(Vector2 cellSize)
	{
		var suitSprite = GetSuitSprite();
		if (suitSprite != null)
		{
			suitSprite.Centered = true;
			suitSprite.TextureFilter = TextureFilterEnum.Nearest;
			suitSprite.Position = new Vector2(0.0f, -cellSize.Y * 0.13f);

			var suitTextureSize = suitSprite.Texture?.GetSize() ?? Vector2.Zero;
			if (suitTextureSize.X > 0.0f && suitTextureSize.Y > 0.0f)
			{
				var maxSuitSize = cellSize * 0.34f;
				var suitScale = Mathf.Min(maxSuitSize.X / suitTextureSize.X, maxSuitSize.Y / suitTextureSize.Y);
				suitSprite.Scale = Vector2.One * suitScale;
			}
		}

		var backSprite = GetBackSprite();
		if (backSprite != null)
		{
			backSprite.Centered = true;
			backSprite.TextureFilter = TextureFilterEnum.Nearest;

			var backTextureSize = backSprite.Texture?.GetSize() ?? Vector2.Zero;
			if (backTextureSize.X > 0.0f && backTextureSize.Y > 0.0f)
			{
				backSprite.Scale = new Vector2(cellSize.X / backTextureSize.X, cellSize.Y / backTextureSize.Y);
			}
		}

		var rankLabel = GetRankLabel();
		if (rankLabel == null)
		{
			return;
		}

		rankLabel.HorizontalAlignment = HorizontalAlignment.Center;
		rankLabel.VerticalAlignment = VerticalAlignment.Center;
		rankLabel.Position = new Vector2(-cellSize.X * 0.5f, cellSize.Y * 0.14f);
		rankLabel.Size = new Vector2(cellSize.X, cellSize.Y * 0.28f);
		rankLabel.AddThemeFontSizeOverride("font_size", Mathf.Max(12, Mathf.RoundToInt(cellSize.Y * 0.18f)));
	}

	private void UpdateFaceVisibility()
	{
		var suitSprite = GetSuitSprite();
		if (suitSprite != null)
		{
			suitSprite.Visible = IsFaceUp;
		}

		var rankLabel = GetRankLabel();
		if (rankLabel != null)
		{
			rankLabel.Visible = IsFaceUp;
		}

		var backSprite = GetBackSprite();
		if (backSprite != null)
		{
			backSprite.Visible = !IsFaceUp;
		}
	}
}
