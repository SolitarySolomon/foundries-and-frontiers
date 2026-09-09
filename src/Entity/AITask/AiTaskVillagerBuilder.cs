using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Puts up whatever the village has decided to build.
    ///
    /// Deliberately not a subclass of the work loop, even though it looks like one. Every
    /// other job finds a block, walks to it and takes it away. This one walks to a site
    /// and adds blocks to it, in an order the schematic chose, and it has no plot, no
    /// target search and nothing to carry home. Forcing it into the same base would have
    /// meant a base with two modes, and a base with two modes is two bases sharing a file.
    ///
    /// It outranks the digger, so a builder who has a site to work goes and works it
    /// rather than levelling ground for a building nobody has started.
    /// </summary>
    public class AiTaskVillagerBuilder : FFTaskBase
    {
        protected override float ThinkIntervalSec => 2f;

        /// <summary>Villages must build whether or not anyone is watching.</summary>
        protected override float ObservedRangeBlocks => -1f;

        private VillageBuildSite site;
        private double nextBatchAt;
        private double travelStartedAt;
        private double standDownUntil;
        private int failures;

        /// <summary>
        /// How long to leave a site alone after finding the village cannot pay for it.
        ///
        /// Without this the builder stood over an unaffordable site for the whole working
        /// day, every day, doing nothing. Worse than nothing: the task outranks the
        /// producing jobs, so the one villager who might have closed the shortfall was the
        /// one prevented from working.
        /// </summary>
        private const double CannotPayStandDownSec = 120;

        public AiTaskVillagerBuilder(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
            : base(entity, taskConfig, aiConfig) { }

        private Vintagestory.API.Server.ICoreServerAPI Sapi
            => entity.Api as Vintagestory.API.Server.ICoreServerAPI;

        private VillageRegistry Registry => Sapi?.ModLoader.GetModSystem<VillageRegistry>();

        private Village Home => Registry?.Get(Villager?.VillageId ?? 0);

        protected override bool ShouldRun()
        {
            if (Villager == null || Villager.Trade != EnumTrade.Builder) return false;
            if (Villager.VillageId == 0) return false;
            if (VillageSchedule.PhaseFor(entity.Api, entity.Pos.AsBlockPos) != EnumDayPhase.Work) return false;

            if (Now < standDownUntil) return false;

            Village village = Home;
            if (village == null) return false;

            return Registry.OpenSiteFor(village, entity.EntityId) != null;
        }

        protected override void OnStart()
        {
            Village village = Home;
            site = village == null ? null : Registry.OpenSiteFor(village, entity.EntityId);
            if (site == null) return;

            site.BuilderEntityId = entity.EntityId;
            travelStartedAt = Now;
            failures = 0;
            nextBatchAt = 0;

            Villager.OrderGoto(WorkSpot(site), MoveSpeeds.Walk);
        }

        protected override bool OnTick(float dt)
        {
            Village village = Home;
            if (village == null || site == null) return false;

            if (!site.IsOpen)
            {
                site.BuilderEntityId = 0;
                return false;
            }

            var cfg = FFConfig.Current.Build;
            BlockPos spot = WorkSpot(site);

            // Not there yet.
            if (entity.Pos.SquareDistanceTo(spot.ToVec3d().Add(0.5, 0, 0.5))
                > cfg.WorkFromBlocks * cfg.WorkFromBlocks)
            {
                // Timed from when the walk began, not from when the task did. Measuring
                // from task start meant a builder who had been working happily for three
                // minutes gave up the moment anything nudged them off the spot, instead of
                // walking the four blocks back.
                if (Now - travelStartedAt > FFConfig.Current.Work.GiveUpAfterSec)
                {
                    entity.Api.Logger.Notification(
                        "[F&F] {0} could not get to the build site at {1}.", Who(), site.Origin);
                    Villager.CancelGoto();
                    site.BuilderEntityId = 0;
                    return false;
                }

                // The walk ended without arriving, so ask again rather than standing here.
                if (Villager.GotoTarget == null)
                {
                    if (++failures > 3)
                    {
                        Villager.CancelGoto();
                        site.BuilderEntityId = 0;
                        return false;
                    }
                    Villager.OrderGoto(spot, MoveSpeeds.Walk);
                }
                return true;
            }

            Villager.CancelGoto();

            // Arrived, so the travel clock starts again from here for the next leg and
            // the failed path count means nothing any more.
            travelStartedAt = Now;
            failures = 0;

            // Materials come out of the ledger once, before anything is placed. A village
            // that cannot cover it stalls here, visibly, with the shortfall written on
            // the site for anyone who looks.
            if (!site.Paid)
            {
                if (!Registry.TryPayFor(village, site))
                {
                    // Stand down and go and do something else. A builder who cannot start
                    // is still a pair of hands, and holding the top priority slot while
                    // waiting for wood that only work brings in is a village that stalls
                    // itself.
                    entity.Api.Logger.VerboseDebug(
                        "[F&F] {0} cannot start the {1}: {2}", Who(), site.PlanCode, site.Holdup);

                    standDownUntil = Now + CannotPayStandDownSec;
                    site.BuilderEntityId = 0;
                    return false;
                }
            }

            if (Now < nextBatchAt) return true;
            nextBatchAt = Now + cfg.SecondsPerBatch;

            entity.AnimManager?.StartAnimation("hit");

            int placed = Registry.PlaceSlice(village, site, cfg.BlocksPerVisit);
            if (placed == 0 && site.State != EnumBuildState.Done)
            {
                entity.Api.Logger.Notification(
                    "[F&F] {0} could not place anything at {1}: {2}",
                    Who(), site.Origin, site.Holdup == "" ? "no reason given" : site.Holdup);

                standDownUntil = Now + CannotPayStandDownSec;
                site.BuilderEntityId = 0;
                return false;
            }

            if (site.State == EnumBuildState.Done)
            {
                site.BuilderEntityId = 0;
                return false;
            }

            return true;
        }

        protected override void OnStop(bool cancelled)
        {
            // Let the site go so somebody else can pick it up, but leave the walk alone:
            // it belongs to ffgoto and this task may well be about to start again.
            if (site != null && site.BuilderEntityId == entity.EntityId) site.BuilderEntityId = 0;
        }

        /// <summary>
        /// Where to stand. Just outside the corner rather than inside the footprint, so a
        /// builder does not end up entombed in their own wall.
        /// </summary>
        private static BlockPos WorkSpot(VillageBuildSite s)
            => new BlockPos(s.X - 1, s.Y, s.Z - 1, 0);

        private double Now => entity.World.ElapsedMilliseconds / 1000.0;

        private string Who()
            => Villager.GivenName == "" ? "#" + entity.EntityId : Villager.GivenName;

        public override string DebugLabel()
        {
            if (site == null) return "builder";
            if (!site.Paid) return "builder: waiting on materials";
            return "builder: " + site.Placed + "/" + site.Total;
        }
    }
}
