using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Vintagestory.API.Common;

namespace FoundriesFrontiers
{
    /// <summary>One culture's names and voice, loaded from config/cultures.json.</summary>
    public class Culture
    {
        [JsonProperty] public string DisplayName;
        [JsonProperty] public string[] MaleNames = Array.Empty<string>();
        [JsonProperty] public string[] FemaleNames = Array.Empty<string>();
        [JsonProperty] public string[] FamilyNames = Array.Empty<string>();

        /// <summary>Names this culture gives its settlements.</summary>
        [JsonProperty] public string[] VillageNames = Array.Empty<string>();

        /// <summary>Utterance code (lowercase) to the things this culture says.</summary>
        [JsonProperty] public Dictionary<string, string[]> Lines = new Dictionary<string, string[]>();

        /// <summary>
        /// Clothing slot to the item codes this culture wears, e.g. "upperbody" to a list
        /// of shirts. Kept as data so a Norse villager can be dressed differently from a
        /// Norman one without a line of code knowing either exists.
        /// </summary>
        [JsonProperty] public Dictionary<string, string[]> Wardrobe = new Dictionary<string, string[]>();

        public string RandomGarment(string slot, Random rand)
        {
            if (Wardrobe != null
                && Wardrobe.TryGetValue(slot, out string[] pool)
                && pool != null && pool.Length > 0)
            {
                return pool[rand.Next(pool.Length)];
            }
            return null;
        }

        public string RandomVillageName(Random rand)
        {
            if (VillageNames == null || VillageNames.Length == 0) return null;
            return VillageNames[rand.Next(VillageNames.Length)];
        }

        public string RandomName(bool female, Random rand)
        {
            string[] pool = female ? FemaleNames : MaleNames;
            if (pool == null || pool.Length == 0) return "Villager";

            string given = pool[rand.Next(pool.Length)];
            if (FamilyNames == null || FamilyNames.Length == 0) return given;

            // Not everyone carries a byname - it reads better when only some do.
            if (rand.NextDouble() < 0.45) return given;
            return given + " " + FamilyNames[rand.Next(FamilyNames.Length)];
        }

        public string RandomLine(string utteranceCode, Random rand)
        {
            if (Lines != null
                && Lines.TryGetValue(utteranceCode, out string[] pool)
                && pool != null && pool.Length > 0)
            {
                return pool[rand.Next(pool.Length)];
            }
            return null;
        }
    }

    /// <summary>
    /// Loads and serves the culture table.
    ///
    /// Cultures are data, not code, from the very start - the design calls for five of
    /// them eventually, and retrofitting a culture into code that assumed two is how mods
    /// end up with a hardcoded check in forty places.
    /// </summary>
    public class CultureSystem : ModSystem
    {
        public const string DefaultCulture = "norman";

        private readonly Dictionary<string, Culture> byCode =
            new Dictionary<string, Culture>(StringComparer.OrdinalIgnoreCase);

        private string[] codes = Array.Empty<string>();

        public IReadOnlyCollection<string> Codes => codes;

        public override bool ShouldLoad(EnumAppSide side) => true;

        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);

            IAsset asset = api.Assets.TryGet(
                new AssetLocation(FoundriesFrontiersMod.ModId, "config/cultures.json"));

            if (asset == null)
            {
                api.Logger.Error("[F&F] config/cultures.json missing. Villagers will be nameless.");
                return;
            }

            try
            {
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, Culture>>(asset.ToText());
                foreach (var kv in loaded)
                {
                    byCode[kv.Key] = kv.Value;
                }
                codes = new string[byCode.Count];
                byCode.Keys.CopyTo(codes, 0);

                api.Logger.Notification("[F&F] Loaded {0} cultures: {1}", codes.Length, string.Join(", ", codes));
            }
            catch (Exception e)
            {
                api.Logger.Error("[F&F] cultures.json failed to parse: {0}", e.Message);
            }
        }

        public Culture Get(string code)
        {
            if (code != null && byCode.TryGetValue(code, out Culture c)) return c;
            if (byCode.TryGetValue(DefaultCulture, out Culture fallback)) return fallback;
            return null;
        }

        public bool Has(string code) => code != null && byCode.ContainsKey(code);
    }
}
