using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace FoundriesFrontiers
{
    /// <summary>
    /// The storehouse window.
    ///
    /// A plain chest grid was unreadable: six rows that each only accept one kind of
    /// thing, with nothing to say which row is which. So this is a hand composed dialog
    /// with the pool's name beside its row, and the village's real total on the right,
    /// including the part that does not fit on the shelves.
    /// </summary>
    public class GuiDialogStorehouse : GuiDialogBlockEntity
    {
        private readonly string[] totals;

        public GuiDialogStorehouse(string title, InventoryBase inventory, BlockPos pos,
                                   ICoreClientAPI capi, string[] totals)
            : base(title, inventory, pos, capi)
        {
            if (IsDuplicate) return;

            this.totals = totals;
            capi.World.Player.InventoryManager.OpenInventory(inventory);
            Compose(title);
        }

        private void Compose(string title)
        {
            const double rowHeight = 52;
            const double labelWidth = 78;
            const double totalWidth = 74;
            const double gridLeft = labelWidth + 6;

            ElementBounds bg = ElementStdBounds.DialogBackground().WithFixedPadding(GuiStyle.ElementToDialogPadding);
            ElementBounds dialog = ElementStdBounds.AutosizedMainDialog.WithAlignment(EnumDialogArea.RightMiddle)
                .WithFixedAlignmentOffset(-GuiStyle.DialogToScreenPadding, 0);

            GuiComposer composer = capi.Gui
                .CreateCompo("ffstorehouse" + BlockEntityPosition, dialog)
                .AddShadedDialogBG(bg)
                .AddDialogTitleBar(title, OnTitleBarClose)
                .BeginChildElements(bg);

            CairoFont label = CairoFont.WhiteSmallText();
            CairoFont figure = CairoFont.WhiteDetailText();

            for (int row = 0; row < VillageResources.Count; row++)
            {
                EnumVillageResource pool = VillageResources.All[row];
                double y = 32 + row * rowHeight;

                var slots = new int[StorehouseInventory.Columns];
                int first = StorehouseInventory.FirstSlotOf(pool);
                for (int c = 0; c < StorehouseInventory.Columns; c++) slots[c] = first + c;

                composer
                    .AddStaticText(
                        pool.ToString(),
                        label,
                        ElementBounds.Fixed(0, y + 12, labelWidth, 24))
                    .AddItemSlotGrid(
                        Inventory, DoSendPacket, StorehouseInventory.Columns, slots,
                        ElementStdBounds.SlotGrid(EnumDialogArea.None, gridLeft, y, StorehouseInventory.Columns, 1),
                        "grid" + row)
                    .AddStaticText(
                        totals != null && row < totals.Length ? totals[row] : "",
                        figure,
                        EnumTextOrientation.Right,
                        ElementBounds.Fixed(gridLeft + StorehouseInventory.Columns * 50 + 6, y + 14, totalWidth, 24));
            }

            composer.EndChildElements().Compose();
            SingleComposer = composer;
        }

        private void OnTitleBarClose() => TryClose();

        public override void OnGuiClosed()
        {
            Inventory.InvNetworkUtil.PauseInventoryUpdates = false;
            base.OnGuiClosed();
        }
    }
}
