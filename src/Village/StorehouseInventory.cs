using System;
using Vintagestory.API.Common;

namespace FoundriesFrontiers
{
    /// <summary>
    /// A slot that only accepts one kind of thing.
    ///
    /// The storehouse shows one row per pool, so the row a stack lands in has to mean
    /// something. Without this a player could drop planks into the food row and the
    /// village would count them as dinner.
    /// </summary>
    public class ItemSlotPool : ItemSlotSurvival
    {
        public readonly EnumVillageResource Pool;
        private readonly ResourceTable table;

        public ItemSlotPool(InventoryBase inventory, EnumVillageResource pool, ResourceTable table)
            : base(inventory)
        {
            Pool = pool;
            this.table = table;
        }

        private bool Accepts(ItemStack stack)
        {
            if (stack == null) return false;
            if (table == null) return false;
            return table.Classify(stack) == Pool;
        }

        public override bool CanHold(ItemSlot sourceSlot)
            => Accepts(sourceSlot?.Itemstack) && base.CanHold(sourceSlot);

        public override bool CanTakeFrom(ItemSlot sourceSlot, EnumMergePriority priority = EnumMergePriority.AutoMerge)
            => Accepts(sourceSlot?.Itemstack) && base.CanTakeFrom(sourceSlot, priority);
    }

    /// <summary>
    /// The storehouse's grid: one row per pool, a fixed number of columns.
    ///
    /// This inventory is not the truth. It is a picture of the ledger, rebuilt from it
    /// when someone opens the door and read back into it when they change something.
    /// The ledger has to stay authoritative because a village whose chunks are unloaded
    /// has no block entities at all, and a village's stores cannot stop existing just
    /// because nobody is standing next to the crate.
    /// </summary>
    public class StorehouseInventory : InventoryGeneric
    {
        public const int Columns = 8;

        public static int SlotCount => VillageResources.Count * Columns;

        public StorehouseInventory(string className, string instanceId, ICoreAPI api, ResourceTable table)
            : base(SlotCount, className, instanceId, api,
                   (id, inv) => new ItemSlotPool(inv, PoolForSlot(id), table))
        {
        }

        /// <summary>Slots run left to right, one pool per row.</summary>
        public static EnumVillageResource PoolForSlot(int slotId)
        {
            int row = slotId / Columns;
            if (row < 0 || row >= VillageResources.Count) row = 0;
            return VillageResources.All[row];
        }

        public static int FirstSlotOf(EnumVillageResource r) => (int)r * Columns;
    }
}
