using Newtonsoft.Json;
using Vintagestory.API.Common;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Every tunable number in the mod, in one file the player can edit.
    ///
    /// Written now rather than at the end because the design's own rule is that every
    /// threshold is config, and there were already 35 hardcoded values by the end of
    /// Phase A. Retrofitting fifty scattered constants later is far more work than
    /// starting from a config object and adding to it - and it means balance passes
    /// don't need a rebuild.
    ///
    /// Lives at VintagestoryData/ModConfig/foundriesfrontiers.json, so it survives a mod
    /// update. Missing values fall back to the defaults declared here.
    /// </summary>
    public class FFConfig
    {
        public const string FileName = "foundriesfrontiers.json";

        [JsonProperty] public MovementConfig Movement = new MovementConfig();
        [JsonProperty] public ChatterConfig Chatter = new ChatterConfig();
        [JsonProperty] public VillagerConfig Villager = new VillagerConfig();
        [JsonProperty] public VillageConfig Village = new VillageConfig();
        [JsonProperty] public PerformanceConfig Performance = new PerformanceConfig();

        public class MovementConfig
        {
            [JsonProperty] public float Stroll = 0.024f;
            [JsonProperty] public float Walk = 0.030f;
            [JsonProperty] public float Laden = 0.024f;
            [JsonProperty] public float Run = 0.045f;

            /// <summary>At or above this speed the run animation is used instead of walk.</summary>
            [JsonProperty] public float RunAnimationThreshold = 0.035f;
        }

        public class ChatterConfig
        {
            /// <summary>
            /// Per render tick. The engine's own default is 0.0005, which is roughly one
            /// utterance every 30 seconds PER VILLAGER - fine for one, a machine shop for
            /// twenty. Raise it if your villages feel too quiet.
            /// </summary>
            [JsonProperty] public float IdleTalkChance = 0.00012f;

            /// <summary>Villager voice volume, 0 to 1.</summary>
            [JsonProperty] public float VoiceVolume = 0.55f;

            [JsonProperty] public float GreetRangeBlocks = 5f;
            [JsonProperty] public double GreetCooldownSec = 90;

            [JsonProperty] public float ChatRangeBlocks = 6f;
            [JsonProperty] public double ChatCooldownSec = 150;
            [JsonProperty] public double ChatStartChance = 0.05;

            /// <summary>Nobody within this range means no conversations are started at all.</summary>
            [JsonProperty] public float ChatWitnessRangeBlocks = 24f;

            /// <summary>How far away a player can still read what a villager says.</summary>
            [JsonProperty] public double SpeechTextRangeBlocks = 14;

            /// <summary>Set false to silence villager speech in chat entirely.</summary>
            [JsonProperty] public bool SpeechInChat = true;
        }

        public class VillagerConfig
        {
            [JsonProperty] public int CarryCapacity = 16;

            /// <summary>Work rate by tool tier: none, copper, bronze, iron, steel.</summary>
            [JsonProperty] public float[] WorkRateByToolTier = { 0.55f, 1.00f, 1.25f, 1.45f, 1.55f };
        }

        public class VillageConfig
        {
            /// <summary>
            /// How far a village claims out from its centre, by tier 0 to 6. A claim is
            /// a square, so a radius of 32 is a 65 block wide box.
            /// </summary>
            [JsonProperty] public int[] ClaimRadiusByTier = { 24, 32, 40, 52, 64, 80, 96 };

            /// <summary>How far the claim reaches above the centre block.</summary>
            [JsonProperty] public int ClaimHeightAbove = 40;

            /// <summary>And below, which is what a cellar or a mine head needs later.</summary>
            [JsonProperty] public int ClaimDepthBelow = 24;

            /// <summary>
            /// Two village centres closer than this are refused. Big enough that even a
            /// pair of tier 6 claims cannot overlap.
            /// </summary>
            [JsonProperty] public float MinBlocksBetweenCentres = 220f;
        }

        public class PerformanceConfig
        {
            /// <summary>
            /// Multiplies every AI task's think interval. Above 1 makes villagers think
            /// less often and cost less; below 1 makes them more responsive and cost more.
            /// The first dial to reach for on a struggling server.
            /// </summary>
            [JsonProperty] public float ThinkIntervalMultiplier = 1.0f;

            /// <summary>Think intervals multiply by this when no player is in range.</summary>
            [JsonProperty] public float UnobservedThrottle = 8f;

            /// <summary>Beyond this distance from any player, tasks are throttled.</summary>
            [JsonProperty] public float ObservedRangeBlocks = 48f;
        }

        // --- loading ---------------------------------------------------------------

        private static FFConfig current = new FFConfig();

        /// <summary>The live config. Never null.</summary>
        public static FFConfig Current => current;

        public static void Load(ICoreAPI api)
        {
            try
            {
                FFConfig loaded = api.LoadModConfig<FFConfig>(FileName);
                if (loaded == null)
                {
                    // First run: write the defaults out so there is something to edit.
                    current = new FFConfig();
                    api.StoreModConfig(current, FileName);
                    api.Logger.Notification("[F&F] Wrote default config to ModConfig/{0}", FileName);
                }
                else
                {
                    current = loaded;
                    // Rewrite it so any newly-added settings appear with their defaults
                    // rather than silently missing from an older file.
                    api.StoreModConfig(current, FileName);
                    api.Logger.Notification("[F&F] Loaded config from ModConfig/{0}", FileName);
                }
            }
            catch (System.Exception e)
            {
                current = new FFConfig();
                api.Logger.Error("[F&F] Config failed to load ({0}). Using defaults.", e.Message);
            }
        }
    }
}
