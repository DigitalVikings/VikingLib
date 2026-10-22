using System;
using UnityEngine;
using VikingLib.Combat;
using VikingLib.Stats;

namespace VikingLib.StatusEffects
{
    /// <summary>
    /// Fluent builder for constructing native Valheim status effects in pure C# with zero Unity asset bundle dependencies.
    /// </summary>
    public class StatusEffectBuilder
    {
        private readonly VikingStatusEffectDefinition _def;

        public StatusEffectBuilder(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Status effect name cannot be empty.", nameof(name));
            _def = new VikingStatusEffectDefinition(name);
        }

        /// <summary>
        /// Creates a new builder instance with the specified internal prefab name.
        /// </summary>
        public static StatusEffectBuilder Create(string name) => new(name);

        /// <summary>
        /// Sets the localized or human-readable display name.
        /// </summary>
        public StatusEffectBuilder WithName(string displayName)
        {
            _def.DisplayName = displayName;
            return this;
        }

        /// <summary>
        /// Sets the tooltip description for HUD and inventory inspection.
        /// </summary>
        public StatusEffectBuilder WithTooltip(string tooltip)
        {
            _def.Tooltip = tooltip;
            return this;
        }

        /// <summary>
        /// Sets the icon sprite displayed on the HUD status effect bar.
        /// </summary>
        public StatusEffectBuilder WithIcon(Sprite? icon)
        {
            _def.Icon = icon;
            return this;
        }

        /// <summary>
        /// Sets the category grouping for this status effect.
        /// </summary>
        public StatusEffectBuilder WithCategory(string category)
        {
            _def.Category = category;
            return this;
        }

        /// <summary>
        /// Sets whether this status effect is hidden from the player's HUD.
        /// </summary>
        public StatusEffectBuilder WithHidden(bool hidden = true)
        {
            _def.Hidden = hidden;
            return this;
        }

        /// <summary>
        /// Sets the base duration in seconds (0 = indefinite / until charges or barrier depleted).
        /// </summary>
        public StatusEffectBuilder WithDuration(float durationSeconds)
        {
            _def.BaseDuration = Mathf.Max(0f, durationSeconds);
            return this;
        }

        /// <summary>
        /// Configures maximum stacks and re-application behavior.
        /// </summary>
        public StatusEffectBuilder WithMaxStacks(int maxStacks, StackPolicy policy = StackPolicy.IntensityAndRefresh, bool scaleModifiers = true)
        {
            _def.MaxStacks = Mathf.Max(1, maxStacks);
            _def.StackPolicy = policy;
            _def.ModifiersScaleWithStacks = scaleModifiers;
            return this;
        }

        /// <summary>
        /// Sets the stack re-application policy.
        /// </summary>
        public StatusEffectBuilder WithStackPolicy(StackPolicy policy)
        {
            _def.StackPolicy = policy;
            return this;
        }

        /// <summary>
        /// Configures charges consumed on specific combat triggers (e.g. next melee hit, next damage taken).
        /// </summary>
        public StatusEffectBuilder WithCharges(int charges, ChargeConsumeTrigger trigger, int consumePerTrigger = 1, bool refreshOnReapply = true)
        {
            _def.InitialCharges = Mathf.Max(0, charges);
            _def.ChargeConsumptionTrigger = trigger;
            _def.ChargesConsumedPerTrigger = Mathf.Max(1, consumePerTrigger);
            _def.RefreshChargesOnReapply = refreshOnReapply;
            return this;
        }

        /// <summary>
        /// Configures a damage absorption barrier that intercepts incoming damage before health loss.
        /// </summary>
        public StatusEffectBuilder WithShield(float capacity, float capacityPerStack = 0f)
        {
            _def.InitialShieldCapacity = Mathf.Max(0f, capacity);
            _def.ShieldCapacityPerStack = Mathf.Max(0f, capacityPerStack);
            return this;
        }

        /// <summary>
        /// Adds a Tier 1 (Flat) modifier for a physical stat.
        /// </summary>
        public StatusEffectBuilder WithFlatModifier(PhysicalStat stat, float amountPerStack, string? source = null, Func<Character, bool>? condition = null)
        {
            _def.Modifiers.Add(new StatModifierBlueprint(stat.Type, ModifierTier.Flat, amountPerStack, source, condition));
            return this;
        }

        /// <summary>
        /// Adds a Tier 2 (Additive Percentage) modifier to any stat token.
        /// </summary>
        public StatusEffectBuilder WithIncreasedModifier(StatToken stat, float percentPerStack, string? source = null, Func<Character, bool>? condition = null)
        {
            _def.Modifiers.Add(new StatModifierBlueprint(stat.Type, ModifierTier.IncreasedPercent, percentPerStack, source, condition));
            return this;
        }

        /// <summary>
        /// Adds a Tier 3 (Compounding Multiplier) modifier to any stat token.
        /// </summary>
        public StatusEffectBuilder WithMultiplierModifier(BaseStatToken stat, float factorPerStack, string? source = null, Func<Character, bool>? condition = null)
        {
            _def.Modifiers.Add(new StatModifierBlueprint(stat.Type, ModifierTier.Multiplier, factorPerStack, source, condition));
            return this;
        }

