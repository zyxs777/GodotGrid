using Godot;

public partial class Projectile : Node2D
{
    [Export(PropertyHint.Range, "50,3000,1")] private float _speed = 720f;
    [Export(PropertyHint.Range, "1,100,1")] private float _hitRadius = 16f;
    [Export] private int _zOffset = 2;
    [Export] private bool _homeToTarget = true;

    private Unit _source;
    private Unit _target;
    private int _damage;
    private Vector2 _lockedTargetPosition;

    public void Initialize(Unit source, Unit target, int damage, float speed)
    {
        _source = source;
        _target = target;
        _damage = Mathf.Max(0, damage);
        _speed = Mathf.Max(1f, speed);
        _lockedTargetPosition = target != null ? target.GetTargetPointGlobalPosition() : GlobalPosition;
        UpdateRotationAndLayer();
    }

    public override void _Process(double delta)
    {
        if (_target == null || !_target.IsAlive)
        {
            QueueFree();
            return;
        }

        Vector2 destination = _homeToTarget ? _target.GetTargetPointGlobalPosition() : _lockedTargetPosition;
        Vector2 toTarget = destination - GlobalPosition;
        float step = _speed * (float)delta;
        float hitRadiusSq = _hitRadius * _hitRadius;

        if (toTarget.LengthSquared() <= hitRadiusSq || toTarget.Length() <= step)
        {
            GlobalPosition = destination;
            Impact();
            return;
        }

        GlobalPosition = GlobalPosition.MoveToward(destination, step);
        UpdateRotationAndLayer();
    }

    private void Impact()
    {
        if (_target != null && _target.IsAlive && (_source == null || _source.IsEnemyOf(_target)))
        {
            _target.TakeDamage(_damage);
        }

        QueueFree();
    }

    private void UpdateRotationAndLayer()
    {
        if (_target != null)
        {
            Vector2 direction = (_target.GetTargetPointGlobalPosition() - GlobalPosition).Normalized();
            if (direction.LengthSquared() > 0.0001f)
            {
                Rotation = direction.Angle();
            }
        }

        ZAsRelative = false;
        ZIndex = Mathf.RoundToInt(GlobalPosition.Y) + _zOffset;
    }
}
