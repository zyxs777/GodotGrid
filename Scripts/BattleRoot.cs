using Godot;
using System.Collections.Generic;

public partial class BattleRoot : Node2D
{
    private enum BattlePhase
    {
        Placement,
        Battle
    }

    [Export] private PackedScene _tileScene;
    [Export] private Camera2D _camera2D;

    [Export] private int _boardWidth = 8;
    [Export] private int _boardHeight = 8;

    [Export] private float _tileWidth = 128f;
    [Export] private float _tileHeight = 64f;
    [Export(PropertyHint.Range, "0.02,1,0.01")] private float _battleStepInterval = 0.08f;

    private Node2D _board;
    private Node2D _unitsRoot;

    private readonly Dictionary<Vector2I, Tile> _tilesByGridPos = new();
    private readonly Dictionary<Vector2I, Unit> _unitsByGridPos = new();

    private Tile _highlightedTile;
    private Unit _selectedUnit;

    private Unit _draggingUnit;
    private Vector2I _dragStartGrid;
    private Vector2 _dragMouseOffset;

    private BattlePhase _currentPhase = BattlePhase.Placement;
    private float _battleStepElapsed;
    private readonly HashSet<Unit> _pendingStepUnits = new();

    // Battle loop pipeline: strategy decides, resolver arbitrates, root executes.
    private readonly ChaseNearestEnemyStrategy _decisionStrategy = new();
    private readonly PriorityMoveResolver _moveResolver = new();

    public override void _Ready()
    {
        SetProcessUnhandledInput(true);

        _board = GetNode<Node2D>("Board");
        _unitsRoot = GetNode<Node2D>("Units");

        GenerateBoard();
        BindUnits();
        SnapExistingUnitsToGrid();
        CenterCamera();

        EnterPlacementPhase();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey keyEvent &&
            keyEvent.Pressed &&
            !keyEvent.Echo &&
            keyEvent.Keycode == Key.Space)
        {
            TogglePhase();
            return;
        }

        if (_currentPhase == BattlePhase.Placement && _draggingUnit != null)
        {
            if (@event is InputEventMouseMotion)
            {
                UpdateDraggingUnitPosition();
                UpdateDragHighlight();
                return;
            }

            if (@event is InputEventMouseButton dragMouseButton &&
                !dragMouseButton.Pressed &&
                dragMouseButton.ButtonIndex == MouseButton.Left)
            {
                TryDropDraggingUnit();
                return;
            }

            return;
        }

        if (@event is not InputEventMouseButton mouseButton)
        {
            return;
        }

        if (!mouseButton.Pressed || mouseButton.ButtonIndex != MouseButton.Left)
        {
            return;
        }

