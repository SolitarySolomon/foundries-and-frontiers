using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Walks the villager to a target block along a real path.
    ///
    /// This is the movement primitive the whole mod is built on. Every job in Phase B is
    /// some arrangement of "go here, do something, go there, put it down", so this task's
    /// job is to be reliable rather than clever: find a path, follow it, notice when it
    /// has gone wrong, and say so honestly through the counters.
    ///
    /// It deliberately does NOT teleport on failure. Upstream's recovery hop is the thing
    /// the design says to drive toward zero, so failures are counted and surfaced rather
    /// than papered over. Recovery belongs to whatever set the target, which knows what
    /// the villager was trying to achieve.
    /// </summary>
    public class AiTaskVillagerGoto : FFTaskBase
    {
        // A journey in progress must be ticked properly, so this is the one task that
        // wants asking often. It is still gated behind having a target at all.
        protected override float ThinkIntervalSec => 0.25f;

        // Villagers must keep walking to work whether or not anyone is watching.
        protected override float ObservedRangeBlocks => -1f;

        private const double ArrivedDistSq = 2.0;
        private const double NodeReachedDistSq = 1.4;
        private const float StuckCheckSec = 1.5f;
        private const double StuckMinProgressSq = 0.09;

        private List<Vec3d> waypoints;
        private int nodeIndex;
        private float moveSpeed = MoveSpeeds.Walk;

        private float stuckAccum;
        private Vec3d lastPos;
        private int stuckStrikes;

        public AiTaskVillagerGoto(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
            : base(entity, taskConfig, aiConfig) { }

        protected override bool ShouldRun()
        {
            return Villager?.GotoTarget != null;
        }

        protected override void OnStart()
        {
            waypoints = null;
            nodeIndex = 0;
            stuckAccum = 0;
            stuckStrikes = 0;
            lastPos = entity.Pos.XYZ.Clone();

            BlockPos target = Villager?.GotoTarget;
            if (target == null) return;

            moveSpeed = Villager.GotoSpeed;

            var system = entity.Api.ModLoader.GetModSystem<PathfindingSystem>();
            VillagerPathfind pf = system?.Pathfinder;
            if (pf == null) { Fail("no pathfinder"); return; }

            BlockPos start = pf.GetStartPos(entity.Pos.XYZ);
            waypoints = pf.FindPathAsWaypoints(start, target);

            if (waypoints == null || waypoints.Count == 0)
            {
                // Either genuinely unreachable, or we are already standing on it.
                if (entity.Pos.SquareDistanceTo(target.ToVec3d().Add(0.5, 0, 0.5)) < ArrivedDistSq)
                {
                    Arrive();
                }
                else
                {
                    Fail("no path");
                }
                return;
            }

            DevStats.Bump(DevStats.PathsFound);
            entity.AnimManager?.StartAnimation(MoveSpeeds.AnimationFor(moveSpeed));
        }

        protected override bool OnTick(float dt)
        {
            if (Villager?.GotoTarget == null) return false;
            if (waypoints == null || nodeIndex >= waypoints.Count) return false;

            Vec3d node = waypoints[nodeIndex];

            double dx = node.X - entity.Pos.X;
            double dz = node.Z - entity.Pos.Z;

            if (dx * dx + dz * dz < NodeReachedDistSq)
            {
                nodeIndex++;
                if (nodeIndex >= waypoints.Count) { Arrive(); return false; }
                return true;
            }

            // Face and walk. Physics does the rest.
            entity.Pos.Yaw = (float)System.Math.Atan2(dx, dz);
            entity.ServerControls.Forward = true;
            entity.Controls.Forward = true;
            entity.Controls.WalkVector.Set(
                System.Math.Sin(entity.Pos.Yaw) * moveSpeed, 0,
                System.Math.Cos(entity.Pos.Yaw) * moveSpeed);

            if (entity is EntityAgent agent) agent.BodyYaw = entity.Pos.Yaw;

            return !CheckStuck(dt);
        }

        /// <summary>
        /// Three consecutive intervals with no real movement means something is wrong -
        /// geometry, a closed door, another villager in a doorway. Give up honestly and
        /// let the counters record it.
        /// </summary>
        private bool CheckStuck(float dt)
        {
            stuckAccum += dt;
            if (stuckAccum < StuckCheckSec) return false;
            stuckAccum = 0;

            double moved = entity.Pos.XYZ.SquareDistanceTo(lastPos);
            lastPos = entity.Pos.XYZ.Clone();

            if (moved >= StuckMinProgressSq) { stuckStrikes = 0; return false; }

            stuckStrikes++;
            if (stuckStrikes < 3) return false;

            Fail("stuck");
            return true;
        }

        private void Arrive()
        {
            Villager?.OnGotoArrived();
            Stop();
        }

        private void Fail(string reason)
        {
            DevStats.Bump(DevStats.PathsFailed);
            entity.Api.Logger.VerboseDebug("[F&F] goto failed for {0}: {1}", entity.EntityId, reason);
            Villager?.OnGotoFailed(reason);
            Stop();
        }

        private void Stop()
        {
            waypoints = null;
            nodeIndex = 0;
            entity.Controls.Forward = false;
            entity.ServerControls.Forward = false;
            entity.Controls.WalkVector.Set(0, 0, 0);
        }

        protected override void OnStop(bool cancelled)
        {
            entity.AnimManager?.StopAnimation(MoveSpeeds.AnimationFor(moveSpeed));
            Stop();
        }

        public override string DebugLabel()
        {
            if (waypoints == null) return "goto";
            return "goto " + nodeIndex + "/" + waypoints.Count;
        }
    }
}
