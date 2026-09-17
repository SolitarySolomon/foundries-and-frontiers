using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Vintagestory.API.Common;

namespace FoundriesFrontiers
{
    /// <summary>One culture's names, voice, dress and principles, loaded from config/cultures/.</summary>
    public class Culture
    {
        /// <summary>
        /// Overrides the file name as this culture's code. Optional, and normally left
        /// out: hovel-norse-a is readable precisely because the file is called norse.json.
        /// </summary>
        [JsonProperty] public string Code;

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

        /// <summary>
        /// Kinds of ground this culture will not work, by <see cref="EnumPlotKind"/> name.
        ///
        /// This is a refusal rather than an inability: the Woodfolk can quarry and choose
        /// not to, and a village of them will find another way or go without. Anything
        /// listed here is rejected at siting with a reason a player can read, so the
        /// culture's principles are visible rather than being a number nobody sees.
        /// </summary>
        [JsonProperty] public string[] RefusesPlots = Array.Empty<string>();

        /// <summary>
        /// The highest tier this culture will climb to, or 0 for no ceiling.
        ///
        /// Also a choice rather than a limit. A Woodfolk village stops at three because it
        /// has what it wants, which is why their tier 3 buildings should be the best
        /// looking in the mod: a people who stopped, not a people who failed.
        /// </summary>
        [JsonProperty] public int MaxTier;

        /// <summary>Whether this culture refuses to work that kind of ground.</summary>
        public bool RefusesPlot(EnumPlotKind kind)
        {
            if (RefusesPlots == null) return false;
            foreach (string s in RefusesPlots)
            {
                if (string.Equals(s, kind.ToString(), StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>This tier, brought down to the culture's ceiling if it has one.</summary>
        public int CapTier(int tier) => MaxTier > 0 && tier > MaxTier ? MaxTier : tier;

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
            => RandomLine(utteranceCode, null, rand);

        /// <summary>
        /// A line for this situation, in this personality's voice if it has one.
        ///
        /// Looks for "remark-whiny" and falls back to "remark", so a new personality
        /// works immediately with the lines that already exist and can be given its own
        /// voice one line at a time rather than needing a full set written up front.
        /// </summary>
        public string RandomLine(string utteranceCode, string tone, Random rand)
        {
            if (Lines == null) return null;

            if (!string.IsNullOrEmpty(tone)
                && Lines.TryGetValue(utteranceCode + "-" + tone, out string[] toned)
                && toned != null && toned.Length > 0)
            {
                return toned[rand.Next(toned.Length)];
            }

            if (Lines.TryGetValue(utteranceCode, out string[] pool) && pool != null && pool.Length > 0)
            {
                return pool[rand.Next(pool.Length)];
            }
            return null;
        }

        /// <summary>Whether this culture has anything to say about a situation at all.</summary>
        public bool HasLinesFor(string utteranceCode)
            => Lines != null && Lines.TryGetValue(utteranceCode, out string[] pool) && pool != null && pool.Length > 0;
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

            // One file per culture, in a folder, rather than one file holding all of them.
            //
            // The reason is other people's mods. Vintage Story merges asset folders across
            // every mod that ships into a domain, so a folder means somebody can add a
            // culture by dropping a file into assets/foundriesfrontiers/config/cultures/
            // from their own mod, with no patch and no fork. One big file makes that
            // impossible: two mods shipping cultures.json means one of them wins.
            // The trailing slash matters. GetMany does an ordinal prefix match with no
            // path boundary check, so "config/cultures" also matches config/cultures.json
            // and config/cultures-anything. The old monolithic file deserialises happily
            // as a Culture with every field empty, which would register a nameless culture
            // called "cultures" that /ff village create could then pick.
            List<IAsset> all = api.Assets.GetMany("config/cultures/", FoundriesFrontiersMod.ModId);

            if (all == null || all.Count == 0)
            {
                api.Logger.Error(
                    "[F&F] No cultures found in config/cultures/. Villagers will be nameless.");
                return;
            }

            int failed = 0;

            foreach (IAsset asset in all)
            {
                string file = asset?.Name;
                if (file == null) continue;
                if (!file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;

                // The template is documentation, not a culture. Anything starting with an
                // underscore is skipped, which gives modders a place to keep notes too.
                if (file.StartsWith("_")) continue;

                string code = file.Substring(0, file.Length - 5);

                try
                {
                    var culture = JsonConvert.DeserializeObject<Culture>(asset.ToText());
                    if (culture == null)
                    {
                        api.Logger.Warning("[F&F] {0} parsed to nothing and was skipped.", file);
                        failed++;
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(culture.Code)) code = culture.Code.Trim();

                    if (byCode.ContainsKey(code))
                    {
                        api.Logger.Warning(
                            "[F&F] More than one culture calls itself '{0}'. Keeping the first.", code);
                        continue;
                    }

                    // A missing key keeps the field initialiser, but an explicit null in
                    // the file clobbers it, and every consumer would then have to guard.
                    // Normalise once here instead, because the next person to write a
                    // culture file is a stranger and "lines": null should not take out
                    // the command somebody runs to find out why cultures look wrong.
                    Normalise(culture);
                    byCode[code] = culture;
                }
                catch (Exception e)
                {
                    // One bad file must not take the rest with it, or a modder's typo
                    // leaves a player with a mod that has no cultures at all.
                    api.Logger.Error("[F&F] Culture {0} would not parse: {1}", file, e.Message);
                    failed++;
                }
            }

            codes = new string[byCode.Count];
            byCode.Keys.CopyTo(codes, 0);

            if (codes.Length == 0)
            {
                api.Logger.Error("[F&F] Every culture file failed to load. Villagers will be nameless.");
                return;
            }

            api.Logger.Notification("[F&F] Loaded {0} culture(s): {1}{2}",
                codes.Length, string.Join(", ", codes),
                failed > 0 ? " (" + failed + " failed)" : "");
        }

        /// <summary>
        /// Replaces nulls with empties so nothing downstream has to null check a field a
        /// culture file left explicitly null.
        /// </summary>
        private static void Normalise(Culture c)
        {
            if (c.MaleNames == null) c.MaleNames = Array.Empty<string>();
            if (c.FemaleNames == null) c.FemaleNames = Array.Empty<string>();
            if (c.FamilyNames == null) c.FamilyNames = Array.Empty<string>();
            if (c.VillageNames == null) c.VillageNames = Array.Empty<string>();
            if (c.RefusesPlots == null) c.RefusesPlots = Array.Empty<string>();
            if (c.Lines == null) c.Lines = new Dictionary<string, string[]>();
            if (c.Wardrobe == null) c.Wardrobe = new Dictionary<string, string[]>();
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
