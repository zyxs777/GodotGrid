using Godot;
using System.Collections.Generic;

public sealed class ChaseNearestEnemyStrategy : IUnitDecisionStrategy
{
    private static readonly Vector2I[] CardinalDirections =
    {
        new Vector2I(1, 0),
        new Vector2I(-1, 0),
        new Vector2I(0, 1),
        new Vector2I(0, -1)
    };

    public UnitDecision Decide(Unit unit, BattleSnapshot snapshot)
    {
        if (unit == null || !unit.IsAlive)
        {
            return new UnitDecision(unit, null, null, 0);
        }

        Unit target = SelectTarget(unit, snapshot);
        if (target == null)
        {
            return new UnitDecision(unit, null, null, 0);
        }

        Vector2I unitGrid = snapshot.GetGridPosition(unit);
        Vector2I targetGrid = snapshot.GetGridPosition(target);
        int currentDistance = BattleRoot.GetGridDistance(unitGrid, targetGrid);
        if (currentDistance <= unit.MaxAttackRange)
        {
            return new UnitDecision(unit, target, null, 0, attackTarget: target);
        }

        Vector2I pursuitGrid = GetPursuitGrid(unit, unitGrid, targetGrid, snapshot);

        if (TryResolveDiagonalMeleeApproach(unit, target, snapshot, out UnitDecision diagonalDecision))
        {
            return diagonalDecision;
        }

        if (TryChooseImmediateStep(unit, pursuitGrid, snapshot, out Vector2I immediateNextGrid, out int immediatePriority))
        {
            return new UnitDecision(unit, target, immediateNextGrid, immediatePriority);
        }

        List<Vector2I> path = BuildPathToClosestTileNearTarget(unit, pursuitGrid, snapshot);
        if (path.Count <= 1)
        {
            return new UnitDecision(unit, target, null, 0);
        }

        Vector2I nextGrid = path[1];
        int pursuitDistance = BattleRoot.GetGridDistance(unitGrid, pursuitGrid);
        int nextDistance = BattleRoot.GetGridDistance(nextGrid, pursuitGrid);
        int movePriority = pursuitDistance - nextDistance;

        if (unit.IsImmediateBacktrack(nextGrid) && movePriority <= 0)
        {
            return new UnitDecision(unit, target, null, 0);
        }

        return new UnitDecision(unit, target, nextGrid, movePriority);
    }

    private static Vector2I GetPursuitGrid(
        Unit unit,
        Vector2I unitGrid,
        Vector2I targetGrid,
        BattleSnapshot snapshot)
    {
        if (unit.MaxAttackRange == 1 &&
            BattleRoot.ActiveAttackDistanceMetric == BattleRoot.GridDistanceMetric.Manhattan &&
            TryGetPreferredMeleeSlot(unit, unitGrid, targetGrid, snapshot, out Vector2I slotGrid))
        {
            return slotGrid;
        }

        return targetGrid;
    }

    private static bool TryGetPreferredMeleeSlot(
        Unit movingUnit,
        Vector2I unitGrid,
        Vector2I targetGrid,
        BattleSnapshot snapshot,
        out Vector2I slotGrid)
    {
        slotGrid = default;
        bool foundSlot = false;
        int bestDistance = int.MaxValue;

        foreach (Vector2I direction in CardinalDirections)
        {
            Vector2I candidateSlot = targetGrid + direction;
            if (!snapshot.IsInsideBoard(candidateSlot))
            {
                continue;
            }

            if (!snapshot.IsWalkable(candidateSlot, movingUnit) && candidateSlot != unitGrid)
            {
                continue;
            }

            int candidateDistance = BattleRoot.GetGridDistance(unitGrid, candidateSlot);
            if (!foundSlot ||
                candidateDistance < bestDistance ||
                (candidateDistance == bestDistance && IsGridEarlier(candidateSlot, slotGrid)))
            {
                foundSlot = true;
                bestDistance = candidateDistance;
                slotGrid = candidateSlot;
            }
        }

        return foundSlot;
    }

