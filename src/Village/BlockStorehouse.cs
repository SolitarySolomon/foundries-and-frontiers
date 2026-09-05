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
        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            var be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityStorehouse;
            if (be != null) return be.OnPlayerRightClick(byPlayer, blockSel);

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }
    }
}
