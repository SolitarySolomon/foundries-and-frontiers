using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace FoundriesFrontiers
{
    /// <summary>
    /// What the jobs need to know about the game's own blocks, worked out by looking
    /// rather than by writing codes down.
    ///
    /// This exists because of a mistake made twice already in this project: a block code
    /// typed from memory that turned out not to exist, failing silently and looking like
    /// a logic bug for an hour. Farmland variants, crop stages, sapling types and seed
    /// names all change between game versions and are extended by other mods, so the only
    /// list that stays right is the one read out of the running game.
    ///
    /// Everything here is discovered at asset finalise and logged, so if a future version
    /// renames farmland the log says so on the first boot instead of a farmer standing in
    /// a field doing nothing.
    /// </summary>
    public class WorldCatalogue : ModSystem
    {
        /// <summary>Farmland by fertility name: "verylow", "low", "medium", "compost", "high".</summary>
        private readonly Dictionary<string, Block> farmlandByFertility =
            new Dictionary<string, Block>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Seed item to the crop block it grows into, at its first stage.</summary>
        private readonly Dictionary<string, Block> cropForSeed =
            new Dictionary<string, Block>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Every plantable seed the game knows, for picking one to sow.</summary>
        private readonly List<Item> seeds = new List<Item>();

        /// <summary>
        /// Soil by fertility name.
        ///
        /// Needed because farmland gets its nutrient levels from the soil block it was
        /// made out of, not from its own code. Laying a farmland block without telling it
        /// which soil it came from produces ground with zero fertility that reads as the
        /// best in the game, which is the worst kind of bug: it looks right and grows
        /// nothing.
        /// </summary>
        private readonly Dictionary<string, Block> soilByFertility =
            new Dictionary<string, Block>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Saplings by tree type, so a felled oak can be replaced with an oak.
        ///
        /// Derived by looking rather than by writing codes down, same as everything else
        /// here: the tree types a world offers depend on which mods are installed, and a
        /// hardcoded list would quietly stop covering half of them.
        /// </summary>
        private readonly Dictionary<string, Block> saplingByTree =
            new Dictionary<string, Block>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The cheapest working example of each tool the game offers, by tool type.
        ///
        /// Villagers need real tools rather than a work-rate multiplier, because the game
        /// puts real behaviour on them: an axe fells a whole tree where bare hands take
        /// one log. Found by asking the registry which items are tools and what tier they
        /// are, so a modded axe is as good as a vanilla one.
        /// </summary>
        private readonly Dictionary<EnumTool, Item> basicToolByType =
            new Dictionary<EnumTool, Item>();

        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        /// <summary>After the resource table, before anything asks a question of it.</summary>
        public override double ExecuteOrder() => 0.3;

        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);

            foreach (Block block in api.World.Blocks)
            {
                string path = block?.Code?.Path;
                if (path == null) continue;

                if (block is BlockFarmland)
                {
                    // Codes look like farmland-dry-medium. The fertility is the last part,
                    // and the dry or moist part is a state the block entity manages itself,
                    // so keep the dry one as what a farmer lays down.
                    string[] parts = path.Split('-');
                    if (parts.Length >= 3 && parts[1] == "dry" && !farmlandByFertility.ContainsKey(parts[2]))
                    {
                        farmlandByFertility[parts[2]] = block;
                    }
                    continue;
                }

                if (block is BlockSapling)
                {
                    // sapling-oak-free, sapling-birch-planted, and so on. The tree type
                    // is the second part whatever the state on the end is.
                    string[] parts = path.Split('-');
                    if (parts.Length >= 2 && !saplingByTree.ContainsKey(parts[1]))
                    {
                        saplingByTree[parts[1]] = block;
                    }
                    continue;
                }

                if (block is BlockSoil)
                {
                    // soil-medium-none: fertility first, ground cover second. Only the
                    // bare variant is worth keeping, since it is the one a farmer would
                    // be turning over.
                    string[] parts = path.Split('-');
                    if (parts.Length >= 3 && parts[2] == "none" && !soilByFertility.ContainsKey(parts[1]))
                    {
                        soilByFertility[parts[1]] = block;
                    }
                }
            }

            foreach (Item item in api.World.Items)
            {
                if (item?.Code != null && item.Tool != null)
                {
                    // The lowest tier of each kind, because a village starting out should
                    // be swinging the cheapest thing that works, not a steel axe.
                    EnumTool kind = item.Tool.Value;
                    if (!basicToolByType.TryGetValue(kind, out Item had)
                        || item.ToolTier < had.ToolTier)
                    {
                        basicToolByType[kind] = item;
                    }
                }

                if (item is not ItemPlantableSeed) continue;
                string path = item.Code?.Path;
                if (path == null) continue;

                seeds.Add(item);

                // seeds-turnip grows crop-turnip-1. Checked rather than assumed: if the
                // convention ever changes, this pair is simply missing and says so.
                string crop = path.StartsWith("seeds-") ? path.Substring("seeds-".Length) : null;
                if (crop == null) continue;

                Block first = api.World.GetBlock(new AssetLocation(item.Code.Domain, "crop-" + crop + "-1"));
                if (first != null) cropForSeed[path] = first;
            }

            api.Logger.Notification(
                "[F&F] World catalogue: {0} farmland grade(s), {1} soil grade(s), {2} seed(s), "
                + "{3} of them matched to a crop, {4} sapling type(s), {5} tool type(s).",
                farmlandByFertility.Count, soilByFertility.Count, seeds.Count,
                cropForSeed.Count, saplingByTree.Count, basicToolByType.Count);

            if (farmlandByFertility.Count == 0)
            {
                api.Logger.Warning("[F&F] No farmland blocks found. Farmers will not be able to till.");
            }
            if (cropForSeed.Count == 0)
            {
                api.Logger.Warning("[F&F] No seed matched a crop block. Farmers will not be able to sow.");
            }
        }

        // --- farmland ---------------------------------------------------------------

        /// <summary>
        /// The farmland a village of this tier lays down, as a fertility name.
        ///
        /// This is the soil grade ladder, and it is deliberately the same ladder the
        /// earth pool hands out: a village can only lay down ground it can produce.
        /// </summary>
        public static string FertilityForTier(int tier)
        {
            if (tier >= 5) return "high";
            if (tier >= 3) return "compost";
            if (tier >= 2) return "medium";
            if (tier >= 1) return "low";
            return "verylow";
        }

        /// <summary>
        /// The best farmland this village knows how to lay, falling back down the ladder
        /// when a grade is missing rather than refusing to farm at all.
        /// </summary>
        public Block FarmlandFor(int tier)
        {
            string[] ladder = { "verylow", "low", "medium", "compost", "high" };
            int want = Math.Min(tier >= 5 ? 4 : tier >= 3 ? 3 : tier, ladder.Length - 1);

            for (int i = want; i >= 0; i--)
            {
                if (farmlandByFertility.TryGetValue(ladder[i], out Block block)) return block;
            }
            return null;
        }

        /// <summary>
        /// The soil a village of this tier lays under its fields, which is what tells the
        /// resulting farmland how fertile it is.
        /// </summary>
        public Block SoilFor(int tier)
        {
            string[] ladder = { "verylow", "low", "medium", "compost", "high" };
            int want = GradeForTier(tier);

            for (int i = want; i >= 0; i--)
            {
                if (soilByFertility.TryGetValue(ladder[i], out Block block)) return block;
            }
            return null;
        }

        /// <summary>How good the farmland already at a position is, on the same ladder.</summary>
        public static int GradeOfFarmland(Block block)
        {
            string path = block?.Code?.Path;
            if (path == null) return -1;

            if (path.EndsWith("-verylow")) return 0;
            if (path.EndsWith("-low")) return 1;
            if (path.EndsWith("-medium")) return 2;
            if (path.EndsWith("-compost")) return 3;
            if (path.EndsWith("-high")) return 4;
            return -1;
        }

        /// <summary>Which grade a village of this tier lays, for comparing against the above.</summary>
        public static int GradeForTier(int tier)
            => tier >= 5 ? 4 : tier >= 3 ? 3 : Math.Min(tier, 2);

        // --- tools ------------------------------------------------------------------

        /// <summary>The simplest tool of this kind the game offers, or null.</summary>
        public Item BasicTool(EnumTool kind)
            => basicToolByType.TryGetValue(kind, out Item item) ? item : null;

        /// <summary>
        /// What a villager of this trade works with.
        ///
        /// Returns null for trades that need nothing in particular, which is most of them
        /// for now. The ones listed here need a tool for a real reason rather than for a
        /// speed bonus: an axe is what makes a tree fall rather than a log come off it.
        /// </summary>
        public Item ToolFor(EnumTrade trade)
        {
            switch (trade)
            {
                case EnumTrade.Lumberjack: return BasicTool(EnumTool.Axe);
                case EnumTrade.Farmer: return BasicTool(EnumTool.Hoe);
                case EnumTrade.Builder: return BasicTool(EnumTool.Shovel);
                case EnumTrade.Quarrier:
                case EnumTrade.Miner: return BasicTool(EnumTool.Pickaxe);
                default: return null;
            }
        }

        // --- trees ------------------------------------------------------------------

        public bool HasSaplings => saplingByTree.Count > 0;

        /// <summary>
        /// The sapling for the kind of tree this log came from.
        ///
        /// Log codes look like log-grown-oak-ud, so the tree type is the third part. If
        /// that does not match anything known, any sapling is better than none: a village
        /// that cannot replant is a village that runs out of wood.
        /// </summary>
        public Block SaplingForLog(Block log, Random rand)
        {
            if (saplingByTree.Count == 0) return null;

            string path = log?.Code?.Path;
            if (path != null)
            {
                string[] parts = path.Split('-');
                for (int i = 0; i < parts.Length; i++)
                {
                    if (saplingByTree.TryGetValue(parts[i], out Block match)) return match;
                }
            }

            var any = new List<Block>(saplingByTree.Values);
            return any[rand.Next(any.Count)];
        }

        // --- crops ------------------------------------------------------------------

        public bool CanSow => cropForSeed.Count > 0;

        /// <summary>
        /// A seed the village would sow, and the crop it becomes.
        ///
        /// Random from what the game has rather than a favourite, because a field of one
        /// crop is both duller to look at and worse for the game's own nutrient system,
        /// which the farmer's rotation will read later.
        /// </summary>
        public bool PickSeed(Random rand, out Item seed, out Block crop)
        {
            seed = null;
            crop = null;
            if (cropForSeed.Count == 0) return false;

            var codes = new List<string>(cropForSeed.Keys);
            string pick = codes[rand.Next(codes.Count)];

            crop = cropForSeed[pick];
            return Resolve(pick, ref seed) && crop != null;
        }

        /// <summary>
        /// The crop this ground most wants: the one that feeds on whatever nutrient the
        /// soil still has most of.
        ///
        /// This is the game's own N, P and K, read off the farmland rather than modelled
        /// separately. Every crop declares which nutrient it eats, so a field that has
        /// been growing cabbage until its nitrogen is gone will be sown with something
        /// that wants phosphorus instead, and the nitrogen recovers while that grows.
        ///
        /// It is the difference between a village that farms and a village that plants
        /// things, and it is deliberately gated on tier: a hamlet scatters seed, a proper
        /// settlement rotates.
        /// </summary>
        public bool PickSeedFor(float[] nutrients, Random rand, out Item seed, out Block crop)
        {
            seed = null;
            crop = null;
            if (cropForSeed.Count == 0) return false;
            if (nutrients == null || nutrients.Length < 3) return PickSeed(rand, out seed, out crop);

            // Whichever of N, P and K is least depleted.
            int best = 0;
            for (int i = 1; i < 3; i++) if (nutrients[i] > nutrients[best]) best = i;
            var want = (EnumSoilNutrient)best;

            var matching = new List<string>();
            foreach (var kv in cropForSeed)
            {
                if (kv.Value?.CropProps?.RequiredNutrient == want) matching.Add(kv.Key);
            }

            // Nothing eats what this ground has spare, so plant anything and let the
            // fertility system do the rest.
            if (matching.Count == 0) return PickSeed(rand, out seed, out crop);

            string pick = matching[rand.Next(matching.Count)];
            crop = cropForSeed[pick];
            return Resolve(pick, ref seed) && crop != null;
        }

        private bool Resolve(string seedCode, ref Item seed)
        {
            foreach (Item item in seeds)
            {
                if (item.Code?.Path == seedCode) { seed = item; return true; }
            }
            return false;
        }

        /// <summary>Whether this block is a crop that is ready to take.</summary>
        public static bool IsRipeCrop(IWorldAccessor world, Block block, Vintagestory.API.MathTools.BlockPos pos)
        {
            if (block is not BlockCrop crop) return false;

            // Ask the farmland underneath rather than reading the stage off the code.
            // A crop can sit on ground with no block entity, in which case fall back to
            // comparing its stage against the last one the block declares.
            if (world.BlockAccessor.GetBlockEntity(pos.DownCopy()) is BlockEntityFarmland farm)
            {
                return farm.HasRipeCrop();
            }

            int stage = crop.CurrentCropStage;
            int last = block.CropProps?.GrowthStages ?? 0;
            return last > 0 && stage >= last;
        }
    }
}
