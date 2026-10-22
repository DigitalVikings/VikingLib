namespace VikingLib.StatusEffects
{
    /// <summary>
    /// Governs how values from active slices are aggregated for modifier evaluation, HUD display, and capacity tracking.
    /// </summary>
    public enum SliceAggregationMode
    {
        /// <summary>
        /// Standard single-instance effect (slices not utilized).
        /// </summary>
        None,

        /// <summary>
        /// Aggregates active slices by summing their values: Σ(slice.Value).
        /// Used by Bleed (total remaining damage), Vulnerable (+X% phys taken), Cripple (-X% slow),
        /// Empowered Strike (+X% damage), Fortified (+X% armor eff), and Ward (total barrier HP).
        /// </summary>
        Sum,

        /// <summary>
        /// Aggregates active slices by taking the maximum active value: max(slice.Value).
        /// </summary>
        Max,

        /// <summary>
        /// Aggregates active slices by counting the number of active slices.
        /// </summary>
        Count
    }

    /// <summary>
    /// Display formatting for status effect HUD icons with multi-slice values.
    /// </summary>
    public enum SliceIconFormat
    {
        /// <summary>
        /// Displays stack count or default behavior: "xN".
        /// </summary>
        Default,

        /// <summary>
        /// Displays positive percentage: "+X%".
        /// </summary>
        PositivePercent,

        /// <summary>
        /// Displays negative percentage: "-X%".
        /// </summary>
        NegativePercent,

        /// <summary>
        /// Displays integer ceiling value (e.g. remaining barrier HP).
        /// </summary>
        IntegerCeiling,

        /// <summary>
        /// Displays single decimal value (e.g. remaining DoT damage like "24.3").
        /// </summary>
        SingleDecimal
    }
}
