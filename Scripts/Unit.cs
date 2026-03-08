using Godot;
using System.Collections.Generic;

public enum UnitFacing
{
    Left,
    Right
}

public partial class Unit : Node2D
{
    [Signal]
    public delegate void ClickedEventHandler(Unit unit);

    [Export] private UnitTeam _team = UnitTeam.Player;
    [Export] private Color _playerSelectedColor = new Color(0.3f, 0.8f, 1f, 1f);
    [Export] private Color _enemySelectedColor = new Color(1f, 0.35f, 0.35f, 1f);
    [Export] private Color _normalSelectedColor = Colors.White;
    [Export(PropertyHint.Range, "10,1000,1")] private float _moveSpeed = 240f;
    [Export(PropertyHint.Range, "1,10000,1")] private int _maxHealth = 100;
    [Export(PropertyHint.Range, "1,1000,1")] private int _attackPower = 20;
    [Export(PropertyHint.Range, "0.1,10,0.1")] private float _attackSpeed = 1f;
    [Export(PropertyHint.Range, "1,10,1")] private int _maxAttackRange = 1;
    [Export(PropertyHint.Range, "0,20,1")] private int _targetLoseRange = 0;
    [Export] private UnitFacing _defaultFacing = UnitFacing.Right;
    [Export] private string _attackAnimationName = "KnightAtk";

    private Area2D _clickArea;
    private Sprite2D _choosedSprite;
    private Control _healthBar;
    private ColorRect _healthBarFill;
    private AnimationPlayer _animationPlayer;
    private Vector2 _healthBarFillBaseSize;
    private Vector2 _baseScale = Vector2.One;
    private readonly List<Vector2I> _moveGridPath = new();
    private readonly List<Vector2> _moveWorldPath = new();
    private int _movePathIndex;
    private bool _hasPreviousGridPos;
    private Vector2I _previousGridPos;
    private float _attackCooldownRemaining;
    private bool _isAttackAnimating;
    private Unit _queuedAttackTarget;

    public Vector2I GridPos { get; private set; }
    public UnitTeam Team => _team;
    public Unit CurrentTarget { get; private set; }
    public bool HasMovePath => _movePathIndex < _moveWorldPath.Count;
    public bool IsMoving => HasMovePath;
    public bool IsAlive { get; private set; } = true;
    public bool IsAttacking => _isAttackAnimating;
    public int CurrentHealth { get; private set; }
    public int MaxAttackRange => _maxAttackRange;
    public int TargetLoseRange => _targetLoseRange > 0 ? _targetLoseRange : _maxAttackRange;
    public UnitFacing Facing { get; private set; } = UnitFacing.Right;

    public override void _Ready()
    {
        _clickArea = GetNode<Area2D>("ClickArea");
        _choosedSprite = GetNode<Sprite2D>("Visual/Choose");
        _healthBar = GetNode<Control>("Visual/HealthBar");
        _healthBarFill = GetNode<ColorRect>("Visual/HealthBar/Fill");
        _animationPlayer = GetNodeOrNull<AnimationPlayer>("AnimationPlayer");
        _healthBarFillBaseSize = _healthBarFill.Size;
        _baseScale = new Vector2(Mathf.Abs(Scale.X), Scale.Y);
        if (Mathf.Abs(_baseScale.X) <= Mathf.Epsilon)
        {
            _baseScale.X = 1f;
        }

        _clickArea.InputEvent += OnClickAreaInputEvent;
        if (_animationPlayer != null)
        {
            _animationPlayer.AnimationFinished += OnAnimationFinished;
        }

        CurrentHealth = _maxHealth;
        IsAlive = true;
        _attackCooldownRemaining = 0f;
        _isAttackAnimating = false;
        _queuedAttackTarget = null;

        SetSelected(false);
        Facing = InferFacingFromScale(Scale.X);
        ApplyFacing(Facing);
        UpdateHealthBar();
    }

    private void OnClickAreaInputEvent(Node viewport, InputEvent @event, long shapeIdx)
    {
        if (@event is InputEventMouseButton mouseButton &&
            mouseButton.Pressed &&
            mouseButton.ButtonIndex == MouseButton.Left)
        {
            EmitSignal(SignalName.Clicked, this);
        }
    }

    private void OnAnimationFinished(StringName animationName)
    {
        if (animationName != _attackAnimationName)
        {
            return;
        }

        _isAttackAnimating = false;
        _queuedAttackTarget = null;
    }

