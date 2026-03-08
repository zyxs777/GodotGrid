using Godot;

public sealed class UnitDecision
{
    public Unit Unit { get; }
    public Unit PrimaryTarget { get; }
    public Vector2I? MoveDestination { get; }
    public int MovePriority { get; }

    // Reserved for attack/skill pipeline extension.
    public Unit AttackTarget { get; }
    public string SkillId { get; }

    public UnitDecision(
        Unit unit,
        Unit primaryTarget,
        Vector2I? moveDestination,
        int movePriority,
        Unit attackTarget = null,
        string skillId = "")
    {
        Unit = unit;
        PrimaryTarget = primaryTarget;
        MoveDestination = moveDestination;
        MovePriority = movePriority;
        AttackTarget = attackTarget;
        SkillId = skillId ?? string.Empty;
    }
}
