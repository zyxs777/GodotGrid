using Godot;
using System.Collections.Generic;

public sealed class BattleSnapshot
{
    private readonly Dictionary<Vector2I, Unit> _unitsByGridPos = new();
    private readonly Dictionary<Vector2I, Unit> _projectedUnitsByGridPos = new();
    private readonly Dictionary<Unit, Vector2I> _projectedGridByUnit = new();
    private readonly List<Unit> _units = new();

    public int BoardWidth { get; }
    public int BoardHeight { get; }
    public IReadOnlyList<Unit> Units => _units;

    public BattleSnapshot(int boardWidth, int boardHeight, IEnumerable<Unit> units)
    {
        BoardWidth = boardWidth;
        BoardHeight = boardHeight;

        foreach (Unit unit in units)
        {
            if (unit == null || !unit.IsAlive)
            {
                continue;
            }

            _units.Add(unit);

            Vector2I gridPos = unit.GridPos;
            _projectedGridByUnit[unit] = gridPos;
            if (!IsInsideBoard(gridPos))
            {
                continue;
            }

            if (!_unitsByGridPos.ContainsKey(gridPos))
            {
                _unitsByGridPos[gridPos] = unit;
            }

            if (!_projectedUnitsByGridPos.ContainsKey(gridPos))
            {
                _projectedUnitsByGridPos[gridPos] = unit;
            }
        }
    }

    public bool IsInsideBoard(Vector2I gridPos)
    {
        return gridPos.X >= 0 &&
               gridPos.X < BoardWidth &&
               gridPos.Y >= 0 &&
               gridPos.Y < BoardHeight;
    }

    public bool TryGetUnitAt(Vector2I gridPos, out Unit unit)
    {
        return _projectedUnitsByGridPos.TryGetValue(gridPos, out unit);
    }

    public bool IsWalkable(Vector2I gridPos, Unit movingUnit)
    {
        if (!IsInsideBoard(gridPos))
        {
            return false;
        }

        if (!_projectedUnitsByGridPos.TryGetValue(gridPos, out Unit occupant))
        {
            return true;
        }

        return occupant == movingUnit;
    }

    public Vector2I GetGridPosition(Unit unit)
    {
        if (unit == null)
        {
            return default;
        }

        return _projectedGridByUnit.TryGetValue(unit, out Vector2I gridPos)
            ? gridPos
            : unit.GridPos;
    }

    public void ReserveMove(Unit unit, Vector2I destination)
    {
        if (unit == null)
        {
            return;
        }

        Vector2I previousGrid = GetGridPosition(unit);
        if (IsInsideBoard(previousGrid) &&
            _projectedUnitsByGridPos.TryGetValue(previousGrid, out Unit previousOccupant) &&
            previousOccupant == unit)
        {
            _projectedUnitsByGridPos.Remove(previousGrid);
        }

        _projectedGridByUnit[unit] = destination;

        if (IsInsideBoard(destination))
        {
            _projectedUnitsByGridPos[destination] = unit;
        }
    }
}
