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

        protected override bool ShouldRun()
        {
            if (Villager == null || Villager.VillageId == 0) return false;

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

            Villager.OrderGoto(target, MoveSpeeds.Walk);
        }

        protected override bool OnTick(float dt)
        {
            Village village = Home;
            if (village == null) return false;

            if (WithinTether(village, entity.Pos.AsBlockPos))
            {
                Villager.CancelGoto();
                return false;
            }

            double elapsed = entity.World.ElapsedMilliseconds / 1000.0 - startedAt;
            if (elapsed > GiveUpAfterSeconds)
            {
                Villager.CancelGoto();
                DevStats.Bump(DevStats.PathsFailed);
                return false;
            }

            // The goto task does the walking. This one only decides when it is done.
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

        private Village Home
            => (entity.Api as ICoreServerAPI)?.ModLoader
               .GetModSystem<VillageRegistry>()?.Get(Villager.VillageId);

        /// <summary>
        /// Inside the claim, plus a little slack so rounding a corner or stepping over a
        /// fence does not yank them back mid-stride.
        /// </summary>
        private static bool WithinTether(Village village, BlockPos pos)
        {
            int slack = FFConfig.Current.Village.TetherSlackBlocks;
            int r = village.ClaimRadius + slack;

            return pos.X >= village.CentreX - r && pos.X <= village.CentreX + r
                && pos.Z >= village.CentreZ - r && pos.Z <= village.CentreZ + r;
        }

        public override string DebugLabel() => "going home";
    }
}
