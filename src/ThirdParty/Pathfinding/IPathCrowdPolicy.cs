namespace FoundriesFrontiers
{
    /// <summary>
    /// Lets an entity tell the pathfinder to stop treating other villagers as obstacles.
    ///
    /// This exists to keep the vendored A* free of any knowledge about villages. Upstream
    /// it checked VS Village's own "is a gather event running" flag directly; here anything
    /// that wants crowd avoidance suspended - mustering, fleeing indoors before a storm,
    /// squeezing through a gate during a raid - implements this instead.
    /// </summary>
    public interface IPathCrowdPolicy
    {
        bool IgnoreCrowding { get; }
    }
}
