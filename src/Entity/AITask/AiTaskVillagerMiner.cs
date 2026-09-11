using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Works a mine: opens a mouth in the ground once, surveys what is under it, then
    /// works the face at a rate the seam decides.
    ///
    /// **The ore stays in the ground.** This is the whole design and it is worth being
    /// blunt about, because the first version did the opposite. That one sank a real
    /// shaft, found real ore blocks and broke them, on the argument that inventing ore
    /// would be dishonest. It was the wrong trade: what it actually did was let a village
    /// quietly strip the seams a player might want to work, in a world they share. Ground
    /// is the player's. A village gets to shape its own plot and nothing beyond it.
    ///
    /// So the seam is **surveyed, not consumed**. The village reads the column under its
    /// mine head, counts what is genuinely down there, and remembers how rich it is. Every
    /// ore block it counted is still sitting in the world afterwards for whoever goes down
    /// with a pickaxe of their own.
    ///
    /// **The survey is what makes the percentage honest.** There is still a roll, and a
    /// village does still end up with ore nobody watched come out of a specific block. But
    /// the odds are not a number somebody typed: they are what that particular patch of
    /// ground actually holds. A village on a rich seam gets ore often, one on barren rock
    /// gets rubble, and the two are different because the world under them is different.
    /// That is the part that was worth protecting, and it survives.
    ///
    /// The survey reaches as deep as the village has learned to dig, so tiering up is a
    /// reason to look again and can turn a poor mine into a good one without moving it.
    ///
    /// A pickaxe is required and gets worn down.
    /// </summary>
    public class AiTaskVillagerMiner : AiTaskVillagerWorkings
    {
        protected override EnumTrade Trade => EnumTrade.Miner;

        protected override EnumPlotKind PlotKind => EnumPlotKind.MineHead;

        protected override int WantedDepth => FFConfig.Current.Work.MineMouthDepth;

        public AiTaskVillagerMiner(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
            : base(entity, taskConfig, aiConfig) { }

        /// <summary>A cap on one survey. It runs once per plot per tier, so it may be big.</summary>
        private const int MaxSurveyLookups = 24000;

        /// <summary>How long to leave a survey that read nothing before trying it again.</summary>
        private const double FailedSurveyRetrySec = 120;

        private double nextSurveyAt;

        /// <summary>
        /// A struck seam the villager could not carry at the time.
        ///
        /// A pair of hands holds one thing, so a miner already carrying stone who strikes
        /// ore has to choose. Throwing the ore away would quietly bias the rate below what
        /// the survey found; walking home on the spot every time means a rich seam spends
        /// its whole day on the path. So the strike is kept and paid out on the next shift
        /// the hands can take it. The odds come out exactly as surveyed either way.
        /// </summary>
        private bool struckOre;

        // --- the survey -----------------------------------------------------------------

        /// <summary>
        /// Reads the ground under the mine head and writes down what is in it.
        ///
        /// Once per plot, and again whenever the village has learned to dig deeper than it
        /// could last time it looked. Nothing is broken, moved or claimed: this is a
        /// person with a lamp counting what they can see.
        /// </summary>
        protected override void Prepare(Village village, VillagePlot plot)
        {
            if (village == null || plot == null) return;
            if (plot.SeamRichness >= 0 && plot.SeamTier >= village.Tier) return;
            if (Now < nextSurveyAt) return;

            IBlockAccessor ba = entity.World.BlockAccessor;
            int floor = SurveyFloor(village, plot);

            int rock = 0, ore = 0, looked = 0;
            var kinds = new Dictionary<string, int>();

            for (int x = plot.MinX; x <= plot.MaxX; x++)
            {
                for (int z = plot.MinZ; z <= plot.MaxZ; z++)
                {
                    for (int y = plot.Y - 1; y >= floor; y--)
                    {
                        if (++looked > MaxSurveyLookups) goto done;

                        Block block = ba.GetBlock(new BlockPos(x, y, z, 0));
                        if (block == null || block.Id == 0) continue;

                        bool isOre = block.BlockMaterial == EnumBlockMaterial.Ore
                                  || (block.Code?.Path?.StartsWith("ore-") ?? false);

                        if (isOre)
                        {
                            ore++;
                            string code = block.Code.ToString();
                            kinds[code] = kinds.TryGetValue(code, out int n) ? n + 1 : 1;
                        }
                        else if (block.BlockMaterial == EnumBlockMaterial.Stone)
                        {
                            rock++;
                        }
                    }
                }
            }

        done:
            // Not one block of rock in a column dozens deep is not a barren seam, it is a
            // survey that read unloaded chunks: an unloaded chunk answers "air" and looks
            // exactly like empty ground. Writing that down would stamp the plot barren
            // until the village tiers up, which for a village already at the top tier
            // means forever. So refuse to record it and look again later.
            if (rock + ore <= 0)
            {
                nextSurveyAt = Now + FailedSurveyRetrySec;
                entity.Api.Logger.VerboseDebug(
                    "[F&F] Survey of mine #{0} read no rock at all. Chunks not loaded? Will look again.",
                    plot.Id);
                return;
            }

            plot.SeamTier = village.Tier;
            plot.SeamRichness = ore / (float)(rock + ore);

            string best = null;
            int bestCount = 0;
            foreach (var kv in kinds)
            {
                if (kv.Value > bestCount) { bestCount = kv.Value; best = kv.Key; }
            }
            plot.SeamOre = best;

            entity.Api.Logger.Notification(
                "[F&F] {0} surveyed mine #{1} down to y{2}: {3} seam ({4} ore in {5} rock){6}.",
                Label(), plot.Id, floor, plot.SeamGrade, ore, rock + ore,
                best == null ? "" : ", mostly " + best);
        }

        /// <summary>
        /// How deep the village can see. Tier says how far it has learned to go, and the
        /// absolute floor says how far is safe at all. The stricter one wins.
        /// </summary>
        private static int SurveyFloor(Village village, VillagePlot plot)
        {
            var cfg = FFConfig.Current.Work;
            int[] table = cfg.MineDepthByVillageTier;

            // An empty array is a config somebody edited, not an impossible state, and
            // Math.Clamp(0, 0, -1) throws rather than clamping.
            int depth = 12;
            if (table != null && table.Length > 0)
            {
                depth = table[Math.Clamp(village?.Tier ?? 0, 0, table.Length - 1)];
            }

            return Math.Max(cfg.MineFloorY, plot.Y - Math.Max(4, depth));
        }

        // --- the face --------------------------------------------------------------------

        /// <summary>
        /// A shift at the face. Ore at the odds the survey found, rubble otherwise.
        ///
        /// The roll is against real ground. A seam with one ore block in fifty is a rich
        /// one by Vintage Story's standards, so the raw share is scaled up into something
        /// a day's work can actually produce, and capped so that even the best ground does
        /// not turn a mine into a metal tap.
        /// </summary>
        protected override ItemStack YieldAtFace(Village village, VillagePlot plot)
        {
            if (plot == null) return null;

            BlockPos face = FacePos(plot);
            if (face == null) return null;

            var cfg = FFConfig.Current.Work;
            float chance = plot.SeamRichness <= 0
                ? 0f
                : Math.Min(cfg.MineOreChanceCap, plot.SeamRichness * cfg.SeamRichnessScale);

            bool hit = struckOre
                    || (chance > 0 && entity.World.Rand.NextDouble() < chance);

            if (!hit || plot.SeamOre == null) return Rubble(face);

            Block ore = entity.World.GetBlock(new AssetLocation(plot.SeamOre));
            ItemStack got = SampleDropOf(ore, face);

            // Deliberately no quiet fallback to rubble. A seam that struck ore and could
            // not turn it into anything is a broken ore code, and handing back stone
            // instead would hide that for weeks.
            if (got == null)
            {
                entity.Api.Logger.Warning(
                    "[F&F] Mine #{0} has seam ore '{1}' that gives nothing. Re-survey it.",
                    plot.Id, plot.SeamOre);
                plot.SeamOre = null;
                struckOre = false;
                return null;
            }

            // Hands already hold rubble the ore will not join, and not much of it. Keep
            // the strike for the next shift rather than losing it or dropping everything
            // and walking home a fifth loaded. A rich seam would otherwise spend its whole
            // day on the path between the face and the storehouse.
            if (Villager != null && Villager.IsCarrying
                && !Villager.CarriedStack.Satisfies(got)
                && Villager.CarriedCount < HaulThreshold / 2)
            {
                struckOre = true;
                return Rubble(face);
            }

            struckOre = false;
            return got;
        }

        /// <summary>What the face gives on an ordinary shift: the rock it is cut into.</summary>
        private ItemStack Rubble(BlockPos face)
        {
            IBlockAccessor ba = entity.World.BlockAccessor;

            Block below = ba.GetBlock(face.DownCopy());
            if (below != null && below.BlockMaterial == EnumBlockMaterial.Stone)
            {
                return SampleDropOf(below, face);
            }

            for (int i = 0; i < BlockFacing.HORIZONTALS.Length; i++)
            {
                Vec3i n = BlockFacing.HORIZONTALS[i].Normali;
                Block side = ba.GetBlock(new BlockPos(face.X + n.X, face.Y + n.Y, face.Z + n.Z, 0));
                if (side != null && side.BlockMaterial == EnumBlockMaterial.Stone)
                {
                    return SampleDropOf(side, face);
                }
            }

            return null;
        }

        protected override void OnNothingToDo(Village village, VillagePlot plot)
        {
            base.OnNothingToDo(village, plot);

            if (plot != null && plot.State == EnumPlotState.Active)
            {
                entity.Api.Logger.VerboseDebug(
                    "[F&F] Mine #{0} has no face worth working. {1} seam.",
                    plot.Id, plot.SeamGrade);
            }
        }
    }
}