        ClearSelectedUnit();
        ClearHighlightedTile();
    }

    public override void _Process(double delta)
    {
        if (_currentPhase != BattlePhase.Battle)
        {
            return;
        }

        float deltaSeconds = (float)delta;
        TickCombatForAllUnits(deltaSeconds);

        bool stepFinished = UpdateAllUnitsMovement(deltaSeconds);

        if (stepFinished)
        {
            _battleStepElapsed -= deltaSeconds;
            if (_battleStepElapsed <= 0f)
            {
                _battleStepElapsed = _battleStepInterval;
                ExecuteBattleDecisionStep();
            }
        }

        ExecuteAttackPhase();
        CleanupDefeatedUnits();
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

    private void BindUnits()
    {
        foreach (Node child in _unitsRoot.GetChildren())
        {
            if (child is Unit unit)
            {
                unit.Clicked += OnUnitClicked;
            }
        }
    }

    private void SnapExistingUnitsToGrid()
    {
        _unitsByGridPos.Clear();

        foreach (Node child in _unitsRoot.GetChildren())
        {
            if (child is not Unit unit)
            {
                continue;
            }

            Vector2 boardLocalPos = _board.ToLocal(unit.GlobalPosition);

            if (!TryWorldToGrid(boardLocalPos, out Vector2I gridPos))
            {
                GD.PushWarning($"{unit.Name} is outside board bounds and was not snapped.");
                continue;
            }

            if (_unitsByGridPos.ContainsKey(gridPos))
            {
                GD.PushWarning($"{unit.Name} snapped to occupied grid {gridPos}, skipped registering.");
                continue;
            }

            RegisterUnitAtGrid(unit, gridPos);
        }
    }

    private void OnUnitClicked(Unit unit)
    {
        if (_draggingUnit != null)
        {
            return;
        }

        if (_selectedUnit != null && _selectedUnit != unit)
        {
            _selectedUnit.SetSelected(false);
        }

        _selectedUnit = unit;
        _selectedUnit.SetSelected(true);

        if (_currentPhase != BattlePhase.Placement)
        {
            return;
        }

        /*if (unit.Team != UnitTeam.Player)
        {
            return;
        }*/

        _draggingUnit = unit;
        _dragStartGrid = unit.GridPos;
        _dragMouseOffset = unit.GlobalPosition - GetGlobalMousePosition();

        UpdateDragHighlight();
    }

    private void UpdateDraggingUnitPosition()
    {
        if (_draggingUnit == null)
        {
            return;
        }

        _draggingUnit.GlobalPosition = GetGlobalMousePosition() + _dragMouseOffset;
    }

    private void UpdateDragHighlight()
    {
        if (_draggingUnit == null)
        {
            ClearHighlightedTile();
            return;
        }

        Vector2 boardLocalPos = _board.ToLocal(_draggingUnit.GlobalPosition);

        if (TryWorldToGrid(boardLocalPos, out Vector2I grid))
        {
            HighlightTile(grid);
        }
        else
        {
            ClearHighlightedTile();
        }
    }

    private void TryDropDraggingUnit()
    {
        if (_draggingUnit == null)
        {
            return;
        }

        Vector2 boardLocalPos = _board.ToLocal(_draggingUnit.GlobalPosition);

        if (!TryWorldToGrid(boardLocalPos, out Vector2I targetGrid))
        {
            RegisterUnitAtGrid(_draggingUnit, _dragStartGrid);
            FinishDragging();
            return;
        }

        if (targetGrid == _dragStartGrid)
        {
            RegisterUnitAtGrid(_draggingUnit, _dragStartGrid);
            FinishDragging();
            return;
        }

        if (_unitsByGridPos.TryGetValue(targetGrid, out Unit occupant) && occupant != _draggingUnit)
        {
            SwapUnits(_draggingUnit, occupant);
            FinishDragging();
            return;
        }

        RegisterUnitAtGrid(_draggingUnit, targetGrid);
        FinishDragging();
    }

    private void SwapUnits(Unit a, Unit b)
    {
        Vector2I aGrid = a.GridPos;
        Vector2I bGrid = b.GridPos;

        _unitsByGridPos.Remove(aGrid);
        _unitsByGridPos.Remove(bGrid);

        RegisterUnitAtGrid(a, bGrid);
        RegisterUnitAtGrid(b, aGrid);
    }

    private void RegisterUnitAtGrid(Unit unit, Vector2I gridPos)
    {
        if (_unitsByGridPos.TryGetValue(unit.GridPos, out Unit current) && current == unit)
        {
            _unitsByGridPos.Remove(unit.GridPos);
        }

        unit.SetGridPos(gridPos);
        unit.GlobalPosition = _board.ToGlobal(GridToWorld(gridPos.X, gridPos.Y));
        _unitsByGridPos[gridPos] = unit;
    }

    private void FinishDragging()
    {
        ClearHighlightedTile();
        _draggingUnit = null;
    }

    private void HighlightTile(Vector2I gridPos)
    {
        if (!_tilesByGridPos.TryGetValue(gridPos, out Tile tile))
        {
            ClearHighlightedTile();
            return;
        }

        if (_highlightedTile == tile)
        {
            return;
        }

        if (_highlightedTile != null)
        {
            _highlightedTile.SetHighlighted(false);
        }

        _highlightedTile = tile;
        _highlightedTile.SetHighlighted(true);
    }

    private void ClearHighlightedTile()
    {
        if (_highlightedTile == null)
        {
            return;
        }

        _highlightedTile.SetHighlighted(false);
        _highlightedTile = null;
    }

    private void ClearSelectedUnit()
    {
        if (_selectedUnit == null)
        {
            return;
        }

        _selectedUnit.SetSelected(false);
        _selectedUnit = null;
    }

    private List<Unit> GetAllUnits()
    {
        List<Unit> units = new();

        foreach (Node child in _unitsRoot.GetChildren())
        {
            if (child is Unit unit && unit.IsAlive)
            {
                units.Add(unit);
            }
        }

        return units;
    }

    private void ExecuteBattleDecisionStep()
    {
        List<Unit> allUnits = GetAllUnits();
        if (allUnits.Count == 0)
        {
            return;
        }

        BattleSnapshot snapshot = new(_boardWidth, _boardHeight, allUnits);
        List<MoveIntent> intents = new();

        foreach (Unit unit in allUnits)
        {
            UnitDecision decision = _decisionStrategy.Decide(unit, snapshot);
            Unit preferredTarget = decision.AttackTarget ?? decision.PrimaryTarget;
            unit.SetCurrentTarget(preferredTarget);

            if (unit.IsAttacking)
            {
                if (preferredTarget != null)
                {
                    unit.FaceTarget(preferredTarget);
                }

                continue;
            }
            if (preferredTarget != null && decision.MoveDestination is not Vector2I)
            {
                unit.FaceTarget(preferredTarget);
            }

            if (decision.MoveDestination is not Vector2I moveDestination)
            {
                continue;
            }

            intents.Add(new MoveIntent(
                unit,
                unit.GridPos,
                moveDestination,
                decision.MovePriority));
        }

        Dictionary<Unit, Vector2I> resolvedMoves = _moveResolver.Resolve(intents, snapshot);
        _pendingStepUnits.Clear();

        foreach (KeyValuePair<Unit, Vector2I> move in resolvedMoves)
        {
            Unit unit = move.Key;
            Vector2I toGrid = move.Value;
            Vector2 toWorld = _board.ToGlobal(GridToWorld(toGrid.X, toGrid.Y));
            unit.BeginStepMove(toGrid, toWorld);
            _pendingStepUnits.Add(unit);
        }
    }

    private bool UpdateAllUnitsMovement(float delta)
    {
        if (_pendingStepUnits.Count == 0)
        {
            return true;
        }

        List<Unit> completedUnits = new();

        foreach (Unit unit in _pendingStepUnits)
        {
            if (unit == null || !unit.IsAlive)
            {
                completedUnits.Add(unit);
                continue;
            }

            if (!unit.TryAdvanceAlongPath(delta, out Vector2I fromGrid, out Vector2I toGrid))
            {
                continue;
            }

            if (_unitsByGridPos.TryGetValue(toGrid, out Unit toOccupant) &&
                toOccupant != unit &&
                toOccupant.IsAlive)
            {
                Vector2 fromWorld = _board.ToGlobal(GridToWorld(fromGrid.X, fromGrid.Y));
                unit.AbortCurrentStep(fromWorld);
                completedUnits.Add(unit);
                continue;
            }

            if (_unitsByGridPos.TryGetValue(fromGrid, out Unit fromOccupant) && fromOccupant == unit)
            {
                _unitsByGridPos.Remove(fromGrid);
            }

            _unitsByGridPos[toGrid] = unit;
            unit.CommitReachedWaypoint(toGrid);
            completedUnits.Add(unit);
        }

        foreach (Unit unit in completedUnits)
        {
            _pendingStepUnits.Remove(unit);
        }

        return _pendingStepUnits.Count == 0;
    }

    private void TickCombatForAllUnits(float delta)
    {
        foreach (Unit unit in GetAllUnits())
        {
            unit.TickCombat(delta);
        }
    }

    private void ExecuteAttackPhase()
    {
        foreach (Unit unit in GetAllUnits())
        {
            if (unit.IsMoving || unit.IsAttacking)
            {
                continue;
            }

            Unit target = unit.CurrentTarget;
            if (target == null || !target.IsAlive)
            {
                unit.ClearTarget();
                continue;
            }

            unit.FaceTarget(target);
            unit.StartAttack(target);
        }
    }

    private void CleanupDefeatedUnits()
    {
        List<Unit> defeated = new();

        foreach (Node child in _unitsRoot.GetChildren())
        {
            if (child is Unit unit && !unit.IsAlive)
            {
                defeated.Add(unit);
            }
        }

        if (defeated.Count == 0)
        {
            return;
        }

        HashSet<Unit> defeatedSet = new(defeated);

        foreach (Unit aliveUnit in GetAllUnits())
        {
            if (defeatedSet.Contains(aliveUnit.CurrentTarget))
            {
                aliveUnit.ClearTarget();
            }
        }

        foreach (Unit deadUnit in defeated)
        {
            _pendingStepUnits.Remove(deadUnit);

            if (_selectedUnit == deadUnit)
            {
                _selectedUnit = null;
            }

            if (_draggingUnit == deadUnit)
            {
                _draggingUnit = null;
                ClearHighlightedTile();
            }

            if (_unitsByGridPos.TryGetValue(deadUnit.GridPos, out Unit occupant) && occupant == deadUnit)
            {
                _unitsByGridPos.Remove(deadUnit.GridPos);
            }

            deadUnit.QueueFree();
        }
    }

    private void EnterPlacementPhase()
    {
        _currentPhase = BattlePhase.Placement;
        _pendingStepUnits.Clear();

        foreach (Unit unit in GetAllUnits())
        {
            unit.ResetMoveState();
            unit.ClearTarget();
        }

        GD.Print("Enter Placement Phase");
    }

    private void EnterBattlePhase()
    {
        _currentPhase = BattlePhase.Battle;

        if (_draggingUnit != null)
        {
            RegisterUnitAtGrid(_draggingUnit, _dragStartGrid);
            _draggingUnit = null;
        }

        ClearHighlightedTile();
        ClearSelectedUnit();

        _pendingStepUnits.Clear();
        _battleStepElapsed = 0f;
        ExecuteBattleDecisionStep();

        GD.Print("Enter Battle Phase");
    }

    private void TogglePhase()
    {
        if (_currentPhase == BattlePhase.Placement)
        {
            EnterBattlePhase();
        }
        else
        {
            EnterPlacementPhase();
        }
    }

    private void CenterCamera()
    {
        if (_camera2D == null)
        {
            GD.PushWarning("Camera2D is not assigned.");
            return;
        }

        _camera2D.Position = GetBoardCenter();
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
        Vector2 topRight = GridToWorld(_boardWidth - 1, 0);
        Vector2 bottomLeft = GridToWorld(0, _boardHeight - 1);
        return (topRight + bottomLeft) * 0.5f;
    }
}

