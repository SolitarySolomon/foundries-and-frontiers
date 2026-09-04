using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Owns the single shared pathfinder.
    ///
    /// One instance for the whole server rather than one per villager: each carries a
    /// caching block accessor, and thirty of those is thirty caches of largely the same
    /// blocks. Pathfinding runs on the server tick thread, so sharing is safe, and the
    /// shared cache actually gets warmer the more villagers use it - a village walking the
    /// same few streets repeatedly is the best case rather than the worst.
    /// </summary>
    public class PathfindingSystem : ModSystem
    {
        private VillagerPathfind pathfinder;

        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            base.StartServerSide(sapi);
            pathfinder = new VillagerPathfind(sapi);
        }

        public VillagerPathfind Pathfinder => pathfinder;
    }
}
