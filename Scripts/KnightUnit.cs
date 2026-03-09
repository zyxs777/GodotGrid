using Godot;

public partial class KnightUnit : Unit
{
    [Export] private string[] _attackAnimationNames = { "KnightAtk","KnightAtk1" };

    private int _attackAnimationIndex;

    protected override string PeekAttackAnimationName()
    {
        return GetAnimationNameAt(_attackAnimationIndex);
    }

    protected override string ConsumeAttackAnimationName()
    {
        string animationName = GetAnimationNameAt(_attackAnimationIndex);
        if (_attackAnimationNames != null && _attackAnimationNames.Length > 0)
        {
            _attackAnimationIndex = (_attackAnimationIndex + 1) % _attackAnimationNames.Length;
        }

        return animationName;
    }

    protected override void ResetAttackSequence()
    {
        _attackAnimationIndex = 0;
    }

    private string GetAnimationNameAt(int index)
    {
        if (_attackAnimationNames == null || _attackAnimationNames.Length == 0)
        {
            return string.Empty;
        }

        int safeIndex = Mathf.Clamp(index, 0, _attackAnimationNames.Length - 1);
        return _attackAnimationNames[safeIndex] ?? string.Empty;
    }
}