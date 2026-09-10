using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Walks a villager back inside their claim when they have drifted out of it.
    ///
    /// This is the tether. Until now a villager was held near wherever it happened to
    /// spawn, which is right by accident on the day it is born and wrong from then on.
    /// A village is a place, so the anchor is the claim.
    ///
    /// It deliberately does not fence them in. Leaving is allowed and will be required
    /// later for a job site outside the walls, a caravan, a raid, or running away. What
    /// it does is make leaving need a reason: with nothing else to do, a villager who
    /// finds themselves outside walks home.
    /// </summary>
    public class AiTaskVillagerReturnHome : FFTaskBase
    {
        private BlockPos target;
        private double startedAt;
        private double retryAt;
        private int failures;

        /// <summary>Wait this long before asking for a path again after one fails.</summary>
        private const double RetryDelaySeconds = 4;

        /// <summary>Give up rather than walk forever if the way home is blocked.</summary>
        private const double GiveUpAfterSeconds = 90;

        public AiTaskVillagerReturnHome(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
            : base(entity, taskConfig, aiConfig) { }

        protected override float ThinkIntervalSec => 3f;

        /// <summary>
        /// Runs whether or not anyone is watching. A villager quietly wandering off the
        /// edge of the world while nobody is looking is exactly the failure this exists
        /// to stop, and checking a distance every few seconds costs nothing.
        /// </summary>
        protected override float ObservedRangeBlocks => -1;

        /// <summary>
        /// How long to leave a stranded villager alone after giving up on walking them
        /// home.
        ///
        /// The give-up used to end the task and nothing else, and the engine offers a
        /// freed slot again on the next tick, so it restarted at once: a full pathfind
        /// every four seconds and a warning in the log every ninety, forever, for a
        /// villager with no route home. Waiting is not a fix for being stranded, but it
        /// is honest about it and it stops one lost villager costing the server real work.
        /// </summary>
        private const double GaveUpStandDownSec = 120;

        private double standDownUntil;

        protected override bool ShouldRun()
        {
            if (Villager == null || Villager.VillageId == 0) return false;
            if (entity.World.ElapsedMilliseconds / 1000.0 < standDownUntil) return false;

            // Somebody already told them where to be. That counts as a reason.
            if (Villager.GotoTarget != null) return false;

            Village village = Home;
            if (village == null) return false;

            return !WithinTether(village, entity.Pos.AsBlockPos);
        }

        protected override void OnStart()
        {
            Village village = Home;
            if (village == null) return;

            // Head for the centre rather than the nearest edge. Aiming at the boundary
            // leaves them standing on the line, one step from doing this again.
            target = village.Centre;
            startedAt = entity.World.ElapsedMilliseconds / 1000.0;
            retryAt = 0;

            // failures deliberately survives a restart now. Staged journeys only begin
            // after a couple of refusals, and zeroing the count every time the task
            // started again made that recovery unreachable for exactly the villager who
            // needed it: the one too far out to path home in one hop.

            Villager.OrderGoto(target, MoveSpeeds.Walk);

            entity.Api.Logger.Notification(
                "[F&F] {0} is {1} blocks outside {2} and heading home.",
                Villager.GivenName == "" ? "#" + entity.EntityId : Villager.GivenName,
                (int)village.HorizontalDistanceTo(entity.Pos.XYZ),
                village.Name);
        }

        protected override bool OnTick(float dt)
        {
            Village village = Home;
            if (village == null) return false;

            // Home means home, not the boundary.
            //
            // Ending the moment they cross the line left them standing on it, one step
            // from being dragged back, which kept them in exactly the region where the
            // job tasks and this one fight over the slot. Carry on to somewhere properly
            // inside instead.
            if (WellInside(village, entity.Pos.AsBlockPos))
            {
                Villager.CancelGoto();
                failures = 0;
                return false;
            }

            double now = entity.World.ElapsedMilliseconds / 1000.0;

            if (now - startedAt > GiveUpAfterSeconds)
            {
                Villager.CancelGoto();
                DevStats.Bump(DevStats.PathsFailed);
                entity.Api.Logger.Warning(
                    "[F&F] Gave up walking {0} home after {1} tries. Still {2} blocks out.",
                    Villager.GivenName == "" ? "#" + entity.EntityId : Villager.GivenName,
                    failures, (int)village.HorizontalDistanceTo(entity.Pos.XYZ));

                standDownUntil = now + GaveUpStandDownSec;
                return false;
            }

            // The walk finished or failed and they are still out here, so ask again.
            // Ordering the journey only once meant a single failed path left them
            // standing in a field for the rest of the timeout doing nothing at all.
            if (Villager.GotoTarget == null)
            {
                if (now < retryAt) return true;

                failures++;
                retryAt = now + RetryDelaySeconds;

                // Capped, because it survives a restart on purpose and an uncapped
                // counter turns the give-up message into nonsense ("after 66 tries") and
                // means every later trip skips straight to a staged journey whether or
                // not the direct one would have worked.
                if (failures > 6) failures = 3;

                // A long way from home is a long way for a pathfinder. After a couple of
                // refusals, aim at a point part of the way back instead and make the
                // journey in stages.
                BlockPos aim = failures <= 2 ? target : PartWayHome(village);
                Villager.OrderGoto(aim, MoveSpeeds.Walk);

                entity.Api.Logger.Notification(
                    "[F&F] Retry {0} getting {1} home, aiming at {2} ({3}).",
                    failures,
                    Villager.GivenName == "" ? "#" + entity.EntityId : Villager.GivenName,
                    aim, Villager.LastGotoResult);
            }

            return true;
        }

        protected override void OnStop(bool cancelled)
        {
            // Same rule as the sleep task: the walk is not this task's to tear up. It is
            // cancelled in OnTick when the villager is actually home, and being stopped
            // because something more important came along is not a reason to abandon a
            // journey that something else is carrying out.
            target = null;
        }

        /// <summary>
        /// A point between here and the village, on the ground. Splitting an impossible
        /// journey into possible ones beats standing still, and beats teleporting.
        /// </summary>
        private BlockPos PartWayHome(Village village)
        {
            Vec3d here = entity.Pos.XYZ;
            double toX = (here.X + village.CentreX) / 2;
            double toZ = (here.Z + village.CentreZ) / 2;

            var probe = new BlockPos((int)toX, 0, (int)toZ, 0);
            int y = entity.World.BlockAccessor.GetTerrainMapheightAt(probe);

            return y > 0 ? new BlockPos((int)toX, y + 1, (int)toZ, 0) : village.Centre;
        }

        private Village Home
            => (entity.Api as ICoreServerAPI)?.ModLoader
               .GetModSystem<VillageRegistry>()?.Get(Villager.VillageId);

        /// <summary>
        /// Inside the claim, plus a little slack so rounding a corner or stepping over a
        /// fence does not yank them back mid-stride.
        /// </summary>
        public static bool WithinTether(Village village, BlockPos pos)
        {
            int slack = FFConfig.Current.Village.TetherSlackBlocks;
            return InBox(village, pos, village.ClaimRadius + slack);
        }

        /// <summary>
        /// Properly back, rather than just over the line. The tether stops here, which is
        /// far enough in that a stroll does not immediately trip it again.
        /// </summary>
        private static bool WellInside(Village village, BlockPos pos)
        {
            int slack = FFConfig.Current.Village.TetherSlackBlocks;
            return InBox(village, pos, Math.Max(4, village.ClaimRadius + slack / 2));
        }

        private static bool InBox(Village village, BlockPos pos, int r)
        {
            return pos.X >= village.CentreX - r && pos.X <= village.CentreX + r
                && pos.Z >= village.CentreZ - r && pos.Z <= village.CentreZ + r;
        }

        public override string DebugLabel() => "going home";
    }
}
