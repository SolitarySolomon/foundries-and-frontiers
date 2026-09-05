using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Remembers which village this cairn belongs to.
    ///
    /// The id is the only thing stored. Everything shown when you look at the cairn is
    /// read live from the registry, so a village that changes tier or gains people is
    /// described correctly without the block entity knowing anything happened.
    /// </summary>
    public class BlockEntityVillageCairn : BlockEntity
    {
        public long VillageId;

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

        /// <summary>What the player sees when they look at it.</summary>
        public string Describe()
        {
            if (Api?.Side != EnumAppSide.Server && VillageId == 0) return "An unclaimed cairn.";

            var registry = Api?.ModLoader?.GetModSystem<VillageRegistry>();
            Village village = registry?.Get(VillageId);

            if (village == null)
            {
                // Client side has no registry, so it falls back to the plain form.
                return VillageId == 0 ? "An unclaimed cairn." : "Village marker.";
            }

            int here = registry.LoadedMembers(village.Id).Count;
            return village.Name + "\n"
                 + "Tier " + village.Tier + " " + village.CultureCode + "\n"
                 + village.MemberIds.Count + " villager(s), " + here + " here now";
        }
    }
}