    public void SetSelected(bool selected)
    {
        _choosedSprite.Visible = selected;

        if (!selected)
        {
            _choosedSprite.Modulate = _normalSelectedColor;
            return;
        }

        _choosedSprite.Modulate = _team switch
        {
            UnitTeam.Player => _playerSelectedColor,
            UnitTeam.Enemy => _enemySelectedColor,
            _ => _normalSelectedColor
        };
    }

    public void SetGridPos(Vector2I gridPos)
    {
        GridPos = gridPos;
    }

    public bool IsEnemyOf(Unit other)
    {
        return other != null && Team != other.Team;
    }

    public bool IsAllyOf(Unit other)
    {
        return other != null && Team == other.Team;
    }

    public void FindNearestEnemy(IEnumerable<Unit> allUnits)
    {
        Unit nearestEnemy = null;
        float nearestDistSq = float.MaxValue;

        foreach (Unit other in allUnits)
        {
            if (other == null || other == this || !other.IsAlive)
            {
                continue;
            }

            if (!IsEnemyOf(other))
            {
                continue;
            }

            float distSq = GlobalPosition.DistanceSquaredTo(other.GlobalPosition);
            if (distSq < nearestDistSq)
            {
                nearestDistSq = distSq;
                nearestEnemy = other;
            }
        }

        bool targetChanged = CurrentTarget != nearestEnemy;
        CurrentTarget = nearestEnemy;

        if (!targetChanged)
        {
            return;
        }

        if (CurrentTarget != null)
        {
            GD.Print($"{Name} target -> {CurrentTarget.Name}");
        }
        else
        {
            GD.Print($"{Name} found no enemy target.");
        }
    }

    public void ClearTarget()
    {
        CurrentTarget = null;
        _queuedAttackTarget = null;
        _isAttackAnimating = false;
    }

    public void TickCombat(float delta)
    {
        if (_attackCooldownRemaining <= 0f)
        {
            return;
        }

        _attackCooldownRemaining -= delta;
        if (_attackCooldownRemaining < 0f)
        {
            _attackCooldownRemaining = 0f;
        }
    }

    public bool StartAttack(Unit target)
    {
        if (!CanStartAttack(target))
        {
            return false;
        }

        _queuedAttackTarget = target;
        float attacksPerSecond = Mathf.Max(0.1f, _attackSpeed);
        _attackCooldownRemaining = 1f / attacksPerSecond;
        _isAttackAnimating = true;

        if (_animationPlayer != null && _animationPlayer.HasAnimation(_attackAnimationName))
        {
            _animationPlayer.Play(_attackAnimationName);
        }
        else
        {
            ResolveAttackHit();
            _isAttackAnimating = false;
            _queuedAttackTarget = null;
        }

        return true;
    }

    public void ResolveAttackHit()
    {
        if (!IsAlive)
        {
            return;
        }

        Unit target = _queuedAttackTarget;
        if (!CanDamageTarget(target))
        {
            return;
        }

        target.TakeDamage(_attackPower);
    }

    public bool TryAttack(Unit target)
    {
        if (target != null)
        {
            _queuedAttackTarget = target;
        }

        ResolveAttackHit();
        return true;
    }

    public void TakeDamage(int damage)
    {
        if (!IsAlive)
        {
            return;
        }

        int finalDamage = Mathf.Max(0, damage);
        CurrentHealth -= finalDamage;

        if (CurrentHealth > 0)
        {
            UpdateHealthBar();
            return;
        }

        CurrentHealth = 0;
        IsAlive = false;
        ClearMovePath();
        ClearTarget();
        UpdateHealthBar();
    }

    public void SetCurrentTarget(Unit target)
    {
        CurrentTarget = target;
    }

    public void SetMovePath(IReadOnlyList<Vector2I> gridPath, IReadOnlyList<Vector2> worldPath)
    {
        ClearMovePath();

        if (gridPath == null || worldPath == null || gridPath.Count == 0 || worldPath.Count == 0)
        {
            return;
        }

        if (gridPath.Count != worldPath.Count)
        {
            GD.PushError($"{Name} received invalid path data.");
            return;
        }

        _moveGridPath.AddRange(gridPath);
        _moveWorldPath.AddRange(worldPath);
        _movePathIndex = 0;
    }

    public void BeginStepMove(Vector2I toGrid, Vector2 toWorld)
    {
        List<Vector2I> gridPath = new(1) { toGrid };
        List<Vector2> worldPath = new(1) { toWorld };
        SetMovePath(gridPath, worldPath);
    }

    public void ClearMovePath()
    {
        _moveGridPath.Clear();
        _moveWorldPath.Clear();
        _movePathIndex = 0;
    }

