using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Shared behaviour for a quarry and a mine: open the workings once, then work the
    /// face for as long as the village wants the material.
    ///
    /// **Why this is not a hole that keeps growing.** The first version of both jobs took
    /// the literal route: real blocks, broken one after another, for as long as there was
    /// rock. It produced a village slowly eating the landscape, and for the mine it meant
    /// villagers consuming the ore seams a player might want to work themselves. That is
    /// the wrong trade. Ground is shared with the player and a village should not swallow
    /// it, so the workings are cut once to a fixed, deliberate shape and after that the
    /// worker stands at the face and works it.
    ///
    /// So the excavation is bounded and the production is abstract, and that split is the
    /// rule rather than an excuse: **the shape of a plot is the village's business and
    /// the world outside it is the player's.** A quarry looks like a quarry, a mine has a
    /// mouth, and neither one keeps chewing.
    ///
    /// **The shape.** A stepped pit: deepest at the centre, one block shallower per ring
    /// outward, so it terraces up to ground level all the way round. That is what a real
    /// quarry looks like, and it matters mechanically too, because a single block step is
    /// something a villager can walk up and down. A sheer pit is a trap.
    ///
    /// The footprint is set by the depth rather than by the plot, so a deep working is a
    /// wider one and neither can spread past the plot's own bounds.
    /// </summary>
    public abstract class AiTaskVillagerWorkings : AiTaskVillagerWork
    {
        protected AiTaskVillagerWorkings(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
            : base(entity, taskConfig, aiConfig) { }

        /// <summary>
        /// How deep the middle of the workings is cut, below the plot's ground level, as
        /// the job would like it.
        /// </summary>
        protected abstract int WantedDepth { get; }

        /// <summary>
        /// How deep it is actually cut.
        ///
        /// The pit terraces up one block per ring, so a pit D deep needs D rings of room
        /// to reach ground level again. A plot narrower than that gets a sheer wall at its
        /// boundary and a worker who cannot climb out of their own workings, which is the
        /// exact trap the stepping exists to avoid. So the plot has the last word.
        /// </summary>
        protected int Depth
        {
            get
            {
                int wanted = Math.Max(1, WantedDepth);
                VillagePlot plot = Plot;
                if (plot == null) return wanted;

                int room = 1 + Math.Min((plot.Width - 1) / 2, (plot.Length - 1) / 2);
                return Math.Clamp(wanted, 1, Math.Max(1, room));
            }
        }

        /// <summary>What one turn at the face gives the village. Null means nothing this time.</summary>
        protected abstract ItemStack YieldAtFace(Village village, VillagePlot plot);

        /// <summary>How far above the plot's ground level overburden is still cleared.</summary>
        protected virtual int Overburden => 6;

        /// <summary>A cap on one scan of the workings, so a think stays a think.</summary>
        private const int MaxLookupsPerScan = 2600;

        /// <summary>
        /// Whether the face is open and standable, as of the last time a target was chosen.
        ///
        /// Cached rather than asked, because both the swing guard and the swing itself
        /// need the answer and each ask would be another walk of every column in the plot.
        /// It is refreshed in FindWork, which runs before every trip to a target, so it
        /// can never be more than one target out of date.
        ///
        /// **It is not the same question as "is there anything left to cut".** That was
        /// the first version and it was a free-stone hole: a worker whose pickaxe is too
        /// crude for the rock finds nothing it is allowed to cut, which looked exactly
        /// like a finished pit, so it stood on top of the untouched ground and produced
        /// stone out of the rock beneath its feet forever without ever breaking a block or
        /// wearing the tool. The face has to actually be a hole in the ground.
        /// </summary>
        private bool faceReady;

        // --- the shape of the workings ------------------------------------------------

        /// <summary>
        /// How deep the column at (x, z) is cut. Zero for ground the workings never touch.
        ///
        /// Chebyshev rings, so the pit is square and every ring is exactly one block
        /// shallower than the one inside it. A villager can walk any of those steps.
        /// </summary>
        protected int CutDepthAt(VillagePlot plot, int x, int z)
        {
            int ring = Math.Max(Math.Abs(x - plot.CentreX), Math.Abs(z - plot.CentreZ));
            return Math.Max(0, Depth - ring);
        }

        /// <summary>The floor of the column at (x, z), which is ground level where untouched.</summary>
        protected int FloorAt(VillagePlot plot, int x, int z) => plot.Y - CutDepthAt(plot, x, z);

        /// <summary>
        /// Where the worker stands: the bottom of the pit, which is also the face.
        /// </summary>
        protected BlockPos FacePos(VillagePlot plot)
            => plot == null ? null
             : new BlockPos(plot.CentreX, plot.Y - Depth + 1, plot.CentreZ, 0);

        // --- deciding what to do next --------------------------------------------------

        protected override BlockPos FindWork(Village village, VillagePlot plot)
        {
            if (plot == null || plot.Y <= 1) return null;

            // Cutting comes first. A face that is not open yet cannot be worked.
            faceReady = false;

            BlockPos cut = NextCutBlock(plot);
            if (cut != null) return cut;

            BlockPos face = FacePos(plot);
            if (face == null || IsSkipped(face)) return null;

            if (!FaceIsOpen(plot, face))
            {
                // Nothing left that this worker may cut, and the face is still in the
                // ground. Something is in the way that the job will not touch: rock above
                // the pickaxe it is carrying, water, a seam of ore it is leaving alone, or
                // a wall somebody built. Say so rather than stand on the roof of the pit
                // pretending to work it.
                NoteBlockedFace(plot, face);
                return null;
            }

            faceReady = true;
            Prepare(village, plot);
            return face;
        }

        /// <summary>
        /// Whether the face is a hole a person can stand in: air at head height, air at
        /// foot height, and something solid underfoot.
        ///
        /// Two reads and a third, against a scan of the whole plot. Cheap enough to be the
        /// thing that is actually trusted.
        /// </summary>
        private bool FaceIsOpen(VillagePlot plot, BlockPos face)
        {
            IBlockAccessor ba = entity.World.BlockAccessor;

            Block at = ba.GetBlock(face);
            if (at != null && at.Id != 0 && !at.IsLiquid()) return false;

            Block head = ba.GetBlock(face.UpCopy());
            if (head != null && head.Id != 0 && !head.IsLiquid()) return false;

            Block under = ba.GetBlock(face.DownCopy());
            return under != null && under.Id != 0;
        }

        private double nextBlockedNoteAt;

        private void NoteBlockedFace(VillagePlot plot, BlockPos face)
        {
            if (Now < nextBlockedNoteAt) return;
            nextBlockedNoteAt = Now + 120;

            Block at = entity.World.BlockAccessor.GetBlock(face);
            entity.Api.Logger.Notification(
                "[F&F] {0} cannot open the face of plot #{1}: {2} is in the way and will not be cut. "
                + "Usually the rock needs a better pickaxe than tier {3}.",
                Label(), plot.Id, at?.Code?.ToString() ?? "something", Villager?.ToolTier ?? 0);
        }

        /// <summary>
        /// Anything the job wants done once, the first time the face is ready. The mine
        /// surveys its seam here. Called on every trip, so it must be cheap when there is
        /// nothing to do.
        /// </summary>
        protected virtual void Prepare(Village village, VillagePlot plot) { }

        /// <summary>
        /// The highest block still standing above where the workings want the ground.
        ///
        /// Top down, always. Taking the block under an overhang drops a column of gravel
        /// on somebody's head and leaves them standing in a hole with the rest of the
        /// face out of reach.
        /// </summary>
        private BlockPos NextCutBlock(VillagePlot plot)
        {
            IBlockAccessor ba = entity.World.BlockAccessor;
            int looked = 0;

            BlockPos best = null;
            double bestDist = double.MaxValue;

            for (int x = plot.MinX; x <= plot.MaxX; x++)
            {
                for (int z = plot.MinZ; z <= plot.MaxZ; z++)
                {
                    if (CutDepthAt(plot, x, z) <= 0) continue;

                    double dx = x + 0.5 - entity.Pos.X;
                    double dz = z + 0.5 - entity.Pos.Z;
                    if (dx * dx + dz * dz >= bestDist) continue;

                    int floor = FloorAt(plot, x, z);

                    for (int y = plot.Y + Overburden; y > floor; y--)
                    {
                        if (++looked > MaxLookupsPerScan) return best;

                        var at = new BlockPos(x, y, z, 0);
                        Block block = ba.GetBlock(at);
                        if (block == null || block.Id == 0) continue;

                        // IsCuttable, never IsTarget. IsTarget has to answer for the face
                        // as well, and the face sits in the centre column of this very
                        // scan, so asking it here is a straight recursion into a stack
                        // overflow the first time a worker looks at a finished pit.
                        if (!IsCuttable(block, at)) break;
                        if (IsSkipped(at)) break;

                        double d = entity.Pos.SquareDistanceTo(at.ToVec3d().Add(0.5, 0, 0.5));
                        if (d < bestDist) { bestDist = d; best = at; }
                        break;
                    }
                }
            }

            return best;
        }

        // --- doing it -------------------------------------------------------------------

        protected override bool Work(BlockPos pos)
        {
            Village village = Home;
            BlockPos face = FacePos(Plot);

            // Standing at the face of a finished working. This is the produce step.
            if (face != null && faceReady && pos.Equals(face))
            {
                ItemStack got = YieldAtFace(village, Plot);
                if (got == null) return false;

                // Asked before producing, because what a face gives is not decided until
                // the swing lands and the base cannot check it in advance the way it does
                // for a block. A miner holding stone who has just struck ore should carry
                // the stone home, not drop the ore on the floor.
                if (Villager.IsCarrying && !Villager.CarriedStack.Satisfies(got))
                {
                    DeferForFullHands = true;
                    return false;
                }

                // Counted rather than assumed. TryCarry reports what is in the hands
                // afterwards rather than what it took, so full hands look like success
                // unless the difference is measured.
                int held = Villager.CarriedCount;
                int wanted = got.StackSize;
                Harvest(got);
                int gained = Villager.CarriedCount - held;

                if (gained <= 0)
                {
                    DeferForFullHands = true;
                    return false;
                }

                // What would not fit falls at the face rather than ceasing to exist. The
                // same thing breaking a block does with a load too big for one pair of
                // hands, and it means a player can walk into a working quarry and find
                // the overspill on the floor.
                if (gained < wanted)
                {
                    ItemStack spill = got.Clone();
                    spill.StackSize = wanted - gained;
                    entity.World.SpawnItemEntity(spill, face.ToVec3d().Add(0.5, 0.5, 0.5));
                }

                WearTool();
                return true;
            }

            int before = entity.World.BlockAccessor.GetBlock(pos)?.Id ?? 0;
            BreakAndCarry(pos);
            int after = entity.World.BlockAccessor.GetBlock(pos)?.Id ?? 0;

            bool came = after != before;
            if (came) WearTool();
            return came;
        }

        /// <summary>
        /// A shift at the face costs a full turn of work, not the breath between two
        /// blocks. The base charges the working time on arrival, and a worker who never
        /// goes anywhere would otherwise never pay it again.
        /// </summary>
        protected override float PauseAfter(BlockPos done)
        {
            BlockPos face = FacePos(Plot);
            return face != null && done != null && done.Equals(face)
                ? WorkTimeSec
                : base.PauseAfter(done);
        }

        /// <summary>
        /// Stay at the face until both hands are full, then go and empty them.
        ///
        /// Without the carry check this returns true forever and the worker never hauls,
        /// which reads as a villager standing in a pit swinging at nothing while the
        /// storehouse stays empty.
        /// </summary>
        protected override bool StillBusyAt(BlockPos pos)
        {
            BlockPos face = FacePos(Plot);
            if (face == null || !pos.Equals(face)) return false;

            return Villager != null && Villager.CarriedCount < HaulThreshold;
        }

        /// <summary>
        /// The base checks this before every swing, and at a finished face the block being
        /// worked is air, so the face has to answer for itself.
        /// </summary>
        protected override bool IsTarget(Block block, BlockPos pos)
        {
            if (Plot == null || Plot.Y <= 1) return false;

            BlockPos face = FacePos(Plot);
            if (face != null && faceReady && pos.Equals(face)) return true;

            return IsCuttable(block, pos);
        }

        /// <summary>Whether this block is part of the ground the workings are shaping.</summary>
        protected virtual bool IsCuttable(Block block, BlockPos pos)
        {
            if (block == null || block.Id == 0) return false;
            if (block.IsLiquid()) return false;

            // Never anything with a block entity behind it. That is how a working sited a
            // shade too close to a storehouse eats the storehouse.
            if (block.EntityClass != null) return false;

            string path = block.Code?.Path;
            if (path == null) return false;

            bool ground = block.BlockMaterial == EnumBlockMaterial.Stone
                       || block.BlockMaterial == EnumBlockMaterial.Gravel
                       || block.BlockMaterial == EnumBlockMaterial.Sand
                       || block.BlockMaterial == EnumBlockMaterial.Soil
                       || block.BlockMaterial == EnumBlockMaterial.Plant
                       || block.BlockMaterial == EnumBlockMaterial.Leaves
                       || path.StartsWith("rock") || path.StartsWith("forestfloor")
                       || path.StartsWith("loose");

            // Ore in the way of the cut is left standing. It belongs to whoever comes
            // down here with a pickaxe of their own.
            if (block.BlockMaterial == EnumBlockMaterial.Ore) return false;
            if (path.StartsWith("ore-")) return false;

            if (!ground) return false;

            // Worked stone is something somebody placed. A quarry does not eat walls.
            if (path.StartsWith("stonebrick") || path.StartsWith("cobblestone")
                || path.StartsWith("polishedrock") || path.StartsWith("drystone")) return false;

            return ToolIsGoodEnough(block, pos);
        }

        /// <summary>
        /// What a block gives without breaking it, used to work out what a face produces.
        ///
        /// Asking the block itself rather than naming an item keeps this working for
        /// modded rock, modded ore and any rock type the game adds later.
        /// </summary>
        protected ItemStack SampleDropOf(Block block, BlockPos pos)
        {
            if (block == null || block.Id == 0) return null;

            ItemStack[] drops = block.GetDrops(entity.World, pos, null);
            if (drops == null) return null;

            var table = Sapi?.ModLoader.GetModSystem<ResourceTable>();

            foreach (ItemStack drop in drops)
            {
                if (drop == null || drop.StackSize <= 0) continue;
                if (table != null && table.Classify(drop) == null) continue;
                return drop;
            }

            return null;
        }
    }
}
