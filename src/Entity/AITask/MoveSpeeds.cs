namespace FoundriesFrontiers
{
    /// <summary>
    /// Movement speeds, in one place.
    ///
    /// Any new task picks a constant from here rather than inventing a number.
    ///
    /// The villager's walk animation is deliberately NOT declared `mulWithWalkSpeed`.
    /// That flag ties animation rate to movement speed, so the moment the two disagree
    /// the legs stop while the body keeps gliding. Without it the animation plays at a
    /// constant, believable rate, and these values only have to feel right rather than
    /// exactly match it.
    /// </summary>
    public static class MoveSpeeds
    {
        /// <summary>Ambling with nothing to do.</summary>
        public static float Stroll => FFConfig.Current.Movement.Stroll;

        /// <summary>
        /// Normal walking, set to match a player's own pace. The default for anything
        /// with somewhere to be.
        /// </summary>
        public static float Walk => FFConfig.Current.Movement.Walk;

        /// <summary>Carrying a load. Deliberately slower, and it should look it.</summary>
        public static float Laden => FFConfig.Current.Movement.Laden;

        /// <summary>Urgent: fleeing, answering an alarm, getting indoors before a storm.</summary>
        public static float Run => FFConfig.Current.Movement.Run;

        /// <summary>Below this, use the walk animation; above it, use run.</summary>
        public static float RunAnimationThreshold => FFConfig.Current.Movement.RunAnimationThreshold;

        /// <summary>Picks the animation code that will look right for a given speed.</summary>
        public static string AnimationFor(float speed)
            => speed >= RunAnimationThreshold ? "run" : "walk";
    }
}
