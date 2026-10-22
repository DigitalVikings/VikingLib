using System;
using VikingLib.Combat;

namespace VikingLib.Stats
{
    /// <summary>
    /// A declarative modifier token registered into VikingLib's 3-Tier Stat Engine.
    /// Final Stat = (Base + Sum(Tier 1 Flat)) * Max(0, 1 + Sum(Tier 2 Increased%)) * Product(Tier 3 Multipliers).
    /// </summary>
    public class StatModifier
    {
        /// <summary>
        /// The target character stat modified by this token.
        /// If this is a custom dynamic stat, this will be (CharacterStatType)(-1) and <see cref="CustomStatKey"/> will be populated.
        /// </summary>
        public CharacterStatType StatType { get; }

        /// <summary>
        /// Optional string key identifying a custom dynamic stat registered outside the core enum.
        /// </summary>
        public string? CustomStatKey { get; }

        /// <summary>
        /// Indicates whether this modifier targets a dynamic custom stat rather than an enum stat.
        /// </summary>
        public bool IsCustom => !string.IsNullOrEmpty(CustomStatKey);

        /// <summary>
        /// Mathematical tier (Flat, IncreasedPercent, or Multiplier).
        /// </summary>
        public ModifierTier Tier { get; }

        /// <summary>
        /// Numerical value (e.g. +15 for flat, +0.20 for +20%, -0.15 for -15%, 0.5 for 0.5x multiplier).
        /// </summary>
        public float Value { get; }

        /// <summary>
        /// Human-readable label for debugging, tooltips, and telemetry audit (e.g. "Bronze Helmet", "Rested").
        /// </summary>
        public string Source { get; }

        /// <summary>
        /// Optional runtime predicate evaluated when the stat is queried (e.g. `c => c.IsBlocking()`).
        /// If null, modifier is unconditionally active.
        /// </summary>
        public Func<Character, bool>? Condition { get; }

        /// <summary>
        /// The owning object used for lifecycle cleanup (e.g. an ItemData, a StatusEffect, or a mod key).
        /// </summary>
        public object? Owner { get; }

        public StatModifier(
            CharacterStatType statType,
            ModifierTier tier,
            float value,
            string source,
            Func<Character, bool>? condition = null,
            object? owner = null)
        {
            StatType = statType;
            CustomStatKey = null;
            Tier = tier;
            Value = value;
            Source = source ?? "Unknown";
            Condition = condition;
            Owner = owner;
        }

        public StatModifier(
            string customStatKey,
            ModifierTier tier,
            float value,
            string source,
            Func<Character, bool>? condition = null,
            object? owner = null)
        {
            CustomStatKey = customStatKey ?? throw new ArgumentNullException(nameof(customStatKey));
            StatType = (CharacterStatType)(-1);
            Tier = tier;
            Value = value;
            Source = source ?? "Unknown";
            Condition = condition;
            Owner = owner;
        }

        /// <summary>
        /// Creates a Tier 1 (Flat) modifier for a physical stat.
        /// </summary>
        public static StatModifier Flat(PhysicalStat stat, float amount, string source, Func<Character, bool>? condition = null, object? owner = null) =>
            new(stat.Type, ModifierTier.Flat, amount, source, condition, owner);

        /// <summary>
        /// Creates a Tier 1 (Flat) modifier using a raw enum.
        /// </summary>
        [Obsolete("Prefer passing a PhysicalStat token from CharacterStats (e.g. CharacterStats.MaxHealth) for compile-time safety.", false)]
        public static StatModifier Flat(CharacterStatType stat, float amount, string source, Func<Character, bool>? condition = null, object? owner = null) =>
            new(stat, ModifierTier.Flat, amount, source, condition, owner);

        /// <summary>
        /// Creates a Tier 2 (Additive Percentage) modifier for any stat token.
        /// </summary>
        public static StatModifier Increased(StatToken stat, float percent, string source, Func<Character, bool>? condition = null, object? owner = null) =>
            new(stat.Type, ModifierTier.IncreasedPercent, percent, source, condition, owner);

        public static StatModifier Increased(CharacterStatType stat, float percent, string source, Func<Character, bool>? condition = null, object? owner = null) =>
            new(stat, ModifierTier.IncreasedPercent, percent, source, condition, owner);

        /// <summary>
        /// Creates a Tier 3 (Compounding Multiplier) modifier for any stat token.
        /// </summary>
        public static StatModifier Multiplier(BaseStatToken stat, float factor, string source, Func<Character, bool>? condition = null, object? owner = null) =>
            new(stat.Type, ModifierTier.Multiplier, factor, source, condition, owner);

        public static StatModifier Multiplier(CharacterStatType stat, float factor, string source, Func<Character, bool>? condition = null, object? owner = null) =>
            new(stat, ModifierTier.Multiplier, factor, source, condition, owner);

        /// <summary>
        /// Creates a custom Tier 1 (Flat) modifier for a string-keyed dynamic stat.
        /// </summary>
        public static StatModifier CustomFlat(string key, float amount, string source, Func<Character, bool>? condition = null, object? owner = null) =>
            new(key, ModifierTier.Flat, amount, source, condition, owner);

        /// <summary>
        /// Creates a custom Tier 2 (Additive Percentage) modifier for a string-keyed dynamic stat.
        /// </summary>
        public static StatModifier CustomIncreased(string key, float percent, string source, Func<Character, bool>? condition = null, object? owner = null) =>
            new(key, ModifierTier.IncreasedPercent, percent, source, condition, owner);

        /// <summary>
        /// Creates a custom Tier 3 (Compounding Multiplier) modifier for a string-keyed dynamic stat.
        /// </summary>
        public static StatModifier CustomMultiplier(string key, float factor, string source, Func<Character, bool>? condition = null, object? owner = null) =>
            new(key, ModifierTier.Multiplier, factor, source, condition, owner);

        public override string ToString()
        {
            string target = IsCustom ? CustomStatKey! : StatType.ToString();
            return Tier switch
            {
                ModifierTier.Flat => $"{Source} -> {target}: {(Value >= 0 ? "+" : "")}{Value:F1}",
                ModifierTier.IncreasedPercent => $"{Source} -> {target}: {(Value >= 0 ? "+" : "")}{Value * 100f:F1}%",
                ModifierTier.Multiplier => $"{Source} -> {target}: x{Value:F2}",
                _ => $"{Source} -> {target}: {Value}"
            };
        }
    }
}