    private static bool TryChooseImmediateStep(
        Unit unit,
        Vector2I pursuitGrid,
        BattleSnapshot snapshot,
        out Vector2I nextGrid,
        out int movePriority)
    {
        nextGrid = default;
        movePriority = 0;

        Vector2I currentGrid = snapshot.GetGridPosition(unit);
        int currentDistance = BattleRoot.GetGridDistance(currentGrid, pursuitGrid);
        int bestDistance = currentDistance;
        bool foundCandidate = false;

        foreach (Vector2I direction in CardinalDirections)
        {
            Vector2I candidateGrid = currentGrid + direction;
            if (!snapshot.IsWalkable(candidateGrid, unit))
            {
                continue;
            }

            int candidateDistance = BattleRoot.GetGridDistance(candidateGrid, pursuitGrid);
            int candidatePriority = currentDistance - candidateDistance;
            if (candidatePriority <= 0)
            {
                continue;
            }

            if (unit.IsImmediateBacktrack(candidateGrid) && candidatePriority <= 0)
            {
                continue;
            }

            if (!foundCandidate ||
                candidateDistance < bestDistance ||
                (candidateDistance == bestDistance && IsGridEarlier(candidateGrid, nextGrid)))
            {
                foundCandidate = true;
                bestDistance = candidateDistance;
                nextGrid = candidateGrid;
                movePriority = candidatePriority;
            }
        }

        if (foundCandidate)
        {
            return true;
        }
        return false;
    }

    private static bool TryResolveDiagonalMeleeApproach(
        Unit unit,
        Unit target,
        BattleSnapshot snapshot,
        out UnitDecision decision)
    {
        decision = null;

        if (BattleRoot.ActiveAttackDistanceMetric != BattleRoot.GridDistanceMetric.Manhattan)
        {
            return false;
        }

        if (unit.MaxAttackRange != 1)
        {
            return false;
        }

        if (target.CurrentTarget != unit)
        {
            return false;
        }

        Vector2I unitGrid = snapshot.GetGridPosition(unit);
        Vector2I targetGrid = snapshot.GetGridPosition(target);
        int dx = targetGrid.X - unitGrid.X;
        int dy = targetGrid.Y - unitGrid.Y;
        if (Mathf.Abs(dx) != 1 || Mathf.Abs(dy) != 1)
        {
            return false;
        }

        bool unitMovesFirst = unit.GetInstanceId() < target.GetInstanceId();
        if (!unitMovesFirst)
        {
            decision = new UnitDecision(unit, target, null, 0);
            return true;
        }

        Vector2I[] options =
        {
            new(unitGrid.X, targetGrid.Y),
            new(targetGrid.X, unitGrid.Y)
        };
        System.Array.Sort(options, static (a, b) =>
        {
            int compareX = a.X.CompareTo(b.X);
            return compareX != 0 ? compareX : a.Y.CompareTo(b.Y);
        });

        int currentDistance = BattleRoot.GetGridDistance(unitGrid, targetGrid);
        foreach (Vector2I nextGrid in options)
        {
            if (!snapshot.IsWalkable(nextGrid, unit))
            {
                continue;
            }

            int nextDistance = BattleRoot.GetGridDistance(nextGrid, targetGrid);
            int movePriority = currentDistance - nextDistance;
            if (unit.IsImmediateBacktrack(nextGrid) && movePriority <= 0)
            {
                continue;
            }

            decision = new UnitDecision(unit, target, nextGrid, movePriority);
            return true;
        }

        decision = new UnitDecision(unit, target, null, 0);
        return true;
    }

    private static Unit SelectTarget(Unit self, BattleSnapshot snapshot)
    {
        if (ShouldKeepCurrentTarget(self, snapshot))
        {
            return self.CurrentTarget;
        }

        return FindNearestReachableEnemy(self, snapshot);
    }

    private static bool ShouldKeepCurrentTarget(Unit self, BattleSnapshot snapshot)
    {
        Unit currentTarget = self.CurrentTarget;
        if (currentTarget == null || !currentTarget.IsAlive)
        {
            return false;
        }

        if (!self.IsEnemyOf(currentTarget))
        {
            return false;
        }

        int loseRange = Mathf.Max(self.MaxAttackRange, self.TargetLoseRange);
        Vector2I selfGrid = snapshot.GetGridPosition(self);
        Vector2I targetGrid = snapshot.GetGridPosition(currentTarget);
        if (BattleRoot.GetGridDistance(selfGrid, targetGrid) > loseRange)
        {
            return false;
        }

        return CanPursueTarget(self, currentTarget, snapshot, selfGrid, targetGrid);
    }

