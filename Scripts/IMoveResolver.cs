using Godot;
using System.Collections.Generic;

public interface IMoveResolver
{
    Dictionary<Unit, Vector2I> Resolve(IReadOnlyList<MoveIntent> intents, BattleSnapshot snapshot);
}
