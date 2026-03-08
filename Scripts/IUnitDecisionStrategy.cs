public interface IUnitDecisionStrategy
{
    UnitDecision Decide(Unit unit, BattleSnapshot snapshot);
}
