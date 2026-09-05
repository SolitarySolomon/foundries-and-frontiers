using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FoundriesFrontiers
{
    /// <summary>
    /// The stone marker at the centre of a village.
    ///
    /// This is the first block the mod adds, and it is deliberately the simplest one:
    /// it holds a village id, it tells you what village you are standing in, and that
    /// is all. The storehouse in B3 needs an inventory, a dialog and a network channel,
    /// so getting the block and block entity plumbing right on something this small
    /// first is cheaper than debugging both at once.
    ///
    /// Breaking it does not destroy the village. A village is data and outlives its
    /// marker, the same way a town outlives its signpost.
    /// </summary>
    public class BlockVillageCairn : Block
    {
        public override string GetPlacedBlockInfo(IWorldAccessor world, BlockPos pos, IPlayer forPlayer)
        {
            var be = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityVillageCairn;
            string info = be?.Describe();
            return info ?? base.GetPlacedBlockInfo(world, pos, forPlayer);
        }

        public override void OnBlockBroken(IWorldAccessor world, BlockPos pos, IPlayer byPlayer, float dropQuantityMultiplier = 1)
        {
            if (world.Side == EnumAppSide.Server)
            {
                var be = world.BlockAccessor.GetBlockEntity(pos) as BlockEntityVillageCairn;
                if (be != null && be.VillageId != 0)
                {
                    var registry = world.Api.ModLoader.GetModSystem<VillageRegistry>();
                    Village village = registry?.Get(be.VillageId);
                    if (village != null)
                    {
                        village.HasMarker = false;
                        world.Api.Logger.Notification(
                            "[F&F] The cairn at {0} was broken. {1} still exists; /ff village mark puts it back.",
                            pos, village.Name);
                    }
                }
            }

            base.OnBlockBroken(world, pos, byPlayer, dropQuantityMultiplier);
        }
    }
}
