using System.Collections.Generic;
using Newtonsoft.Json;
using Vintagestory.API.MathTools;

namespace FoundriesFrontiers
{
    /// <summary>
    /// One settlement. This is the record the whole mod hangs off: the brain reads it,
    /// the ledger lives on it, villagers point back at it by id.
    ///
    /// Everything marked JsonProperty is written to world save data, so treat the shape
    /// of this class as a save format. Add fields freely, because a missing field in an
    /// old save just takes its default. Never rename or repurpose one.
    ///
    /// Position is stored as three ints rather than a BlockPos because BlockPos carries
    /// a dimension field and copy semantics that have no business being in a save file.
    /// </summary>
    public class Village
    {
        /// <summary>Unique and never reused, even after a village is removed.</summary>
        [JsonProperty] public long Id;

        [JsonProperty] public string Name = "";

        /// <summary>Culture code, matching a key in config/cultures.json.</summary>
        [JsonProperty] public string CultureCode = CultureSystem.DefaultCulture;

        /// <summary>0 to 6. Drives claim size, buildings and what jobs exist.</summary>
        [JsonProperty] public int Tier;

        /// <summary>Calendar day the village was founded, for age and for the event log.</summary>
        [JsonProperty] public double FoundedTotalDays;

        [JsonProperty] public int CentreX;
        [JsonProperty] public int CentreY;
        [JsonProperty] public int CentreZ;

        /// <summary>
        /// What the village owns, and what it has actually been earning. Everything the
        /// brain decides is read from here, so it is part of the record rather than a
        /// separate object keyed by village id.
        /// </summary>
        [JsonProperty] public VillageLedger Ledger = new VillageLedger();

        /// <summary>
        /// Days held at the current tier. Tier gates require a condition to hold for a
        /// stretch rather than for one lucky morning, so this is what they read.
        /// </summary>
        [JsonProperty] public int DaysAtCurrentTier;

        /// <summary>
        /// The last whole day this village has been brought up to date for. The day clock
        /// advances it one day at a time, which is also how fast-forward will catch up a
        /// village that was unloaded for a season.
        /// </summary>
        [JsonProperty] public double LastSimulatedDay;

        /// <summary>
        /// Beds and workstations found inside the claim, and who owns them.
        ///
        /// Persisted rather than rescanned on load, because a scan of a large claim is
        /// expensive and because bed ownership is a village decision that should survive
        /// a restart. The world is re-checked only when something changes or somebody asks.
        /// </summary>
        [JsonProperty] public List<VillageFacility> Facilities = new List<VillageFacility>();

        /// <summary>
        /// Ground the village has set aside for a purpose: woodlots, fields, pastures,
        /// quarries, clay pits, mine heads, terraces.
        ///
        /// Every producing job works a plot rather than roaming, which is what stops a
        /// lumberjack clear-cutting the forest a player is standing in and what gives a
        /// village something to improve. A plot is a record, not a block: the ground
        /// itself stays ordinary world.
        /// </summary>
        [JsonProperty] public List<VillagePlot> Plots = new List<VillagePlot>();

        /// <summary>Next plot id. Kept on the village so ids are never reused after a removal.</summary>
        [JsonProperty] public int NextPlotId = 1;

        /// <summary>
        /// Buildings the village has decided on: standing, going up, or waiting on
        /// materials.
        ///
        /// A finished building stays on this list rather than being cleared, because the
        /// list is how the village knows it already has three houses and does not need a
        /// fourth, and how the tier gate counts building quality.
        /// </summary>
        [JsonProperty] public List<VillageBuildSite> BuildSites = new List<VillageBuildSite>();

        [JsonProperty] public int NextBuildSiteId = 1;

        /// <summary>
        /// Made tools waiting at the storehouse for whoever needs one.
        ///
        /// A count per item code rather than a pool, because a tool is not fungible value:
        /// six wood of axe is meaningless, and a village with four axes and no hoes is in
        /// a different position from one with two of each.
        ///
        /// This is what a smith fills. Without a rack a broken tool could only be replaced
        /// at the moment somebody needed one and the village happened to be able to pay,
        /// so a place with plenty of metal still had workers standing about bare handed.
        /// A rack means the smith works ahead of the need, which is what a smith is for.
        /// </summary>
        [JsonProperty] public Dictionary<string, int> ToolRack = new Dictionary<string, int>();

