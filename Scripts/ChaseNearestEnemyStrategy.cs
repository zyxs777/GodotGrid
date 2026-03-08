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

        Unit target = SelectTarget(unit, snapshot.Units);
        if (target == null)
        {
            return new UnitDecision(unit, null, null, 0);
        }

        int currentDistance = GridDistance(unit.GridPos, target.GridPos);
        if (currentDistance <= unit.MaxAttackRange)
        {
            return new UnitDecision(unit, target, null, 0, attackTarget: target);
        }

        List<Vector2I> path = BuildPathToClosestTileNearTarget(unit, target.GridPos, snapshot);
        if (path.Count <= 1)
        {
            return new UnitDecision(unit, target, null, 0);
        }

        Vector2I nextGrid = path[1];
        int nextDistance = GridDistance(nextGrid, target.GridPos);
        int movePriority = currentDistance - nextDistance;

        if (unit.IsImmediateBacktrack(nextGrid) && movePriority <= 0)
        {
            return new UnitDecision(unit, target, null, 0);
        }

        return new UnitDecision(unit, target, nextGrid, movePriority);
    }

    private static Unit SelectTarget(Unit self, IReadOnlyList<Unit> allUnits)
    {
        if (ShouldKeepCurrentTarget(self))
        {
            return self.CurrentTarget;
        }

        return FindNearestEnemy(self, allUnits);
    }

    private static bool ShouldKeepCurrentTarget(Unit self)
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
        return GridDistance(self.GridPos, currentTarget.GridPos) <= loseRange;
    }

    private static Unit FindNearestEnemy(Unit self, IReadOnlyList<Unit> allUnits)
    {
        Unit nearestEnemy = null;
        float nearestDistSq = float.MaxValue;

        foreach (Unit other in allUnits)
        {
            if (other == null || other == self || !other.IsAlive)
            {
                continue;
            }

            if (!self.IsEnemyOf(other))
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

    private static List<Vector2I> BuildPathToClosestTileNearTarget(
        Unit movingUnit,
        Vector2I targetGrid,
        BattleSnapshot snapshot)
    {
        Vector2I start = movingUnit.GridPos;
        Queue<Vector2I> queue = new();
        Dictionary<Vector2I, Vector2I> cameFrom = new();
        Dictionary<Vector2I, int> pathCost = new();

        queue.Enqueue(start);
        cameFrom[start] = start;
        pathCost[start] = 0;

        Vector2I bestGrid = start;
        int bestTargetDistance = GridDistance(start, targetGrid);
        int bestPathCost = 0;

        while (queue.Count > 0)
        {
            Vector2I current = queue.Dequeue();
            int currentDistanceToTarget = GridDistance(current, targetGrid);
            int currentPathCost = pathCost[current];

            if (currentDistanceToTarget < bestTargetDistance ||
                (currentDistanceToTarget == bestTargetDistance && currentPathCost < bestPathCost))
            {
                bestGrid = current;
                bestTargetDistance = currentDistanceToTarget;
                bestPathCost = currentPathCost;
            }

            foreach (Vector2I direction in CardinalDirections)
            {
                Vector2I next = current + direction;

                if (cameFrom.ContainsKey(next))
                {
                    continue;
                }

                if (!snapshot.IsWalkable(next, movingUnit))
                {
                    continue;
                }

                cameFrom[next] = current;
                pathCost[next] = currentPathCost + 1;
                queue.Enqueue(next);
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

    private static int GridDistance(Vector2I a, Vector2I b)
    {
        return Mathf.Max(Mathf.Abs(a.X - b.X), Mathf.Abs(a.Y - b.Y));
    }
}
