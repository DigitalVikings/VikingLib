namespace VikingLib.Combat.Telemetry
{
    /// <summary>
    /// Configures the scope of combat telemetry and pipeline inspection logging.
    /// </summary>
    public enum DamageLoggingMode
    {
        /// <summary>
        /// Logging disabled. Zero performance overhead and zero string allocations.
        /// </summary>
        Off,

        /// <summary>
        /// Only log damage breakdowns when the local player is either the attacker or the target.
        /// </summary>
        PlayerOnly,

        /// <summary>
        /// Log all combat damage breakdowns occurring within loaded active zones.
        /// </summary>
        AllCombat
    }
}
