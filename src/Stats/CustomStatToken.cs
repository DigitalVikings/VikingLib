using System;
using VikingLib.Combat;

namespace VikingLib.Stats
{
    /// <summary>
    /// Represents a dynamically registered custom character stat identified by a unique string key.
    /// Provides full 3-tier mathematical engine integration for downstream mods.
    /// </summary>
    public class CustomStatToken
    {
        public string Key { get; }
        public string Name { get; }
        public string Description { get; }
        public string Unit { get; }
        public float DefaultValue { get; }
        public bool PermitsFlat { get; }
        public bool PermitsAdditive { get; }
        public bool PermitsMultiplier { get; }

        public CustomStatToken(
            string key,
            string name,
            float defaultValue = 0f,
            bool permitsFlat = false,
            bool permitsAdditive = true,
            bool permitsMultiplier = true,
            string unit = "",
            string description = "")
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Custom stat key cannot be null or empty.", nameof(key));
            Key = key;
            Name = string.IsNullOrWhiteSpace(name) ? key : name;
            DefaultValue = defaultValue;
            PermitsFlat = permitsFlat;
            PermitsAdditive = permitsAdditive;
            PermitsMultiplier = permitsMultiplier;
            Unit = unit;
            Description = description;
        }

        /// <summary>
        /// Evaluates the final calculated value of this custom stat for the given character.
        /// </summary>
        public float Get(Character? character) =>
            CharacterStatManager.GetStat(character, Key, DefaultValue);

        /// <summary>
        /// Evaluates the custom stat and returns a granular diagnostic trace of all contributing modifiers.
        /// </summary>
        public StatTrace GetTrace(Character? character) =>
            CharacterStatManager.GetTrace(character, Key, DefaultValue);

        /// <summary>
        /// Adds a Tier 1 (Flat) modifier to this custom stat.
        /// </summary>
        public void AddFlat(Character character, float amount, string source, Func<Character, bool>? condition = null, object? owner = null)
        {
            if (!PermitsFlat)
            {
                throw new InvalidOperationException($"Custom stat '{Key}' does not permit Tier 1 (Flat) modifiers.");
            }
            CharacterStatManager.AddModifier(character, StatModifier.CustomFlat(Key, amount, source, condition, owner));
        }

        /// <summary>
        /// Adds a Tier 2 (Additive Percentage) modifier to this custom stat.
        /// </summary>
        public void AddIncreased(Character character, float percent, string source, Func<Character, bool>? condition = null, object? owner = null)
        {
            if (!PermitsAdditive)
            {
                throw new InvalidOperationException($"Custom stat '{Key}' does not permit Tier 2 (Additive Percentage) modifiers.");
            }
            CharacterStatManager.AddModifier(character, StatModifier.CustomIncreased(Key, percent, source, condition, owner));
        }

        /// <summary>
        /// Adds a Tier 3 (Compounding Multiplier) modifier to this custom stat.
        /// </summary>
        public void AddMultiplier(Character character, float multiplier, string source, Func<Character, bool>? condition = null, object? owner = null)
        {
            if (!PermitsMultiplier)
            {
                throw new InvalidOperationException($"Custom stat '{Key}' does not permit Tier 3 (Compounding Multiplier) modifiers.");
            }
            CharacterStatManager.AddModifier(character, StatModifier.CustomMultiplier(Key, multiplier, source, condition, owner));
        }

        /// <summary>
        /// Removes all modifiers registered under this custom stat from the specified source.
        /// </summary>
        public void RemoveFromSource(Character character, string source) =>
            CharacterStatManager.RemoveModifiersFromSource(character, source, Key);

        public override string ToString() => Name;
    }
}
