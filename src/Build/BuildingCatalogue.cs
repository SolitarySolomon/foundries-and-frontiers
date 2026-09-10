using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FoundriesFrontiers
{
    /// <summary>
    /// What a village is trying to achieve by putting a building up.
    ///
    /// A settlement does not want "a farmhouse", it wants somewhere for another four
    /// people to sleep, and the farmhouse is one answer. Keeping the need separate from
    /// the building is what lets a culture answer the same need with a different shape,
    /// and what lets the brain reason about a village at all.
    /// </summary>
    public enum EnumBuildingNeed
    {
        /// <summary>Beds. The thing population growth is gated on.</summary>
        Housing = 0,

        /// <summary>A workstation for a trade, which is what makes that trade possible.</summary>
        Workshop = 1,

        /// <summary>Somewhere to keep things.</summary>
        Storage = 2,

        /// <summary>Water, warmth, a well, a fire. The things that make a place liveable.</summary>
        Amenity = 3,

        /// <summary>Walls, gates, towers.</summary>
        Defence = 4,

        /// <summary>The square, the monument, the things a village builds because it can.</summary>
        Civic = 5
    }

    /// <summary>
    /// What a schematic cannot know about itself, written by hand next to it.
    ///
    /// A .json export knows its blocks and its size and nothing else. It does not know
    /// which culture builds it, which need it answers, which trade it houses or how far
    /// up the ladder it belongs. Those are design decisions, so they live in a manifest
    /// rather than being guessed from the file name.
    /// </summary>
    public class BuildingManifest
    {
        /// <summary>Schematic file name without its extension.</summary>
        [JsonProperty] public string Code;

        /// <summary>Shown in game. Falls back to the code.</summary>
        [JsonProperty] public string Name;

        /// <summary>Which cultures build it. Empty means all of them.</summary>
        [JsonProperty] public string[] Cultures = Array.Empty<string>();

        [JsonProperty] public EnumBuildingNeed Need = EnumBuildingNeed.Housing;

        /// <summary>Which trade this houses, for a workshop. Ignored otherwise.</summary>
        [JsonProperty] public EnumTrade Houses = EnumTrade.Headman;

        /// <summary>Earliest tier a village will put this up.</summary>
        [JsonProperty] public int Tier;

        /// <summary>
        /// Roughly how good it is, for the tier gate that reads building quality.
        /// One for a hovel, five for something a town would be proud of.
        /// </summary>
        [JsonProperty] public int Quality = 1;

        /// <summary>How many of these one village will ever want. Zero means no limit.</summary>
        [JsonProperty] public int MaxPerVillage;
    }

    /// <summary>
    /// One building the village knows how to put up: its blocks, its two costs, and what
    /// it is for.
    ///
    /// **Both costs are read out of the schematic and neither is written by hand.** The
    /// bill of blocks is what physically gets placed, fifteen oak logs and six planks.
    /// The ledger cost is what those blocks are worth in pool value, so fifteen logs at
    /// four each plus six planks at one is sixty six wood. The village pays the pool cost
    /// and the builder places the blocks.
    ///
    /// That split is the whole point. It is what lets a village that only has firewood
    /// still afford a log cabin: it has the wood, and turning wood into the shape the
    /// building needs is the craft chain's job, gated by tier. A settlement with no saw
    /// cannot spend its wood on anything that needs planks.
    /// </summary>
    public class BuildingPlan
    {
        public string Code;
        public BuildingManifest Manifest;
        public BlockSchematic Schematic;

        /// <summary>Block code to how many of it the building needs.</summary>
        public readonly Dictionary<string, int> BillOfBlocks = new Dictionary<string, int>();

        /// <summary>Pool value the village must have before work can start.</summary>
        public readonly float[] LedgerCost = new float[VillageResources.Count];

        /// <summary>Blocks that are not air. Drives how long it takes to build.</summary>
        public int SolidBlockCount;

        /// <summary>
        /// Blocks nothing could put a price on. A high number here means a building the
        /// village is getting most of for free, which is worth seeing rather than
        /// discovering later as an economy that does not bite.
        /// </summary>
        public int Unpriced;

        /// <summary>
        /// Where each block goes, relative to the building's corner, and which block it is.
        ///
        /// Decoded once at load through the schematic's own unpacking rather than by
        /// picking the packed integer apart by hand here. The bit layout is an internal
        /// detail of the engine's file format and has no business being reimplemented in
        /// this mod, where it would keep working right up until the day it silently did
        /// not.
        ///
        /// The blocks are resolved, not raw ids. **The numbers in a schematic's BlockIds
        /// are keys into its own BlockCodes table, not block ids in this world**, and
        /// they came from whichever machine exported the file. Treating one as a live id
        /// means building a house out of whatever happens to sit at that slot in the
        /// player's registry, which changes the moment they install another mod. So every
        /// id goes through BlockCodes and back through the world's own lookup.
        /// </summary>
        public readonly List<(BlockPos Offset, Block Block)> Layout =
            new List<(BlockPos, Block)>();

        public int SizeX => Schematic?.SizeX ?? 0;
        public int SizeY => Schematic?.SizeY ?? 0;
        public int SizeZ => Schematic?.SizeZ ?? 0;

        public string Name => string.IsNullOrEmpty(Manifest?.Name) ? Code : Manifest.Name;

        /// <summary>Whether a culture of this name builds it.</summary>
        public bool BuiltBy(string cultureCode)
        {
            if (Manifest?.Cultures == null || Manifest.Cultures.Length == 0) return true;
            foreach (string c in Manifest.Cultures)
            {
                if (string.Equals(c, cultureCode, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        public string CostLine()
        {
            var parts = new List<string>();
            foreach (EnumVillageResource r in VillageResources.All)
            {
                float v = LedgerCost[(int)r];
                if (v > 0.01f) parts.Add(v.ToString("0.#") + " " + r.ToString().ToLowerInvariant());
            }
            return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
        }
    }

    /// <summary>
    /// What one block costs a village to build with, and out of which pool.
    /// </summary>
    public class BuildCost
    {
        [JsonProperty] public string Pool;
        [JsonProperty] public float Value;
    }

    /// <summary>
    /// The fallback that stops a building being free.
    ///
    /// The resource table answers a different question from this one. It decides whether
    /// a village would take something off a villager's hands, and it is right to ignore
    /// beds, doors, panes of glass and thatched roofs, because nobody carries those into
    /// a storehouse. But a building is made of exactly those things, and for a while that
    /// meant every one of them was free: a hovel of cob and thatch cost a village nothing
    /// at all, which quietly made the whole two-cost design meaningless.
    ///
    /// So build cost falls back to the block's own material, which the game already knows
    /// for every block including modded ones, with a short override list for the things
    /// that are a job to make rather than a shaping.
    /// </summary>
    public class BuildCostTable
    {
        [JsonProperty("byMaterial")] public Dictionary<string, BuildCost> ByMaterial =
            new Dictionary<string, BuildCost>(StringComparer.OrdinalIgnoreCase);

        [JsonProperty("byCode")] public Dictionary<string, BuildCost> ByCode =
            new Dictionary<string, BuildCost>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// What this block costs. The longest matching code fragment wins over the
        /// material, so a wooden bed is a bed rather than eight planks' worth of wood.
        /// </summary>
        public bool CostOf(Block block, out EnumVillageResource pool, out float value)
        {
            pool = default;
            value = 0;
            if (block?.Code == null) return false;

            string code = block.Code.Path.ToLowerInvariant();

            BuildCost best = null;
            int bestLen = -1;
            foreach (var kv in ByCode)
            {
                if (kv.Key.Length > bestLen && code.Contains(kv.Key.ToLowerInvariant()))
                {
                    best = kv.Value;
                    bestLen = kv.Key.Length;
                }
            }

            if (best == null) ByMaterial.TryGetValue(block.BlockMaterial.ToString(), out best);
            if (best == null) return false;

            EnumVillageResource? r = VillageResources.Parse(best.Pool);
            if (r == null) return false;

            pool = r.Value;
            value = best.Value;
            return value > 0;
        }
    }

    /// <summary>
    /// Loads every schematic the mod ships, works out what each one costs, and answers
    /// "what should this village build next".
    ///
    /// Nothing in Phase D can place a building without this. It is deliberately tolerant
    /// of finding no schematics at all: that is the state the mod ships in until the
    /// placeholder structures exist, and a catalogue that threw on an empty folder would
    /// make the mod unloadable in exactly that state.
    /// </summary>
    public class BuildingCatalogue : ModSystem
    {
        private readonly Dictionary<string, BuildingPlan> plans =
            new Dictionary<string, BuildingPlan>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Problems found at load, kept so a command can show them in game.</summary>
        private readonly List<string> complaints = new List<string>();

        private BuildCostTable costs = new BuildCostTable();

        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        /// <summary>After the resource table, since costing a building needs it.</summary>
        public override double ExecuteOrder() => 0.35;

        public IEnumerable<BuildingPlan> All => plans.Values;

        public int Count => plans.Count;

        public IReadOnlyList<string> Complaints => complaints;

        public BuildingPlan Get(string code)
            => code != null && plans.TryGetValue(code, out BuildingPlan p) ? p : null;

        public override void StartServerSide(ICoreServerAPI api)
        {
            base.StartServerSide(api);

            // Loaded here rather than at asset finalise because costing a schematic needs
            // the world's block registry to resolve ids, and that is not ready earlier.
            api.Event.ServerRunPhase(EnumServerRunPhase.RunGame, () => Load(api));
        }

        private void Load(ICoreServerAPI api)
        {
            plans.Clear();
            complaints.Clear();

            costs = LoadCostTable(api);
            Dictionary<string, BuildingManifest> manifests = LoadManifests(api);
            // loadAsset defaults to true and must stay that way. Passing false leaves
            // every asset unhydrated, so ToText returns "" and each schematic fails to
            // parse with no error text at all. That looked exactly like an empty folder.
            List<IAsset> assets = api.Assets.GetMany("worldgen/schematics", FoundriesFrontiersMod.ModId);

            if (assets == null || assets.Count == 0)
            {
                api.Logger.Notification(
                    "[F&F] No building schematics found. Villages will not build anything until "
                    + "some are exported into assets/foundriesfrontiers/worldgen/schematics/.");
                return;
            }

            var table = api.ModLoader.GetModSystem<ResourceTable>();

            foreach (IAsset asset in assets)
            {
                string code = asset.Name;
                if (code.EndsWith(".json")) code = code.Substring(0, code.Length - 5);

                BuildingPlan plan = LoadOne(api, asset, code, manifests, table);
                if (plan != null) plans[code] = plan;
            }

            api.Logger.Notification("[F&F] Loaded {0} building schematic(s).", plans.Count);

            foreach (string complaint in complaints) api.Logger.Warning("[F&F] {0}", complaint);
        }

        private BuildingPlan LoadOne(
            ICoreServerAPI api, IAsset asset, string code,
            Dictionary<string, BuildingManifest> manifests, ResourceTable table)
        {
            BlockSchematic schematic;
            try
            {
                string error = null;
                schematic = BlockSchematic.LoadFromString(asset.ToText(), ref error);
                if (schematic == null)
                {
                    complaints.Add(code + " would not load: "
                        + (string.IsNullOrEmpty(error)
                            ? "it parsed to nothing, so the file is probably empty or not a schematic"
                            : error));
                    return null;
                }
            }
            catch (Exception e)
            {
                complaints.Add(code + " threw while loading: " + e.Message);
                return null;
            }

            try
            {
                schematic.Init(api.World.BlockAccessor);
                schematic.LoadMetaInformationAndValidate(api.World.BlockAccessor, api.World, code);
            }
            catch (Exception e)
            {
                complaints.Add(code + " failed validation: " + e.Message);
                return null;
            }

            if (!manifests.TryGetValue(code, out BuildingManifest manifest))
            {
                // A schematic with no manifest entry still loads and can be inspected,
                // but no village will choose it, because nothing knows what it is for.
                complaints.Add(code + " has no entry in config/buildings.json, so no village will build it.");
                manifest = new BuildingManifest { Code = code, Name = code };
            }

            var plan = new BuildingPlan { Code = code, Manifest = manifest, Schematic = schematic };
            Cost(api, plan, table);
            return plan;
        }

        /// <summary>
        /// Walks every block in the schematic, counts it, and adds what it is worth to
        /// the pool it belongs to.
        ///
        /// Air is skipped, and so is anything the resource table has no opinion about:
        /// a village should not have to pay for the grass it is building on top of.
        /// </summary>
        private void Cost(ICoreServerAPI api, BuildingPlan plan, ResourceTable table)
        {
            List<int> ids = plan.Schematic.BlockIds;
            if (ids == null) return;

            // Ask the schematic where its own blocks go rather than unpacking the indices
            // by hand. Relative to nothing, so a site can simply add its corner.
            BlockPos[] offsets = plan.Schematic.GetJustPositions(new BlockPos(0, 0, 0, 0));

            // Only walk as far as both lists reach. A truncated file used to be costed in
            // full and built in part, so the village paid for a house and got a wall.
            int n = Math.Min(ids.Count, offsets?.Length ?? 0);
            if (offsets == null || offsets.Length != ids.Count)
            {
                complaints.Add(plan.Code + " unpacked "
                    + (offsets?.Length ?? 0) + " position(s) for " + ids.Count
                    + " block(s). Only the part that has both is costed and built.");
            }

            var codes = plan.Schematic.BlockCodes;

            for (int i = 0; i < n; i++)
            {
                int key = ids[i];
                if (key == 0) continue;

                // The number is a key into the schematic's own table, not a block id.
                if (codes == null || !codes.TryGetValue(key, out AssetLocation code) || code == null) continue;

                Block block = api.World.GetBlock(code);
                if (block == null || block.Id == 0) continue;

                // Markers the exporter left behind rather than material: worldgen's
                // underground and aboveground hints, and the filler and pathway blocks
                // that tell the engine where a structure may be cut into the ground.
                if (code.Path.StartsWith("meta-")) continue;
                if (plan.Schematic.IsFillerOrPath(block)) continue;

                plan.Layout.Add((offsets[i], block));

                string blockCode = code.ToShortString();
                plan.SolidBlockCount++;
                plan.BillOfBlocks.TryGetValue(blockCode, out int had);
                plan.BillOfBlocks[blockCode] = had + 1;

                // Cost it as the item a player would hold where the resource table has an
                // opinion, because that is what the storehouse deals in and what the
                // ledger is denominated in. Only blocks that will actually be placed are
                // costed, so a village never pays for something it does not get.
                if (table != null)
                {
                    ItemStack stack = new ItemStack(block);
                    EnumVillageResource? pool = table.Classify(stack);
                    if (pool != null)
                    {
                        plan.LedgerCost[(int)pool.Value] += table.UnitValue(stack, pool.Value);
                        continue;
                    }
                }

                // Nothing a village would accept as a deposit, which is most of what a
                // building is made of. Fall back to what it costs to build with.
                if (costs.CostOf(block, out EnumVillageResource bpool, out float bvalue))
                {
                    plan.LedgerCost[(int)bpool] += bvalue;
                    continue;
                }

                plan.Unpriced++;
            }
        }

        private BuildCostTable LoadCostTable(ICoreServerAPI api)
        {
            IAsset asset = api.Assets.TryGet(
                new AssetLocation(FoundriesFrontiersMod.ModId, "config/buildcosts.json"));

            if (asset == null)
            {
                complaints.Add("config/buildcosts.json is missing, so most of a building is free.");
                return new BuildCostTable();
            }

            try
            {
                return JsonConvert.DeserializeObject<BuildCostTable>(asset.ToText()) ?? new BuildCostTable();
            }
            catch (Exception e)
            {
                complaints.Add("config/buildcosts.json would not parse: " + e.Message);
                return new BuildCostTable();
            }
        }

        private Dictionary<string, BuildingManifest> LoadManifests(ICoreServerAPI api)
        {
            var found = new Dictionary<string, BuildingManifest>(StringComparer.OrdinalIgnoreCase);

            IAsset asset = api.Assets.TryGet(
                new AssetLocation(FoundriesFrontiersMod.ModId, "config/buildings.json"));

            if (asset == null)
            {
                complaints.Add("config/buildings.json is missing, so no schematic has a purpose.");
                return found;
            }

            try
            {
                var loaded = JsonConvert.DeserializeObject<BuildingManifest[]>(asset.ToText());
                if (loaded == null) return found;

                foreach (BuildingManifest m in loaded)
                {
                    if (string.IsNullOrWhiteSpace(m?.Code)) continue;
                    found[m.Code] = m;
                }
            }
            catch (Exception e)
            {
                complaints.Add("config/buildings.json would not parse: " + e.Message);
            }

            return found;
        }

        // --- choosing ------------------------------------------------------------------

        /// <summary>
        /// What this village should put up next, or null if there is nothing it both
        /// wants and can build.
        ///
        /// Housing first, because population growth is gated on beds and a village with
        /// nowhere to sleep cannot grow into anything. Then workshops, then the rest.
        /// Affordability is checked last on purpose: a village that wants a house it
        /// cannot afford should be saving up for a house, not quietly building a shed.
        /// </summary>
        public BuildingPlan ChooseFor(Village village, System.Func<BuildingPlan, int> countBuilt)
        {
            if (village == null) return null;

            BuildingPlan best = null;
            int bestScore = int.MinValue;

            foreach (BuildingPlan plan in plans.Values)
            {
                if (plan.Manifest == null) continue;
                if (plan.Manifest.Tier > village.Tier) continue;
                if (!plan.BuiltBy(village.CultureCode)) continue;

                int built = countBuilt?.Invoke(plan) ?? 0;
                if (plan.Manifest.MaxPerVillage > 0 && built >= plan.Manifest.MaxPerVillage) continue;

                // Lower need value is more urgent, and a higher tier building of the same
                // need beats a lower one, so a village stops putting up hovels once it
                // knows how to build a house.
                int score = (10 - (int)plan.Manifest.Need) * 100 + plan.Manifest.Tier * 10 - built;

                if (score > bestScore)
                {
                    bestScore = score;
                    best = plan;
                }
            }

            return best;
        }

        /// <summary>Whether the village's stores cover a plan's ledger cost right now.</summary>
        public static bool CanAfford(Village village, BuildingPlan plan)
        {
            if (village == null || plan == null) return false;

            foreach (EnumVillageResource r in VillageResources.All)
            {
                if (village.Ledger.Get(r) < plan.LedgerCost[(int)r]) return false;
            }
            return true;
        }

        /// <summary>What is missing, for the message a stalled build site shows.</summary>
        public static string Shortfall(Village village, BuildingPlan plan)
        {
            if (village == null || plan == null) return "nothing";

            var parts = new List<string>();
            foreach (EnumVillageResource r in VillageResources.All)
            {
                float short_ = plan.LedgerCost[(int)r] - village.Ledger.Get(r);
                if (short_ > 0.01f)
                {
                    parts.Add(short_.ToString("0.#") + " more " + r.ToString().ToLowerInvariant());
                }
            }
            return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
        }
    }
}