        /// <summary>How many tools are on the rack, of any kind.</summary>
        [JsonIgnore]
        public int ToolsInStock
        {
            get
            {
                int n = 0;
                foreach (int c in ToolRack.Values) n += c;
                return n;
            }
        }

        /// <summary>
        /// Entity ids of everyone who belongs here, including those in unloaded chunks.
        /// A villager's own ffVillage attribute is the authoritative link; this is the
        /// reverse index, so it gets reconciled rather than trusted blindly.
        /// </summary>
        [JsonProperty] public List<long> MemberIds = new List<long>();

        /// <summary>
        /// Whether the centre cairn is currently in the world. A broken cairn does not
        /// destroy the village, it just leaves it unmarked until someone puts one back.
        /// </summary>
        [JsonProperty] public bool HasMarker;

        [JsonProperty] public int MarkerX;
        [JsonProperty] public int MarkerY;
        [JsonProperty] public int MarkerZ;

        /// <summary>
        /// Whether the storehouse crate is standing. Like the cairn, breaking it costs
        /// the village nothing: the stores are in the ledger, not in the box.
        /// </summary>
        [JsonProperty] public bool HasStorehouse;

        [JsonProperty] public int StorehouseX;
        [JsonProperty] public int StorehouseY;
        [JsonProperty] public int StorehouseZ;

        /// <summary>
        /// Standing per player, keyed by player UID.
        ///
        /// A map rather than a single number because two players on one server must be
        /// able to have completely different relationships with the same village. Not
        /// used yet, but the shape is settled now so it never has to be migrated.
        /// </summary>
        [JsonProperty] public Dictionary<string, float> Standing = new Dictionary<string, float>();

        // --- derived ---------------------------------------------------------------

        [JsonIgnore]
        public BlockPos Centre => new BlockPos(CentreX, CentreY, CentreZ, 0);

        /// <summary>How far the claim reaches out from centre at the current tier.</summary>
        [JsonIgnore]
        public int ClaimRadius
        {
            get
            {
                int[] byTier = FFConfig.Current.Village.ClaimRadiusByTier;
                if (byTier == null || byTier.Length == 0) return 32;
                int t = GameMath.Clamp(Tier, 0, byTier.Length - 1);
                return byTier[t];
            }
        }

        /// <summary>
        /// The claim, as a box. Square rather than circular on purpose: a village is laid
        /// out on a grid later, and every containment test in the mod runs against this.
        /// </summary>
        [JsonIgnore]
        public Cuboidi ClaimBox
        {
            get
            {
                int r = ClaimRadius;
                var cfg = FFConfig.Current.Village;
                return new Cuboidi(
                    CentreX - r, CentreY - cfg.ClaimDepthBelow, CentreZ - r,
                    CentreX + r, CentreY + cfg.ClaimHeightAbove, CentreZ + r);
            }
        }

        public bool Contains(BlockPos pos)
        {
            if (pos == null) return false;
            int r = ClaimRadius;
            var cfg = FFConfig.Current.Village;
            return pos.X >= CentreX - r && pos.X <= CentreX + r
                && pos.Z >= CentreZ - r && pos.Z <= CentreZ + r
                && pos.Y >= CentreY - cfg.ClaimDepthBelow
                && pos.Y <= CentreY + cfg.ClaimHeightAbove;
        }

        /// <summary>Horizontal distance from the centre, ignoring height.</summary>
        public double HorizontalDistanceTo(BlockPos pos)
        {
            if (pos == null) return double.MaxValue;
            double dx = pos.X - CentreX;
            double dz = pos.Z - CentreZ;
            return System.Math.Sqrt(dx * dx + dz * dz);
        }

        public double HorizontalDistanceTo(Vec3d pos)
        {
            if (pos == null) return double.MaxValue;
            double dx = pos.X - CentreX;
            double dz = pos.Z - CentreZ;
            return System.Math.Sqrt(dx * dx + dz * dz);
        }

        public bool IsMember(long entityId) => MemberIds.Contains(entityId);

        public override string ToString()
            => Name + " (#" + Id + ", " + CultureCode + ", tier " + Tier + ")";
    }
}
