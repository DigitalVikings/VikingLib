namespace VikingLib.Combat.Stagger
{
    /// <summary>
    /// Immutable profile defining creature-specific poise threshold, duration scaling, and native animation availability.
    /// </summary>
    public sealed class StaggerProfile
    {
        /// <summary>
        /// Base decoupled poise threshold in points.
        /// </summary>
        public float PoiseThreshold { get; }

        /// <summary>
        /// Multiplier on the duration or playback speed of the stagger state (default 1.0x).
        /// </summary>
        public float DurationMultiplier { get; }

        /// <summary>
        /// True if the creature's Unity Animator Controller contains a native "stagger" animation clip and trigger.
        /// If false, poise break triggers an artificial stun (AI pause and animator speed freeze).
        /// </summary>
        public bool HasNativeAnimation { get; }

        public StaggerProfile(float poiseThreshold, float durationMultiplier = 1.0f, bool hasNativeAnimation = true)
        {
            PoiseThreshold = poiseThreshold;
            DurationMultiplier = durationMultiplier > 0.001f ? durationMultiplier : 1.0f;
            HasNativeAnimation = hasNativeAnimation;
        }
    }
}
