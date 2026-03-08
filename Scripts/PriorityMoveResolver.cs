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

        Dictionary<Vector2I, MoveIntent> bestIntentByDestination = new();

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

    private static bool IsHigherPriority(MoveIntent contender, MoveIntent current)
    {
        if (contender.Priority != current.Priority)
        {
            return contender.Priority > current.Priority;
        }

        return contender.Unit.GetInstanceId() < current.Unit.GetInstanceId();
    }
}
