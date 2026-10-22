namespace VikingLib.Combat
{
    /// <summary>
    /// The mathematical tier of a damage modifier in VikingLib's 3-tier RPG calculation formula:
    /// Final = (Base + Sum(Flat)) * (1 + Sum(Increased%)) * Product(Multipliers).
    /// </summary>
    public enum ModifierTier
    {
        /// <summary>
        /// Tier 1: Fixed numerical value added directly to base damage before percentage calculations.
        /// </summary>
        Flat,

        /// <summary>
        /// Tier 2: Additive percentage modifier (e.g. +0.15f for +15%). All modifiers in this tier
        /// are summed together: (1 + sum).
        /// </summary>
        IncreasedPercent,

        /// <summary>
        /// Tier 3: Independent compounding factor (e.g. 1.5f for a 1.5x critical hit or execution bonus).
        /// Each multiplier is multiplied sequentially.
        /// </summary>
        Multiplier
    }
}
