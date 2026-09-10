using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Fells trees in the village woodlot, plants what it can from what falls, and hauls
    /// the timber home.
    ///
    /// The interesting problem here is that the game has no idea what a tree is. Blocks
    /// know they are logs and leaves; nothing anywhere says "these four hundred blocks
    /// are one oak". So the flood fill below is ours: from the trunk block a villager is
    /// standing at, walk every connected log and leaf and take the lot in one go.
    ///
    /// Felling the whole tree at once rather than one block at a time is deliberate. A
    /// villager who takes a trunk block every two seconds leaves a canopy hanging in the
    /// air for a minute, which looks broken, and the game's own leaf decay is slow enough
    /// that the woodlot would be full of floating hedges.
    ///
    /// Replanting does not rely on vanilla luck. A player breaking leaves gets a sapling
    /// rarely enough that a woodlot left to the base drop rate empties out and never
    /// recovers, which makes the whole plot pointless. So a felled tree yields a set
    /// number of saplings for the tree it was, on top of anything the leaves happened to
    /// drop. That is a balance decision rather than a simulation one, and the number is
    /// config so it can be argued with.
    ///
    /// Which sapling comes from the world catalogue, matched to the log that was felled,
    /// so an oak woodlot stays an oak woodlot.
    /// </summary>
    public class AiTaskVillagerLumberjack : AiTaskVillagerWork
    {
        protected override EnumTrade Trade => EnumTrade.Lumberjack;

        protected override EnumPlotKind PlotKind => EnumPlotKind.Woodlot;

        /// <summary>Trunks stand on the ground and grow up, so look a good way above it.</summary>
        protected override int VerticalSearch => 3;

        /// <summary>
        /// Felling a whole tree is one action and a slow one. Trunk blocks are not taken
        /// individually, so the per block time would make a tree instant.
        /// </summary>
        private const float FellTimeMultiplier = 3f;

        protected override float WorkTimeSec => base.WorkTimeSec * FellTimeMultiplier;

        /// <summary>
        /// A hard stop on the flood fill. A redwood is enormous and a badly formed world
        /// could in principle connect two forests through touching canopies. Better a
        /// half felled giant than a server frozen walking a hundred thousand blocks.
        /// </summary>
        private const int MaxTreeBlocks = 1400;

        /// <summary>
        /// How much of a tree comes down in one go.
        ///
        /// A redwood is well over a thousand blocks, and taking all of them in a single
        /// tick means a thousand block breaks with their relight and leaf decay cascades
        /// while the server holds its breath. Felling in slices spreads that over a few
        /// seconds, and has the side effect of looking like a tree coming down rather
        /// than a tree blinking out of existence.
        /// </summary>
        private const int BlocksPerSlice = 60;

        /// <summary>Saplings kept back from the last tree, waiting on a stump to go into.</summary>
        private readonly List<ItemStack> saplings = new List<ItemStack>();

        /// <summary>What is left of the tree currently being felled, lowest first.</summary>
        private readonly List<BlockPos> felling = new List<BlockPos>();

        private BlockPos lastStump;

        public AiTaskVillagerLumberjack(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
            : base(entity, taskConfig, aiConfig) { }

        /// <summary>
        /// A trunk block standing on the ground. Only the base counts as a target, so a
        /// villager walks to the foot of a tree rather than trying to reach a branch
        /// twenty blocks up.
        /// </summary>
        protected override bool IsTarget(Block block, BlockPos pos)
        {
            if (!IsLog(block)) return false;

            // Not a target if there is another log directly below: that is the middle of
            // a trunk, and its base is the block we actually want.
            Block below = entity.World.BlockAccessor.GetBlock(pos.DownCopy());
            return !IsLog(below);
        }

        protected override bool Work(BlockPos pos)
        {
            // Start a new tree only when the last one is down. A villager interrupted
            // halfway through a redwood comes back and finishes it rather than picking a
            // fresh one and leaving half a trunk standing.
            if (felling.Count == 0)
            {
                List<BlockPos> tree = CollectTree(pos);
                if (tree.Count == 0) return false;

                // Bottom up. The cut goes into the base of the trunk and the rest of the
                // tree comes down after it, which is both what a lumberjack does and what
                // it should look like from a distance.
                tree.Sort((a, b) => a.Y.CompareTo(b.Y));

                felling.AddRange(tree);
                lastStump = pos.Copy();
                saplings.Clear();

                StockSaplingsFor(pos);

                entity.Api.Logger.VerboseDebug(
                    "[F&F] Felling a tree of {0} blocks at {1}", tree.Count, pos);
            }

            int logs = 0;
            int cut = 0;

            while (felling.Count > 0 && cut < BlocksPerSlice)
            {
                BlockPos part = felling[0];
                felling.RemoveAt(0);
                cut++;

                Block block = entity.World.BlockAccessor.GetBlock(part);
                if (block == null || block.Id == 0) continue;

                if (IsLog(block))
                {
                    BreakAndCarry(part);
                    logs++;
                }
                else
                {
                    // Leaves are the only place a sapling comes from, so only they are
                    // worth the drop roll. Rolling drops on every trunk block was pure
                    // waste, and a village has no use for foliage either way.
                    KeepAnySapling(block, part);
                    entity.World.BlockAccessor.BreakBlock(part, null, 0f);
                }
            }

            // Still standing means come back next tick. The base has already gone by
            // now, so the work loop would normally drop the target: StillBusyAt is what
            // keeps the villager here until the rest of the tree is down.
            return logs > 0 || cut > 0;
        }

        /// <summary>
        /// The stump is the first thing to go, so the target stops looking like a target
        /// almost immediately. This is what stops the work loop wandering off to another
        /// tree with half of this one still hanging in the air.
        /// </summary>
        protected override bool StillBusyAt(BlockPos pos) => felling.Count > 0;

        /// <summary>
        /// Takes the saplings a felled tree is worth, before any of it comes down.
        ///
        /// Read off the log itself rather than off the leaves, because leaf drops are
        /// rare enough in this game that a woodlot relying on them thins out and dies.
        /// A lumberjack who fells a tree knows how to keep seed from it.
        /// </summary>
        private void StockSaplingsFor(BlockPos stump)
        {
            int want = FFConfig.Current.Work.SaplingsPerTree;
            if (want <= 0) return;

            var catalogue = entity.Api.ModLoader.GetModSystem<WorldCatalogue>();
            if (catalogue?.HasSaplings != true) return;

            Block log = entity.World.BlockAccessor.GetBlock(stump);
            Block sapling = catalogue.SaplingForLog(log, entity.World.Rand);
            if (sapling == null) return;

            for (int i = 0; i < want; i++) saplings.Add(new ItemStack(sapling));
        }

        /// <summary>Puts a sapling back on the stump, if the tree gave us one.</summary>
        protected override void AfterWork(BlockPos pos)
        {
            if (lastStump == null || saplings.Count == 0) return;
            if (entity.World.Rand.NextDouble() > FFConfig.Current.Work.ReplantChance) return;

            IBlockAccessor ba = entity.World.BlockAccessor;

            Block ground = ba.GetBlock(lastStump.DownCopy());
            if (ground == null || ground.Id == 0 || ground.IsLiquid()) return;

            Block here = ba.GetBlock(lastStump);
            if (here != null && here.Id != 0 && !here.IsReplacableBy(saplings[0].Block)) return;

            Block sapling = saplings[0].Block;
            if (sapling == null) return;

            ba.SetBlock(sapling.BlockId, lastStump);
            ba.TriggerNeighbourBlockUpdate(lastStump);
            saplings.RemoveAt(0);
        }

        /// <summary>
        /// A woodlot with nothing standing in it is not a dead end, it is a nursery. If
        /// the village has saplings from previous fellings they go in now; otherwise the
        /// lumberjack simply waits, which is what a real one would do.
        /// </summary>
        protected override void OnNothingToDo(Village village, VillagePlot plot)
        {
            base.OnNothingToDo(village, plot);
            if (saplings.Count > 0 && lastStump != null) AfterWork(lastStump);
        }

        // --- what counts as a tree ---------------------------------------------------

        /// <summary>
        /// Every log and leaf connected to this one, in all twenty six directions.
        ///
        /// Diagonals are included because tree canopies in this game are not
        /// face-connected, and a fill that only walks the six faces leaves half a crown
        /// hanging. Only grown trunks start a fill; placed logs are somebody's house.
        /// </summary>
        private List<BlockPos> CollectTree(BlockPos start)
        {
            var found = new List<BlockPos>();
            var seen = new HashSet<BlockPos>();
            var queue = new Queue<BlockPos>();

            IBlockAccessor ba = entity.World.BlockAccessor;

            queue.Enqueue(start.Copy());
            seen.Add(start.Copy());

            while (queue.Count > 0 && found.Count < MaxTreeBlocks)
            {
                BlockPos at = queue.Dequeue();
                Block block = ba.GetBlock(at);
                if (block == null || block.Id == 0) continue;

                bool log = IsLog(block);
                bool leaf = IsLeaf(block);
                if (!log && !leaf) continue;

                found.Add(at);

                // Leaves are an edge of the tree, not a bridge into the next one. Walking
                // on through them is how two touching oaks become one very large oak.
                if (!log) continue;

                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dz = -1; dz <= 1; dz++)
                        {
                            if (dx == 0 && dy == 0 && dz == 0) continue;

                            var next = new BlockPos(at.X + dx, at.Y + dy, at.Z + dz, 0);

                            // Never walk below where we started. A trunk that touches a
                            // neighbour's roots should not take the neighbour with it.
                            if (next.Y < start.Y) continue;
                            if (!seen.Add(next)) continue;

                            queue.Enqueue(next);
                        }
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// A grown trunk, not a placed one.
        ///
        /// The distinction matters more than it looks: log-placed is what a player builds
        /// a cabin out of, and a lumberjack who cannot tell the difference will
        /// eventually dismantle somebody's house and file it as timber.
        /// </summary>
        private static bool IsLog(Block block)
        {
            string path = block?.Code?.Path;
            if (path == null) return false;
            if (path.StartsWith("log-placed")) return false;
            return path.StartsWith("log-grown") || path.StartsWith("logsection");
        }

        private static bool IsLeaf(Block block)
        {
            string path = block?.Code?.Path;
            return path != null && (path.StartsWith("leaves") || path.StartsWith("leavesbranchy"));
        }

        /// <summary>
        /// Keeps back anything plantable that a block would have dropped, before it is
        /// broken. Saplings are not a resource the village pools, they are seed stock,
        /// so they never go near the ledger.
        /// </summary>
        private void KeepAnySapling(Block block, BlockPos pos)
        {
            int cap = FFConfig.Current.Work.SaplingsPerTree + 2;
            if (saplings.Count >= cap) return;

            ItemStack[] drops = block.GetDrops(entity.World, pos, null);
            if (drops == null) return;

            foreach (ItemStack drop in drops)
            {
                if (drop?.Block is not BlockSapling) continue;
                saplings.Add(drop.Clone());
                if (saplings.Count >= cap) return;
            }
        }

        public override string DebugLabel()
        {
            string baseLabel = base.DebugLabel();
            return saplings.Count > 0 ? baseLabel + " (+" + saplings.Count + " saplings)" : baseLabel;
        }
    }
}
