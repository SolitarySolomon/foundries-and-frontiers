using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace FoundriesFrontiers
{
    /// <summary>
    /// One concrete thing a village can hand back out of a pool, and when it learns how.
    ///
    /// Pools are abstract value, but a village does not store "sixty wood", it stores
    /// logs and firewood and planks. This is the list of shapes that value can take, and
    /// the tier is what stops a founding hamlet with no saw producing finished planks.
    /// </summary>
    public class ResourceForm
    {
        [JsonProperty] public string Code;

        /// <summary>Earliest tier at which the village can make this.</summary>
        [JsonProperty] public int Tier;

        /// <summary>
        /// Whether this is what the village shows and hands over by default at its tier.
        /// The highest unlocked primary wins, so a village that learns to saw starts
        /// handing out planks where it used to hand out firewood.
        /// </summary>
        [JsonProperty] public bool Primary;
    }

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
        /// Codes that are exactly one of these are excluded, matched whole rather than as
        /// a fragment.
        ///
        /// This exists because "rot" as a fragment also excludes car-rot, which is how a
        /// village came to refuse carrots. Anything short enough to hide inside a real
        /// word belongs here instead.
        /// </summary>
        [JsonProperty] public string[] ExcludeExact = Array.Empty<string>();

        /// <summary>
        /// How much one item is worth to the pool. A log is worth more than a stick, and
        /// without this a village could stockpile kindling and call it a timber yard.
        /// The longest matching fragment wins, so "plank" can override "log".
        /// </summary>
        [JsonProperty] public Dictionary<string, float> Weights = new Dictionary<string, float>();

        /// <summary>Worth per item when nothing in Weights matches.</summary>
        [JsonProperty] public float DefaultWeight = 1f;

        /// <summary>Every shape this pool's value can take, and the tier that unlocks it.</summary>
        [JsonProperty] public ResourceForm[] Forms = Array.Empty<ResourceForm>();
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

        /// <summary>
        /// Both sides. The server needs it to value deposits, and the client needs it to
        /// know which slot will accept what, because a slot that refuses on the client
        /// never sends anything to the server to be judged.
        /// </summary>
        public override bool ShouldLoad(EnumAppSide side) => true;

        public override double ExecuteOrder() => 0.25;

        private ICoreAPI api;

        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);
            this.api = api;

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
                VerifyForms(api);
            }
            catch (Exception e)
            {
                api.Logger.Error("[F&F] resources.json failed to parse: {0}", e.Message);
            }
        }

        /// <summary>
        /// Checks every form code actually resolves to something in the game.
        ///
        /// A form code with a typo in it does not fail loudly. It produces a storehouse
        /// row that is simply empty, which looks exactly like a village that has none of
        /// that resource, and the only way to tell the difference is to already suspect
        /// the config. Checking once at load turns a silent mystery into a log line.
        /// </summary>
        private void VerifyForms(ICoreAPI api)
        {
            int bad = 0;
            foreach (var kv in rules)
            {
                if (kv.Value.Forms == null) continue;
                foreach (ResourceForm form in kv.Value.Forms)
                {
                    if (form?.Code == null) continue;
                    var loc = new AssetLocation(form.Code);
                    if (api.World.GetItem(loc) != null) continue;
                    if (api.World.GetBlock(loc) != null) continue;

                    bad++;
                    api.Logger.Warning(
                        "[F&F] {0} lists a form '{1}' that is not a real item or block. "
                        + "That row will look empty in the storehouse.",
                        kv.Key.ToString().ToLowerInvariant(), form.Code);
                }
            }
            if (bad == 0) api.Logger.Notification("[F&F] All resource forms resolve.");
        }

        /// <summary>
        /// Which pool this stack belongs to, or null if the village has no use for it.
        /// Returning null is a real answer: a village does not want your rusty gears.
        /// </summary>
        public EnumVillageResource? Classify(ItemStack stack)
        {
            if (stack?.Collectible?.Code == null) return null;

            // A bowl of stew is food, not a clay bowl. The container is incidental and
            // matching on the code would call it pottery, so ask the game whether there
            // is a meal in it first. An empty bowl has no servings and falls through to
            // the fragment rules, where it correctly reads as clay.
            if (ServingsIn(stack) > 0) return EnumVillageResource.Food;

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

        /// <summary>
        /// How many portions of food are actually in this stack.
        ///
        /// Cooked meals live in a container and carry their remaining servings on the
        /// stack, so a pot holding four helpings of stew and one holding half a helping
        /// are the same item with the same stack size. Bowls, crocks, pots and pies all
        /// answer to the same interface, so one question covers every meal in the game.
        /// Returns 0 for anything that is not a meal, including an empty bowl.
        /// </summary>
        public float ServingsIn(ItemStack stack)
        {
            if (stack?.Collectible is not IBlockMealContainer meal) return 0;

            try
            {
                return Math.Max(0, meal.GetQuantityServings(api?.World, stack));
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// What a whole stack is worth to its pool.
        ///
        /// Almost everything is worth its unit value times how many of it there are. A
        /// meal is worth its value per serving times how many servings are left, because
        /// a nearly empty pot and a full one are the same item and should not be worth
        /// the same to a hungry village.
        /// </summary>
        public float ValueOf(ItemStack stack)
        {
            if (stack == null) return 0;
            EnumVillageResource? r = Classify(stack);
            if (r == null) return 0;

            float servings = ServingsIn(stack);
            if (servings > 0) return MealServingValue * servings * stack.StackSize;

            return UnitValue(stack, r.Value) * stack.StackSize;
        }

        /// <summary>Food value of one serving of a cooked meal.</summary>
        public float MealServingValue => FFConfig.Current.Village.MealServingValue;

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

        /// <summary>
        /// What a village of this tier hands out of this pool.
        ///
        /// The highest unlocked primary form, so the same request gives you firewood from
        /// a hamlet and planks from a place with a sawmill. That is the whole point of
        /// the storehouse working as a converter: you bring a village raw material and
        /// take back whatever it has learned to make, and what it has learned is a
        /// readable measure of how far it has come.
        /// </summary>
        public string PrimaryFormFor(EnumVillageResource r, int tier)
        {
            if (!rules.TryGetValue(r, out ResourceRule rule) || rule.Forms == null) return null;

            string best = null;
            int bestTier = -1;
            foreach (ResourceForm form in rule.Forms)
            {
                if (!form.Primary || form.Tier > tier || form.Tier <= bestTier) continue;
                bestTier = form.Tier;
                best = form.Code;
            }
            return best;
        }

        /// <summary>Everything a village of this tier could hand out of this pool.</summary>
        public List<ResourceForm> UnlockedForms(EnumVillageResource r, int tier)
        {
            var found = new List<ResourceForm>();
            if (!rules.TryGetValue(r, out ResourceRule rule) || rule.Forms == null) return found;

            foreach (ResourceForm form in rule.Forms)
            {
                if (form.Tier <= tier) found.Add(form);
            }
            return found;
        }

        /// <summary>Whether a village of this tier knows how to make this item at all.</summary>
        public bool IsUnlocked(EnumVillageResource r, string code, int tier)
        {
            if (!rules.TryGetValue(r, out ResourceRule rule) || rule.Forms == null) return false;
            foreach (ResourceForm form in rule.Forms)
            {
                if (form.Tier <= tier && string.Equals(form.Code, code, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        private static bool Matches(ResourceRule rule, string code)
        {
            if (rule.ExcludeExact != null)
            {
                foreach (string exact in rule.ExcludeExact)
                {
                    if (string.Equals(exact, code, StringComparison.OrdinalIgnoreCase)) return false;
                }
            }

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
                if (rule.Forms != null && rule.Forms.Length > 0)
                {
                    var forms = new List<string>();
                    foreach (ResourceForm f in rule.Forms)
                    {
                        forms.Add(f.Code.Replace("game:", "") + " t" + f.Tier + (f.Primary ? "*" : ""));
                    }
                    sb.AppendLine("  hands out: " + string.Join(", ", forms) + "   (* = default at that tier)");
                }
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
