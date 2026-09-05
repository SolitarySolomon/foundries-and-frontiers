using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Ambling about the village.
    ///
    /// Replaces the game's own wander task, which anchors to wherever an entity spawned.
    /// That is right by accident on the day a villager is born and wrong every day after,
    /// and it is why a village looked like a crowd standing where it was put rather than
    /// people living somewhere. A village is a place, so the anchor is the claim.
    ///
    /// This is the bottom of the pile. Anything with an actual reason to move outranks
    /// it, and it only fills the time when nothing else wants the villager.
    /// </summary>
    public class AiTaskVillagerWander : FFTaskBase
    {
        private double nextWanderAt;
        private double abandonAt;

        public AiTaskVillagerWander(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
            : base(entity, taskConfig, aiConfig) { }

        protected override float ThinkIntervalSec => 2f;

        /// <summary>Nobody watching means nobody to see them stroll. Throttle hard.</summary>
        protected override float ObservedRangeBlocks => 48f;
        protected override float UnobservedThrottle => 20f;

        protected override bool ShouldRun()
        {
            if (Villager == null) return false;

            // Something else is already moving them, or it is not a strolling hour.
            if (Villager.GotoTarget != null) return false;

            EnumDayPhase phase = VillageSchedule.PhaseFor(entity.Api, entity.Pos.AsBlockPos);
            if (phase == EnumDayPhase.Sleep || phase == EnumDayPhase.Shelter) return false;

            return entity.World.ElapsedMilliseconds / 1000.0 >= nextWanderAt;
        }

        protected override void OnStart()
        {
            var cfg = FFConfig.Current.Movement;
            BlockPos target = PickSomewhereToGo();

            nextWanderAt = entity.World.ElapsedMilliseconds / 1000.0
                         + entity.World.Rand.NextDouble()
                           * (cfg.WanderCooldownMaxSec - cfg.WanderCooldownMinSec)
                         + cfg.WanderCooldownMinSec;

            if (target == null) return;

            abandonAt = entity.World.ElapsedMilliseconds / 1000.0 + cfg.WanderGiveUpSec;
            Villager.OrderGoto(target, MoveSpeeds.Stroll);
        }

        protected override bool OnTick(float dt)
        {
            // Give up on a stroll that is not working out. Nothing important is riding
            // on it, and standing there failing to path somewhere looks worse than
            // simply going somewhere else in a minute.
            if (entity.World.ElapsedMilliseconds / 1000.0 > abandonAt)
            {
                Villager.CancelGoto();
                return false;
            }

            return Villager.GotoTarget != null;
        }

        protected override void OnStop(bool cancelled)
        {
            // The walk belongs to ffgoto. Being stopped because something better came
            // along is not a reason to tear it up.
        }

        /// <summary>
        /// Somewhere inside the claim, on the ground.
        ///
        /// A villager with no village strolls around wherever they happen to be, which
        /// is the right behaviour for an orphan: they have nowhere to belong yet.
        /// </summary>
        private BlockPos PickSomewhereToGo()
        {
            var registry = (entity.Api as ICoreServerAPI)?.ModLoader.GetModSystem<VillageRegistry>();
            Village village = Villager.VillageId != 0 ? registry?.Get(Villager.VillageId) : null;

            int cx, cz, radius;
            if (village != null)
            {
                cx = village.CentreX;
                cz = village.CentreZ;

                // Just inside the claim, so a stroll does not constantly end on the
                // boundary and hand them straight to the tether.
                radius = (int)(village.ClaimRadius * FFConfig.Current.Movement.WanderClaimFraction);
            }
            else
            {
                BlockPos here = entity.Pos.AsBlockPos;
                cx = here.X;
                cz = here.Z;
                radius = FFConfig.Current.Movement.WanderRadiusWithoutVillage;
            }

            if (radius < 2) radius = 2;

            for (int attempt = 0; attempt < 6; attempt++)
            {
                int x = cx + entity.World.Rand.Next(-radius, radius + 1);
                int z = cz + entity.World.Rand.Next(-radius, radius + 1);

                var probe = new BlockPos(x, 0, z, 0);
                int y = entity.World.BlockAccessor.GetTerrainMapheightAt(probe);
                if (y <= 0) continue;

                var target = new BlockPos(x, y + 1, z, 0);

                // Do not set off for somewhere already standing in. Picking the spot you
                // are on reads as a villager twitching rather than strolling.
                if (target.HorDistanceSqTo(entity.Pos.X, entity.Pos.Z) < 9) continue;

                return target;
            }

            return null;
        }

        public override string DebugLabel() => "wandering";
    }
}
