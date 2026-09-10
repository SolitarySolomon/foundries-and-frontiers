using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Fells trees in the village woodlot, gathers the timber, and plants what comes back.
    ///
    /// **The game already knows how to fell a tree, and it is the axe that does it, not
    /// the block.** `ItemAxe.OnBlockBrokenWith` finds the whole tree from the block being
    /// cut and takes it down, respecting the game's own idea of what one tree is, its
    /// felling groups, the reduced drops from leaves and branchy wood, and the axe's
    /// durability. This task had a flood fill of its own for a while, which was a
    /// reimplementation of something already there and would have drifted out of step
    /// with the real rules the first time the game changed them.
    ///
    /// So the lumberjack swings the axe the way a player does, and the reason a villager
    /// with no axe only gets one log is the same reason a player with no axe only gets
    /// one log. That makes the tool matter for a real reason rather than as a speed
    /// multiplier, and it is why villagers are handed one.
    ///
    /// The timber lands on the ground, because that is what the game's felling does, and
    /// the lumberjack then gathers it. That reads better than logs teleporting into
    /// somebody's arms, and it means a player who wanders past a fresh stump finds what
    /// the villager has not picked up yet.
    ///
    /// Replanting does not run on vanilla luck. Leaf drops are rare enough that a woodlot
    /// living off them thins out and never recovers, so a lumberjack keeps a set number of
    /// saplings from each tree they fell. That is a lumberjack's skill, not a change to
    /// the world: a player breaking the same leaves gets exactly what they always did.
    /// </summary>
    public class AiTaskVillagerLumberjack : AiTaskVillagerWork
    {
        protected override EnumTrade Trade => EnumTrade.Lumberjack;

        protected override EnumPlotKind PlotKind => EnumPlotKind.Woodlot;

        /// <summary>Trunks stand on the ground, so there is no point looking far below it.</summary>
        protected override int VerticalSearch => 3;

        /// <summary>
        /// Felling a whole tree is one long action rather than one block's worth of work.
        /// </summary>
        private const float FellTimeMultiplier = 3f;

        protected override float WorkTimeSec => base.WorkTimeSec * FellTimeMultiplier;

        /// <summary>How far from the stump to look for what the tree dropped.</summary>
        private const float GatherRangeBlocks = 7f;

        /// <summary>Ticks spent gathering before giving up on what is left on the ground.</summary>
        private const int GatherAttempts = 4;

        /// <summary>Saplings kept back from the last tree, waiting on a stump to go into.</summary>
        private readonly List<ItemStack> saplings = new List<ItemStack>();

        private BlockPos lastStump;
        private int gathering;

        public AiTaskVillagerLumberjack(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
            : base(entity, taskConfig, aiConfig) { }

        /// <summary>
        /// A trunk block standing on the ground.
        ///
        /// Only the base counts, so a villager walks to the foot of a tree rather than
        /// trying to reach a branch twenty blocks up, and so the axe is swung at the
        /// block the game expects a tree to be felled from.
        /// </summary>
        protected override bool IsTarget(Block block, BlockPos pos)
        {
            if (!IsLog(block)) return false;

            Block below = entity.World.BlockAccessor.GetBlock(pos.DownCopy());
            return !IsLog(below);
        }

        protected override bool Work(BlockPos pos)
        {
            // Still picking up the last tree.
            if (gathering > 0)
            {
                gathering--;
                return Gather(lastStump ?? pos) > 0 || gathering > 0;
            }

            lastStump = pos.Copy();
            saplings.Clear();
            StockSaplingsFor(pos);

            ItemSlot axeSlot = AxeSlot();
            if (axeSlot?.Itemstack?.Collectible is ItemAxe axe)
            {
                var selection = new BlockSelection
                {
                    Position = pos.Copy(),
                    Face = BlockFacing.NORTH,
                    HitPosition = new Vec3d(0.5, 0.5, 0.5)
                };

                // The game's own felling. It breaks the whole tree, damages the axe once
                // per log, and plays the tree-fell sound. It copes with a non-player
                // entity: the player it would have credited simply stays null.
                axe.OnBlockBrokenWith(entity.World, entity, axeSlot, selection);

                // The swing damaged the axe, and may have broken it. That happened on the
                // slot, so the villager's own record of what it is holding has to be told.
                Villager.RefreshHands();

                entity.Api.Logger.VerboseDebug("[F&F] Felled a tree at {0} with an axe.", pos);

                gathering = GatherAttempts;
                Gather(pos);
                return true;
            }

            // No axe, so one log at a time, exactly as it would be for a player. The
            // village will notice its wood coming in slowly, which is the pressure that
            // makes a tool worth making.
            return BreakAndCarry(pos) > 0;
        }

        /// <summary>Puts a sapling back on the stump, once the tree is down and gathered.</summary>
        protected override void AfterWork(BlockPos pos)
        {
            if (gathering > 0) return;
            if (lastStump == null || saplings.Count == 0) return;
            if (entity.World.Rand.NextDouble() > FFConfig.Current.Work.ReplantChance) return;

            IBlockAccessor ba = entity.World.BlockAccessor;

            Block ground = ba.GetBlock(lastStump.DownCopy());
            if (ground == null || ground.Id == 0 || ground.IsLiquid()) return;

            Block sapling = saplings[0].Block;
            if (sapling == null) return;

            Block here = ba.GetBlock(lastStump);
            if (here != null && here.Id != 0 && !here.IsReplacableBy(sapling)) return;

            ba.SetBlock(sapling.BlockId, lastStump);
            saplings.RemoveAt(0);
        }

        /// <summary>
        /// Still gathering counts as unfinished business, so the work loop keeps the
        /// villager at the stump instead of deciding the job is done the moment the block
        /// it sent them to stops being a tree.
        /// </summary>
        protected override bool StillBusyAt(BlockPos pos) => gathering > 0;

        /// <summary>
        /// A woodlot with nothing standing in it is not a dead end, it is a nursery. Put
        /// any saplings still in hand into the ground before standing down.
        /// </summary>
        protected override void OnNothingToDo(Village village, VillagePlot plot)
        {
            base.OnNothingToDo(village, plot);
            if (saplings.Count > 0 && lastStump != null) AfterWork(lastStump);
        }

        // --- the axe -------------------------------------------------------------------

        /// <summary>
        /// The villager's axe, as a slot the game's own code will accept.
        ///
        /// A real slot rather than a dummy, because the axe damages what it is swung with
        /// and a dummy would mean an axe that never wears out.
        /// </summary>
        private ItemSlot AxeSlot()
        {
            ItemSlot slot = entity.RightHandItemSlot;
            return slot?.Itemstack?.Collectible is ItemAxe ? slot : null;
        }

        // --- picking the timber up -------------------------------------------------------

        /// <summary>
        /// Collects what the felled tree left on the ground.
        ///
        /// Only what the village has a use for, and only what will fit in one pair of
        /// hands. Anything else stays where it fell, which is the right outcome: a
        /// villager is not a vacuum cleaner and a player walking past a fresh stump should
        /// find the leftovers.
        /// </summary>
        private int Gather(BlockPos around)
        {
            if (around == null) return 0;

            Entity[] loose = entity.World.GetEntitiesAround(
                around.ToVec3d().Add(0.5, 0.5, 0.5),
                GatherRangeBlocks, GatherRangeBlocks,
                e => e is EntityItem);

            int taken = 0;
            foreach (Entity e in loose)
            {
                if (e is not EntityItem drop) continue;

                ItemStack stack = drop.Slot?.Itemstack;
                if (stack == null) continue;

                int got = Harvest(stack);
                if (got <= 0) continue;

                taken += got;
                stack.StackSize -= got;
                if (stack.StackSize <= 0) drop.Die(EnumDespawnReason.PickedUp);
                else drop.Slot.MarkDirty();

                if (Villager.CarriedCount >= VillagerCarry.CarryCapacity) break;
            }

            if (taken > 0)
            {
                entity.Api.Logger.VerboseDebug("[F&F] Gathered {0} from the ground at {1}", taken, around);
            }
            return taken;
        }

        // --- saplings ---------------------------------------------------------------------

        /// <summary>
        /// Takes the saplings a felled tree is worth, before it comes down.
        ///
        /// This is the lumberjack's skill and nothing else. It creates saplings in their
        /// hands rather than changing what leaves drop, so a player breaking the same
        /// leaves gets exactly the same rare chance they always did.
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

        /// <summary>
        /// A grown trunk, not a placed one.
        ///
        /// The distinction matters more than it looks: log-placed is what a player builds
        /// a cabin out of, and a lumberjack who cannot tell the difference will eventually
        /// dismantle somebody's house and file it as timber.
        /// </summary>
        private static bool IsLog(Block block)
        {
            string path = block?.Code?.Path;
            if (path == null) return false;
            if (path.StartsWith("log-placed")) return false;
            return path.StartsWith("log-grown") || path.StartsWith("logsection");
        }

        public override string DebugLabel()
        {
            string baseLabel = base.DebugLabel();
            if (gathering > 0) return baseLabel + " (gathering)";
            return saplings.Count > 0 ? baseLabel + " (+" + saplings.Count + " saplings)" : baseLabel;
        }
    }
}
