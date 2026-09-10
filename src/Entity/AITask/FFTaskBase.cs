using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace FoundriesFrontiers
{
    /// <summary>
    /// Base class for every Foundries and Frontiers AI task.
    ///
    /// Exists mainly to make the expensive parts cheap by default. A village is dozens of
    /// entities each running several tasks, and `ShouldExecute` is called constantly - so
    /// the cost of *deciding not to act* dominates everything else. Three defences, all
    /// applied before a subclass is consulted:
    ///
    ///  1. **Staggered thinking.** Each villager evaluates on its own offset derived from
    ///     its entity id, so thirty villagers never all run their checks on the same tick.
    ///  2. **Think interval.** Subclasses declare how often they *need* to be asked. Most
    ///     village work is fine being reconsidered once a second, not sixty times.
    ///  3. **Distance culling.** Work nobody can see runs at a fraction of the rate, or
    ///     not at all, depending on the task.
    ///
    /// Subclasses override ShouldRun / OnStart / OnTick / OnStop rather than the engine's
    /// methods directly, so none of the above can be forgotten by accident.
    /// </summary>
    public abstract class FFTaskBase : AiTaskBase
    {
        /// <summary>How often this task wants its ShouldRun asked, in seconds.</summary>
        protected virtual float ThinkIntervalSec => 1.0f;

        /// <summary>
        /// Beyond this distance from the nearest player, the task is throttled hard.
        /// Negative means "always think", for work that must continue unobserved.
        /// </summary>
        protected virtual float ObservedRangeBlocks => FFConfig.Current.Performance.ObservedRangeBlocks;

        /// <summary>Multiplier applied to the think interval when nobody is watching.</summary>
        protected virtual float UnobservedThrottle => FFConfig.Current.Performance.UnobservedThrottle;

        protected FFVillager Villager => entity as FFVillager;

        private double lastThoughtAt = double.NegativeInfinity;
        private readonly float stagger;

        protected FFTaskBase(EntityAgent entity, JsonObject taskConfig, JsonObject aiConfig)
            : base(entity, taskConfig, aiConfig)
        {
            // Deterministic per entity, so a villager keeps the same phase across reloads
            // and the load stays evenly spread rather than re-clumping.
            stagger = (entity.EntityId % 97) / 97f;

            baseConfig(taskConfig);
        }

        private void baseConfig(JsonObject cfg)
        {
            // Nothing yet - here so subclasses have one obvious place to add shared config.
        }

        public sealed override bool ShouldExecute()
        {
            if (entity?.World?.Side != EnumAppSide.Server) return false;
            if (!entity.Alive) return false;

            float interval = ThinkIntervalSec * FFConfig.Current.Performance.ThinkIntervalMultiplier;

            if (ObservedRangeBlocks > 0 && !IsObserved())
            {
                interval *= UnobservedThrottle;
            }

            // Real elapsed time, not a count of calls.
            //
            // The engine only asks a task whether it wants to run when it could actually
            // win the slot, so counting calls measured a task's interval in *eligible*
            // ticks. A low priority task froze completely while something else held the
            // slot, then had to wait its full interval again from the moment the slot
            // freed, by which point a higher priority task with a shorter interval had
            // already taken it. The tether lost that race every time.
            double now = entity.World.ElapsedMilliseconds / 1000.0;

            // First time through, back-date the clock by the villager's own offset so
            // thirty of them do not all think on the same tick for the rest of the save.
            if (double.IsNegativeInfinity(lastThoughtAt)) lastThoughtAt = now - stagger * interval;

            if (now - lastThoughtAt < interval) return false;
            lastThoughtAt = now;

            bool run = ShouldRun();
            if (run) DevStats.Bump(DevStats.TasksStarted);
            return run;
        }

        /// <summary>Cheap check for whether any player is close enough for this to matter.</summary>
        protected bool IsObserved()
        {
            IPlayer plr = entity.World.NearestPlayer(entity.Pos.X, entity.Pos.Y, entity.Pos.Z);
            if (plr?.Entity == null) return false;
            return plr.Entity.Pos.SquareDistanceTo(entity.Pos.XYZ)
                   <= ObservedRangeBlocks * ObservedRangeBlocks;
        }

        public sealed override void StartExecute()
        {
            base.StartExecute();
            OnStart();
        }

        public sealed override bool ContinueExecute(float dt)
        {
            return OnTick(dt);
        }

        public sealed override void FinishExecute(bool cancelled)
        {
            OnStop(cancelled);
            base.FinishExecute(cancelled);
        }

        // --- what subclasses actually implement -------------------------------------

        /// <summary>Should this task start now? Called at most once per think interval.</summary>
        protected abstract bool ShouldRun();

        protected virtual void OnStart() { }

        /// <summary>Return false to end the task.</summary>
        protected virtual bool OnTick(float dt) => false;

        protected virtual void OnStop(bool cancelled) { }

        /// <summary>One short line for the debug overlay.</summary>
        public virtual string DebugLabel() => GetType().Name.Replace("AiTaskFF", "").ToLowerInvariant();
    }
}
