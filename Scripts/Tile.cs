using Godot;

public partial class Tile : Node2D
{
    [Export] private Sprite2D _sprite;
    [Export] private Sprite2D _spriteHilight;

    public Vector2I GridPos { get; private set; }

    public override void _Ready()
    {
        _sprite ??= GetNodeOrNull<Sprite2D>("Sprite2D");
        _spriteHilight ??= GetNodeOrNull<Sprite2D>("Sprite2DHilight");
        SetHighlighted(false);
    }

    public void Init(Vector2I gridPos)
    {
        GridPos = gridPos;
        Name = $"Tile_{gridPos.X}_{gridPos.Y}";
    }

    public void SetHighlighted(bool highlighted)
    {
        if (_spriteHilight != null)
        {
            _spriteHilight.Visible = highlighted;
        }
    }
}