        /// <summary>
        /// Adds a modifier for a built-in stat using raw enum and tier.
        /// </summary>
        public StatusEffectBuilder WithModifier(CharacterStatType statType, ModifierTier tier, float valuePerStack, string? source = null, Func<Character, bool>? condition = null)
        {
            _def.Modifiers.Add(new StatModifierBlueprint(statType, tier, valuePerStack, source, condition));
            return this;
        }

        /// <summary>
        /// Adds a custom Tier 1 (Flat) modifier for a registered custom stat token.
        /// </summary>
        public StatusEffectBuilder WithCustomFlatModifier(CustomStatToken token, float amountPerStack, string? source = null, Func<Character, bool>? condition = null)
        {
            if (!token.PermitsFlat) throw new InvalidOperationException($"Custom stat '{token.Key}' does not permit Tier 1 (Flat) modifiers.");
            _def.Modifiers.Add(new StatModifierBlueprint(token.Key, ModifierTier.Flat, amountPerStack, source, condition));
            return this;
        }

        /// <summary>
        /// Adds a custom Tier 2 (Additive Percentage) modifier for a registered custom stat token.
        /// </summary>
        public StatusEffectBuilder WithCustomIncreasedModifier(CustomStatToken token, float percentPerStack, string? source = null, Func<Character, bool>? condition = null)
        {
            if (!token.PermitsAdditive) throw new InvalidOperationException($"Custom stat '{token.Key}' does not permit Tier 2 (Additive Percentage) modifiers.");
            _def.Modifiers.Add(new StatModifierBlueprint(token.Key, ModifierTier.IncreasedPercent, percentPerStack, source, condition));
            return this;
        }

        /// <summary>
        /// Adds a custom Tier 3 (Compounding Multiplier) modifier for a registered custom stat token.
        /// </summary>
        public StatusEffectBuilder WithCustomMultiplierModifier(CustomStatToken token, float factorPerStack, string? source = null, Func<Character, bool>? condition = null)
        {
            if (!token.PermitsMultiplier) throw new InvalidOperationException($"Custom stat '{token.Key}' does not permit Tier 3 (Compounding Multiplier) modifiers.");
            _def.Modifiers.Add(new StatModifierBlueprint(token.Key, ModifierTier.Multiplier, factorPerStack, source, condition));
            return this;
        }

        /// <summary>
        /// Adds a custom modifier for a string-keyed stat.
        /// </summary>
        public StatusEffectBuilder WithCustomModifier(string customKey, ModifierTier tier, float valuePerStack, string? source = null, Func<Character, bool>? condition = null)
        {
            _def.Modifiers.Add(new StatModifierBlueprint(customKey, tier, valuePerStack, source, condition));
            return this;
        }

        /// <summary>
        /// Configures periodic ticking damage (DoT / bleed / poison).
        /// </summary>
        public StatusEffectBuilder WithPeriodicDamage(DamageType type, float amountPerTick, float intervalSeconds = 1.0f, bool ignoresArmor = true)
        {
            _def.PeriodicDamageType = type;
            _def.PeriodicDamageAmount = Mathf.Max(0f, amountPerTick);
            _def.PeriodicDamageInterval = Mathf.Max(0.1f, intervalSeconds);
            _def.PeriodicIgnoresArmor = ignoresArmor;
            return this;
        }

        /// <summary>
        /// Configures this status effect to operate in multi-slice mode with independent timers and continuous values.
        /// </summary>
        public StatusEffectBuilder AsMultiSlice(
            SliceAggregationMode aggregation = SliceAggregationMode.Sum,
            int maxSlices = 0,
            float defaultSliceValue = 1.0f,
            float defaultDuration = 0f)
        {
            _def.SliceAggregation = aggregation;
            _def.MaxSlices = Mathf.Max(0, maxSlices);
            _def.DefaultSliceValue = defaultSliceValue;
            _def.DefaultSliceDuration = Mathf.Max(0f, defaultDuration);
            if (defaultDuration > 0f && _def.BaseDuration <= 0f)
            {
                _def.BaseDuration = defaultDuration;
            }
            return this;
        }

        /// <summary>
        /// Configures multi-slice periodic ticking damage (DoT) where each slice deals its damage over its duration.
        /// </summary>
        public StatusEffectBuilder WithSlicePeriodicDamage(
            DamageType type,
            float defaultDamagePerSlice,
            float tickIntervalSeconds = 2.0f,
            bool ignoresArmor = true)
        {
            _def.SliceAggregation = SliceAggregationMode.Sum;
            _def.PeriodicDamageType = type;
            _def.DefaultSliceValue = defaultDamagePerSlice;
            _def.SliceTickInterval = Mathf.Max(0.1f, tickIntervalSeconds);
            _def.PeriodicIgnoresArmor = ignoresArmor;
            _def.SliceIconFormat = SliceIconFormat.SingleDecimal;
            return this;
        }

