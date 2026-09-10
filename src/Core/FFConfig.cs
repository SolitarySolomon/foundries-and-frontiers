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
        [JsonProperty] public ScheduleConfig Schedule = new ScheduleConfig();
        [JsonProperty] public PerformanceConfig Performance = new PerformanceConfig();
        [JsonProperty] public PlotConfig Plots = new PlotConfig();
        [JsonProperty] public WorkConfig Work = new WorkConfig();
        [JsonProperty] public BuildConfig Build = new BuildConfig();

        public class MovementConfig
        {
            [JsonProperty] public float Stroll = 0.024f;
            [JsonProperty] public float Walk = 0.030f;
            [JsonProperty] public float Laden = 0.024f;
            [JsonProperty] public float Run = 0.045f;

            /// <summary>At or above this speed the run animation is used instead of walk.</summary>
            [JsonProperty] public float RunAnimationThreshold = 0.035f;

            /// <summary>
            /// How much of the claim a villager will stroll across, as a fraction of the
            /// radius. Under 1 so a stroll rarely ends exactly on the boundary and hands
            /// them straight to the tether.
            /// </summary>
            [JsonProperty] public float WanderClaimFraction = 0.85f;

            /// <summary>How far an unaffiliated villager strolls from wherever they are.</summary>
            [JsonProperty] public int WanderRadiusWithoutVillage = 16;

            [JsonProperty] public double WanderCooldownMinSec = 6;
            [JsonProperty] public double WanderCooldownMaxSec = 22;

            /// <summary>Abandon a stroll that has not arrived after this long.</summary>
            [JsonProperty] public double WanderGiveUpSec = 45;
        }

        public class ChatterConfig
        {
            /// <summary>
            /// Per render tick. The engine's own default is 0.0005, which is roughly one
            /// utterance every 30 seconds PER VILLAGER - fine for one, a machine shop for
            /// twenty. Raise it if your villages feel too quiet.
            /// </summary>
            [JsonProperty] public float IdleTalkChance = 0.00009f;

            /// <summary>Villager voice volume, 0 to 1.</summary>
            [JsonProperty] public float VoiceVolume = 0.55f;

            [JsonProperty] public float GreetRangeBlocks = 5f;
            [JsonProperty] public double GreetCooldownSec = 90;

            [JsonProperty] public float ChatRangeBlocks = 6f;
            [JsonProperty] public double ChatCooldownSec = 150;
            [JsonProperty] public double ChatStartChance = 0.04;

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

            /// <summary>
            /// Chance an ordinary villager is bold enough to fight back rather than run.
            /// Low on purpose: a village where every farmer swings at a drifter is a
            /// village that loses its farmers. Guards ignore this and are always bold.
            /// </summary>
            [JsonProperty] public double BoldChance = 0.2;

            /// <summary>Damage a villager with no weapon does when it fights back.</summary>
            [JsonProperty] public float UnarmedDamage = 1.5f;

            /// <summary>Seconds between swings.</summary>
            [JsonProperty] public float AttackIntervalSec = 1.5f;

            /// <summary>How long being hurt keeps a villager frightened or angry.</summary>
            [JsonProperty] public double ThreatMemorySec = 12;

            /// <summary>How close they need to be to swing.</summary>
            [JsonProperty] public float AttackRangeBlocks = 2.2f;

            /// <summary>Extra damage per tool tier they happen to be holding.</summary>
            [JsonProperty] public float DamagePerToolTier = 0.75f;

            /// <summary>
            /// Health fraction below which a fighter will fall back, but only if there is
            /// somebody to fall back behind. Alone, they fight on.
            /// </summary>
            [JsonProperty] public float RetreatBelowHealthFraction = 0.4f;

            /// <summary>How near another fighter has to be to count as support.</summary>
            [JsonProperty] public float SupportRangeBlocks = 12f;

            /// <summary>Work rate by tool tier: none, copper, bronze, iron, steel.</summary>
            [JsonProperty] public float[] WorkRateByToolTier = { 0.55f, 1.00f, 1.25f, 1.45f, 1.55f };
        }

        public class VillageConfig
        {
            /// <summary>
            /// How far a village claims out from its centre, by tier 0 to 5. A claim is
            /// a square, so a radius of 32 is a 65 block wide box.
            ///
            /// Six entries because there are six tiers. A tier-5 town keeps sprawling
            /// after this by appending grid modules rather than by claiming further out.
            /// </summary>
            [JsonProperty] public int[] ClaimRadiusByTier = { 24, 32, 44, 58, 74, 96 };

            /// <summary>How far the claim reaches above the centre block.</summary>
            [JsonProperty] public int ClaimHeightAbove = 40;

            /// <summary>And below, which is what a cellar or a mine head needs later.</summary>
            [JsonProperty] public int ClaimDepthBelow = 24;

            /// <summary>
            /// Two village centres closer than this are refused. Big enough that even a
            /// pair of tier 6 claims cannot overlap.
            /// </summary>
            [JsonProperty] public float MinBlocksBetweenCentres = 220f;

            /// <summary>
            /// How much of a day a village must have been loaded for before that day is
            /// allowed into its measured production history. Below this the day is not
            /// recorded as zero production, it is not recorded at all.
            /// </summary>
            [JsonProperty] public float ObservedDayThreshold = 0.5f;

            /// <summary>
            /// How much of a dead village's stores are still in the crate when you find
            /// it. A settlement does not fail with a full granary, so the rest is assumed
            /// eaten, carried off by the survivors, or spoiled where it stood.
            /// </summary>
            [JsonProperty] public float RuinLootFraction = 0.4f;

            /// <summary>
            /// Food value of one serving of a cooked meal. A pot of stew is worth this
            /// times however many helpings are left in it, which is why a nearly empty
            /// pot and a full one are not worth the same to a village.
            /// </summary>
            /// <summary>
            /// Food value of one serving of a cooked meal.
            ///
            /// Kept at or below what the ingredients were worth on purpose. At two, a
            /// single carrot worth one became a one serving meal worth two, and a village
            /// with a cook could print food by passing it through the crate.
            /// </summary>
            [JsonProperty] public float MealServingValue = 1f;

            /// <summary>How far above the village centre a facility scan looks.</summary>
            [JsonProperty] public int FacilityScanUp = 20;

            /// <summary>And below, for cellars and anything dug in.</summary>
            [JsonProperty] public int FacilityScanDown = 10;

            /// <summary>
            /// How far outside its claim a villager may drift before it turns round and
            /// walks home. A little slack so they can round a corner without being yanked.
            /// </summary>
            [JsonProperty] public int TetherSlackBlocks = 20;

            /// <summary>
            /// Stone the village spends putting its cairn back up.
            ///
            /// Repairs used to be free, which meant a player could break a village's
            /// storehouse every morning and it would appear again by lunchtime out of
            /// nothing. A village that cannot pay stays broken.
            /// </summary>
            [JsonProperty] public float MarkerRepairStone = 8f;

            /// <summary>Wood the village spends rebuilding its storehouse.</summary>
            [JsonProperty] public float StorehouseRepairWood = 16f;
        }

        public class ScheduleConfig
        {
            /// <summary>Hour of day work begins. The game runs a 24 hour clock.</summary>
            [JsonProperty] public float WorkStartHour = 7f;

            /// <summary>And ends. Between this and sleeping, villagers are off duty.</summary>
            [JsonProperty] public float WorkEndHour = 19f;

            /// <summary>Hour they head for bed.</summary>
            [JsonProperty] public float SleepStartHour = 21f;

            /// <summary>And get up. Wraps past midnight, so this is a small number.</summary>
            [JsonProperty] public float SleepEndHour = 6f;

            /// <summary>
            /// Temporal stability below which a storm counts as bad enough to hide from.
            /// The game's own figure, so villagers shelter during the storms a player
            /// would shelter from rather than during ones we invented.
            /// </summary>
            [JsonProperty] public float ShelterBelowStability = 0.7f;

            /// <summary>How close to their bed counts as being in it, if mounting fails.</summary>
            [JsonProperty] public float BedArrivalBlocks = 1.8f;
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

        public class PlotConfig
        {
            /// <summary>
            /// Half the width of a new plot, by kind, in the order the enum declares
            /// them: woodlot, field, pasture, quarry, clay pit, mine head, terrace.
            /// A half size of 6 makes a 13x13 plot.
            /// </summary>
            [JsonProperty] public int[] HalfSizeByKind = { 8, 6, 7, 5, 4, 2, 6 };

            /// <summary>How many people each kind of plot has room for.</summary>
            [JsonProperty] public int[] WorkerCapByKind = { 2, 2, 1, 2, 1, 2, 3 };

            /// <summary>
            /// How many of each kind a village may hold, by tier. A hamlet with four
            /// quarries is not a hamlet. Indexed by tier, then by kind.
            /// </summary>
            [JsonProperty] public int[] MaxPlotsByTier = { 2, 4, 6, 9, 12, 16 };

            /// <summary>Keep plots this far apart, so work on one does not land on another.</summary>
            [JsonProperty] public int SeparationBlocks = 2;

            /// <summary>Keep plots this far inside the claim edge.</summary>
            [JsonProperty] public int ClaimEdgeMarginBlocks = 6;

            /// <summary>
            /// Keep plots this far from the village centre, so nobody sites a quarry on
            /// the town square. Smaller than it sounds: it is measured from the cairn.
            /// </summary>
            [JsonProperty] public int CentreClearanceBlocks = 10;

            /// <summary>
            /// The most the ground may rise and fall across a plot before it is rejected.
            /// A field on a cliff is not a field.
            /// </summary>
            [JsonProperty] public int FlatnessTolerance = 5;

            /// <summary>How many candidate sites to try before giving up on a plot.</summary>
            [JsonProperty] public int SiteAttempts = 60;

            /// <summary>
            /// How many columns to sample when scoring a candidate. Every column would be
            /// exact and pointless: a 17x17 woodlot is 289 height lookups per candidate
            /// per attempt, and the answer barely moves.
            /// </summary>
            [JsonProperty] public int SampleColumns = 24;

            /// <summary>
            /// A plot nobody has worked for this many days is called exhausted rather
            /// than left on the books as active forever.
            /// </summary>
            [JsonProperty] public int IdleDaysBeforeExhausted = 20;
        }

        public class WorkConfig
        {
            /// <summary>How close a worker must be to a block to act on it.</summary>
            [JsonProperty] public float ReachBlocks = 2.6f;

            /// <summary>
            /// Seconds of work for one block, before tools and skill. Deliberately slow:
            /// a villager who strips a forest in a minute is a machine, not a person.
            /// </summary>
            [JsonProperty] public float SecondsPerBlock = 2.2f;

            /// <summary>Each tool tier takes this fraction off the time.</summary>
            [JsonProperty] public float ToolTierSpeedBonus = 0.12f;

            /// <summary>Fastest a villager may ever work, as a fraction of the base time.</summary>
            [JsonProperty] public float MinWorkTimeFraction = 0.35f;

            /// <summary>
            /// Carry this much before heading for the storehouse. Lower means more
            /// walking and a village that looks busier; higher means fewer trips.
            /// </summary>
            [JsonProperty] public int HaulAtCarriedCount = 12;

            /// <summary>Give up on a job site after this long and pick another.</summary>
            [JsonProperty] public float GiveUpAfterSec = 120f;

            /// <summary>How far a job may look outside its plot for a target. Zero means never.</summary>
            [JsonProperty] public int SearchSlackBlocks = 2;

            /// <summary>Replant this fraction of what gets felled, when the village has seed.</summary>
            [JsonProperty] public float ReplantChance = 1f;

            /// <summary>
            /// Saplings a lumberjack keeps from each tree they fell, regardless of what
            /// the leaves happened to drop.
            ///
            /// Vanilla sapling drops are rare enough that a woodlot living off them thins
            /// out and never recovers, which makes the plot pointless. Two per tree means
            /// a woodlot slowly thickens, which is what a managed one should do. Set it to
            /// zero for vanilla drop rates only.
            /// </summary>
            [JsonProperty] public int SaplingsPerTree = 2;

            /// <summary>
            /// The tier at which farmers start rotating crops against the ground's real
            /// nitrogen, phosphorus and potassium instead of sowing at random.
            ///
            /// Set it to 0 to have every village rotate from the day it is founded, or to
            /// something above 5 to turn rotation off entirely.
            /// </summary>
            [JsonProperty] public int RotateCropsFromTier = 2;

            /// <summary>Pause between finishing one block and starting the next, in seconds.</summary>
            [JsonProperty] public float BetweenBlocksSec = 0.6f;
        }

        public class BuildConfig
        {
            /// <summary>How many buildings one village may have going up at once.</summary>
            [JsonProperty] public int MaxSitesAtOnce = 1;

            /// <summary>Candidate spots to try before giving up on siting a building.</summary>
            [JsonProperty] public int SiteAttempts = 80;

            /// <summary>
            /// The most the ground may rise and fall across a footprint. Stricter than a
            /// field: a farm can be lumpy and a house cannot.
            /// </summary>
            [JsonProperty] public int FlatnessTolerance = 2;

            /// <summary>Keep buildings this far apart, and this far off any plot.</summary>
            [JsonProperty] public int SeparationBlocks = 2;

            /// <summary>
            /// Blocks a builder lays per trip to the site. Low on purpose: a building
            /// that goes up over in game days is the point of the whole phase, and one
            /// that appears in a puff is a schematic being pasted.
            /// </summary>
            [JsonProperty] public int BlocksPerVisit = 8;

            /// <summary>Seconds between one batch of blocks and the next.</summary>
            [JsonProperty] public float SecondsPerBatch = 3f;

            /// <summary>How close a builder stands to the site while working.</summary>
            [JsonProperty] public float WorkFromBlocks = 3.5f;
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
