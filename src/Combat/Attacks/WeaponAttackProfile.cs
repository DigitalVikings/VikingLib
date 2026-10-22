using System;
using System.Collections.Generic;
using System.Linq;

namespace VikingLib.Combat.Attacks
{
    /// <summary>
    /// Represents the full attack profile for a weapon, mapping attack step keys
    /// (e.g. primary_1, primary_2, primary_3, secondary) to their corresponding AttackStepModifiers.
    /// </summary>
    public class WeaponAttackProfile
    {
        /// <summary>
        /// Dictionary mapping case-insensitive step keys to modifiers.
        /// </summary>
        public Dictionary<string, AttackStepModifiers> Steps { get; } = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Retrieves the modifier configuration for a given attack step key.
        /// Returns a neutral (1.0x) modifier if not explicitly registered.
        /// </summary>
        public AttackStepModifiers GetStep(string stepKey)
        {
            if (string.IsNullOrEmpty(stepKey)) return new AttackStepModifiers(1.0f);
            return Steps.TryGetValue(stepKey, out var mod) ? mod : new AttackStepModifiers(1.0f);
        }

        /// <summary>
        /// Gets the effective multiplier for a specific step and damage type.
        /// </summary>
        public float GetMultiplier(string stepKey, DamageType type) =>
            GetStep(stepKey).GetMultiplier(type);

        /// <summary>
        /// Adds or replaces an attack step with a default multiplier and cleave factor.
        /// </summary>
        public void AddStep(string stepKey, float defaultMultiplier, float cleave = 0.375f)
        {
            if (string.IsNullOrEmpty(stepKey)) return;
            Steps[stepKey] = new AttackStepModifiers(defaultMultiplier, cleave);
        }

        /// <summary>
        /// Sets a specific damage type multiplier on an attack step.
        /// </summary>
        public void SetTypeMultiplier(string stepKey, DamageType type, float multiplier)
        {
            if (string.IsNullOrEmpty(stepKey)) return;
            if (!Steps.TryGetValue(stepKey, out var step))
            {
                step = new AttackStepModifiers(1.0f);
                Steps[stepKey] = step;
            }
            step.SetMultiplier(type, multiplier);
        }

        /// <summary>
        /// Number of attack steps defined.
        /// </summary>
        public int StepCount => Steps.Count;

        /// <summary>
        /// Indicates whether this weapon profile defines a secondary attack.
        /// </summary>
        public bool HasSecondary => Steps.ContainsKey("secondary");

        /// <summary>
        /// Number of primary combo hits defined.
        /// </summary>
        public int PrimaryChainLength => Steps.Keys.Count(k => k.StartsWith("primary_", StringComparison.OrdinalIgnoreCase));
    }
}