        /// <summary>
        /// Adds a Tier 2 (Additive Percentage) modifier scaled by the aggregated multi-slice value: scale * aggregate.
        /// </summary>
        public StatusEffectBuilder WithSliceIncreased(
            StatToken stat,
            float scale = 1.0f,
            string? source = null,
            Func<Character, bool>? condition = null)
        {
            _def.Modifiers.Add(new StatModifierBlueprint(stat.Type, ModifierTier.IncreasedPercent, scale, source, condition));
            return this;
        }

        /// <summary>
        /// Adds a Tier 3 (Compounding Multiplier) modifier dynamically transformed from the aggregated multi-slice value via <paramref name="transformer"/>.
        /// </summary>
        public StatusEffectBuilder WithSliceMultiplier(
            BaseStatToken stat,
            Func<float, float> transformer,
            string? source = null,
            Func<Character, bool>? condition = null)
        {
            _def.Modifiers.Add(new StatModifierBlueprint(stat.Type, ModifierTier.Multiplier, 1.0f, source, condition, transformer));
            return this;
        }

        /// <summary>
        /// Adds a continuous resistance modifier dynamically transformed from the aggregated multi-slice value via <paramref name="transformer"/>.
        /// </summary>
        public StatusEffectBuilder WithSliceResistance(
            ResistanceStat stat,
            Func<float, float> transformer,
            string? source = null,
            Func<Character, bool>? condition = null)
        {
            _def.Modifiers.Add(new StatModifierBlueprint(stat.Type, ModifierTier.Multiplier, 1.0f, source, condition, transformer));
            return this;
        }

        /// <summary>
        /// Adds a custom stat modifier dynamically transformed from the aggregated multi-slice value via <paramref name="transformer"/>.
        /// </summary>
        public StatusEffectBuilder WithSliceCustomModifier(
            string customKey,
            ModifierTier tier,
            Func<float, float> transformer,
            string? source = null,
            Func<Character, bool>? condition = null)
        {
            _def.Modifiers.Add(new StatModifierBlueprint(customKey, tier, 1.0f, source, condition, transformer));
            return this;
        }

        /// <summary>
        /// Configures multi-slice consumption where ALL active slices are consumed in a single trigger (e.g. next melee hit delivered or damage taken).
        /// </summary>
        public StatusEffectBuilder WithSliceConsumeAllOnTrigger(ChargeConsumeTrigger trigger)
        {
            _def.ChargeConsumptionTrigger = trigger;
            _def.ConsumeAllChargesOnTrigger = true;
            return this;
        }

        /// <summary>
        /// Configures multi-slice damage absorption barrier where incoming damage depletes the shortest-remaining-duration slice first.
        /// </summary>
        public StatusEffectBuilder WithSliceShield(float defaultCapacityPerSlice = 100f, float defaultDuration = 30f)
        {
            _def.SliceAggregation = SliceAggregationMode.Sum;
            _def.InitialShieldCapacity = Mathf.Max(0f, defaultCapacityPerSlice);
            _def.DefaultSliceValue = Mathf.Max(0f, defaultCapacityPerSlice);
            _def.DefaultSliceDuration = Mathf.Max(0f, defaultDuration);
            _def.SliceIconFormat = SliceIconFormat.IntegerCeiling;
            return this;
        }

        /// <summary>
        /// Sets the icon text display formatting for multi-slice values on the HUD.
        /// </summary>
        public StatusEffectBuilder WithSliceIconFormat(SliceIconFormat format)
        {
            _def.SliceIconFormat = format;
            return this;
        }

        /// <summary>
        /// Sets a custom icon text formatter delegate for the HUD.
        /// </summary>
        public StatusEffectBuilder WithCustomIconFormat(Func<VikingStatusEffect, string> formatter)
        {
            _def.CustomIconTextFormatter = formatter;
            return this;
        }

        /// <summary>
        /// Sets visual and audio effects played when this status effect is applied.
        /// </summary>
        public StatusEffectBuilder WithStartEffects(EffectList startEffects)
        {
            _def.StartEffects = startEffects ?? new EffectList();
            return this;
        }

        /// <summary>
        /// Sets visual and audio effects played when this status effect expires or is removed.
        /// </summary>
        public StatusEffectBuilder WithStopEffects(EffectList stopEffects)
        {
            _def.StopEffects = stopEffects ?? new EffectList();
            return this;
        }

        /// <summary>
        /// Returns the raw definition blueprint.
        /// </summary>
        public VikingStatusEffectDefinition BuildDefinition() => _def;

        /// <summary>
        /// Instantiates a runtime <see cref="VikingStatusEffect"/> ScriptableObject prefab without registering it globally.
        /// </summary>
        public VikingStatusEffect Build()
        {
            var se = StatusEffectRegistry.CreateInstance();
            se.Initialize(_def);
            return se;
        }

        /// <summary>
        /// Instantiates the <see cref="VikingStatusEffect"/>, registers it into <see cref="StatusEffectRegistry"/>,
        /// and injects it into Jötunn's ObjectDB system.
        /// </summary>
        public VikingStatusEffect BuildAndRegister() => StatusEffectRegistry.Register(_def);
    }
}
