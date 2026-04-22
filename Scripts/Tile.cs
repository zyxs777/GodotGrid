using System;
using Godot;

public partial class Tile : Node2D
{
    public event Action<Tile> CardFaceUpChanged;

    [Export] private Sprite2D _sprite;
    [Export] private Sprite2D _spriteHilight;
    [Export] private Sprite2D _suitSprite;
    [Export] private Label _rankLabel;
    [Export] private Sprite2D _backSprite;
    [Export] private Color _cursorHighlightColor = new Color(0.95f, 0.95f, 0.2f, 0.72f);
    [Export] private Color _rangeHighlightColor = new Color(1f, 0.35f, 0.2f, 0.35f);
    [Export] private Color _cardHighlightColor = new Color(1.0f, 0.86f, 0.12f, 0.58f);
    [Export] private Vector2 _cardVisualSize = new Vector2(44f, 44f);
    [Export] private bool _useDiamondHitArea = true;

    private bool _isCursorHighlighted;
    private bool _isRangeHighlighted;
    private bool _isCardHighlighted;

    public Vector2I GridPos { get; private set; }
    public bool IsFaceUp { get; private set; }

    public override void _Ready()
    {
        SetProcessUnhandledInput(true);
        _sprite ??= GetNodeOrNull<Sprite2D>("Sprite2D");
        _spriteHilight ??= GetNodeOrNull<Sprite2D>("Sprite2DHilight");
        _suitSprite ??= GetNodeOrNull<Sprite2D>("SuitSprite2D");
        _rankLabel ??= GetNodeOrNull<Label>("RankLabel");
        _backSprite ??= GetNodeOrNull<Sprite2D>("CardBack");
        ApplyCardVisual();
        UpdateFaceVisibility();
        RefreshHighlight();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            return;
        }

        if (!IsMouseInsideTile())
        {
            return;
        }

        FlipCard();
        GetViewport()?.SetInputAsHandled();
    }

    public void Init(Vector2I gridPos)
    {
        GridPos = gridPos;
        Name = $"Tile_{gridPos.X}_{gridPos.Y}";
    }

    public void SetHighlighted(bool highlighted)
    {
        SetCursorHighlighted(highlighted);
    }

    public void SetCursorHighlighted(bool highlighted)
    {
        _isCursorHighlighted = highlighted;
        RefreshHighlight();
    }

    public void SetRangeHighlighted(bool highlighted)
    {
        _isRangeHighlighted = highlighted;
        RefreshHighlight();
    }

    public void SetCardHighlighted(bool highlighted)
    {
        _isCardHighlighted = highlighted;
        RefreshHighlight();
    }

    public void FlipCard()
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
            CardFaceUpChanged?.Invoke(this);
        }
    }

    public void SetCard(Texture2D suitTexture, string rankText, Color rankColor)
    {
        _suitSprite ??= GetNodeOrNull<Sprite2D>("SuitSprite2D");
        _rankLabel ??= GetNodeOrNull<Label>("RankLabel");
        _backSprite ??= GetNodeOrNull<Sprite2D>("CardBack");

        if (_suitSprite != null)
        {
            _suitSprite.Texture = suitTexture;
        }

        if (_rankLabel != null)
        {
            _rankLabel.Text = rankText;
            _rankLabel.Modulate = rankColor;
        }

        ApplyCardVisual();
        SetFaceUp(false, emitChanged: false);
        UpdateFaceVisibility();
        SetCardHighlighted(false);
    }

    private void RefreshHighlight()
    {
        if (_spriteHilight == null)
        {
            return;
        }

        _spriteHilight.Visible = _isCursorHighlighted || _isCardHighlighted || _isRangeHighlighted;
        if (!_spriteHilight.Visible)
        {
            return;
        }

        if (_isCursorHighlighted)
        {
            _spriteHilight.Modulate = _cursorHighlightColor;
            return;
        }

        _spriteHilight.Modulate = _isCardHighlighted ? _cardHighlightColor : _rangeHighlightColor;
    }

    private bool IsMouseInsideTile()
    {
        Vector2 localMousePosition = ToLocal(GetGlobalMousePosition());
        Vector2 halfSize = GetTileSize() * 0.5f;

        if (halfSize.X <= 0f || halfSize.Y <= 0f)
        {
            return false;
        }

        if (_useDiamondHitArea)
        {
            return Mathf.Abs(localMousePosition.X) / halfSize.X +
                Mathf.Abs(localMousePosition.Y) / halfSize.Y <= 1f;
        }

        return Mathf.Abs(localMousePosition.X) <= halfSize.X &&
            Mathf.Abs(localMousePosition.Y) <= halfSize.Y;
    }

    private Vector2 GetTileSize()
    {
        return _sprite?.Texture?.GetSize() ?? new Vector2(128f, 64f);
    }

    private void ApplyCardVisual()
    {
        Vector2 tileSize = GetTileSize();

        if (_suitSprite != null)
        {
            _suitSprite.Centered = true;
            _suitSprite.TextureFilter = TextureFilterEnum.Nearest;
            _suitSprite.Position = new Vector2(0f, -tileSize.Y * 0.16f);

            Vector2 suitTextureSize = _suitSprite.Texture?.GetSize() ?? Vector2.Zero;
            if (suitTextureSize.X > 0f && suitTextureSize.Y > 0f)
            {
                Vector2 maxSuitSize = _cardVisualSize * 0.44f;
                float suitScale = Mathf.Min(maxSuitSize.X / suitTextureSize.X, maxSuitSize.Y / suitTextureSize.Y);
                _suitSprite.Scale = Vector2.One * suitScale;
            }
        }

        if (_backSprite != null)
        {
            _backSprite.Centered = true;
            _backSprite.TextureFilter = TextureFilterEnum.Nearest;
            _backSprite.Position = new Vector2(0f, -tileSize.Y * 0.02f);

            Vector2 backTextureSize = _backSprite.Texture?.GetSize() ?? Vector2.Zero;
            if (backTextureSize.X > 0f && backTextureSize.Y > 0f)
            {
                _backSprite.Scale = new Vector2(
                    _cardVisualSize.X / backTextureSize.X,
                    _cardVisualSize.Y / backTextureSize.Y);
            }
        }

        if (_rankLabel == null)
        {
            return;
        }

        _rankLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _rankLabel.VerticalAlignment = VerticalAlignment.Center;
        _rankLabel.Position = new Vector2(-tileSize.X * 0.5f, -tileSize.Y * 0.02f);
        _rankLabel.Size = new Vector2(tileSize.X, tileSize.Y * 0.44f);
        _rankLabel.AddThemeFontSizeOverride("font_size", 18);
    }

    private void UpdateFaceVisibility()
    {
        if (_suitSprite != null)
        {
            _suitSprite.Visible = IsFaceUp;
        }

        if (_rankLabel != null)
        {
            _rankLabel.Visible = IsFaceUp;
        }

        if (_backSprite != null)
        {
            _backSprite.Visible = !IsFaceUp;
        }
    }
}
