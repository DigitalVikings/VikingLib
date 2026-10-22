using System;
using System.Collections.Generic;
using UnityEngine;
using VikingLib.Combat;
using VikingLib.Stats;

namespace VikingLib.StatusEffects
{
    /// <summary>
    /// Blueprint for a declarative stat modifier applied by a status effect.
    /// </summary>
    public class StatModifierBlueprint
    {
        public CharacterStatType? StatType { get; }
        public string? CustomStatKey { get; }
        public ModifierTier Tier { get; }
        public float ValuePerStack { get; }
        public string? Source { get; }
        public Func<Character, bool>? Condition { get; }
        public Func<float, float>? SliceValueTransformer { get; }

        public StatModifierBlueprint(CharacterStatType statType, ModifierTier tier, float valuePerStack, string? source = null, Func<Character, bool>? condition = null, Func<float, float>? sliceValueTransformer = null)
        {
            StatType = statType;
            Tier = tier;
            ValuePerStack = valuePerStack;
            Source = source;
            Condition = condition;
            SliceValueTransformer = sliceValueTransformer;
        }

        public StatModifierBlueprint(string customStatKey, ModifierTier tier, float valuePerStack, string? source = null, Func<Character, bool>? condition = null, Func<float, float>? sliceValueTransformer = null)
        {
            CustomStatKey = customStatKey ?? throw new ArgumentNullException(nameof(customStatKey));
            Tier = tier;
            ValuePerStack = valuePerStack;
            Source = source;
            Condition = condition;
            SliceValueTransformer = sliceValueTransformer;
        }
    }

    /// <summary>
    /// Blueprint definition specifying all metadata, lifecycle, stacking, charge, and modifier behaviors
    /// for a <see cref="VikingStatusEffect"/>.
    /// </summary>
    public class VikingStatusEffectDefinition
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string Tooltip { get; set; } = string.Empty;
        public Sprite? Icon { get; set; }
        public string Category { get; set; } = string.Empty;
        public bool Hidden { get; set; }

        // Duration & Stacking
        public float BaseDuration { get; set; }
        public int MaxStacks { get; set; } = 1;
        public StackPolicy StackPolicy { get; set; } = StackPolicy.RefreshDurationOnly;
        public bool ModifiersScaleWithStacks { get; set; } = true;

        // Multi-Slice Configuration
        public SliceAggregationMode SliceAggregation { get; set; } = SliceAggregationMode.None;
        public bool IsMultiSlice => SliceAggregation != SliceAggregationMode.None;
        public int MaxSlices { get; set; } = 0; // 0 = uncapped
        public float DefaultSliceValue { get; set; } = 1.0f;
        public float DefaultSliceDuration { get; set; } = 0f;
        public float SliceTickInterval { get; set; } = 0f;
        public bool ConsumeAllChargesOnTrigger { get; set; } = false;
        public SliceIconFormat SliceIconFormat { get; set; } = SliceIconFormat.Default;
        public Func<VikingStatusEffect, string>? CustomIconTextFormatter { get; set; }

        // Charges
        public int InitialCharges { get; set; }
        public ChargeConsumeTrigger ChargeConsumptionTrigger { get; set; } = ChargeConsumeTrigger.None;
        public int ChargesConsumedPerTrigger { get; set; } = 1;
        public bool RefreshChargesOnReapply { get; set; } = true;

        // Damage Absorption Shield
        public float InitialShieldCapacity { get; set; }
        public float ShieldCapacityPerStack { get; set; }

        // Stat Modifiers
        public List<StatModifierBlueprint> Modifiers { get; } = new();

        // Periodic Damage (DoT)
        public DamageType? PeriodicDamageType { get; set; }
        public float PeriodicDamageAmount { get; set; }
        public float PeriodicDamageInterval { get; set; } = 1.0f;
        public bool PeriodicIgnoresArmor { get; set; } = true;

        // Effects
        public EffectList StartEffects { get; set; } = new EffectList();
        public EffectList StopEffects { get; set; } = new EffectList();

        public VikingStatusEffectDefinition(string name)
        {
            Name = name;
            DisplayName = name;
        }
    }
}
