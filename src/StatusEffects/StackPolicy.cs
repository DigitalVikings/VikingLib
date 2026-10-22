namespace VikingLib.StatusEffects
{
    /// <summary>
    /// Governs how a status effect behaves when re-applied to a character that already has it active.
    /// </summary>
    public enum StackPolicy
    {
        /// <summary>
        /// Reapplication refreshes the remaining duration (TTL) back to the base duration without increasing stack count.
        /// </summary>
        RefreshDurationOnly,

        /// <summary>
        /// Reapplication increments the stack count (up to MaxStacks) but does not refresh the remaining duration.
        /// </summary>
        IntensityOnly,

        /// <summary>
        /// Reapplication both increments the stack count (up to MaxStacks) and refreshes the remaining duration back to the base duration.
        /// </summary>
        IntensityAndRefresh,

        /// <summary>
        /// Reapplication does nothing if already active.
        /// </summary>
        IgnoreIfActive
    }
}
