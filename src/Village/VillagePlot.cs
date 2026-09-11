using System.Collections.Generic;
using Newtonsoft.Json;
using Vintagestory.API.MathTools;

namespace FoundriesFrontiers
{
    /// <summary>
    /// What a piece of ground has been set aside for.
    ///
    /// Values are permanent. They go into save data, so a value may be appended but
    /// never reordered or reused.
    /// </summary>
    public enum EnumPlotKind
    {
        /// <summary>Trees, felled and replanted. The lumberjack's ground.</summary>
        Woodlot = 0,

        /// <summary>Farmland. Carries a soil grade, which is the thing that improves.</summary>
        Field = 1,

        /// <summary>Fenced grazing. Troughs, animals, culling.</summary>
        Pasture = 2,

        /// <summary>Exposed rock, worked at the surface.</summary>
        Quarry = 3,

        /// <summary>Clay, dug from a bank or a shallow.</summary>
        ClayPit = 4,

        /// <summary>The mouth of a shaft. Everything below it is out of the claim's reach.</summary>
        MineHead = 5,

        /// <summary>Ground being cut level, whose spoil is the village's building material.</summary>
        Terrace = 6
    }

    public enum EnumPlotState
    {
        /// <summary>Chosen but not yet prepared. Nothing has been done to the ground.</summary>
        Planned = 0,

        /// <summary>Being worked.</summary>
        Active = 1,

        /// <summary>
        /// Worked out. A quarry with no rock left in it, a woodlot that has been cut
        /// faster than it grew. Kept rather than deleted, because a village that keeps
        /// siting new plots on ground it has already stripped is a village with no memory.
        /// </summary>
        Exhausted = 2,

        /// <summary>Given up on. Nobody works it and it will not be reassigned.</summary>
        Abandoned = 3
    }

    /// <summary>
    /// A rectangle of ground the village has claimed for a purpose.
    ///
    /// This is deliberately not a block, a block entity, or a set of marker posts. It is
    /// a record on the village, for the same reason facilities are: the ground itself is
    /// ordinary world, and a plot is the village's opinion about it. That means a plot
    /// survives a player rearranging the terrain inside it, and it means siting a plot
    /// costs nothing but a decision.
    ///
    /// Bounds are stored as flat ints rather than a Cuboidi so the save format has no
    /// opinion about which engine type we happened to be using at the time. Y is the
    /// ground level the plot was sited at; work happens near it, not at a fixed height,
    /// because ground is not flat and pretending otherwise is how a farmer ends up tilling
    /// the air.
    /// </summary>
    public class VillagePlot
    {
        /// <summary>Unique within its village, never reused.</summary>
        [JsonProperty] public int Id;

        [JsonProperty] public EnumPlotKind Kind;

        [JsonProperty] public EnumPlotState State = EnumPlotState.Planned;

        [JsonProperty] public int MinX;
        [JsonProperty] public int MinZ;
        [JsonProperty] public int MaxX;
        [JsonProperty] public int MaxZ;

        /// <summary>Ground level when the plot was sited. A reference, not a ceiling.</summary>
        [JsonProperty] public int Y;

        /// <summary>
        /// For a field, which grade of soil is laid down: the tier of the earth form the
        /// village used. For everything else, how far the plot has been developed.
        /// Either way it is what makes a plot get better rather than just get used.
        /// </summary>
        [JsonProperty] public int Tier;

        /// <summary>
        /// Everyone assigned here. A list rather than a single owner because a field big
        /// enough to feed a town is not a one person job, and because the alternative is
        /// siting six overlapping fields to employ six farmers.
        /// </summary>
        [JsonProperty] public List<long> WorkerIds = new List<long>();

        /// <summary>
        /// How many people this plot has room for. Stored rather than derived so raising
        /// it later is a decision the village makes, not a silent rule change that
        /// reshuffles every existing plot on load.
        /// </summary>
        [JsonProperty] public int WorkerCap = 1;

        /// <summary>
        /// What the ground under a mine head was found to hold, as a share of the rock
        /// around it. Negative means nobody has looked yet.
        ///
        /// This is a survey, not a store. Nothing is taken out of the seam when it is
        /// read and the ore blocks stay in the world for whoever wants to mine them. What
        /// the village gets out of it is the knowledge, and the knowledge is what sets
        /// how often a shift at the face turns up ore rather than rubble.
        /// </summary>
        [JsonProperty] public float SeamRichness = -1f;

