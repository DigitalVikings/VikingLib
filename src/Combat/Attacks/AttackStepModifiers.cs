using System;
using System.Collections.Generic;

namespace VikingLib.Combat.Attacks
{
    /// <summary>
    /// Represents damage multipliers for a specific attack step (e.g. primary_1, primary_3, secondary).
    /// Provides a default multiplier and allows per-DamageType granular overrides.
    /// </summary>
    public class AttackStepModifiers
    {
        /// <summary>
        /// Default multiplier applied to all damage types unless explicitly overridden.
        /// </summary>
        public float DefaultMultiplier { get; set; } = 1.0f;

        /// <summary>
        /// Cleave efficiency factor (0.0 to 1.0+). Controls multi-target damage scaling.
        /// In Continuous mode: Multiplier = 1 / (Cleave + (1 - Cleave) * N).
        /// Default is 0.375f (37.5% cleave). Attacks with 1.0f suffer no damage falloff across multiple targets.
        /// </summary>
        public float Cleave { get; set; } = 0.375f;

        /// <summary>
        /// Optional damage type conversion: if set, total base physical damage (Blunt + Slash + Pierce)
        /// is converted entirely into this damage type at attack origin.
        /// </summary>
        public DamageType? OverrideDamageType { get; set; }

        /// <summary>
        /// Optional status effect ID to apply to targets on hit during PostHitProcs.
        /// </summary>
        public string? OnHitStatusEffect { get; set; }

        /// <summary>
        /// Duration of the on-hit status effect slice in seconds.
        /// </summary>
        public float StatusEffectDuration { get; set; } = 5f;

        /// <summary>
        /// Potency or value of the on-hit status effect slice.
        /// </summary>
        public float StatusEffectValue { get; set; } = 1f;

        /// <summary>
        /// Granular per-DamageType multiplier overrides.
        /// </summary>
        public Dictionary<DamageType, float> TypeMultipliers { get; } = new();

        public AttackStepModifiers(float defaultMultiplier = 1.0f, float cleave = 0.375f)
        {
            DefaultMultiplier = defaultMultiplier;
            Cleave = cleave;
        }

        /// <summary>
        /// Gets the effective multiplier for a specific damage type.
        /// </summary>
        public float GetMultiplier(DamageType type) =>
            TypeMultipliers.TryGetValue(type, out float val) ? val : DefaultMultiplier;

        /// <summary>
        /// Sets a specific multiplier override for a given damage type.
        /// </summary>
        public void SetMultiplier(DamageType type, float multiplier) =>
            TypeMultipliers[type] = multiplier;

        /// <summary>
        /// Indicates whether any damage type has an explicit override differing from DefaultMultiplier.
        /// </summary>
        public bool HasTypeOverrides => TypeMultipliers.Count > 0;
    }
}
