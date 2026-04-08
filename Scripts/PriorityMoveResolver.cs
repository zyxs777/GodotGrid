using Godot;
using System.Collections.Generic;

public sealed class PriorityMoveResolver : IMoveResolver
{
    public Dictionary<Unit, Vector2I> Resolve(IReadOnlyList<MoveIntent> intents, BattleSnapshot snapshot)
    {
        Dictionary<Unit, Vector2I> resolvedMoves = new();
        if (intents == null || intents.Count == 0)
        {
            return resolvedMoves;
        }

        List<MoveIntent> candidateIntents = new();
        Dictionary<Unit, MoveIntent> candidateByUnit = new();

        foreach (MoveIntent intent in intents)
        {
            if (intent.Unit == null)
            {
                continue;
            }

            if (intent.FromGrid == intent.ToGrid)
            {
                continue;
            }

            if (!snapshot.IsInsideBoard(intent.ToGrid))
            {
                continue;
            }

            // Keep resolution simple and stable: destination must be empty in the snapshot.
            if (snapshot.TryGetUnitAt(intent.ToGrid, out Unit occupant) && occupant != intent.Unit)
            {
                continue;
            }

            candidateIntents.Add(intent);
            candidateByUnit[intent.Unit] = intent;
        }

        Dictionary<Vector2I, MoveIntent> bestIntentByDestination = new();

        foreach (MoveIntent intent in candidateIntents)
        {
            if (ShouldSuppressForMutualMeleeApproach(intent, candidateByUnit))
            {
                continue;
            }

            if (!bestIntentByDestination.TryGetValue(intent.ToGrid, out MoveIntent bestIntent))
            {
                bestIntentByDestination[intent.ToGrid] = intent;
                continue;
            }

            if (IsHigherPriority(intent, bestIntent))
            {
                bestIntentByDestination[intent.ToGrid] = intent;
            }
        }

        foreach (KeyValuePair<Vector2I, MoveIntent> pair in bestIntentByDestination)
        {
            MoveIntent intent = pair.Value;
            resolvedMoves[intent.Unit] = intent.ToGrid;
        }

        return resolvedMoves;
    }

    private static bool ShouldSuppressForMutualMeleeApproach(
        MoveIntent intent,
        IReadOnlyDictionary<Unit, MoveIntent> candidateByUnit)
    {
        if (BattleRoot.ActiveAttackDistanceMetric != BattleRoot.GridDistanceMetric.Manhattan)
        {
            return false;
        }

        Unit unit = intent.Unit;
        if (unit == null || unit.MaxAttackRange != 1)
        {
            return false;
        }

        Unit target = unit.CurrentTarget;
        if (target == null || target.CurrentTarget != unit || target.MaxAttackRange != 1)
        {
            return false;
        }

        if (!candidateByUnit.TryGetValue(target, out MoveIntent targetIntent))
        {
            return false;
        }

        int currentDistance = BattleRoot.GetGridDistance(unit.GridPos, target.GridPos);
        if (currentDistance > 3)
        {
            return false;
        }

        return unit.GetInstanceId() > targetIntent.Unit.GetInstanceId();
    }

    private static bool IsHigherPriority(MoveIntent contender, MoveIntent current)
    {
        if (contender.Priority != current.Priority)
        {
            return contender.Priority > current.Priority;
        }

        return contender.Unit.GetInstanceId() < current.Unit.GetInstanceId();
    }
}