        /// <summary>The block code of whatever ore the seam mostly holds, if any.</summary>
        [JsonProperty] public string SeamOre;

        /// <summary>
        /// How much of a quarry's own pit turned out to be soil and gravel rather than
        /// rock, as a share. Negative means nobody has looked yet.
        ///
        /// The same idea as the mine's seam and for the same reason: what a working gives
        /// should come from what is actually in it. A quarry cut into a bare rock face is
        /// nearly all stone; one cut through a metre of topsoil gives earth alongside it,
        /// which is worth having, because earth is what cob is made of.
        /// </summary>
        [JsonProperty] public float EarthShare = -1f;

        /// <summary>The block code of whatever the overburden mostly is, if any.</summary>
        [JsonProperty] public string OverburdenBlock;

        /// <summary>
        /// The village tier the survey was done at. A village that has learned to dig
        /// deeper gets to look again, and may find better ground under the same plot.
        /// </summary>
        [JsonProperty] public int SeamTier = -1;

        [JsonProperty] public double CreatedTotalDays;

        /// <summary>Last day anyone actually did anything here. Drives the exhausted check.</summary>
        [JsonProperty] public double LastWorkedTotalDays;

        /// <summary>
        /// How much has been taken out of here, in pool value. A quarry that has given up
        /// four hundred stone has earned its keep even if it is empty now, and this is
        /// what makes that visible instead of guessed at.
        /// </summary>
        [JsonProperty] public float LifetimeYield;

        // --- derived ---------------------------------------------------------------

        [JsonIgnore] public int Width => MaxX - MinX + 1;
        [JsonIgnore] public int Length => MaxZ - MinZ + 1;
        [JsonIgnore] public int Area => Width * Length;

        [JsonIgnore] public int CentreX => (MinX + MaxX) / 2;
        [JsonIgnore] public int CentreZ => (MinZ + MaxZ) / 2;

        [JsonIgnore] public BlockPos Centre => new BlockPos(CentreX, Y, CentreZ, 0);

        [JsonIgnore] public bool IsWorkable => State == EnumPlotState.Planned || State == EnumPlotState.Active;

        [JsonIgnore] public bool HasRoom => IsWorkable && WorkerIds.Count < WorkerCap;

        public bool Contains(int x, int z) => x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;

        public bool Contains(BlockPos pos) => pos != null && Contains(pos.X, pos.Z);

        /// <summary>
        /// Whether two plots share any ground. Checked with a margin, because plots that
        /// merely touch still fight: a lumberjack felling on the boundary drops a tree
        /// into the next field over.
        /// </summary>
        public bool OverlapsWithMargin(VillagePlot other, int margin)
        {
            if (other == null) return false;
            return MinX - margin <= other.MaxX && MaxX + margin >= other.MinX
                && MinZ - margin <= other.MaxZ && MaxZ + margin >= other.MinZ;
        }

        public bool OverlapsWithMargin(int minX, int minZ, int maxX, int maxZ, int margin)
        {
            return MinX - margin <= maxX && MaxX + margin >= minX
                && MinZ - margin <= maxZ && MaxZ + margin >= minZ;
        }

        /// <summary>Every ground column in the plot, for a job that wants to walk it.</summary>
        public IEnumerable<BlockPos> Columns()
        {
            for (int x = MinX; x <= MaxX; x++)
            {
                for (int z = MinZ; z <= MaxZ; z++)
                {
                    yield return new BlockPos(x, Y, z, 0);
                }
            }
        }

        public override string ToString()
            => "#" + Id + " " + Kind.ToString().ToLowerInvariant()
             + " " + Width + "x" + Length + " at " + Centre
             + ", t" + Tier + ", " + State.ToString().ToLowerInvariant()
             + ", " + WorkerIds.Count + "/" + WorkerCap + " working"
             + (SeamRichness >= 0 ? ", " + SeamGrade + " seam" : "")
             + (EarthShare >= 0 ? ", " + (EarthShare * 100f).ToString("0") + "% earth" : "");

        /// <summary>The survey in one word, which is what anybody actually wants to know.</summary>
        [JsonIgnore] public string SeamGrade
        {
            get
            {
                if (SeamRichness < 0) return "unsurveyed";
                if (SeamRichness <= 0) return "barren";
                if (SeamRichness < 0.004f) return "poor";
                if (SeamRichness < 0.010f) return "fair";
                if (SeamRichness < 0.020f) return "good";
                return "rich";
            }
        }
    }
}
