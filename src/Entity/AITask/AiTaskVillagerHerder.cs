using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Keeps the village's animals: fills the troughs out of the village's own grain,
    /// clears the hen boxes, and culls the herd back to what the pasture can carry.
    ///
    /// Feeding is where the cost lives. Animals are a way of turning grain the village
    /// already has into meat and eggs it does not, and the whole point is that this is a
    /// trade rather than free food. A village with nothing in its food pool cannot feed
    /// a herd, the herd stops breeding, and that is a real consequence of poor farming
    /// rather than a number quietly going down somewhere.
    ///
    /// Culling reads the animal's own drop table rather than a list of meat codes here,
    /// so it gives exactly what a player butchering the same animal would get, and works
    /// unchanged for animals added by other mods.
    /// </summary>
    public class AiTaskVillagerHerder : AiTaskVillagerWork
    {
        protected override EnumTrade Trade => EnumTrade.Herder;

        protected override EnumPlotKind PlotKind => EnumPlotKind.Pasture;

        protected override int VerticalSearch => 2;

        /// <summary>
        /// How many animals a pasture will carry per hundred blocks of ground.
        ///
        /// A cap rather than a hard fence: the herd is culled back to it, which is what a
        /// herder does. Without one, a village's pasture fills up until the server is
        /// simulating four hundred chickens.
        /// </summary>
        private const float AnimalsPerHundredBlocks = 6f;

        /// <summary>Never cull below this many, or a herd culls itself out of existence.</summary>
        private const int KeepAtLeast = 4;

        private double nextCullCheckAt;

        public AiTaskVillagerHerder(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
            : base(entity, taskConfig, aiConfig) { }

        protected override bool IsTarget(Block block, BlockPos pos)
        {
            if (block == null || block.Id == 0) return false;

            BlockEntity be = entity.World.BlockAccessor.GetBlockEntity(pos);
            if (be is BlockEntityTrough trough) return !trough.IsFull && CanAffordFeed();
            // Count what is actually collectable, not what is in the box. A nest holding
            // nothing but fertilised eggs looks full and yields nothing, and asking the
            // wrong question here left a herder walking to the same nest forever.
            if (be is BlockEntityHenBox box) return CollectableEggs(box) > 0;

            return false;
        }

        protected override bool Work(BlockPos pos)
        {
            BlockEntity be = entity.World.BlockAccessor.GetBlockEntity(pos);

            if (be is BlockEntityTrough trough) return FillTrough(trough, pos);
            if (be is BlockEntityHenBox box) return CollectEggs(box, pos);

            return false;
        }

        /// <summary>
        /// A pasture with full troughs and empty nest boxes is not idle: it is time to
        /// look at the herd. Culling is checked here rather than as a target because the
        /// thing being worked on is an animal, not a block, and the work loop deals in
        /// blocks.
        /// </summary>
        protected override void OnNothingToDo(Village village, VillagePlot plot)
        {
            if (Now < nextCullCheckAt) return;
            nextCullCheckAt = Now + 30;

            CullToCap(village, plot);
        }

        // --- feeding ------------------------------------------------------------------

        private bool CanAffordFeed()
        {
            Village village = Home;
            return village != null && village.Ledger.Get(EnumVillageResource.Food) >= FeedCost;
        }

        /// <summary>
        /// Puts something the trough will accept into it, paid for out of the food pool.
        ///
        /// What goes in comes from the trough's own content list rather than a guess:
        /// each trough declares what it takes and how much of it makes one fill level,
        /// so the right answer is already sitting on the block entity.
        /// </summary>
        private bool FillTrough(BlockEntityTrough trough, BlockPos pos)
        {
            Village village = Home;
            if (village == null) return false;

            ContentConfig[] configs = trough.contentConfigs;
            if (configs == null || configs.Length == 0) return false;

            foreach (ContentConfig config in configs)
            {
                if (config?.Content == null || config.QuantityPerFillLevel <= 0) continue;

                config.Content.Resolve(entity.World, "[F&F] trough feed");
                ItemStack stack = config.Content.ResolvedItemstack?.Clone();
                if (stack == null) continue;

                stack.StackSize = config.QuantityPerFillLevel;

                if (!village.Ledger.Withdraw(EnumVillageResource.Food, FeedCost)) return false;

                ItemSlot slot = trough.Inventory?[0];
                if (slot == null)
                {
                    village.Ledger.Refund(EnumVillageResource.Food, FeedCost);
                    return false;
                }

                if (slot.Empty)
                {
                    slot.Itemstack = stack;
                }
                else if (slot.Itemstack.Satisfies(stack))
                {
                    slot.Itemstack.StackSize += stack.StackSize;
                }
                else
                {
                    village.Ledger.Refund(EnumVillageResource.Food, FeedCost);
                    continue;
                }

                slot.MarkDirty();
                trough.MarkDirty(true);
                entity.Api.Logger.VerboseDebug("[F&F] Trough at {0} filled with {1}", pos, stack.GetName());
                return true;
            }

            return false;
        }

        /// <summary>Food value spent on one fill level of feed.</summary>
        private static float FeedCost => 3f;

        // --- eggs ----------------------------------------------------------------------

        /// <summary>
        /// How many eggs in this nest the village may actually take.
        ///
        /// A fertile egg is a chicken the village has not got yet, so it is left alone.
        /// That makes "how many eggs are here" and "how many can I have" different
        /// questions, and both callers have to ask the second one.
        /// </summary>
        private static int CollectableEggs(BlockEntityHenBox box)
        {
            InventoryBase inv = box?.Inventory;
            if (inv == null) return 0;

            int n = 0;
            for (int i = 0; i < inv.Count; i++)
            {
                ItemStack stack = inv[i]?.Itemstack;
                if (stack == null) continue;
                if (IsFertile(stack)) continue;
                n += stack.StackSize;
            }
            return n;
        }

        private static bool IsFertile(ItemStack stack)
            => stack?.Attributes?.HasAttribute("chick") == true;

        private bool CollectEggs(BlockEntityHenBox box, BlockPos pos)
        {
            InventoryBase inv = box.Inventory;
            if (inv == null) return false;

            int taken = 0;
            for (int i = 0; i < inv.Count; i++)
            {
                ItemSlot slot = inv[i];
                if (slot?.Itemstack == null) continue;
                if (IsFertile(slot.Itemstack)) continue;

                int got = Harvest(slot.Itemstack);
                if (got <= 0) continue;

                slot.Itemstack.StackSize -= got;
                if (slot.Itemstack.StackSize <= 0) slot.Itemstack = null;
                slot.MarkDirty();
                taken += got;
            }

            if (taken > 0) box.MarkDirty(true);
            return taken > 0;
        }

        // --- culling ---------------------------------------------------------------------

        /// <summary>
        /// Takes the herd back down to what the pasture carries.
        ///
        /// Adults only, and never below a floor, so a village culls a surplus rather than
        /// eating its own breeding stock. The drops come from the animal's own table, so
        /// this gives the same meat, hide and fat a player would have got.
        /// </summary>
        private void CullToCap(Village village, VillagePlot plot)
        {
            var herd = new List<Entity>();
            int cap = Math.Max(KeepAtLeast, (int)(plot.Area * AnimalsPerHundredBlocks / 100f));

            Entity[] nearby = entity.World.GetEntitiesAround(
                plot.Centre.ToVec3d().Add(0.5, 0, 0.5),
                Math.Max(plot.Width, plot.Length),
                Math.Max(8, VerticalSearch * 4),
                e => IsLivestock(e) && plot.Contains(e.Pos.AsBlockPos));

            foreach (Entity e in nearby) herd.Add(e);

            if (herd.Count <= cap) return;

            int over = herd.Count - cap;
            int culled = 0;

            foreach (Entity animal in herd)
            {
                if (culled >= over) break;
                if (!IsAdult(animal)) continue;

                CarryDropsOf(animal);

                // Removed, not Death. Dying makes the game spawn the animal's drops on
                // the ground, and we have just put that same drop table into the herder's
                // hands, so a cull would give the village its meat twice and leave a
                // second set rotting in the field.
                animal.Die(EnumDespawnReason.Removed);
                culled++;
            }

            if (culled > 0)
            {
                entity.Api.Logger.Notification(
                    "[F&F] {0} culled {1} from a herd of {2} (cap {3}).",
                    village.Name, culled, herd.Count, cap);
                Registry.NotePlotWorked(village, plot, culled);
            }
        }

        private void CarryDropsOf(Entity animal)
        {
            BlockDropItemStack[] drops = animal.Properties?.Drops;
            if (drops == null) return;

            foreach (BlockDropItemStack drop in drops)
            {
                if (drop?.ResolvedItemstack == null) continue;

                // Roll the drop's own distribution rather than rounding its average up.
                // A rare drop with an average of 0.15 is meant to be rare; taking the
                // ceiling of it hands the village one every single time.
                ItemStack stack = drop.GetNextItemStack();
                if (stack == null || stack.StackSize <= 0) continue;

                Harvest(stack);
            }
        }

        private static bool IsLivestock(Entity e)
        {
            if (e == null || !e.Alive) return false;
            if (e is EntityPlayer || e is FFVillager) return false;

            string path = e.Code?.Path;
            if (path == null) return false;

            return path.StartsWith("chicken") || path.StartsWith("sheep") || path.StartsWith("pig")
                || path.StartsWith("goat") || path.StartsWith("cow") || path.StartsWith("bighorn")
                || path.StartsWith("hare") || path.StartsWith("rabbit");
        }

        /// <summary>
        /// Grown, by the game's own growth attribute where there is one. Codes ending in
        /// a life stage such as chicken-baby are the other half of the same question.
        /// </summary>
        private static bool IsAdult(Entity e)
        {
            string path = e.Code?.Path ?? "";
            if (path.Contains("-baby") || path.Contains("-chick") || path.Contains("-lamb")) return false;

            ITreeAttribute grow = e.WatchedAttributes?.GetTreeAttribute("grow");
            if (grow == null) return true;

            return grow.GetDouble("timeSpawned", 0) > 0
                ? e.World.Calendar.TotalHours - grow.GetDouble("timeSpawned") > 24
                : true;
        }
    }
}
