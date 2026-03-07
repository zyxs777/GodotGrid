using Godot;
using System.Collections.Generic;

public partial class BattleRoot : Node2D
{
    [Export] private PackedScene _tileScene;
    [Export] private Camera2D _camera2D;

    [Export] private int _boardWidth = 8;
    [Export] private int _boardHeight = 8;

    [Export] private float _tileWidth = 128f;
    [Export] private float _tileHeight = 64f;

    private Node2D _board;
    private readonly Dictionary<Vector2I, Tile> _tilesByGridPos = new();
    private Tile _selectedTile;
    
    public override void _Ready()
    {
        SetProcessUnhandledInput(true);
        _board = GetNode<Node2D>("Board");
        GenerateBoard();
        _camera2D.SetPosition(GetBoardCenter());
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton mouseButton)
        {
            return;
        }

        if (!mouseButton.Pressed || mouseButton.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        Vector2 boardLocalPos = _board.ToLocal(GetGlobalMousePosition());
        if (TryWorldToGrid(boardLocalPos, out Vector2I grid))
        {
            SelectTile(grid);
            return;
        }

        ClearSelection();
    }
    
    private void GenerateBoard()
    {
        _tilesByGridPos.Clear();

        for (int y = 0; y < _boardHeight; y++)
        {
            for (int x = 0; x < _boardWidth; x++)
            {
                Node node = _tileScene.Instantiate();
                Tile tile = node as Tile;

                if (tile == null)
                {
                    GD.PushError("Tile scene root is not Tile.");
                    return;
                }

                tile.Position = GridToWorld(x, y);
                tile.Init(new Vector2I(x, y));
                _board.AddChild(tile);
                _tilesByGridPos[tile.GridPos] = tile;
            }
        }
    }
    
    private Vector2 GridToWorld(int x, int y)
    {
        float worldX = (x - y) * (_tileWidth * 0.5f);
        float worldY = (x + y) * (_tileHeight * 0.5f);
        return new Vector2(worldX, worldY);
    }

    private bool TryWorldToGrid(Vector2 worldPos, out Vector2I grid)
    {
        float halfW = _tileWidth * 0.5f;
        float halfH = _tileHeight * 0.5f;

        float gx = (worldPos.X / halfW + worldPos.Y / halfH) * 0.5f;
        float gy = (worldPos.Y / halfH - worldPos.X / halfW) * 0.5f;

        int x = Mathf.RoundToInt(gx);
        int y = Mathf.RoundToInt(gy);

        if (x >= 0 && x < _boardWidth && y >= 0 && y < _boardHeight)
        {
            grid = new Vector2I(x, y);
            return true;
        }

        grid = default;
        return false;
    }
    
    private Vector2 GetBoardCenter()
    {
        Vector2 top = GridToWorld(_boardWidth - 1, 0);
        Vector2 bottom = GridToWorld(0, _boardHeight - 1);
        return (top + bottom) * 0.5f;
    }

    private void SelectTile(Vector2I gridPos)
    {
        if (!_tilesByGridPos.TryGetValue(gridPos, out Tile tile))
        {
            return;
        }

        if (_selectedTile == tile)
        {
            return;
        }

        if (_selectedTile != null)
        {
            _selectedTile.SetHighlighted(false);
        }

        _selectedTile = tile;
        _selectedTile.SetHighlighted(true);
    }

    private void ClearSelection()
    {
        if (_selectedTile == null)
        {
            return;
        }

        _selectedTile.SetHighlighted(false);
        _selectedTile = null;
    }
}