    public bool IsImmediateBacktrack(Vector2I nextGrid)
    {
        return _hasPreviousGridPos && nextGrid == _previousGridPos;
    }

    public void ResetMoveState()
    {
        ClearMovePath();
        _hasPreviousGridPos = false;
        _previousGridPos = default;
        _attackCooldownRemaining = 0f;
        _queuedAttackTarget = null;
        _isAttackAnimating = false;
        if (_animationPlayer != null)
        {
            _animationPlayer.Stop();
        }
    }

    public bool TryAdvanceAlongPath(float delta, out Vector2I fromGrid, out Vector2I toGrid)
    {
        fromGrid = GridPos;
        toGrid = default;

        if (!HasMovePath)
        {
            return false;
        }

        Vector2 moveTargetWorld = _moveWorldPath[_movePathIndex];
        FaceHorizontal(moveTargetWorld.X - GlobalPosition.X);

        float step = _moveSpeed * delta;
        GlobalPosition = GlobalPosition.MoveToward(moveTargetWorld, step);

        if (GlobalPosition.DistanceSquaredTo(moveTargetWorld) > 0.0001f)
        {
            return false;
        }

        GlobalPosition = moveTargetWorld;
        toGrid = _moveGridPath[_movePathIndex];
        return true;
    }

    public void CommitReachedWaypoint(Vector2I toGrid)
    {
        if (!HasMovePath)
        {
            return;
        }

        Vector2I oldGrid = GridPos;
        SetGridPos(toGrid);
        _previousGridPos = oldGrid;
        _hasPreviousGridPos = true;
        _movePathIndex++;

        if (!HasMovePath)
        {
            ClearMovePath();
        }
    }

    public void AbortCurrentStep(Vector2 fallbackWorldPosition)
    {
        GlobalPosition = fallbackWorldPosition;
        ClearMovePath();
    }

    public void FaceTarget(Unit target)
    {
        if (target == null)
        {
            return;
        }

        FaceHorizontal(target.GlobalPosition.X - GlobalPosition.X);
    }

    public void Face(UnitFacing facing)
    {
        ApplyFacing(facing);
    }

    private bool CanStartAttack(Unit target)
    {
        if (!CanDamageTarget(target))
        {
            return false;
        }

        if (_attackCooldownRemaining > 0f || _isAttackAnimating)
        {
            return false;
        }

        return true;
    }

    private bool CanDamageTarget(Unit target)
    {
        if (!IsAlive || IsMoving)
        {
            return false;
        }

        if (target == null || !target.IsAlive || !IsEnemyOf(target))
        {
            return false;
        }

        int dx = Mathf.Abs(GridPos.X - target.GridPos.X);
        int dy = Mathf.Abs(GridPos.Y - target.GridPos.Y);
        int distance = Mathf.Max(dx, dy);
        return distance <= _maxAttackRange;
    }

    private void FaceHorizontal(float deltaX)
    {
        if (Mathf.Abs(deltaX) <= 0.001f)
        {
            return;
        }

        ApplyFacing(deltaX > 0f ? UnitFacing.Right : UnitFacing.Left);
    }

    private void ApplyFacing(UnitFacing facing)
    {
        Facing = facing;
        float scaleX = facing == _defaultFacing ? _baseScale.X : -_baseScale.X;
        Scale = new Vector2(scaleX, _baseScale.Y);
    }

    private UnitFacing InferFacingFromScale(float scaleX)
    {
        if (Mathf.Abs(scaleX) <= Mathf.Epsilon)
        {
            return _defaultFacing;
        }

        bool mirrored = scaleX < 0f;
        if (!mirrored)
        {
            return _defaultFacing;
        }

        return _defaultFacing == UnitFacing.Right ? UnitFacing.Left : UnitFacing.Right;
    }

    private void UpdateHealthBar()
    {
        if (_healthBar == null || _healthBarFill == null)
        {
            return;
        }

        _healthBar.Visible = IsAlive;

        float ratio = _maxHealth > 0 ? Mathf.Clamp((float)CurrentHealth / _maxHealth, 0f, 1f) : 0f;
        _healthBarFill.Size = new Vector2(_healthBarFillBaseSize.X * ratio, _healthBarFillBaseSize.Y);

        _healthBarFill.Color = ratio switch
        {
            > 0.6f => new Color(0.2f, 0.85f, 0.35f, 1f),
            > 0.3f => new Color(0.95f, 0.75f, 0.2f, 1f),
            _ => new Color(0.9f, 0.2f, 0.2f, 1f)
        };
    }
}
