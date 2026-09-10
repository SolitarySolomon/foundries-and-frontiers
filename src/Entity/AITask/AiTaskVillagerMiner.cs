using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Works a mine: sinks a spiral stair inside the mine head plot and drives drifts
    /// sideways to any ore within reach of it.
    ///
    /// **Where the ore comes from.** Not from a dice roll. There is no percentage in this
    /// file that decides whether a swing produces copper, because inventing ore out of
    /// nothing is the same trick as a tool appearing in somebody's hand, and this mod has
    /// already refused that once. What is in the ground is what Vintage Story's own world
    /// generation put there. The miner finds real ore blocks, tunnels to them, and breaks
    /// them, and the ratio of ore to stone is whatever the rock under that particular
    /// village actually is. Two mines will not give the same answer, and a village sited
    /// on a poor seam will know about it.
    ///
    /// So the lever on ore output is not a chance value. It is <b>depth</b>, which the
    /// village tier gates, and <b>drift reach</b>, which is how far the miner will tunnel
    /// toward something it has spotted. Both are config. /ff stats counts what actually
    /// came up, which is the honest version of the percentage.
    ///
    /// **Why the stair is drawn rather than emergent.** A miner cannot walk down a
    /// vertical hole and cannot path through solid rock, so a mine that grows by picking
    /// whatever block looks promising gets a villager stuck inside the ground within a
    /// minute. The shaft is therefore a fixed shape: a square spiral around the inside of
    /// the plot, one block down and one block along per step, cut two blocks tall. That
    /// is a staircase the pathfinder already understands, it stays inside the plot the
    /// village claimed, and its depth is a number rather than a hope.
    ///
    /// A drift is the same idea run sideways: a corridor from the stair to the ore, cut
    /// one block at a time from the near end, so the miner is always standing in a
    /// passage that leads back out. It turns square corners rather than running diagonally,
    /// because two blocks touching only at an edge are not a corridor anyone can walk
    /// through, and the pathfinder will refuse the corner even though the blocks are gone.
    ///
    /// Everything is cut three blocks tall. Two is the obvious answer and it is wrong: a
    /// villager stands 1.85 high, so a two block passage that steps down one block per
    /// position always leaves the ceiling of the next step in the way of their head. The
    /// pathfinder cannot see it, because it tests the destination cell rather than the
    /// move, so the miner is handed a route it physically cannot walk and spends two
    /// minutes failing to walk it. Three tall costs one more swing per step and works.
    /// </summary>
    public class AiTaskVillagerMiner : AiTaskVillagerWork
    {
        protected override EnumTrade Trade => EnumTrade.Miner;

        protected override EnumPlotKind PlotKind => EnumPlotKind.MineHead;

        public AiTaskVillagerMiner(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
            : base(entity, taskConfig, aiConfig) { }

        /// <summary>Where the current drift is headed. Kept so a corridor gets finished.</summary>
        private BlockPos driftTarget;

        /// <summary>
        /// Ceiling on what one ore hunt may look at, so a think stays a think.
        ///
        /// At the default drift reach of 10 the box is 21 x 21 x 3, which is 1323 and
        /// never reaches this. It exists for somebody who sets the reach to 30.
        /// </summary>
        private const int MaxOreLookups = 1600;

        // --- the shape of the mine ---------------------------------------------------

        /// <summary>
        /// The lowest block this mine may cut, for this village.
        ///
        /// Two limits, and the stricter wins. Tier says how deep the village has learned
        /// to dig; the absolute floor says how deep is safe at all. Nothing gets to mine
        /// into the lava.
        /// </summary>
        private int FloorY(Village village, VillagePlot plot)
        {
            var cfg = FFConfig.Current.Work;
            int[] byTierTable = cfg.MineDepthByVillageTier;

            // An empty array is a config somebody edited, not an impossible state, and
            // Math.Clamp(0, 0, -1) throws rather than clamping. Every other table in the
            // mod guards this and this one has to as well.
            int depth = 12;
            if (byTierTable != null && byTierTable.Length > 0)
            {
                int tier = Math.Clamp(village?.Tier ?? 0, 0, byTierTable.Length - 1);
                depth = byTierTable[tier];
            }

            return Math.Max(cfg.MineFloorY, plot.Y - Math.Max(4, depth));
        }

        /// <summary>
        /// Step n of the spiral stair, counting down from the mouth.
        ///
        /// The ring is the inside edge of the plot, so the stair hugs the wall and leaves
        /// the middle of the shaft open. One step down per position means the descent is
        /// as gentle as the ring is long: a 5x5 plot gives a sixteen step turn, which is
        /// sixteen blocks of depth per full circle.
        /// </summary>
        private static BlockPos StairStep(VillagePlot plot, int n)
        {
            int minX = plot.MinX + 1, maxX = plot.MaxX - 1;
            int minZ = plot.MinZ + 1, maxZ = plot.MaxZ - 1;

            // A ring needs two blocks in each direction to be a ring. Anything smaller
            // collapses to the same column over and over, which is a vertical hole with
            // a staircase's name on it: the miner cuts it, cannot climb it, and is stuck
            // at the bottom of their own mine. Refuse the plot instead.
            int w = maxX - minX;
            int h = maxZ - minZ;
            if (w < 1 || h < 1) return null;

            int ring = 2 * (w + h);

            int i = n % ring;

            int x, z;
            if (i < w) { x = minX + i; z = minZ; }
            else if (i < w + h) { x = maxX; z = minZ + (i - w); }
            else if (i < w + h + w) { x = maxX - (i - w - h); z = maxZ; }
            else { x = minX; z = maxZ - (i - w - h - w); }

            return new BlockPos(x, plot.Y - n, z, 0);
        }

        // --- deciding what to cut next -----------------------------------------------

        protected override BlockPos FindWork(Village village, VillagePlot plot)
        {
            if (village == null || plot == null || plot.Y <= 1) return null;

            int floor = FloorY(village, plot);
            BlockPos bottom = StairBottom(plot, floor);

            // A drift already under way is finished before anything else. Half a corridor
            // is worse than none: it is a dead end with the ore still behind it.
            BlockPos onward = DriveDrift(plot, bottom, floor);
            if (onward != null) return onward;

            // Nothing in hand, so look for something worth going sideways for.
            driftTarget = FindOre(plot, bottom, floor);
            onward = DriveDrift(plot, bottom, floor);
            if (onward != null) return onward;

            // No ore in reach. Take the stair down another step.
            return NextStairBlock(plot, floor);
        }

        /// <summary>
        /// The deepest step of the stair that has actually been cut.
        ///
        /// Read off the world rather than stored, so a mine survives a reload, a cave in,
        /// and a player filling half of it back in with cobblestone.
        /// </summary>
        private BlockPos StairBottom(VillagePlot plot, int floor)
        {
            IBlockAccessor ba = entity.World.BlockAccessor;
            BlockPos deepest = StairStep(plot, 0);

            for (int n = 0; plot.Y - n > floor; n++)
            {
                BlockPos step = StairStep(plot, n);
                if (step == null) break;

                bool cut = true;
                for (int up = 0; up < PassageHeight && cut; up++)
                {
                    Block at = ba.GetBlock(new BlockPos(step.X, step.Y + up, step.Z, 0));
                    cut = at == null || at.Id == 0;
                }

                if (!cut) break;
                deepest = step;
            }

            return deepest;
        }

        /// <summary>
        /// The next block of the stair that still needs cutting, from the ceiling down.
        ///
        /// Ceiling first matters: cutting the floor out from under a ceiling you have not
        /// removed yet leaves a hole with a lid on it.
        ///
        /// A step that cannot be cut stops the stair rather than being walked past. That
        /// is the difference between a mine that has hit water and knows it, and a mine
        /// that carries on carving a sealed void further down that nobody will ever reach.
        /// </summary>
        private BlockPos NextStairBlock(VillagePlot plot, int floor)
        {
            IBlockAccessor ba = entity.World.BlockAccessor;

            for (int n = 0; plot.Y - n > floor; n++)
            {
                BlockPos step = StairStep(plot, n);
                if (step == null) return null;

                for (int up = PassageHeight - 1; up >= 0; up--)
                {
                    var at = new BlockPos(step.X, step.Y + up, step.Z, 0);
                    Block block = ba.GetBlock(at);

                    if (block == null || block.Id == 0) continue;   // already cut
                    if (!Breakable(block, at)) return Plugged(at);  // water, a chest, a wall
                    if (!ToolIsGoodEnough(block, at)) return null;  // fetch a pickaxe first
                    if (IsSkipped(at)) return null;                 // given up on already

                    return at;
                }
            }

            return null;
        }

        /// <summary>How tall the stair and every drift is cut. See the note on the class.</summary>
        private const int PassageHeight = 3;

        /// <summary>
        /// Says out loud that the shaft has hit something it will not cut through, then
        /// returns nothing so the plot goes idle and is eventually given up rather than
        /// the miner mining a pocket on the far side of it.
        /// </summary>
        private BlockPos Plugged(BlockPos at)
        {
            entity.Api.Logger.VerboseDebug(
                "[F&F] Mine shaft is blocked at {0} and will go no deeper.", at);
            return null;
        }

        // --- drifts -------------------------------------------------------------------

        /// <summary>
        /// The next block of the corridor toward whatever the drift is aimed at.
        ///
        /// The route turns one square corner: all of the X, then all of the Z, at the
        /// stair bottom's own height. Straight line interpolation was the first attempt
        /// and it is wrong, because rounding each axis on its own produces steps that move
        /// diagonally, and two blocks meeting at an edge are not something a 0.6 wide
        /// villager can walk between. The pathfinder refuses the corner, the miner times
        /// out, and the drift dies two blocks in having cost two minutes.
        ///
        /// Cut from the near end, so there is never an open gap between the miner and the
        /// way out. Returns null when the drift is finished, aimed at nothing, or aimed at
        /// something that is no longer there.
        /// </summary>
        private BlockPos DriveDrift(VillagePlot plot, BlockPos bottom, int floor)
        {
            if (driftTarget == null || bottom == null) return null;

            IBlockAccessor ba = entity.World.BlockAccessor;

            Block goal = ba.GetBlock(driftTarget);
            if (goal == null || goal.Id == 0 || !IsOre(goal))
            {
                // Somebody got there first, or it came out on the last trip.
                driftTarget = null;
                return null;
            }

            int y = bottom.Y;
            if (y <= floor) { driftTarget = null; return null; }

            int stepX = Math.Sign(driftTarget.X - bottom.X);
            int stepZ = Math.Sign(driftTarget.Z - bottom.Z);
            var at = new BlockPos(bottom.X, y, bottom.Z, 0);

            // X first, then Z. One axis at a time is what keeps every pair of blocks in
            // the corridor face to face rather than corner to corner.
            for (int guard = 0; guard < MaxCorridorBlocks; guard++)
            {
                if (at.X != driftTarget.X) at.X += stepX;
                else if (at.Z != driftTarget.Z) at.Z += stepZ;
                else break;

                BlockPos here = OpenColumn(at, y);
                if (here == null) continue;                    // already cut through

                if (IsSkipped(here)) { driftTarget = null; return null; }
                return here;
            }

            // The corridor is open all the way. Whatever is left is the ore itself, which
            // sits at the end of it and may be a block above or below the floor of the drift.
            BlockPos face = OpenColumn(driftTarget, driftTarget.Y);
            if (face != null && !IsSkipped(face)) return face;

            driftTarget = null;
            return null;
        }

        /// <summary>
        /// The next uncut block of a passage sized column, ceiling first, or null when the
        /// whole column is already open.
        /// </summary>
        private BlockPos OpenColumn(BlockPos at, int floorY)
        {
            IBlockAccessor ba = entity.World.BlockAccessor;

            for (int up = PassageHeight - 1; up >= 0; up--)
            {
                var probe = new BlockPos(at.X, floorY + up, at.Z, 0);
                Block block = ba.GetBlock(probe);
                if (block == null || block.Id == 0) continue;
                if (!Cuttable(block, probe)) continue;
                return probe;
            }

            return null;
        }

        /// <summary>A drift that has run this far has lost its way. Stop rather than tunnel on.</summary>
        private static int MaxCorridorBlocks => Math.Max(4, FFConfig.Current.Work.MineDriftBlocks * 3);

        /// <summary>
        /// The nearest ore block worth driving a drift to.
        ///
        /// Searched around the bottom of the stair rather than around the villager,
        /// because the stair bottom is where a drift starts from and a miner halfway up
        /// the stair would otherwise keep re-aiming at whatever is beside them.
        ///
        /// Sideways only, and never the column the miner is standing in. Ore straight
        /// down is the stair's business: a drift that descends under its own start is a
        /// one block pit with a villager at the bottom of it who cannot climb out, and
        /// the tether and the sleep task will both fail to get them home for good.
        /// Anything below comes up when the spiral reaches that depth.
        /// </summary>
        private BlockPos FindOre(VillagePlot plot, BlockPos bottom, int floor)
        {
            if (bottom == null) return null;

            IBlockAccessor ba = entity.World.BlockAccessor;
            int reach = Math.Max(2, FFConfig.Current.Work.MineDriftBlocks);

            BlockPos best = null;
            double bestDist = double.MaxValue;
            int looked = 0;
            var probe = new BlockPos(0, 0, 0, 0);

            for (int dy = PassageHeight - 1; dy >= 0; dy--)
            {
                int y = bottom.Y + dy;
                if (y <= floor || y > plot.Y) continue;

                for (int dx = -reach; dx <= reach; dx++)
                {
                    for (int dz = -reach; dz <= reach; dz++)
                    {
                        // The miner's own column. Never a drift target.
                        if (dx == 0 && dz == 0) continue;

                        if (++looked > MaxOreLookups) return best;

                        double d = dx * dx + dz * dz + dy * dy;
                        if (d >= bestDist) continue;

                        probe.Set(bottom.X + dx, y, bottom.Z + dz);
                        Block block = ba.GetBlock(probe);
                        if (!IsOre(block)) continue;
                        if (!Cuttable(block, probe)) continue;
                        if (IsSkipped(probe)) continue;

                        bestDist = d;
                        best = probe.Copy();
                    }
                }
            }

            return best;
        }

        private static bool IsOre(Block block)
        {
            if (block == null || block.Id == 0) return false;
            if (block.BlockMaterial == EnumBlockMaterial.Ore) return true;

            string path = block.Code?.Path;
            return path != null && (path.StartsWith("ore-") || path.Contains("crystalizedore"));
        }

        // --- what may be cut at all ---------------------------------------------------

        /// <summary>
        /// Whether this block may be broken as part of the mine.
        ///
        /// The liquid check is the one that matters. A shaft that breaks into water fills
        /// up (and the pickaxe question is asked separately, above), and a village that keeps sending people down a flooded shaft has invented
        /// a very slow way of drowning its own workforce. Anything touching water or lava
        /// is left alone and the mine simply goes another way.
        /// </summary>
        private bool Cuttable(Block block, BlockPos pos)
            => Breakable(block, pos) && ToolIsGoodEnough(block, pos);

        /// <summary>
        /// Whether this block is rock the mine may safely take, ignoring what the miner
        /// happens to be holding.
        ///
        /// Kept apart from the tool question so the two failures stay distinguishable. A
        /// shaft that has hit water is blocked and the mine should say so; a miner whose
        /// pickaxe just snapped is not blocked, they need a new pickaxe, and reporting
        /// that as a blocked shaft would send somebody looking for an aquifer that is not
        /// there.
        /// </summary>
        private bool Breakable(Block block, BlockPos pos)
        {
            if (block == null || block.Id == 0) return false;
            if (block.IsLiquid()) return false;
            if (block.EntityClass != null) return false;

            // Not everything underground is rock. Cutting through a village's own cellar
            // or a player's chest room would be a bad way to find that out.
            string path = block.Code?.Path;
            if (path == null) return false;

            bool rock = block.BlockMaterial == EnumBlockMaterial.Stone
                     || block.BlockMaterial == EnumBlockMaterial.Ore
                     || block.BlockMaterial == EnumBlockMaterial.Gravel
                     || block.BlockMaterial == EnumBlockMaterial.Sand
                     || block.BlockMaterial == EnumBlockMaterial.Soil
                     || path.StartsWith("rock") || path.StartsWith("stone")
                     || path.StartsWith("ore-") || path.StartsWith("loose");

            if (!rock) return false;

            return !FFConfig.Current.Work.MineAvoidsLiquid || !TouchesLiquid(pos);
        }

        /// <summary>
        /// Water or lava in any of the six neighbours, including the fluid layer, which
        /// is where Vintage Story actually keeps it.
        /// </summary>
        private bool TouchesLiquid(BlockPos pos)
        {
            IBlockAccessor ba = entity.World.BlockAccessor;
            var at = new BlockPos(0, 0, 0, 0);

            for (int i = 0; i < BlockFacing.NumberOfFaces; i++)
            {
                Vec3i n = BlockFacing.ALLFACES[i].Normali;
                at.Set(pos.X + n.X, pos.Y + n.Y, pos.Z + n.Z);

                Block fluid = ba.GetBlock(at, BlockLayersAccess.Fluid);
                if (fluid != null && fluid.Id != 0) return true;

                Block solid = ba.GetBlock(at);
                if (solid != null && solid.IsLiquid()) return true;
            }

            return false;
        }

        // --- the actual swing ----------------------------------------------------------

        /// <summary>Never used: this task decides its own targets. Declared because the base asks.</summary>
        protected override bool IsTarget(Block block, BlockPos pos) => Cuttable(block, pos);

        protected override bool Work(BlockPos pos)
        {
            Block block = entity.World.BlockAccessor.GetBlock(pos);
            bool ore = IsOre(block);

            int before = block?.Id ?? 0;
            BreakAndCarry(pos);
            int after = entity.World.BlockAccessor.GetBlock(pos)?.Id ?? 0;

            bool came = after != before;

            // Counted rather than assumed. This is the mine's real ore to stone ratio and
            // /ff stats is where to read it: no number in config decides it.
            if (came)
            {
                DevStats.Bump(ore ? "mine.ore" : "mine.stone");
                WearTool();
            }

            return came;
        }

        protected override void OnStop(bool cancelled)
        {
            base.OnStop(cancelled);
            driftTarget = null;
        }

        protected override void OnNothingToDo(Village village, VillagePlot plot)
        {
            base.OnNothingToDo(village, plot);

            if (plot != null && plot.State == EnumPlotState.Active && village != null)
            {
                entity.Api.Logger.VerboseDebug(
                    "[F&F] Mine #{0} is down to its floor at y{1} for a tier {2} village.",
                    plot.Id, FloorY(village, plot), village.Tier);
            }
        }
    }
}
