using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Vintagestory.API.Common;

namespace FoundriesFrontiers
{
    /// <summary>One pool's matching rules, loaded from config/resources.json.</summary>
    public class ResourceRule
    {
        /// <summary>
        /// An item counts toward this pool if its code contains any of these. Matching on
        /// fragments rather than exact codes means the table keeps working when the base
        /// game adds another kind of plank, and it costs nothing to extend for a mod.
        /// </summary>
        [JsonProperty] public string[] Match = Array.Empty<string>();

        /// <summary>Codes containing any of these never count toward this pool, whatever Match says.</summary>
        [JsonProperty] public string[] Exclude = Array.Empty<string>();

        /// <summary>
        /// How much one item is worth to the pool. A log is worth more than a stick, and
        /// without this a village could stockpile kindling and call it a timber yard.
        /// The longest matching fragment wins, so "plank" can override "log".
        /// </summary>
        [JsonProperty] public Dictionary<string, float> Weights = new Dictionary<string, float>();

        /// <summary>Worth per item when nothing in Weights matches.</summary>
        [JsonProperty] public float DefaultWeight = 1f;
    }

    /// <summary>
    /// Decides which pool an item belongs to, and what it is worth there.
    ///
    /// Kept as data rather than a switch statement because the alternative is a hundred
    /// hardcoded item codes that break whenever the game or another mod adds a food.
    /// </summary>
    public class ResourceTable : ModSystem
    {
        private readonly Dictionary<EnumVillageResource, ResourceRule> rules =
            new Dictionary<EnumVillageResource, ResourceRule>();

        /// <summary>
        /// When nothing in the table matches, anything edible still counts as food. The
        /// game already knows what is edible, so this catches every food the table has
        /// not been told about instead of silently dropping it.
        /// </summary>
        private bool foodFallbackFromNutrition = true;

        public override bool ShouldLoad(EnumAppSide side) => side == EnumAppSide.Server;

        public override double ExecuteOrder() => 0.25;

        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);

            IAsset asset = api.Assets.TryGet(
                new AssetLocation(FoundriesFrontiersMod.ModId, "config/resources.json"));

            if (asset == null)
            {
                api.Logger.Error("[F&F] config/resources.json missing. Deposits will not be classified.");
                return;
            }

            try
            {
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, ResourceRule>>(asset.ToText());
                foreach (var kv in loaded)
                {
                    EnumVillageResource? r = VillageResources.Parse(kv.Key);
                    if (r == null)
                    {
                        api.Logger.Warning("[F&F] resources.json has an unknown pool '{0}', ignoring it.", kv.Key);
                        continue;
                    }
                    rules[r.Value] = kv.Value;
                }
                api.Logger.Notification("[F&F] Resource table loaded for {0} pool(s).", rules.Count);
            }
            catch (Exception e)
            {
                api.Logger.Error("[F&F] resources.json failed to parse: {0}", e.Message);
            }
        }

        /// <summary>
        /// Which pool this stack belongs to, or null if the village has no use for it.
        /// Returning null is a real answer: a village does not want your rusty gears.
        /// </summary>
        public EnumVillageResource? Classify(ItemStack stack)
        {
            if (stack?.Collectible?.Code == null) return null;
            string code = stack.Collectible.Code.ToShortString().ToLowerInvariant();

            foreach (EnumVillageResource r in VillageResources.All)
            {
                if (!rules.TryGetValue(r, out ResourceRule rule)) continue;
                if (Matches(rule, code)) return r;
            }

            if (foodFallbackFromNutrition && stack.Collectible.NutritionProps != null)
            {
                return EnumVillageResource.Food;
            }

            return null;
        }

        /// <summary>What a whole stack is worth to its pool.</summary>
        public float ValueOf(ItemStack stack)
        {
            if (stack == null) return 0;
            EnumVillageResource? r = Classify(stack);
            if (r == null) return 0;
            return UnitValue(stack, r.Value) * stack.StackSize;
        }

        /// <summary>What one item of this stack is worth to the given pool.</summary>
        public float UnitValue(ItemStack stack, EnumVillageResource r)
        {
            if (!rules.TryGetValue(r, out ResourceRule rule)) return 1f;
            if (stack?.Collectible?.Code == null) return rule.DefaultWeight;

            string code = stack.Collectible.Code.ToShortString().ToLowerInvariant();

            // Longest fragment wins, so a specific rule beats a general one no matter
            // what order the config happens to list them in.
            float best = rule.DefaultWeight;
            int bestLen = -1;
            foreach (var kv in rule.Weights)
            {
                if (kv.Key.Length > bestLen && code.Contains(kv.Key.ToLowerInvariant()))
                {
                    best = kv.Value;
                    bestLen = kv.Key.Length;
                }
            }
            return best;
        }

        private static bool Matches(ResourceRule rule, string code)
        {
            if (rule.Exclude != null)
            {
                foreach (string bad in rule.Exclude)
                {
                    if (!string.IsNullOrEmpty(bad) && code.Contains(bad.ToLowerInvariant())) return false;
                }
            }

            if (rule.Match == null) return false;
            foreach (string frag in rule.Match)
            {
                if (!string.IsNullOrEmpty(frag) && code.Contains(frag.ToLowerInvariant())) return true;
            }
            return false;
        }

        /// <summary>For /ff village table, so the classification is checkable in game.</summary>
        public string Describe()
        {
            var sb = new System.Text.StringBuilder();
            foreach (EnumVillageResource r in VillageResources.All)
            {
                if (!rules.TryGetValue(r, out ResourceRule rule))
                {
                    sb.AppendLine(r.ToString().ToLowerInvariant() + ": no rules loaded");
                    continue;
                }
                sb.AppendLine(r.ToString().ToLowerInvariant() + " (default weight "
                              + rule.DefaultWeight.ToString("0.##") + ")");
                sb.AppendLine("  matches: " + string.Join(", ", rule.Match));
                if (rule.Exclude != null && rule.Exclude.Length > 0)
                    sb.AppendLine("  except:  " + string.Join(", ", rule.Exclude));
                if (rule.Weights != null && rule.Weights.Count > 0)
                {
                    var parts = new List<string>();
                    foreach (var kv in rule.Weights) parts.Add(kv.Key + " x" + kv.Value.ToString("0.##"));
                    sb.AppendLine("  weights: " + string.Join(", ", parts));
                }
            }
            return sb.ToString().TrimEnd();
        }
    }
}
