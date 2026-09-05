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

        /// <summary>
        /// The village's name, copied here so the client can label the crate. The client
        /// has no village registry to ask, so anything it needs to show has to be sent.
        /// </summary>
        public string VillageName = "";

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

        /// <summary>
        /// What the open dialog should be showing for a pool right now.
        ///
        /// Read every frame by the window rather than handed over once when it opens, so
        /// the numbers move as you take things off the shelf instead of being a snapshot
        /// of what was there when you walked up.
        /// </summary>
        public string TotalFor(EnumVillageResource pool)
        {
            int i = (int)pool;
            return displayTotals != null && i < displayTotals.Length ? displayTotals[i] ?? "" : "";
        }

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
                int tier = v.Tier;
                VillageName = v.Name;

                foreach (EnumVillageResource pool in VillageResources.All)
                {
                    int first = StorehouseInventory.FirstSlotOf(pool);
                    for (int c = 0; c < StorehouseInventory.Columns; c++) inventory[first + c].Itemstack = null;

                    float held = v.Ledger.Get(pool);
                    shownValue[(int)pool] = 0;
                    displayTotals[(int)pool] = held <= 0 ? "" : held.ToString("0.#");
                    if (held <= 0) continue;

                    // Every shape the village has learned to make gets its own slot, so a
                    // place that can saw still offers you firewood and logs rather than
                    // silently replacing them with planks. Taking one debits the pool by
                    // what that shape is worth, and the shelves are refilled straight
                    // after, so you can keep drawing until the pool is empty.
                    var forms = table?.UnlockedForms(pool, tier);
                    if (forms == null || forms.Count == 0) continue;

                    // Each shape gets an equal share of the pool, so offering three of
                    // them never adds up to more than the village actually has and no
                    // single shape starves the others.
                    float budget = held / forms.Count;
                    float placedTotal = 0;
                    int slot = first;

                    foreach (ResourceForm form in forms)
                    {
                        if (slot >= first + StorehouseInventory.Columns) break;

                        ItemStack sample = StackFromCode(form.Code);
                        if (sample == null) continue;

                        float perItem = Math.Max(0.01f, table.UnitValue(sample, pool));
                        int count = (int)Math.Floor(budget / perItem);
                        count = Math.Min(count, sample.Collectible.MaxStackSize);
                        if (count <= 0) continue;

                        ItemStack stack = sample.Clone();
                        stack.StackSize = count;
                        inventory[slot].Itemstack = stack;
                        slot++;

                        placedTotal += count * perItem;
                    }

                    shownValue[(int)pool] = placedTotal;

                    // Say so when the shelves hold less than the village does, rather
                    // than letting a partial figure read as the whole.
                    if (placedTotal < held - 0.01f)
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
        /// What a pool comes back out as: whatever the village currently knows how to
        /// make, not whatever went in last.
        ///
        /// The crate is deliberately a converter. Bring a village raw material and take
        /// back the best shape it can put that value into, which for wood is firewood in
        /// a hamlet and planks once it can saw. What it cannot do is hand you something
        /// its tier has not unlocked, so a founding settlement has no planks to give you
        /// however many logs you push across the counter.
        /// </summary>
        private ItemStack SampleStack(EnumVillageResource pool)
        {
            var table = Api.ModLoader.GetModSystem<ResourceTable>();
            int tier = Village?.Tier ?? 0;
            return StackFromCode(table?.PrimaryFormFor(pool, tier));
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
                shown += table?.ValueOf(stack) ?? stack.StackSize;
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

            // Put the shelves back to what the village now holds. This is what lets you
            // keep taking stack after stack until the pool is actually empty, and it is
            // what absorbs a deposit larger than a slot could ever show.
            RebuildFromLedger();
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
                    DialogTitle(), Inventory, Pos, Api as ICoreClientAPI, this));
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
            tree.SetString("ffVillageName", VillageName ?? "");
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
            VillageName = tree.GetString("ffVillageName", "");
        }

        /// <summary>
        /// The hover text, built from figures that travel with the block entity rather
        /// than from the registry, because the registry is server side and the player
        /// looking at the crate is not.
        /// </summary>
        public override void GetBlockInfo(IPlayer forPlayer, System.Text.StringBuilder sb)
        {
            if (Abandoned)
            {
                sb.AppendLine(string.IsNullOrEmpty(RuinedName)
                    ? "An abandoned storehouse."
                    : "What is left of " + RuinedName + "'s storehouse.");
                return;
            }

            sb.AppendLine(string.IsNullOrEmpty(VillageName) ? "Storehouse" : VillageName + " storehouse");

            bool anything = false;
            foreach (EnumVillageResource pool in VillageResources.All)
            {
                string total = displayTotals[(int)pool];
                if (string.IsNullOrEmpty(total)) continue;
                sb.AppendLine("  " + pool.ToString().ToLowerInvariant().PadRight(7) + total);
                anything = true;
            }

            if (!anything) sb.AppendLine("  empty");
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
