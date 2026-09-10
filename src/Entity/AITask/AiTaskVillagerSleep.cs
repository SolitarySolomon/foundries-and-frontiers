using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Goes to bed at dusk and gets up at dawn.
    ///
    /// The first task that makes a village look like it has a routine rather than a
    /// crowd, and the frame every job hangs off: a job only runs during working hours,
    /// and this is what owns the rest of the day.
    ///
    /// A villager with no bed still stops working and stands down. Sleeping rough is a
    /// visible symptom of a village that has not built enough houses, which is exactly
    /// the pressure the design wants housing to apply.
    /// </summary>
    public class AiTaskVillagerSleep : FFTaskBase
    {
        private BlockPos bedPos;
        private bool mounted;
        private double gaveUpChasingAt;
        private double retryAt;

        /// <summary>Stop trying to reach a bed after this long and settle where you are.</summary>
        private const double WalkTimeoutSeconds = 120;

        public AiTaskVillagerSleep(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
            : base(entity, taskConfig, aiConfig) { }

        protected override float ThinkIntervalSec => 2f;

        /// <summary>
        /// Runs unwatched. A village that only goes to bed when somebody is looking is
        /// a village that never sleeps, and the daily tick reads this state.
        /// </summary>
        protected override float ObservedRangeBlocks => -1;

        private EnumDayPhase Phase => VillageSchedule.PhaseFor(entity.Api, entity.Pos.AsBlockPos);

        /// <summary>
        /// Wait this long before trying for a bed again after failing to reach one.
        ///
        /// Ending the task is not enough on its own. This task outranks both the tether
        /// and every job, and the engine re-offers a slot the instant it frees, so a
        /// villager whose bed was unreachable simply retook the slot on the next tick and
        /// started the whole chase again. Standing down is what actually lets the tether
        /// walk them somewhere they can sleep from.
        /// </summary>
        private const double GaveUpStandDownSec = 45;

        private double standDownUntil;

        protected override bool ShouldRun()
        {
            if (Villager == null || Villager.VillageId == 0) return false;
            if (entity.World.ElapsedMilliseconds / 1000.0 < standDownUntil) return false;

            EnumDayPhase phase = Phase;
            return phase == EnumDayPhase.Sleep || phase == EnumDayPhase.Shelter;
        }

        protected override void OnStart()
        {
            mounted = false;
            retryAt = 0;
            gaveUpChasingAt = entity.World.ElapsedMilliseconds / 1000.0 + WalkTimeoutSeconds;

            bedPos = FindBed();
            if (bedPos != null) Villager.OrderGoto(bedPos, MoveSpeeds.Walk);
        }

        protected override bool OnTick(float dt)
        {
            EnumDayPhase phase = Phase;
            if (phase != EnumDayPhase.Sleep && phase != EnumDayPhase.Shelter) return false;

            // Still actually in it?
            //
            // Beds throw people out. A temporal storm unmounts a sleeper within a fifth
            // of a second unless the server allows storm sleeping, and this task used to
            // set a flag and believe it for the rest of the night, holding the slot while
            // the villager stood beside the bed in the open.
            if (mounted && entity.MountedOn == null) mounted = false;

            if (mounted) return true;

            double now2 = entity.World.ElapsedMilliseconds / 1000.0;

            if (bedPos == null)
            {
                // No bed to go to, or could not get to the one they had. End and wait a
                // while before asking again, so the tether gets a turn.
                standDownUntil = now2 + GaveUpStandDownSec;
                return false;
            }

            double distSq = entity.Pos.XYZ.SquareDistanceTo(bedPos.ToVec3d().Add(0.5, 0, 0.5));
            float arrival = FFConfig.Current.Schedule.BedArrivalBlocks;

            if (distSq <= arrival * arrival)
            {
                TryGetIntoBed();
                return true;
            }

            double now = entity.World.ElapsedMilliseconds / 1000.0;

            if (now > gaveUpChasingAt)
            {
                // Could not get there. Sleeping on the ground beats standing in a field
                // walking into a wall all night.
                Villager.CancelGoto();
                bedPos = null;
                DevStats.Bump(DevStats.PathsFailed);
                standDownUntil = now + GaveUpStandDownSec;
                return false;
            }

            // The walk ended without arriving, so ask for it again rather than standing
            // there until the timeout. One refused path should not cost a night's sleep.
            if (Villager.GotoTarget == null && now >= retryAt)
            {
                retryAt = now + 4;
                Villager.OrderGoto(bedPos, MoveSpeeds.Walk);
            }

            return true;
        }

        /// <summary>
        /// Actually lie down. The game's beds are mountable seats, so a villager uses one
        /// the same way a player does, which is why they visibly lie in it rather than
        /// standing beside it looking tired.
        /// </summary>
        private void TryGetIntoBed()
        {
            Villager.CancelGoto();

            BlockEntityBed bed = entity.World.BlockAccessor.GetBlockEntity(bedPos) as BlockEntityBed;

            // The block entity sits on one half of the bed. If this is the other half,
            // look at the four neighbours for it rather than giving up.
            if (bed == null)
            {
                foreach (BlockFacing face in BlockFacing.HORIZONTALS)
                {
                    if (entity.World.BlockAccessor.GetBlockEntity(bedPos.AddCopy(face)) is BlockEntityBed neighbour)
                    {
                        bed = neighbour;
                        break;
                    }
                }
            }

            if (bed == null || bed.AnyMounted()) return;

            IMountableSeat seat = (bed as IMountable)?.Seats?.Length > 0
                ? ((IMountable)bed).Seats[0]
                : null;

            if (seat != null && seat.CanMount(entity) && entity.TryMount(seat))
            {
                mounted = true;
            }
        }

        protected override void OnStop(bool cancelled)
        {
            if (mounted) entity.TryUnmount();
            mounted = false;

            // Deliberately does not cancel the walk. This task hands the actual walking
            // to ffgoto, and ffgoto outranks it, so being stopped is the normal way a
            // trip to bed begins rather than a sign anything went wrong. Cancelling here
            // tore up the order the instant it was given, which is why nobody ever
            // arrived anywhere.
            bedPos = null;
        }

        private BlockPos FindBed()
        {
            var registry = (entity.Api as ICoreServerAPI)?.ModLoader.GetModSystem<VillageRegistry>();
            Village village = registry?.Get(Villager.VillageId);
            if (village == null) return null;

            VillageFacility bed = VillageRegistry.BedOf(village, entity.EntityId);
            if (bed != null) return bed.Pos;

            // Not assigned one yet. Ask now rather than standing around until the daily
            // tick gets round to it, because it is already bedtime.
            return registry.AssignBed(village, Villager)?.Pos;
        }

        public override string DebugLabel()
            => Phase == EnumDayPhase.Shelter
                ? (mounted ? "sheltering in bed" : "sheltering")
                : (mounted ? "asleep" : bedPos == null ? "no bed" : "going to bed");
    }
}
