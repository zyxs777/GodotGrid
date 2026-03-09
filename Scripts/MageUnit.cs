using Godot;

public partial class MageUnit : Unit
{
    [Export] private string[] _attackAnimationNames = { "MageAtk" };
    [Export] private PackedScene _projectileScene;
    [Export(PropertyHint.Range, "50,2000,1")] private float _projectileSpeed = 720f;

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

    protected override bool CanResolveAttackHit(Unit target)
    {
        return IsValidEnemyTarget(target);
    }

    protected override void PerformAttackHit(Unit target)
    {
        Projectile projectile = CreateProjectileInstance();
        if (projectile == null)
        {
            target.TakeDamage(AttackPower);
            return;
        }

        Node parent = GetParent();
        if (parent == null)
        {
            target.TakeDamage(AttackPower);
            projectile.QueueFree();
            return;
        }

        parent.AddChild(projectile);
        projectile.GlobalPosition = GetAttackOriginGlobalPosition();
        projectile.Initialize(this, target, AttackPower, _projectileSpeed);
    }

    private Projectile CreateProjectileInstance()
    {
        if (_projectileScene == null) return null;
        Node projectileNode = _projectileScene.Instantiate();
        if (projectileNode is Projectile typedProjectile)
        {
            return typedProjectile;
        }

        return null;
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
