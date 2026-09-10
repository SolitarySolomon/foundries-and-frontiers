using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Walks to the storehouse and picks up a tool.
    ///
    /// This task exists because the alternative is a tool appearing in somebody's hand
    /// from across the village, and that is the same poof-magic the storehouse and the
    /// build sites both refuse. A village that makes you carry logs home should make you
    /// walk over for a new axe.
    ///
    /// It sits just above the producing jobs. A worker with no tool is a worker doing a
    /// fraction of their job, so the walk pays for itself many times over, and it is a
    /// short walk: the rack is at the storehouse they already visit with every load.
    ///
    /// The rack is filled by the village overnight, so this task only ever collects. If
    /// there is nothing of the right kind on it, the villager carries on bare handed and
    /// tries again later rather than standing at the door waiting.
    /// </summary>
    public class AiTaskVillagerFetchTool : FFTaskBase
    {
        protected override float ThinkIntervalSec => 6f;

        /// <summary>Villages must keep working whether or not a player is watching.</summary>
        protected override float ObservedRangeBlocks => -1f;

        /// <summary>How close is close enough to reach the rack.</summary>
        private const double AtTheDoorBlocks = 3.0;

        /// <summary>Leave it a while after a failed trip. The rack refills overnight.</summary>
        private const double StandDownSec = 90;

        private double travelStartedAt;
        private double standDownUntil;
        private int failures;

        public AiTaskVillagerFetchTool(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
            : base(entity, taskConfig, aiConfig) { }

        private ICoreServerAPI Sapi => entity.Api as ICoreServerAPI;

        private VillageRegistry Registry => Sapi?.ModLoader.GetModSystem<VillageRegistry>();

        private Village Home => Registry?.Get(Villager?.VillageId ?? 0);

        protected override bool ShouldRun()
        {
            if (Villager == null || Villager.VillageId == 0) return false;
            if (Villager.ToolStack != null) return false;
            if (Now < standDownUntil) return false;

            if (VillageSchedule.PhaseFor(entity.Api, entity.Pos.AsBlockPos) != EnumDayPhase.Work) return false;

            Village village = Home;
            if (village == null) return false;

            // Same rule as every other job: somebody outside their own claim goes home
            // first, or this preempts the tether and they never leave the field.
            if (!AiTaskVillagerReturnHome.WithinTether(village, entity.Pos.AsBlockPos)) return false;

            // Do not set off for something that is not there.
            return Registry.RackHasToolFor(village, Villager.Trade);
        }

        protected override void OnStart()
        {
            travelStartedAt = Now;
            failures = 0;

            BlockPos rack = RackPos(Home);
            if (rack != null) Villager.OrderGoto(rack, MoveSpeeds.Walk);
        }

        protected override bool OnTick(float dt)
        {
            Village village = Home;
            if (village == null) return false;

            // Somebody else took the last one while this villager was walking.
            if (!Registry.RackHasToolFor(village, Villager.Trade))
            {
                Villager.CancelGoto();
                standDownUntil = Now + StandDownSec;
                return false;
            }

            BlockPos rack = RackPos(village);
            if (rack == null) return false;

            if (entity.Pos.SquareDistanceTo(rack.ToVec3d().Add(0.5, 0, 0.5))
                > AtTheDoorBlocks * AtTheDoorBlocks)
            {
                if (Now - travelStartedAt > FFConfig.Current.Work.GiveUpAfterSec)
                {
                    Note("could not get to the tool rack");
                    Villager.CancelGoto();
                    standDownUntil = Now + StandDownSec;
                    return false;
                }

                // The walk ended without arriving, so ask again rather than standing here.
                if (Villager.GotoTarget == null)
                {
                    if (++failures > 3)
                    {
                        Villager.CancelGoto();
                        standDownUntil = Now + StandDownSec;
                        return false;
                    }
                    Villager.OrderGoto(rack, MoveSpeeds.Walk);
                }
                return true;
            }

            Villager.CancelGoto();

            if (!Registry.TryCollectTool(village, Villager, out string outcome))
            {
                Note("found nothing on the rack: " + outcome);
                standDownUntil = Now + StandDownSec;
            }

            return false;
        }

        protected override void OnStop(bool cancelled)
        {
            // The walk belongs to ffgoto and this task may be about to start again.
        }

        /// <summary>
        /// Where the rack is: the storehouse, or the village centre when the crate is
        /// down. The same answer the work loop gives for where to deliver a load, and for
        /// the same reason: the stores live in the ledger, not in the box.
        /// </summary>
        private static BlockPos RackPos(Village village)
        {
            if (village == null) return null;

            return village.HasStorehouse
                ? new BlockPos(village.StorehouseX, village.StorehouseY, village.StorehouseZ, 0)
                : village.Centre;
        }

        private double Now => entity.World.ElapsedMilliseconds / 1000.0;

        private void Note(string what)
            => entity.Api.Logger.Notification(
                "[F&F] {0} {1}.",
                Villager.GivenName == "" ? "#" + entity.EntityId : Villager.GivenName, what);

        public override string DebugLabel() => "fetching a tool";
    }
}
