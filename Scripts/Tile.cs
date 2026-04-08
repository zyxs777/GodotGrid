using Godot;

public partial class Tile : Node2D
{
    [Export] private Sprite2D _sprite;
    [Export] private Sprite2D _spriteHilight;
    [Export] private Color _cursorHighlightColor = new Color(0.95f, 0.95f, 0.2f, 0.72f);
    [Export] private Color _rangeHighlightColor = new Color(1f, 0.35f, 0.2f, 0.35f);

    private bool _isCursorHighlighted;
    private bool _isRangeHighlighted;

    public Vector2I GridPos { get; private set; }

    public override void _Ready()
    {
        _sprite ??= GetNodeOrNull<Sprite2D>("Sprite2D");
        _spriteHilight ??= GetNodeOrNull<Sprite2D>("Sprite2DHilight");
        RefreshHighlight();
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

    private void RefreshHighlight()
    {
        if (_spriteHilight == null)
        {
            return;
        }

        _spriteHilight.Visible = _isCursorHighlighted || _isRangeHighlighted;
        if (!_spriteHilight.Visible)
        {
            return;
        }

        _spriteHilight.Modulate = _isCursorHighlighted ? _cursorHighlightColor : _rangeHighlightColor;
    }
}
