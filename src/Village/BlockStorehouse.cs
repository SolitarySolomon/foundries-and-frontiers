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
        /// The hover text. The base block does not go and ask the block entity, so the
        /// crate was showing nothing at all when you looked at it.
        /// </summary>
        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            var be = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityStorehouse;
            if (be == null) return base.GetPlacedBlockInfo(world, pos, forPlayer);

            var sb = new System.Text.StringBuilder();
            be.GetBlockInfo(forPlayer, sb);

            // Never hand back an empty string. An empty result reads as "this block has
            // nothing to say" and the hover panel shows nothing at all, which is exactly
            // what it was doing.
            string text = sb.ToString().Trim();
            return text.Length > 0 ? text : base.GetPlacedBlockInfo(world, pos, forPlayer);
        }

        public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel)
        {
            var be = world.BlockAccessor.GetBlockEntity(blockSel.Position) as BlockEntityStorehouse;
            if (be != null) return be.OnPlayerRightClick(byPlayer, blockSel);

            return base.OnBlockInteractStart(world, byPlayer, blockSel);
        }
    }
}
