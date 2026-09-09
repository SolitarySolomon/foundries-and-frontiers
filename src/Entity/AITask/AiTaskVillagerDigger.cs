using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Cuts a terrace: levels a patch of ground to one height and keeps the spoil.
    ///
    /// This is the piece that makes the earth pool worth having, and it is the answer to
    /// a question worth writing down because it took two wrong answers to get to. Earth
    /// is not what a village builds its walls out of. Walls are wood and stone. What
    /// earth is for is cob and daub, which the game's own recipes make out of soil and
    /// dry grass, and which is what tier 1 and tier 2 houses are built from.
    ///
    /// So the spoil from levelling a building site is not waste to be carted off. It is
    /// the material the building is made of. A village that works its ground can put up
    /// cob houses before it has found a scrap of clay, and levelling the site and
    /// gathering the material are the same job rather than two.
    ///
    /// It cuts down to the plot's recorded ground level and no further. A digger with no
    /// floor to stop at will happily excavate to bedrock.
    /// </summary>
    public class AiTaskVillagerDigger : AiTaskVillagerWork
    {
        protected override EnumTrade Trade => EnumTrade.Builder;

        protected override EnumPlotKind PlotKind => EnumPlotKind.Terrace;

        /// <summary>
        /// Only ever look above the floor. The whole point is that this never digs below
        /// the level it is cutting to.
        /// </summary>
        protected override int VerticalSearch => 8;

        public AiTaskVillagerDigger(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
            : base(entity, taskConfig, aiConfig) { }

        /// <summary>
        /// Anything standing above the terrace floor that can be dug out by hand.
        ///
        /// Rock is deliberately excluded. Cutting a terrace through a hillside of soil is
        /// a day's work with a shovel; cutting one through granite is a quarry, and that
        /// is a different plot with a different worker.
        /// </summary>
        protected override bool IsTarget(Block block, BlockPos pos)
        {
            // A floor of zero means the plot was sited without a real height reading, and
            // "everything above bedrock is spoil" is not an instruction anyone wants
            // carried out. Refuse rather than dig.
            if (Plot == null || Plot.Y <= 1) return false;
            if (pos.Y <= Plot.Y) return false;
            if (block == null || block.Id == 0) return false;
            if (block.IsLiquid()) return false;

            // Never touch anything the village put there itself, or anything a player
            // built. A digger that levels the storehouse is not a digger.
            if (block.BlockEntityBehaviors != null && block.EntityClass != null) return false;

            string path = block.Code?.Path;
            if (path == null) return false;

            return path.StartsWith("soil") || path.StartsWith("sand") || path.StartsWith("gravel")
                || path.StartsWith("forestfloor") || path.StartsWith("peat")
                || path.StartsWith("muddygravel") || path.StartsWith("clay")
                || path.StartsWith("tallgrass") || path.StartsWith("flower")
                || path.StartsWith("fern") || path.StartsWith("mushroom");
        }

        protected override bool Work(BlockPos pos)
        {
            // Dig from the top down, or the digger stands under an overhang taking the
            // block beneath it and falls into its own hole.
            BlockPos top = HighestDiggableAbove(pos) ?? pos;

            int before = entity.World.BlockAccessor.GetBlock(top)?.Id ?? 0;
            BreakAndCarry(top);
            int after = entity.World.BlockAccessor.GetBlock(top)?.Id ?? 0;

            // Judged on whether the ground came down, not on whether anything went into
            // the villager's hands. Clearing a flower off a building site is real work
            // even though a flower is worth nothing to the village, and treating it as a
            // failure would leave the terrace permanently un-level.
            return after != before;
        }

        /// <summary>
        /// Walks up the column from the target and returns the highest block that still
        /// wants digging, so a column comes down in the order gravity expects.
        /// </summary>
        private BlockPos HighestDiggableAbove(BlockPos pos)
        {
            IBlockAccessor ba = entity.World.BlockAccessor;
            BlockPos best = pos;

            for (int y = pos.Y + 1; y <= pos.Y + VerticalSearch; y++)
            {
                var at = new BlockPos(pos.X, y, pos.Z, 0);
                Block block = ba.GetBlock(at);
                if (block == null || block.Id == 0) continue;
                if (!IsTarget(block, at)) break;
                best = at;
            }

            return best;
        }

        /// <summary>
        /// A terrace that has been cut flat is finished, and saying so is the point: the
        /// plot goes quiet, the day clock eventually calls it exhausted, and the village
        /// is free to site the next one somewhere it still has a slope.
        /// </summary>
        protected override void OnNothingToDo(Village village, VillagePlot plot)
        {
            base.OnNothingToDo(village, plot);

            if (plot != null && plot.State == EnumPlotState.Active)
            {
                entity.Api.Logger.VerboseDebug(
                    "[F&F] Terrace #{0} is level at y{1}.", plot.Id, plot.Y);
            }
        }
    }
}
