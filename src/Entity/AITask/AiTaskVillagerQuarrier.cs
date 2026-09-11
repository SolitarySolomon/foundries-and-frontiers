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
    /// **What it gives comes from what the pit is cut through.** Once the pit is open
    /// the quarrier surveys its own walls and floor and writes down how much of it turned
    /// out to be soil and gravel rather than rock. A quarry cut into a bare rock face is
    /// nearly all stone; one cut through a metre of topsoil gives earth alongside it,
    /// which is worth having, because earth is what cob is made of and cob is what the
    /// first houses are. Same principle as the mine's seam: the working gives what is
    /// actually in it.
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
            if (face == null || plot == null) return null;

            int per = System.Math.Max(1, FFConfig.Current.Work.QuarryStonePerTurn);

            // Overburden, at the share the survey found in this pit's own walls.
            bool wantsEarth = plot.EarthShare > 0
                           && entity.World.Rand.NextDouble() < plot.EarthShare;

            if (wantsEarth)
            {
                ItemStack soil = SoilInTheWalls(plot, face);
                if (soil != null) return Per(soil, per);
            }

            // The bed and walls of the pit, at the height a person swings at.
            Block rock = FaceRock(face);
            if (rock != null)
            {
                ItemStack sample = SampleDropOf(rock, face);
                if (sample != null) return Per(sample, per);
            }

            // No rock in this pit at all. A quarry cut into a sandbank is still a working:
            // giving nothing here would have the base treat the face as unworkable and
            // blacklist it for the best part of a minute, over and over, on exactly the
            // soft ground this survey exists to make useful.
            if (!wantsEarth)
            {
                ItemStack soil = SoilInTheWalls(plot, face);
                if (soil != null) return Per(soil, per);
            }

            return null;
        }

        /// <summary>
        /// A shift's worth, rather than one block's worth. A turn at the face is a stretch
        /// of work, and both what it is cut out of and what it gives should scale the same
        /// way, or earth quietly pays half what stone does.
        /// </summary>
        private static ItemStack Per(ItemStack stack, int per)
        {
            if (stack == null) return null;
            stack.StackSize = System.Math.Max(1, stack.StackSize) * System.Math.Max(1, per);
            return stack;
        }

        /// <summary>
        /// Surveys the pit the quarrier has just finished cutting, and writes down how
        /// much of it was overburden.
        ///
        /// Counted over the ground the pit was actually cut out of, from the plot's own
        /// level down to the pit floor, which is exactly the material a quarrier is
        /// standing in. Nothing is broken and the count is only of what is still there,
        /// so a pit cut through deep soil keeps giving earth for as long as it is worked.
        /// </summary>
        protected override void Prepare(Village village, VillagePlot plot)
        {
            if (plot == null || plot.EarthShare >= 0) return;
            if (Now < nextSurveyAt) return;

            IBlockAccessor ba = entity.World.BlockAccessor;
            int rock = 0, earth = 0;
            var soils = new System.Collections.Generic.Dictionary<string, int>();
            var pick = new System.Collections.Generic.Dictionary<string, string>();

            // Read the ground the pit did NOT touch, over the range of heights the pit
            // was cut through. The undisturbed column beside the hole is exactly what the
            // hole was made of.
            //
            // Reading the cut columns is the trap, and the first version fell in it. A cut
            // column is air down to its floor and undisturbed bed below that, so sampling
            // it measures what the pit is standing on rather than what came off it, and
            // calls every quarry pure rock however much topsoil was moved.
            int bottom = plot.Y - Depth;
            bool anyUntouched = false;

            for (int x = plot.MinX; x <= plot.MaxX; x++)
            {
                for (int z = plot.MinZ; z <= plot.MaxZ; z++)
                {
                    if (CutDepthAt(plot, x, z) > 0) continue;
                    anyUntouched = true;

                    for (int y = plot.Y; y > bottom; y--)
                    {
                        Block block = ba.GetBlock(new BlockPos(x, y, z, 0));
                        if (block == null || block.Id == 0) continue;

                        switch (block.BlockMaterial)
                        {
                            case EnumBlockMaterial.Stone:
                            case EnumBlockMaterial.Ore:
                                rock++;
                                break;
                            case EnumBlockMaterial.Soil:
                            case EnumBlockMaterial.Sand:
                            case EnumBlockMaterial.Gravel:
                                earth++;

                                // Keyed on material and grade rather than on the exact
                                // variant, because soil-medium-normal, -sparse and -none
                                // are one material wearing three coats of grass and all
                                // drop the same thing. Counting them separately let a
                                // minority of gravel out-vote a majority of soil.
                                string key = Grouped(block.Code);
                                soils[key] = soils.TryGetValue(key, out int n) ? n + 1 : 1;
                                if (!pick.ContainsKey(key)) pick[key] = block.Code.ToString();
                                break;
                        }
                    }
                }
            }

            if (rock + earth <= 0)
            {
                // Nothing read at all. Either the chunks are not loaded, or the pit was
                // configured deep enough to swallow its whole plot and there is no
                // untouched ground left to read. Writing a zero down would fix the quarry
                // at pure stone for good, so refuse it, say so, and leave it a while.
                nextSurveyAt = Now + FailedSurveyRetrySec;
                entity.Api.Logger.Notification(
                    "[F&F] Could not size up quarry #{0}: {1}. Will look again.",
                    plot.Id,
                    anyUntouched
                        ? "read no ground at all, chunks probably not loaded"
                        : "the pit fills its whole plot, so there is no undug ground to compare it to");
                return;
            }

            plot.EarthShare = earth / (float)(rock + earth);

            // Which soil, remembered now rather than hunted for on every swing. The walls
            // of a finished pit are steps and the block beside a worker's feet is as often
            // the bed as the overburden, so looking it up live gave the wrong answer half
            // the time and cost a scan to do it.
            string bestKey = null;
            int bestCount = 0;
            foreach (var kv in soils)
            {
                if (kv.Value > bestCount) { bestCount = kv.Value; bestKey = kv.Key; }
            }

            string best = bestKey != null && pick.TryGetValue(bestKey, out string code) ? code : null;
            plot.OverburdenBlock = best;

            entity.Api.Logger.Notification(
                "[F&F] {0} sized up quarry #{1}: {2:0}% of it is overburden{3}.",
                Label(), plot.Id, plot.EarthShare * 100f,
                best == null ? "" : ", mostly " + best);
        }

        /// <summary>
        /// What the face is actually made of. The block under the worker's feet first,
        /// since that is the pit floor and therefore the bed being worked, and the four
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
            // mistake to notice, and gravel is the most it should give.
            return below != null && below.BlockMaterial == EnumBlockMaterial.Gravel ? below : null;
        }

        /// <summary>
        /// Material and grade, dropping whatever comes after: soil-medium-normal and
        /// soil-medium-none group together, gravel-granite stays its own thing.
        /// </summary>
        private static string Grouped(AssetLocation code)
        {
            string path = code?.Path ?? "";
            int first = path.IndexOf('-');
            if (first < 0) return path;

            int second = path.IndexOf('-', first + 1);
            return second < 0 ? path : path.Substring(0, second);
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

        /// <summary>How long to leave a survey that read nothing before trying it again.</summary>
        private const double FailedSurveyRetrySec = 120;

        private double nextSurveyAt;

        /// <summary>
        /// The overburden the survey found, sampled for its drop.
        /// </summary>
        private ItemStack SoilInTheWalls(VillagePlot plot, BlockPos face)
        {
            if (plot?.OverburdenBlock == null) return null;

            Block soil = entity.World.GetBlock(new AssetLocation(plot.OverburdenBlock));
            return SampleDropOf(soil, face);
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
