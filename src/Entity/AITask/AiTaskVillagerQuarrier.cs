using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Works a quarry: cuts one stepped pit, then keeps cutting stone out of its face for
    /// as long as the village wants stone.
    ///
    /// Until this job existed the stone pool had almost no inflow. A village earned wood
    /// from a woodlot, food from a field and earth from a terrace, but stone arrived only
    /// as gravel a digger happened to cut through on the way to something else, which
    /// meant the cairn went unrepaired, stone tools could not be made and every stone
    /// building was out of reach, in a village that looked like it was working.
    ///
    /// **It does not eat the countryside.** The first version cut down and kept going
    /// until the plot was worked out, which made a village a slow-moving hole. This one
    /// cuts a single stepped pit inside the plot and then stops digging. What the village
    /// gets after that comes off the face, and the face does not run out: a quarry is a
    /// place a village goes for stone, not a resource counter that empties.
    ///
    /// So a quarry disfigures exactly its own plot and nothing else, forever, which is
    /// what a quarry is supposed to look like.
    ///
    /// A pickaxe is required and gets worn down. Bare handed a quarrier will still shift
    /// the soil and gravel on top, because that comes up by hand, but the rock will not
    /// give: the game gates it behind a mining tier and a village that walked past that
    /// gate would be getting its stone for free.
    /// </summary>
    public class AiTaskVillagerQuarrier : AiTaskVillagerWorkings
    {
        protected override EnumTrade Trade => EnumTrade.Quarrier;

        protected override EnumPlotKind PlotKind => EnumPlotKind.Quarry;

        protected override int WantedDepth => FFConfig.Current.Work.QuarryDepthBlocks;

        public AiTaskVillagerQuarrier(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
            : base(entity, taskConfig, aiConfig) { }

        /// <summary>
        /// A turn at the face gives whatever the rock behind it gives.
        ///
        /// Read off the block rather than named, so a granite quarry gives granite, a
        /// chalk one gives chalk, and a rock type from another mod gives whatever that
        /// mod says it gives. The block itself is not broken: the face stays a face.
        /// </summary>
        protected override ItemStack YieldAtFace(Village village, VillagePlot plot)
        {
            BlockPos face = FacePos(plot);
            if (face == null) return null;

            // The wall of the pit, at the height a person swings at.
            Block rock = FaceRock(face);
            if (rock == null) return null;

            ItemStack sample = SampleDropOf(rock, face);
            if (sample == null) return null;

            int per = System.Math.Max(1, FFConfig.Current.Work.QuarryStonePerTurn);
            sample.StackSize = System.Math.Max(1, sample.StackSize) * per;
            return sample;
        }

        /// <summary>
        /// What the face is actually made of. The block under the worker's feet first,
        /// since that is the pit floor and therefore the seam being worked, and the four
        /// walls after it in case the floor turned out to be gravel.
        /// </summary>
        private Block FaceRock(BlockPos face)
        {
            IBlockAccessor ba = entity.World.BlockAccessor;

            Block below = ba.GetBlock(face.DownCopy());
            if (IsRock(below)) return below;

            for (int i = 0; i < BlockFacing.HORIZONTALS.Length; i++)
            {
                Vec3i n = BlockFacing.HORIZONTALS[i].Normali;
                Block side = ba.GetBlock(new BlockPos(face.X + n.X, face.Y + n.Y, face.Z + n.Z, 0));
                if (IsRock(side)) return side;
            }

            // No rock in the pit at all. A quarry sited on a sandbank is a village's own
            // mistake to notice, and it should give nothing rather than give soil.
            return below != null && below.BlockMaterial == EnumBlockMaterial.Gravel ? below : null;
        }

        private static bool IsRock(Block block)
        {
            if (block == null || block.Id == 0) return false;
            if (block.BlockMaterial != EnumBlockMaterial.Stone) return false;

            // Worked stone is not a quarry face, it is somebody's wall.
            string path = block.Code?.Path ?? "";
            return !path.StartsWith("stonebrick") && !path.StartsWith("cobblestone")
                && !path.StartsWith("polishedrock") && !path.StartsWith("drystone");
        }

        /// <summary>
        /// A quarry does not report itself finished, because it is not the kind of thing
        /// that finishes. Only a quarry with no rock in it at all goes quiet, and that is
        /// a siting mistake worth hearing about.
        /// </summary>
        protected override void OnNothingToDo(Village village, VillagePlot plot)
        {
            base.OnNothingToDo(village, plot);

            if (plot != null && plot.State == EnumPlotState.Active)
            {
                entity.Api.Logger.VerboseDebug(
                    "[F&F] Quarry #{0} has no face worth working. Sited on the wrong ground?",
                    plot.Id);
            }
        }
    }
}