    private static Unit FindNearestReachableEnemy(Unit self, BattleSnapshot snapshot)
    {
        Unit nearestEnemy = null;
        float nearestDistSq = float.MaxValue;
        Vector2I selfGrid = snapshot.GetGridPosition(self);

        foreach (Unit other in snapshot.Units)
        {
            if (other == null || other == self || !other.IsAlive)
            {
                continue;
            }

            if (!self.IsEnemyOf(other))
            {
                continue;
            }

            Vector2I targetGrid = snapshot.GetGridPosition(other);
            if (!CanPursueTarget(self, other, snapshot, selfGrid, targetGrid))
            {
                continue;
            }

            float distSq = self.GlobalPosition.DistanceSquaredTo(other.GlobalPosition);
            if (distSq < nearestDistSq)
            {
                nearestDistSq = distSq;
                nearestEnemy = other;
            }
        }

        return nearestEnemy;
    }

    private static bool CanPursueTarget(
        Unit self,
        Unit target,
        BattleSnapshot snapshot,
        Vector2I selfGrid,
        Vector2I targetGrid)
    {
        if (BattleRoot.GetGridDistance(selfGrid, targetGrid) <= self.MaxAttackRange)
        {
            return true;
        }

        if (self.MaxAttackRange == 1 &&
            BattleRoot.ActiveAttackDistanceMetric == BattleRoot.GridDistanceMetric.Manhattan)
        {
            return TryGetPreferredMeleeSlot(self, selfGrid, targetGrid, snapshot, out _);
        }

        return true;
    }

    private static List<Vector2I> BuildPathToClosestTileNearTarget(
        Unit movingUnit,
        Vector2I targetGrid,
        BattleSnapshot snapshot)
    {
        Vector2I start = snapshot.GetGridPosition(movingUnit);
        PriorityQueue<Vector2I, int> openSet = new();
        Dictionary<Vector2I, Vector2I> cameFrom = new();
        Dictionary<Vector2I, int> gScore = new();

        cameFrom[start] = start;
        gScore[start] = 0;
        openSet.Enqueue(start, Heuristic(start, targetGrid));

        Vector2I bestGrid = start;
        int bestTargetDistance = BattleRoot.GetGridDistance(start, targetGrid);
        int bestPathCost = 0;

        while (openSet.Count > 0)
        {
            Vector2I current = openSet.Dequeue();
            int currentPathCost = gScore[current];
            int currentDistanceToTarget = BattleRoot.GetGridDistance(current, targetGrid);

            if (currentDistanceToTarget < bestTargetDistance ||
                (currentDistanceToTarget == bestTargetDistance && currentPathCost < bestPathCost))
            {
                bestGrid = current;
                bestTargetDistance = currentDistanceToTarget;
                bestPathCost = currentPathCost;

                if (bestTargetDistance == 1)
                {
                    break;
                }
            }

            foreach (Vector2I direction in CardinalDirections)
            {
                Vector2I next = current + direction;
                if (!snapshot.IsWalkable(next, movingUnit))
                {
                    continue;
                }

                int tentativeG = currentPathCost + 1;
                if (gScore.TryGetValue(next, out int existingG) && tentativeG >= existingG)
                {
                    continue;
                }

                cameFrom[next] = current;
                gScore[next] = tentativeG;
                int priority = tentativeG + Heuristic(next, targetGrid);
                openSet.Enqueue(next, priority);
            }
        }

        if (!cameFrom.ContainsKey(bestGrid))
        {
            return new List<Vector2I> { start };
        }

        return ReconstructPath(start, bestGrid, cameFrom);
    }

    private static List<Vector2I> ReconstructPath(
        Vector2I start,
        Vector2I end,
        Dictionary<Vector2I, Vector2I> cameFrom)
    {
        List<Vector2I> path = new();
        Vector2I current = end;
        path.Add(current);

        while (current != start)
        {
            current = cameFrom[current];
            path.Add(current);
        }

        path.Reverse();
        return path;
    }

    private static int Heuristic(Vector2I from, Vector2I to)
    {
        return BattleRoot.GetGridDistance(from, to);
    }

    private static bool IsGridEarlier(Vector2I a, Vector2I b)
    {
        int compareX = a.X.CompareTo(b.X);
        return compareX != 0 ? compareX < 0 : a.Y < b.Y;
    }
}
