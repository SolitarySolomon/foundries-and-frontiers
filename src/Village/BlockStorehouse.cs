using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace FoundriesFrontiers
{
    /// <summary>
    /// The storehouse block. Thin on purpose: everything interesting is in the block
    /// entity, and this exists to hand a right click to it.
    /// </summary>
    public class BlockStorehouse : Block
    {
        /// <summary>
        /// A living village does not let you take its storehouse apart.
        ///
        /// Refusing outright rather than making it merely expensive, because the crate is
        /// the village's stores made visible and knocking it down would read as having
        /// destroyed them when the ledger would carry on regardless. Once the village is
        /// dead the crate becomes an ordinary looted box and this stops applying.
        /// </summary>
        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            var be = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityStorehouse;

            if (be != null && !be.Abandoned && be.VillageId != 0
                && world.Api.ModLoader.GetModSystem<VillageRegistry>()?.Get(be.VillageId) != null)
            {
                if (world.Side == EnumAppSide.Server && byPlayer is Vintagestory.API.Server.IServerPlayer sp)
                {
                    sp.SendIngameError("ffstorehouse", "This belongs to a living village. Empty it if you must.");
                }

                // The client has already predicted the break, so tell it otherwise.
                world.BlockAccessor.MarkBlockDirty(pos);
                return;
            }

            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            var be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityStorehouse;
            if (be != null) return be.OnPlayerRightClick(byPlayer, blockSel);

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }
    }
}
