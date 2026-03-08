using Godot;

public readonly struct MoveIntent
{
    public Unit Unit { get; }
    public Vector2I FromGrid { get; }
    public Vector2I ToGrid { get; }
    public int Priority { get; }

    public MoveIntent(Unit unit, Vector2I fromGrid, Vector2I toGrid, int priority)
    {
        Unit = unit;
        FromGrid = fromGrid;
        ToGrid = toGrid;
        Priority = priority;
    }
}
