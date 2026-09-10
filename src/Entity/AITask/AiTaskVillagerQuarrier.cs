using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Works a quarry: cuts rock at the surface and carries the stone home.
    ///
    /// Until this existed the stone pool had almost no inflow. A village could earn wood
    /// from a woodlot, food from a field and earth from a terrace, but stone arrived only
    /// as gravel a digger happened to cut through, which meant the cairn went unrepaired,
    /// stone tools could not be made, and every stone building was out of reach. The
    /// quarry is the plainest possible fix: exposed rock, a pickaxe, and a pile of stone.
    ///
    /// It differs from the digger in the one way that matters. A digger cuts <em>down to</em>
    /// a floor and stops, because the point is a level building site. A quarry cuts
    /// <em>below</em> its floor, to a fixed depth, because the point is the material. When
    /// the pit reaches that depth the plot has nothing left and goes quiet, which is what
    /// the Exhausted state was put there for: a village that keeps siting quarries on
    /// ground it has already emptied is a village with no memory.
    ///
    /// A quarrier needs a pickaxe, and this enforces it. Bare handed they will still
    /// shift loose stone, gravel and the soil on top, because those come up by hand, but
    /// solid rock will not give: the game gates it behind a mining tier and a village
    /// that ignored that gate would be getting its stone for free.
    /// </summary>
    public class AiTaskVillagerQuarrier : AiTaskVillagerWork
    {
        protected override EnumTrade Trade => EnumTrade.Quarrier;

        protected override EnumPlotKind PlotKind => EnumPlotKind.Quarry;

        /// <summary>How far above the sited ground level a quarry still looks for rock.</summary>
        protected override int VerticalSearch => 8;

        public AiTaskVillagerQuarrier(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
            : base(entity, taskConfig, aiConfig) { }

        /// <summary>The lowest the pit may be cut. Below this the plot is worked out.</summary>
        private int FloorY
            => (Plot?.Y ?? 0) - System.Math.Max(1, FFConfig.Current.Work.QuarryDepthBlocks);

        protected override bool IsTarget(Block block, BlockPos pos)
        {
            // A plot sited without a real height reading has no floor, and "everything
            // below you is stone" is not an instruction anyone wants carried out.
            if (Plot == null || Plot.Y <= 1) return false;
            if (pos.Y <= FloorY) return false;

            if (block == null || block.Id == 0) return false;
            if (block.IsLiquid()) return false;

            // Never touch anything with a block entity. That is how a quarry sited a
            // little too close to a storehouse eats it.
            if (block.EntityClass != null) return false;

            string path = block.Code?.Path;
            if (path == null) return false;

            // Natural rock and what lies on top of it. Soil and gravel count because a
            // quarry face is usually under an overburden, and refusing to move it would
            // leave the quarrier standing on a metre of dirt insisting there is no stone
            // here.
            //
            // Worked stone is deliberately absent. Cobblestone, stone brick and slabs are
            // things somebody placed, and a quarry sited a little too close to a wall
            // should not eat the wall.
            bool worth = path.StartsWith("rock")
                      || path.StartsWith("crackedrock")
                      || path.StartsWith("ore-")
                      || path.StartsWith("loose")
                      || path.StartsWith("gravel")
                      || path.StartsWith("sand")
                      || path.StartsWith("soil")
                      || path.StartsWith("forestfloor");

            if (!worth) return false;

            // The mining tier gate. Without it a quarrier with no pickaxe cuts granite,
            // which is free stone and makes the whole tool rack pointless.
            if (!ToolIsGoodEnough(block, pos)) return false;

            // Ore in a quarry face is a windfall, not a reason to refuse the block, so
            // this asks whether the village gets anything at all rather than whether it
            // gets stone specifically.
            return WorthTaking(block, pos);
        }

        protected override bool Work(BlockPos pos)
        {
            // Top down, always. A quarrier who takes the block under an overhang gets a
            // column of gravel on their head and, worse, ends up standing in their own
            // pit with the face above them out of reach.
            BlockPos top = HighestTargetAbove(pos) ?? pos;

            int before = entity.World.BlockAccessor.GetBlock(top)?.Id ?? 0;
            BreakAndCarry(top);
            int after = entity.World.BlockAccessor.GetBlock(top)?.Id ?? 0;

            if (after != before) WearTool();

            // Judged on whether the rock came away, not on whether anything reached the
            // villager's hands. Clearing overburden is real work even though soil is not
            // what the village came here for.
            return after != before;
        }

        private BlockPos HighestTargetAbove(BlockPos pos)
        {
            IBlockAccessor ba = entity.World.BlockAccessor;
            BlockPos best = pos;

            for (int y = pos.Y + 1; y <= pos.Y + VerticalSearch; y++)
            {
                var at = new BlockPos(pos.X, y, pos.Z, 0);
                Block block = ba.GetBlock(at);

                // An air gap ends the column. Carrying on past one is how a quarrier
                // standing at the bottom of a finished pit reaches up through six blocks
                // of nothing and takes a block off the rim, out of arm's reach, with the
                // drops arriving in their hands anyway.
                if (block == null || block.Id == 0) break;
                if (!IsTarget(block, at)) break;
                best = at;
            }

            return best;
        }

        /// <summary>
        /// Finds the working face itself rather than asking the base scan.
        ///
        /// The base scan hangs its search window off GetTerrainMapheightAt, and that is a
        /// world generation height map: breaking a block does not change it. For a
        /// lumberjack or a farmer that is fine, because nobody moves the ground. A quarry
        /// moves the ground on purpose, and the deeper the pit got the further its floor
        /// fell outside a window still anchored to where the hill used to be. The quarry
        /// would report itself worked out with most of its stone still in it.
        ///
        /// So the window here is the plot's own recorded ground level down to its floor,
        /// which are both numbers the village wrote down and neither of which drifts.
        /// </summary>
        protected override BlockPos FindWork(Village village, VillagePlot plot)
        {
            if (plot == null || plot.Y <= 1) return null;

            IBlockAccessor ba = entity.World.BlockAccessor;
            int floor = FloorY;
            int ceiling = plot.Y + VerticalSearch;

            BlockPos best = null;
            double bestDist = double.MaxValue;
            int looked = 0;

            for (int x = plot.MinX; x <= plot.MaxX; x++)
            {
                for (int z = plot.MinZ; z <= plot.MaxZ; z++)
                {
                    // Cheap reject before touching the world: a column that cannot beat
                    // the best so far even at its nearest point is not worth reading.
                    double dx = x + 0.5 - entity.Pos.X;
                    double dz = z + 0.5 - entity.Pos.Z;
                    if (dx * dx + dz * dz >= bestDist) continue;

                    for (int y = ceiling; y > floor; y--)
                    {
                        if (++looked > MaxLookupsPerScan) return best;

                        var at = new BlockPos(x, y, z, 0);
                        if (IsSkipped(at)) continue;

                        Block block = ba.GetBlock(at);
                        if (block == null || block.Id == 0) continue;
                        if (!IsTarget(block, at)) continue;

                        double d = entity.Pos.SquareDistanceTo(at.ToVec3d().Add(0.5, 0, 0.5));
                        if (d < bestDist) { bestDist = d; best = at; }
                        break;   // highest block in this column, and only this column
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// A cap on one scan. An 11x11 quarry six deep is a bit over 1700 columns' worth
        /// of lookups, which is one think, not a stall.
        /// </summary>
        private const int MaxLookupsPerScan = 2400;

        /// <summary>
        /// A quarry cut to its floor is finished, and saying so lets the day clock retire
        /// the plot and free the village to site the next one on ground that still has
        /// something in it.
        /// </summary>
        protected override void OnNothingToDo(Village village, VillagePlot plot)
        {
            base.OnNothingToDo(village, plot);

            if (plot != null && plot.State == EnumPlotState.Active)
            {
                entity.Api.Logger.VerboseDebug(
                    "[F&F] Quarry #{0} is cut out down to y{1} and has given {2:0} stone.",
                    plot.Id, FloorY, plot.LifetimeYield);
            }
        }
    }
}
