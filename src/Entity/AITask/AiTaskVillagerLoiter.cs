using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

namespace FoundriesFrontiers
{
    /// <summary>
    /// A deliberately trivial task, and the proof that the framework works end to end:
    /// registration, staggering, think intervals, distance culling, the debug overlay and
    /// the counters, with none of the complexity of real work in the way.
    ///
    /// The villager pauses what they're doing and looks around for a moment. It will be
    /// replaced by real jobs, but it stays useful afterwards as the thing a villager does
    /// when they have nothing better to do.
    /// </summary>
    public class AiTaskVillagerLoiter : FFTaskBase
    {
        // Nobody needs to reconsider standing still sixty times a second.
        protected override float ThinkIntervalSec => 4f;

        // Pointless to run at all if no one is there to see it.
        protected override float ObservedRangeBlocks => 32f;
        protected override float UnobservedThrottle => 20f;

        private readonly float chance;
        private float remaining;

        public AiTaskVillagerLoiter(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
            : base(entity, taskConfig, aiConfig)
        {
            chance = taskConfig["chance"].AsFloat(0.15f);
        }

        protected override bool ShouldRun()
        {
            return entity.World.Rand.NextDouble() < chance;
        }

        protected override void OnStart()
        {
            remaining = 2f + (float)entity.World.Rand.NextDouble() * 3f;

            // Glance somewhere new rather than staring straight ahead.
            entity.Pos.Yaw += (float)(entity.World.Rand.NextDouble() - 0.5) * 2.2f;
            if (entity is EntityAgent agent) agent.BodyYaw = entity.Pos.Yaw;
        }

        protected override bool OnTick(float dt)
        {
            remaining -= dt;
            return remaining > 0;
        }

        public override string DebugLabel() => "loitering";
    }
}
