using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Vintagestory.API.Common;

namespace FoundriesFrontiers
{
    /// <summary>How a villager answers being hurt. The one axis that changes behaviour.</summary>
    public enum EnumCourage
    {
        /// <summary>Runs.</summary>
        Timid = 0,

        /// <summary>Stands and swings back.</summary>
        Bold = 1
    }

    /// <summary>
    /// One personality, loaded from config/personalities.json.
    ///
    /// A personality carries both halves of what makes someone feel like a person: how
    /// likely they are to fight rather than run, and how they talk. Keeping them as data
    /// means adding a new one is an edit to a file rather than a change to the mod.
    /// </summary>
    public class Personality
    {
        [JsonProperty] public string Name = "";

        /// <summary>Chance this sort of person stands and fights rather than running.</summary>
        [JsonProperty] public double BoldChance = 0.2;

        /// <summary>
        /// Multiplier on how often they speak unprompted. The spread is deliberately
        /// narrow: a quiet villager is not silent and a warm one is not exhausting.
        /// </summary>
        [JsonProperty] public float TalkFrequency = 1f;

        /// <summary>
        /// Tone suffix for dialogue. A line is looked up as "remark-whiny" first and
        /// falls back to "remark", so a new personality works with the lines that
        /// already exist and can be given its own voice a line at a time.
        /// </summary>
        [JsonProperty] public string Tone = "";

        /// <summary>Relative likelihood of a villager being born this way.</summary>
        [JsonProperty] public double Weight = 1;
    }

    /// <summary>
    /// The personality table.
    ///
    /// Rolled once at spawn and kept for life. Courage is rolled from the personality's
    /// own chance rather than being a separate axis, so "strong" people mostly fight and
    /// "whiny" ones mostly do not, without either being guaranteed. Guards are always
    /// bold whatever they were born as, because that is what being a guard means.
    /// </summary>
    public class PersonalitySystem : ModSystem
    {
        public const string DefaultCode = "even";

        private readonly Dictionary<string, Personality> byCode =
            new Dictionary<string, Personality>(StringComparer.OrdinalIgnoreCase);

        private readonly List<string> codes = new List<string>();
        private double totalWeight;

        public override bool ShouldLoad(EnumAppSide side) => true;

        public override double ExecuteOrder() => 0.26;

        public override void AssetsFinalize(ICoreAPI api)
        {
            base.AssetsFinalize(api);

            IAsset asset = api.Assets.TryGet(
                new AssetLocation(FoundriesFrontiersMod.ModId, "config/personalities.json"));

            if (asset == null)
            {
                api.Logger.Error("[F&F] config/personalities.json missing. Everyone will be the same.");
                return;
            }

            try
            {
                var loaded = JsonConvert.DeserializeObject<Dictionary<string, Personality>>(asset.ToText());
                foreach (var kv in loaded)
                {
                    // Comment keys. JSON has no comments, so the config uses "//" keys
                    // and they must not become personalities somebody can be born with.
                    if (kv.Key.StartsWith("//")) continue;

                    kv.Value.Name = string.IsNullOrEmpty(kv.Value.Name) ? kv.Key : kv.Value.Name;
                    byCode[kv.Key] = kv.Value;
                    codes.Add(kv.Key);
                    totalWeight += Math.Max(0, kv.Value.Weight);
                }
                api.Logger.Notification("[F&F] Loaded {0} personalities: {1}", codes.Count, string.Join(", ", codes));
            }
            catch (Exception e)
            {
                api.Logger.Error("[F&F] personalities.json failed to parse: {0}", e.Message);
            }
        }

        public Personality Get(string code)
        {
            if (code != null && byCode.TryGetValue(code, out Personality p)) return p;
            if (byCode.TryGetValue(DefaultCode, out Personality fallback)) return fallback;
            return new Personality();
        }

        public IReadOnlyList<string> Codes => codes;

        /// <summary>Picks a personality by weight.</summary>
        public string Roll(Random rand)
        {
            if (codes.Count == 0 || totalWeight <= 0) return DefaultCode;

            double roll = rand.NextDouble() * totalWeight;
            foreach (string code in codes)
            {
                roll -= Math.Max(0, byCode[code].Weight);
                if (roll <= 0) return code;
            }
            return codes[codes.Count - 1];
        }

        /// <summary>
        /// Whether this villager fights when hurt. Guards always do; everyone else rolls
        /// against the chance their personality carries.
        /// </summary>
        public EnumCourage RollCourage(string personalityCode, EnumTrade trade, Random rand)
        {
            if (AlwaysBold(trade)) return EnumCourage.Bold;
            return rand.NextDouble() < Get(personalityCode).BoldChance ? EnumCourage.Bold : EnumCourage.Timid;
        }

        /// <summary>Trades that never run, whatever they were born as.</summary>
        public static bool AlwaysBold(EnumTrade trade)
        {
            switch (trade)
            {
                case EnumTrade.Spearman:
                case EnumTrade.Swordsman:
                case EnumTrade.Archer:
                    return true;
                default:
                    return false;
            }
        }
    }
}
