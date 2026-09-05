using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FoundriesFrontiers
{
    /// <summary>
    /// The village's stores, made physical.
    ///
    /// The ledger is the truth and this is its face. When you open the crate the grid is
    /// built from the ledger; when you move something the ledger is told what changed.
    /// It has to work this way round: an unloaded village has no block entities at all,
    /// and a village's grain cannot stop existing because nobody is standing next to it.
    ///
    /// One row per pool, eight columns, and a slot only accepts things that belong in its
    /// row. What you see is only as much as fits, so the hover text carries the real
    /// totals for a village with more wood than a crate can show.
    /// </summary>
    public class BlockEntityStorehouse : BlockEntityOpenableContainer
    {
        public long VillageId;

        private StorehouseInventory inventory;
        private bool rebuilding;

        public override InventoryBase Inventory => inventory;
        public override string InventoryClassName => "ffstorehouse";

        public BlockEntityStorehouse()
        {
            // The table is not available until Initialize, so slots start unrestricted
            // and are replaced once the block entity knows where it lives.
            inventory = new StorehouseInventory(InventoryClassName, "ffstorehouse-0", null, null);
        }

        public override void Initialize(ICoreAPI api)
        {
            var table = api.ModLoader.GetModSystem<ResourceTable>();
            inventory = new StorehouseInventory(InventoryClassName, "ffstorehouse-" + Pos, api, table);

            base.Initialize(api);
            inventory.LateInitialize(InventoryClassName + "-" + Pos, api);
            inventory.SlotModified += OnSlotModified;

            if (api.Side == EnumAppSide.Server) RebuildFromLedger();
        }

        private VillageRegistry Registry => Api?.ModLoader?.GetModSystem<VillageRegistry>();

        private Village Village => Registry?.Get(VillageId);

        // --- the ledger is the truth ------------------------------------------------

        /// <summary>
        /// Fills the grid from the ledger. Server side only, because the client is shown
        /// the result rather than working it out for itself.
        /// </summary>
        public void RebuildFromLedger()
        {
            Village v = Village;
            if (v == null || Api?.Side != EnumAppSide.Server) return;

            rebuilding = true;
            try
            {
                var table = Api.ModLoader.GetModSystem<ResourceTable>();

                foreach (EnumVillageResource pool in VillageResources.All)
                {
                    int first = StorehouseInventory.FirstSlotOf(pool);
                    for (int c = 0; c < StorehouseInventory.Columns; c++) inventory[first + c].Itemstack = null;

                    float remaining = v.Ledger.Get(pool);
                    if (remaining <= 0) continue;

                    ItemStack sample = SampleStack(v, pool);
                    if (sample == null) continue;

                    float perItem = Math.Max(0.01f, table?.UnitValue(sample, pool) ?? 1f);
                    int wanted = (int)Math.Floor(remaining / perItem);

                    for (int c = 0; c < StorehouseInventory.Columns && wanted > 0; c++)
                    {
                        int take = Math.Min(wanted, sample.Collectible.MaxStackSize);
                        ItemStack stack = sample.Clone();
                        stack.StackSize = take;
                        inventory[first + c].Itemstack = stack;
                        wanted -= take;
                    }
                }
            }
            finally
            {
                rebuilding = false;
            }

            MarkDirty(true);
        }

        /// <summary>
        /// One of whatever a village last carried into this pool, falling back to the
        /// resource table's own idea of what the pool looks like.
        /// </summary>
        private ItemStack SampleStack(Village v, EnumVillageResource pool)
        {
            string code = v.Ledger.DisplayItemCode(pool);
            ItemStack stack = StackFromCode(code);
            if (stack != null) return stack;

            var table = Api.ModLoader.GetModSystem<ResourceTable>();
            return StackFromCode(table?.DisplayCodeFor(pool));
        }

        private ItemStack StackFromCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;

            var loc = new AssetLocation(code);
            Item item = Api.World.GetItem(loc);
            if (item != null) return new ItemStack(item, 1);

            Block block = Api.World.GetBlock(loc);
            if (block != null && block.BlockId != 0) return new ItemStack(block, 1);

            return null;
        }

        /// <summary>
        /// Somebody moved something. Work out what the row is now worth and tell the
        /// ledger the difference, so taking from a village is recorded as spending on
        /// the day it happened rather than quietly vanishing from the books.
        /// </summary>
        private void OnSlotModified(int slotId)
        {
            if (rebuilding || Api?.Side != EnumAppSide.Server) return;

            Village v = Village;
            if (v == null) return;

            EnumVillageResource pool = StorehouseInventory.PoolForSlot(slotId);
            var table = Api.ModLoader.GetModSystem<ResourceTable>();

            float shown = 0;
            string newestCode = null;
            int first = StorehouseInventory.FirstSlotOf(pool);
            for (int c = 0; c < StorehouseInventory.Columns; c++)
            {
                ItemStack stack = inventory[first + c].Itemstack;
                if (stack == null) continue;
                shown += (table?.UnitValue(stack, pool) ?? 1f) * stack.StackSize;
                newestCode ??= stack.Collectible?.Code?.ToShortString();
            }

            float held = v.Ledger.Get(pool);
            float delta = shown - held;

            if (Math.Abs(delta) < 0.001f) return;

            if (delta > 0)
            {
                v.Ledger.Deposit(pool, delta, newestCode);
                Api.Logger.Notification("[F&F] {0} was given {1:0.#} {2}.", v.Name, delta, pool);
            }
            else
            {
                v.Ledger.WithdrawUpTo(pool, -delta);
                // No reputation system yet, so this is only recorded. G4 turns it into
                // a standing hit, and the ledger already knows the stock went down
                // without anybody depositing it, which is the signal it will read.
                Api.Logger.Notification("[F&F] {0} lost {1:0.#} {2} from its storehouse.", v.Name, -delta, pool);
            }
        }

        // --- opening it -------------------------------------------------------------

        /// <summary>
        /// Client side opens the dialog, which asks the server to open the inventory for
        /// real. The base class handles that exchange; all this has to do is name the
        /// window and say how wide the grid is.
        /// </summary>
        public override bool OnPlayerRightClick(IPlayer byPlayer, BlockSelection blockSel)
        {
            if (Api.Side == EnumAppSide.Client)
            {
                toggleInventoryDialogClient(byPlayer, () => new GuiDialogBlockEntityInventory(
                    DialogTitle(), Inventory, Pos, StorehouseInventory.Columns, Api as ICoreClientAPI));
            }

            return true;
        }

        /// <summary>
        /// Rebuilt from the ledger at the moment someone asks to look inside, so the
        /// crate always shows what the village actually has rather than what it had the
        /// last time a villager walked past.
        ///
        /// This hangs off the open packet because the base class's own OnInventoryOpened
        /// is not virtual, and the packet arrives before the inventory is handed over.
        /// </summary>
        public override void OnReceivedClientPacket(IPlayer player, int packetid, byte[] data)
        {
            if (packetid == (int)EnumBlockEntityPacketId.Open && Api?.Side == EnumAppSide.Server)
            {
                RebuildFromLedger();
            }

            base.OnReceivedClientPacket(player, packetid, data);
        }

        private string DialogTitle()
        {
            Village v = Village;
            return v == null ? "Storehouse" : v.Name + " storehouse";
        }

        // --- persistence and info ---------------------------------------------------

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetLong("ffVillage", VillageId);
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolve)
        {
            base.FromTreeAttributes(tree, worldForResolve);
            VillageId = tree.GetLong("ffVillage", 0);
        }

        public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder sb)
        {
            Village v = Village;
            if (v == null)
            {
                sb.AppendLine(VillageId == 0 ? "An unclaimed storehouse." : "Storehouse.");
                return;
            }

            sb.AppendLine(v.Name + " storehouse");
            foreach (EnumVillageResource pool in VillageResources.All)
            {
                float amount = v.Ledger.Get(pool);
                if (amount <= 0) continue;
                sb.AppendLine("  " + pool.ToString().ToLowerInvariant().PadRight(7) + amount.ToString("0.#"));
            }
        }

        /// <summary>
        /// Breaking the crate must not delete the village's stores. The ledger keeps
        /// them and a new storehouse shows the same contents, so there is nothing here
        /// to spill on the ground.
        /// </summary>
        public override void OnBlockBroken(IPlayer byPlayer = null)
        {
            Inventory?.Clear();

            Village v = Village;
            if (v != null && Api?.Side == EnumAppSide.Server)
            {
                v.HasStorehouse = false;
                Api.Logger.Notification(
                    "[F&F] {0}'s storehouse was broken. Its stores are untouched; /ff village storehouse rebuilds it.",
                    v.Name);
            }
        }
    }
}
