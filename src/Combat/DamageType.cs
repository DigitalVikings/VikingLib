namespace VikingLib.Combat
{
    /// <summary>
    /// Explicit enumeration of damage types recognized by VikingLib, mapping directly to Valheim's HitData types
    /// while adding TrueDamage.
    /// </summary>
    public enum DamageType
    {
        Blunt,
        Slash,
        Pierce,
        Chop,
        Pickaxe,
        Fire,
        Frost,
        Lightning,
        Poison,
        Spirit,
        TrueDamage
    }

    /// <summary>
    /// High-level categorization of damage types for armor application and broad resistance modifiers.
    /// </summary>
    public enum DamageCategory
    {
        Physical,
        Elemental,
        Utility,
        Biological,
        Pure
    }

    /// <summary>
    /// Helper extensions for DamageType classification.
    /// </summary>
    public static class DamageTypeExtensions
    {
        /// <summary>
        /// Returns the high-level category of the specified damage type.
        /// </summary>
        public static DamageCategory GetCategory(this DamageType type) => type switch
        {
            DamageType.Blunt or DamageType.Slash or DamageType.Pierce => DamageCategory.Physical,
            DamageType.Fire or DamageType.Frost or DamageType.Lightning => DamageCategory.Elemental,
            DamageType.Chop or DamageType.Pickaxe => DamageCategory.Utility,
            DamageType.Poison or DamageType.Spirit => DamageCategory.Biological,
            DamageType.TrueDamage => DamageCategory.Pure,
            _ => DamageCategory.Physical
        };

        /// <summary>
        /// Returns true if this damage type is considered physical (Blunt, Slash, Pierce).
        /// </summary>
        public static bool IsPhysical(this DamageType type) =>
            type is DamageType.Blunt or DamageType.Slash or DamageType.Pierce;

        /// <summary>
        /// Returns true if this damage type is considered elemental (Fire, Frost, Lightning).
        /// </summary>
        public static bool IsElemental(this DamageType type) =>
            type is DamageType.Fire or DamageType.Frost or DamageType.Lightning;
    }
}
