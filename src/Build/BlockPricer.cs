using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace FoundriesFrontiers
{
    /// <summary>What one block costs a village, and out of which pool.</summary>
    public class BuildCost
    {
        [JsonProperty] public string Pool;
        [JsonProperty] public float Value;
    }

    /// <summary>
    /// Works out what a block costs a village to build with.
    ///
    /// Three answers, tried in order, and the order is the whole point.
    ///
    /// **First, the resource table.** If a village would take the thing off a villager's
    /// hands, it already has a price: a log is four wood because that is what a log is
    /// worth in the storehouse. Nothing else should get a say.
    ///
    /// **Second, the game's own crafting recipe.** A bed, a door, a chest, a block of cob:
    /// nobody carries those into a storehouse, so the table rightly ignores them, but the
    /// game knows exactly what each one is made of. Cob is five soil and four dry grass;
    /// a bed is planks and cloth. Adding up the ingredients gives a real number derived
    /// from the game rather than a number somebody typed, and it updates itself when the
    /// game or another mod changes a recipe.
    ///
    /// **Third, the block's material.** Stone is stone, wood is wood. A rough floor for
    /// anything with no recipe, which is mostly natural blocks and the odd decoration.
    ///
    /// Only when all three fail is a block unpriced, and the catalogue counts those so a
    /// building the village is getting for free is visible rather than discovered later
    /// as an economy that does not bite.
    /// </summary>
    public class BlockPricer
    {
        /// <summary>Material name to what a block of it is worth. The floor.</summary>
        [JsonProperty("byMaterial")] public Dictionary<string, BuildCost> ByMaterial =
            new Dictionary<string, BuildCost>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Hand written prices, for things with no recipe the game can be asked about.
        /// Longest matching code fragment wins. Kept as short as possible: every entry
        /// here is a number nobody derived, which is exactly what the recipe pass exists
        /// to avoid.
        /// </summary>
        [JsonProperty("byCode")] public Dictionary<string, BuildCost> ByCode =
            new Dictionary<string, BuildCost>(StringComparer.OrdinalIgnoreCase);

        [JsonIgnore] private ResourceTable table;
        [JsonIgnore] private ICoreServerAPI api;

        /// <summary>Prices worked out from recipes, so the walk happens once per block.</summary>
        [JsonIgnore] private readonly Dictionary<int, (EnumVillageResource pool, float value)> derived =
            new Dictionary<int, (EnumVillageResource, float)>();

        [JsonIgnore] private readonly HashSet<int> noPrice = new HashSet<int>();

        /// <summary>How many blocks got their price from each source, for the report.</summary>
        [JsonIgnore] public int FromTable, FromRecipe, FromMaterial, FromCode, Unpriced;

        public void Prepare(ICoreServerAPI serverApi, ResourceTable resourceTable)
        {
            api = serverApi;
            table = resourceTable;
            derived.Clear();
            noPrice.Clear();
            FromTable = FromRecipe = FromMaterial = FromCode = Unpriced = 0;
        }

        /// <summary>
        /// What this block costs. False means nothing could put a price on it at all.
        /// </summary>
        public bool CostOf(Block block, out EnumVillageResource pool, out float value)
        {
            pool = default;
            value = 0;
            if (block?.Code == null || api == null) return false;

            // 1. Would a village accept one as a deposit? Then that is its price.
            if (table != null)
            {
                var stack = new ItemStack(block);
                EnumVillageResource? r = table.Classify(stack);
                if (r != null)
                {
                    pool = r.Value;
                    value = table.UnitValue(stack, pool);
                    FromTable++;
                    return value > 0;
                }
            }

            if (noPrice.Contains(block.BlockId)) { Unpriced++; return false; }

            if (derived.TryGetValue(block.BlockId, out var cached))
            {
                pool = cached.pool;
                value = cached.value;
                return true;
            }

            // 2. What does the game say it is made of?
            if (FromRecipeOf(block, out pool, out value))
            {
                derived[block.BlockId] = (pool, value);
                FromRecipe++;
                return true;
            }

            // 3. A hand written price, for the handful with no recipe worth having.
            if (FromCodeTable(block, out pool, out value))
            {
                derived[block.BlockId] = (pool, value);
                FromCode++;
                return true;
            }

            // 4. What it is made of, roughly.
            if (ByMaterial.TryGetValue(block.BlockMaterial.ToString(), out BuildCost mat)
                && VillageResources.Parse(mat.Pool) is EnumVillageResource mr
                && mat.Value > 0)
            {
                pool = mr;
                value = mat.Value;
                derived[block.BlockId] = (pool, value);
                FromMaterial++;
                return true;
            }

            noPrice.Add(block.BlockId);
            Unpriced++;
            return false;
        }

        // --- asking the game --------------------------------------------------------

        /// <summary>
        /// Adds up what a grid recipe for this block consumes.
        ///
        /// Only ingredients that are actually used up count: a recipe that wants a hammer
        /// held in the grid is asking for a tool, not spending one, and charging a village
        /// for the hammer every time it lays a block would be nonsense.
        ///
        /// The result is divided by how many the recipe makes, so four planks from one log
        /// is one wood each rather than four.
        ///
        /// One level deep on purpose. An ingredient that is itself crafted almost always
        /// has a storehouse price already (planks, bricks, logs), and chasing the chain
        /// further risks a loop for very little.
        /// </summary>
        private bool FromRecipeOf(Block block, out EnumVillageResource pool, out float value)
        {
            pool = default;
            value = 0;

            GridRecipe recipe = FindRecipeFor(block);
            if (recipe?.ResolvedIngredients == null) return false;

            var totals = new float[VillageResources.Count];
            bool anything = false;

            foreach (CraftingRecipeIngredient ing in recipe.ResolvedIngredients)
            {
                if (ing == null) continue;
                if (ing.IsTool || !ing.Consume) continue;

                ItemStack stack = ing.ResolvedItemStack;
                if (stack == null) continue;

                EnumVillageResource? r = table?.Classify(stack);
                float per;

                if (r != null)
                {
                    per = table.UnitValue(stack, r.Value);
                }
                else if (stack.Block != null
                         && ByMaterial.TryGetValue(stack.Block.BlockMaterial.ToString(), out BuildCost m)
                         && VillageResources.Parse(m.Pool) is EnumVillageResource mr2)
                {
                    r = mr2;
                    per = m.Value;
                }
                else continue;

                int count = Math.Max(1, ing.Quantity);
                totals[(int)r.Value] += per * count;
                anything = true;
            }

            if (!anything) return false;

            int made = Math.Max(1, recipe.Output?.Quantity ?? 1);

            // Charge it to whichever pool the bulk of it came from. A bed is mostly wood
            // with a little cloth, and splitting one block across two pools would make a
            // village unable to build a house for want of a scrap of linen.
            int best = 0;
            for (int i = 1; i < totals.Length; i++) if (totals[i] > totals[best]) best = i;
            if (totals[best] <= 0) return false;

            float sum = 0;
            foreach (float t in totals) sum += t;

            pool = (EnumVillageResource)best;
            value = sum / made;
            return value > 0;
        }

        /// <summary>
        /// The first grid recipe whose output is this block.
        ///
        /// Walked rather than indexed because it happens once per distinct block at load
        /// and the list is a few thousand entries, which is nothing next to reading the
        /// schematics themselves.
        /// </summary>
        private GridRecipe FindRecipeFor(Block block)
        {
            List<GridRecipe> recipes = api?.World?.GridRecipes;
            if (recipes == null) return null;

            foreach (GridRecipe recipe in recipes)
            {
                ItemStack made = recipe?.Output?.ResolvedItemStack;
                if (made?.Block == null) continue;
                if (made.Block.BlockId == block.BlockId) return recipe;
            }
            return null;
        }

        private bool FromCodeTable(Block block, out EnumVillageResource pool, out float value)
        {
            pool = default;
            value = 0;

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

            if (best == null) return false;
            if (VillageResources.Parse(best.Pool) is not EnumVillageResource r) return false;
            if (best.Value <= 0) return false;

            pool = r;
            value = best.Value;
            return true;
        }

        public string Report()
            => "priced " + FromTable + " from the storehouse table, "
             + FromRecipe + " from crafting recipes, "
             + FromCode + " from the fallback list, "
             + FromMaterial + " from material, "
             + Unpriced + " not at all";
    }
}
