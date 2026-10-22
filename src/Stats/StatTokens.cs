using System;
using VikingLib.Combat;

namespace VikingLib.Stats
{
    /// <summary>
    /// Root base class for all strongly-typed character statistic descriptors.
    /// <para>
    /// Provides compile-time safe access, evaluation, and Tier 3 (Compounding Multiplier) modifiers.
    /// </para>
    /// </summary>
    public abstract class BaseStatToken
    {
        /// <summary>
        /// Underlying enum token used by VikingLib's internal engine and registries.
        /// </summary>
        public CharacterStatType Type { get; }

        /// <summary>
        /// Human-readable name for logging, tooltips, and glossary presentation.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Detailed architectural description of this stat.
        /// </summary>
        public string Description { get; }

        protected BaseStatToken(CharacterStatType type, string name, string description = "")
        {
            Type = type;
            Name = name;
            Description = description;
        }

        /// <summary>
        /// Evaluates the final calculated value of this stat for the given character.
        /// </summary>
        public float Get(Character? character) =>
            CharacterStatManager.GetStat(character, Type);

        /// <summary>
        /// Evaluates the stat and returns a granular diagnostic trace of all contributing modifiers.
        /// </summary>
        public StatTrace GetTrace(Character? character) =>
            CharacterStatManager.GetTrace(character, Type);

        /// <summary>
        /// Adds a Tier 3 (Compounding Multiplier) modifier to this stat.
        /// Multiplies independently: Π Tier 3.
        /// </summary>
        public void AddMultiplier(Character character, float multiplier, string source, Func<Character, bool>? condition = null, object? owner = null) =>
            CharacterStatManager.AddModifier(character, StatModifier.Multiplier(this, multiplier, source, condition, owner));

        /// <summary>
        /// Removes all modifiers registered under this stat from the specified source.
        /// </summary>
        public void RemoveFromSource(Character character, string source) =>
            CharacterStatManager.RemoveModifiersFromSource(character, source, Type);

        public override string ToString() => Name;

        /// <summary>
        /// Enables seamless passing of BaseStatToken instances into methods expecting CharacterStatType.
        /// </summary>
        public static implicit operator CharacterStatType(BaseStatToken token) => token.Type;
    }

    /// <summary>
    /// Represents a damage resistance factor on incoming damage (baseline 1.0x = normal damage taken).
    /// <para>
    /// <b>ONLY permits Tier 3 (Compounding Multipliers).</b><br/>
    /// Calling <c>AddFlat</c> or <c>AddIncreased</c> is prevented at compile time.
    /// Multiple resistance modifiers compound multiplicatively: <c>Damage Taken = Base × Π Multipliers</c>.
    /// (e.g. 0.5x Poison Resistance mead × 0.8x perk = 0.40x damage taken).
    /// </para>
    /// </summary>
    public class ResistanceStat : BaseStatToken
    {
        public ResistanceStat(CharacterStatType type, string name, string description = "")
            : base(type, name, description) { }
    }

    /// <summary>
    /// Base class for stats that support Tier 2 (Additive Percentage) in addition to Tier 3 Multipliers.
    /// </summary>
    public abstract class StatToken : BaseStatToken
    {
        protected StatToken(CharacterStatType type, string name, string description = "")
            : base(type, name, description) { }

        /// <summary>
        /// Adds a Tier 2 (Additive Percentage) modifier to this stat.
        /// Stacks additively with all other Tier 2 modifiers: (1 + Σ Tier 2).
        /// </summary>
        public void AddIncreased(Character character, float percent, string source, Func<Character, bool>? condition = null, object? owner = null) =>
            CharacterStatManager.AddModifier(character, StatModifier.Increased(this, percent, source, condition, owner));
    }

    /// <summary>
    /// Represents a stat with real physical units (e.g. HP, kg, meters, m/s, seconds, armor points).
    /// <para>
    /// <b>ONLY PhysicalStat supports Tier 1 (Flat) modifiers.</b>
    /// Adding a flat bonus adds directly to the base value before percentage scaling:
    /// <c>Final = (Base + Σ Flat) × max(0, 1 + Σ Increased%) × Π Multipliers</c>.
    /// </para>
    /// </summary>
    public class PhysicalStat : StatToken
    {
        /// <summary>
        /// The physical unit of measurement (e.g. "HP", "kg", "m", "m/s", "sec").
        /// </summary>
        public string Unit { get; }

        public PhysicalStat(CharacterStatType type, string name, string unit, string description = "")
            : base(type, name, description)
        {
            Unit = unit;
        }

        /// <summary>
        /// Adds a Tier 1 (Flat) modifier to this physical stat.
        /// Added directly to the base pool/value before percentage scaling.
        /// </summary>
        public void AddFlat(Character character, float flatAmount, string source, Func<Character, bool>? condition = null, object? owner = null) =>
            CharacterStatManager.AddModifier(character, StatModifier.Flat(this, flatAmount, source, condition, owner));
    }

    /// <summary>
    /// Represents a pure dimensionless rate or multiplier stat (baseline 1.0x).
    /// <para>
    /// Supports ONLY Tier 2 (IncreasedPercent) and Tier 3 (Multiplier) modifiers.
    /// Attempting to call <c>AddFlat</c> is prevented at compile time.
    /// </para>
    /// </summary>
    public class MultiplierStat : StatToken
    {
        public MultiplierStat(CharacterStatType type, string name, string description = "")
            : base(type, name, description) { }
    }

    /// <summary>
    /// Represents an action resource efficiency rating (baseline 0.0).
    /// <para>
    /// Governed by VikingLib's Asymptotic Efficiency Engine where costs scale in the denominator:
    /// <c>Effective Cost = Base Cost / ((1 + Σ Tier 2 Efficiency%) × Π Tier 3 Multipliers)</c>.
    /// Higher values are always better. Supports ONLY Tier 2 and Tier 3 modifiers.
    /// Attempting to call <c>AddFlat</c> is prevented at compile time.
    /// </para>
    /// </summary>
    public class EfficiencyStat : StatToken
    {
        public EfficiencyStat(CharacterStatType type, string name, string description = "")
            : base(type, name, description) { }
    }
}
