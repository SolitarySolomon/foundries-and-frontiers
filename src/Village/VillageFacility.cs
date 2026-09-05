using Newtonsoft.Json;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace FoundriesFrontiers
{
    public enum EnumFacilityKind
    {
        /// <summary>Somewhere to sleep. Population growth gates on free ones.</summary>
        Bed = 0,

        /// <summary>Somewhere to work. A trade cannot exist without its station.</summary>
        Workstation = 1
    }

    /// <summary>
    /// A thing inside a claim that a villager can be assigned to.
    ///
    /// Registered with the game's own point of interest registry, which is what turns
    /// "find the nearest free bed" into a spatial lookup we did not have to write. The
    /// registry knows where things are; ownership and whether a slot is free are ours,
    /// because they are village rules rather than world facts.
    ///
    /// Beds are vanilla blocks placed by schematics, not blocks this mod adds, so a
    /// facility is a record pointing at a position rather than a block entity. That also
    /// means it survives the block being something we did not anticipate.
    /// </summary>
    public class VillageFacility : IPointOfInterest
    {
        [JsonProperty] public int X;
        [JsonProperty] public int Y;
        [JsonProperty] public int Z;

        [JsonProperty] public EnumFacilityKind Kind;

        /// <summary>The block code as found, for the debug output and for rechecking.</summary>
        [JsonProperty] public string BlockCode = "";

        /// <summary>Which trade this workstation serves. Meaningless for a bed.</summary>
        [JsonProperty] public EnumTrade Serves;

        /// <summary>Entity id of whoever this belongs to, or 0 for free.</summary>
        [JsonProperty] public long OwnerEntityId;

        [JsonProperty] public long VillageId;

        [JsonIgnore] public BlockPos Pos => new BlockPos(X, Y, Z, 0);

        /// <summary>Centre of the block, which is where a villager should stand.</summary>
        [JsonIgnore] public Vec3d Position => new Vec3d(X + 0.5, Y, Z + 0.5);

        /// <summary>
        /// The registry's own type string. Two of them so a bed lookup never walks the
        /// forges, which matters once a town has a few hundred of these.
        /// </summary>
        [JsonIgnore] public string Type => Kind == EnumFacilityKind.Bed ? PoiTypeBed : PoiTypeWork;

        public const string PoiTypeBed = "ffbed";
        public const string PoiTypeWork = "ffworkstation";

        [JsonIgnore] public bool IsFree => OwnerEntityId == 0;

        public override string ToString()
            => (Kind == EnumFacilityKind.Bed ? "bed" : Serves.ToString().ToLowerInvariant() + " station")
             + " at " + Pos + (IsFree ? " (free)" : " (taken)");
    }
}
