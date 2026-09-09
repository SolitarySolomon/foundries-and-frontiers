using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Works the village field: reaps what is ripe, tills bare ground, sows what the
    /// village can afford, and relays poor soil once the village learns to make better.
    ///
    /// The order matters and is not the order those things appear in a farming tutorial.
    /// Reaping comes first because a ripe crop left standing is the only one of these
    /// that can be lost. Then relaying, because upgrading ground the village has outgrown
    /// is the thing that makes a field visibly improve as a settlement climbs. Then
    /// tilling, then sowing.
    ///
    /// Every block code this touches is looked up through the world catalogue rather than
    /// written here, because farmland variants and crop names are exactly the sort of
    /// thing that changes under you between game versions.
    /// </summary>
    public class AiTaskVillagerFarmer : AiTaskVillagerWork
    {
        protected override EnumTrade Trade => EnumTrade.Farmer;

        protected override EnumPlotKind PlotKind => EnumPlotKind.Field;

        /// <summary>Fields are the surface. Nothing worth farming is six blocks down.</summary>
        protected override int VerticalSearch => 2;

        private WorldCatalogue catalogue;

        public AiTaskVillagerFarmer(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
            : base(entity, taskConfig, aiConfig) { }

        private WorldCatalogue Catalogue
            => catalogue ??= entity.Api.ModLoader.GetModSystem<WorldCatalogue>();

        protected override bool IsTarget(Block block, BlockPos pos)
        {
            if (block == null || block.Id == 0) return false;

            if (WorldCatalogue.IsRipeCrop(entity.World, block, pos)) return true;
            if (NeedsRelaying(block)) return true;
            if (NeedsTilling(block, pos)) return true;
            if (NeedsSowing(block, pos)) return true;

            return false;
        }

        protected override bool Work(BlockPos pos)
        {
            Block block = entity.World.BlockAccessor.GetBlock(pos);
            if (block == null || block.Id == 0) return false;

            if (WorldCatalogue.IsRipeCrop(entity.World, block, pos)) return Reap(pos);
            if (NeedsRelaying(block)) return Relay(pos, block);
            if (NeedsTilling(block, pos)) return Till(pos);
            if (NeedsSowing(block, pos)) return Sow(pos);

            return false;
        }

        // --- reaping -----------------------------------------------------------------

        private bool Reap(BlockPos pos)
        {
            int taken = BreakAndCarry(pos);

            // A field that is reaped and never sown is a field that empties. The next
            // pass over the plot will find bare farmland and put something in it.
            return taken > 0;
        }

        // --- relaying ----------------------------------------------------------------

        /// <summary>
        /// Farmland the village has outgrown. This is the soil grade ladder made visible:
        /// a hamlet lays very poor ground, and the same field is rich by the time the
        /// place is a town.
        /// </summary>
        private bool NeedsRelaying(Block block)
        {
            if (block is not BlockFarmland) return false;

            Village village = Home;
            if (village == null) return false;

            int have = WorldCatalogue.GradeOfFarmland(block);
            int want = WorldCatalogue.GradeForTier(village.Tier);
            if (have < 0 || have >= want) return false;

            // Only if the village can actually pay for the better soil. Ground does not
            // improve because somebody wished it would.
            return village.Ledger.Get(EnumVillageResource.Earth) >= RelayCost;
        }

        private bool Relay(BlockPos pos, Block old)
        {
            Village village = Home;
            if (village == null) return false;

            Block better = Catalogue?.FarmlandFor(village.Tier);
            Block soil = Catalogue?.SoilFor(village.Tier);
            if (better == null || soil == null || better.BlockId == old.BlockId) return false;

            if (!village.Ledger.Withdraw(EnumVillageResource.Earth, RelayCost)) return false;

            // Carry the old plot's state across rather than starting it from nothing.
            //
            // Replacing the block replaces the block entity with it, and everything a
            // field has earned lives on that block entity: how wet it is, how much of
            // each nutrient it holds, how long since it was last watered. Swapping it out
            // blind means paying to make an established field worse, which is the exact
            // opposite of what relaying is for.
            var carried = new TreeAttribute();
            if (entity.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityFarmland was)
            {
                was.ToTreeAttributes(carried);
            }

            // The crop, if any, is the block above and is not touched by this.
            entity.World.BlockAccessor.SetBlock(better.BlockId, pos);

            if (entity.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityFarmland now)
            {
                // Restore what it had, then tell it which soil it is made of now. That
                // second call is what raises the fertility it recovers toward, so the
                // field keeps its moisture and its nutrients and gains a better ceiling.
                now.FromTreeAttributes(carried, entity.World);
                now.OnCreatedFromSoil(soil, carried);
                now.MarkDirty(true);
            }

            return true;
        }

        /// <summary>Earth spent laying one block of better ground.</summary>
        private static float RelayCost => 2f;

        // --- tilling -----------------------------------------------------------------

        /// <summary>
        /// Bare soil with sky above it, inside the field. Not grass, not gravel: the
        /// game's own soil block, because that is what farmland is made from.
        /// </summary>
        private bool NeedsTilling(Block block, BlockPos pos)
        {
            if (block is not BlockSoil) return false;
            if (Catalogue?.FarmlandFor(Home?.Tier ?? 0) == null) return false;

            Block above = entity.World.BlockAccessor.GetBlock(pos.UpCopy());
            return above == null || above.Id == 0 || above.IsReplacableBy(block);
        }

        private bool Till(BlockPos pos)
        {
            Village village = Home;
            Block farmland = Catalogue?.FarmlandFor(village?.Tier ?? 0);
            if (farmland == null) return false;

            // The soil actually being turned over, before it is replaced. Farmland takes
            // its nutrient levels from the soil it was made from, so this is the thing
            // that decides whether the field grows anything.
            Block soil = entity.World.BlockAccessor.GetBlock(pos);

            // Clear whatever is standing on it first, tall grass and the like, and keep
            // anything it drops. A farmer clearing a field is a farmer gathering thatch.
            BlockPos above = pos.UpCopy();
            Block over = entity.World.BlockAccessor.GetBlock(above);
            if (over != null && over.Id != 0) BreakAndCarry(above);

            entity.World.BlockAccessor.SetBlock(farmland.BlockId, pos);

            // This is the step a hoe does and the one it is easy to leave out. Without
            // it the new farmland has no nutrients and no original fertility to recover
            // toward, so it grows crops at a tenth speed and never improves, while
            // looking in every way like good ground.
            if (entity.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityFarmland farm)
            {
                farm.OnCreatedFromSoil(soil, null);
                farm.MarkDirty(true);
            }

            return true;
        }

        // --- sowing ------------------------------------------------------------------

        /// <summary>Empty farmland, and a village with food to spare for seed.</summary>
        private bool NeedsSowing(Block block, BlockPos pos)
        {
            if (block is not BlockFarmland) return false;
            if (Catalogue?.CanSow != true) return false;

            Village village = Home;
            if (village == null) return false;
            if (village.Ledger.Get(EnumVillageResource.Food) < SeedCost) return false;

            return entity.World.BlockAccessor.GetBlockEntity(pos) is BlockEntityFarmland farm
                && farm.CanPlant();
        }

        private bool Sow(BlockPos pos)
        {
            Village village = Home;
            if (village == null) return false;

            if (entity.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityFarmland farm) return false;
            if (!Catalogue.PickSeed(entity.World.Rand, out Item seed, out Block crop)) return false;

            // Seed comes out of the village's own food, which is what makes sowing a real
            // decision rather than free growth. A village down to its last meal cannot
            // plant its way out, and that is the pressure the design wants.
            if (!village.Ledger.Withdraw(EnumVillageResource.Food, SeedCost)) return false;

            var slot = new DummySlot(new ItemStack(seed));
            var selection = new BlockSelection
            {
                Position = pos.Copy(),
                Face = BlockFacing.UP,
                HitPosition = new Vec3d(0.5, 1, 0.5)
            };

            bool planted;
            try
            {
                planted = farm.TryPlant(crop, slot, entity, selection);
            }
            catch (Exception e)
            {
                entity.Api.Logger.Warning("[F&F] Sowing {0} at {1} threw: {2}", crop.Code, pos, e.Message);
                planted = false;
            }

            if (!planted)
            {
                // Give the seed back rather than charging for nothing. Silently eating a
                // village's food on a failed action is the kind of leak nobody notices
                // until the food pool is inexplicably empty. A refund, not a deposit:
                // nobody carried this home, and the measured flow must never be told
                // otherwise.
                village.Ledger.Refund(EnumVillageResource.Food, SeedCost);
                return false;
            }

            return true;
        }

        /// <summary>Food value spent on the seed for one square.</summary>
        private static float SeedCost => 0.5f;

        public override string DebugLabel() => base.DebugLabel();
    }
}
