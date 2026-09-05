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

        /// <summary>
        /// Set when the village that owned this died. An abandoned storehouse stops
        /// mirroring a ledger that no longer exists and becomes an ordinary box holding
        /// whatever was left, which anyone can break open and empty.
        /// </summary>
        public bool Abandoned;

        /// <summary>Whose stores these were, for the hover text once they are nobody's.</summary>
        public string RuinedName = "";

        private StorehouseInventory inventory;
        private bool rebuilding;

        /// <summary>
        /// What each row was worth the last time the crate was filled from the ledger.
        ///
        /// This has to exist because the shelves hold less than a village can. Comparing
        /// a row against the ledger total would read a full crate belonging to a village
        /// with two thousand wood as somebody having stolen fifteen hundred of it the
        /// moment they picked up a single log. The crate can only ever report the change
        /// in what it was showing.
        /// </summary>
        private readonly float[] shownValue = new float[VillageResources.Count];

        /// <summary>Totals for the dialog, sent to the client with the block entity.</summary>
        private string[] displayTotals = new string[VillageResources.Count];

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
            if (v == null || Abandoned || Api?.Side != EnumAppSide.Server) return;

            rebuilding = true;
            try
            {
                var table = Api.ModLoader.GetModSystem<ResourceTable>();

                foreach (EnumVillageResource pool in VillageResources.All)
                {
                    int first = StorehouseInventory.FirstSlotOf(pool);
                    for (int c = 0; c < StorehouseInventory.Columns; c++) inventory[first + c].Itemstack = null;

                    float held = v.Ledger.Get(pool);
                    shownValue[(int)pool] = 0;
                    displayTotals[(int)pool] = held <= 0 ? "" : held.ToString("0.#");

                    if (held <= 0) continue;

                    ItemStack sample = SampleStack(pool);
                    if (sample == null) continue;

                    float perItem = Math.Max(0.01f, table?.UnitValue(sample, pool) ?? 1f);
                    int wanted = (int)Math.Floor(held / perItem);
                    float placed = 0;

                    for (int c = 0; c < StorehouseInventory.Columns && wanted > 0; c++)
                    {
                        int take = Math.Min(wanted, sample.Collectible.MaxStackSize);
                        ItemStack stack = sample.Clone();
                        stack.StackSize = take;
                        inventory[first + c].Itemstack = stack;
                        wanted -= take;
                        placed += take * perItem;
                    }

                    shownValue[(int)pool] = placed;

                    // Say so when the shelves cannot hold everything, rather than quietly
                    // showing a fraction and letting the number look like the whole.
                    if (wanted > 0)
                    {
                        displayTotals[(int)pool] = held.ToString("0.#") + " held";
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
        /// What a pool comes back out as: always the pool's own base item, never the last
        /// thing that went in.
        ///
        /// Showing the last deposit read better, but it made the crate a material
        /// converter. Sticks are worth a quarter each and logs four, so a hundred and
        /// sixty sticks in and ten logs out is value neutral to the village and a free
        /// upgrade to whoever did it. A village hands back firewood, planks and stone,
        /// and what it did with your sticks is its own business.
        /// </summary>
        private ItemStack SampleStack(EnumVillageResource pool)
        {
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
            // An abandoned crate answers to nobody. What is in it is simply what is in it.
            if (rebuilding || Abandoned || Api?.Side != EnumAppSide.Server) return;

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

            float delta = shown - shownValue[(int)pool];
            shownValue[(int)pool] = shown;

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
                toggleInventoryDialogClient(byPlayer, () => new GuiDialogStorehouse(
                    DialogTitle(), Inventory, Pos, Api as ICoreClientAPI, displayTotals));
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

        /// <summary>
        /// Turns this into a ruin: the ledger link is cut, and what the village had left
        /// is written into the box as real items for whoever finds it.
        ///
        /// Only a fraction survives, because a settlement does not fail with its granary
        /// full. What is standing in the crate is the remainder nobody managed to carry
        /// away, which is the right amount of reward for walking into somewhere that died.
        /// </summary>
        public void AbandonWith(Village village, float survivingFraction)
        {
            if (Api?.Side != EnumAppSide.Server) return;

            var table = Api.ModLoader.GetModSystem<ResourceTable>();

            rebuilding = true;
            try
            {
                for (int i = 0; i < Inventory.Count; i++) Inventory[i].Itemstack = null;

                if (village != null)
                {
                    foreach (EnumVillageResource pool in VillageResources.All)
                    {
                        float left = village.Ledger.Get(pool) * survivingFraction;
                        if (left <= 0) continue;

                        ItemStack sample = SampleStack(pool);
                        if (sample == null) continue;

                        float perItem = Math.Max(0.01f, table?.UnitValue(sample, pool) ?? 1f);
                        int wanted = (int)Math.Floor(left / perItem);

                        int first = StorehouseInventory.FirstSlotOf(pool);
                        for (int c = 0; c < StorehouseInventory.Columns && wanted > 0; c++)
                        {
                            int take = Math.Min(wanted, sample.Collectible.MaxStackSize);
                            ItemStack stack = sample.Clone();
                            stack.StackSize = take;
                            Inventory[first + c].Itemstack = stack;
                            wanted -= take;
                        }
                    }

                    RuinedName = village.Name;
                }
            }
            finally
            {
                rebuilding = false;
            }

            Abandoned = true;
            VillageId = 0;
            MarkDirty(true);
        }

        public override void ToTreeAttributes(ITreeAttribute tree)
        {
            base.ToTreeAttributes(tree);
            tree.SetLong("ffVillage", VillageId);
            tree.SetBool("ffAbandoned", Abandoned);
            for (int i = 0; i < VillageResources.Count; i++)
            {
                tree.SetString("ffTotal" + i, displayTotals[i] ?? "");
            }
            tree.SetString("ffRuinedName", RuinedName ?? "");
        }

        public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldForResolve)
        {
            base.FromTreeAttributes(tree, worldForResolve);
            VillageId = tree.GetLong("ffVillage", 0);
            Abandoned = tree.GetBool("ffAbandoned", false);
            for (int i = 0; i < VillageResources.Count; i++)
            {
                displayTotals[i] = tree.GetString("ffTotal" + i, "");
            }
            RuinedName = tree.GetString("ffRuinedName", "");
        }

        public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder sb)
        {
            if (Abandoned)
            {
                sb.AppendLine(string.IsNullOrEmpty(RuinedName)
                    ? "An abandoned storehouse."
                    : "What is left of " + RuinedName + "'s storehouse.");
                base.GetBlockInfo(forPlayer, sb);
                return;
            }

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
            // A ruin is an ordinary box: whatever is inside spills like anything else.
            if (Abandoned)
            {
                base.OnBlockBroken(byPlayer);
                return;
            }

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
