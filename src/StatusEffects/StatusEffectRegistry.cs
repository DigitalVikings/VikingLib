using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Jotunn.Entities;
using Jotunn.Managers;
using UnityEngine;
using VikingLib.Combat;

namespace VikingLib.StatusEffects
{
    /// <summary>
    /// Central registry and lifecycle manager for VikingLib custom status effects.
    /// Manages prefab registration, Jötunn ObjectDB integration, charge consumption, and shield absorption.
    /// </summary>
    public static class StatusEffectRegistry
    {
        private static readonly ConcurrentDictionary<string, VikingStatusEffectDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, VikingStatusEffect> _prefabs = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<int, VikingStatusEffectDefinition> _hashToDefinition = new();

        internal static VikingStatusEffect CreateInstance()
        {
            try
            {
                return ScriptableObject.CreateInstance<VikingStatusEffect>();
            }
            catch
            {
                return (VikingStatusEffect)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(VikingStatusEffect));
            }
        }

        /// <summary>
        /// Registers a <see cref="VikingStatusEffectDefinition"/>, creates the runtime ScriptableObject prefab,
        /// and registers it with Jötunn's ItemManager for ObjectDB injection.
        /// </summary>
        public static VikingStatusEffect Register(VikingStatusEffectDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.Name)) throw new ArgumentException("Status effect name cannot be empty.", nameof(definition));

            var se = CreateInstance();
            se.Initialize(definition);

            _definitions[definition.Name] = definition;
            _prefabs[definition.Name] = se;
            _hashToDefinition[definition.Name.GetStableHashCode()] = definition;

            // Register with Jötunn if running in Valheim runtime
            try
            {
                TryRegisterWithJotunn(se);
            }
            catch (Exception ex)
            {
                VikingLibPlugin.Log?.LogDebug($"[VikingLib] StatusEffect '{definition.Name}' queued internally (Jötunn runtime note: {ex.Message})");
            }

            return se;
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static void TryRegisterWithJotunn(VikingStatusEffect se)
        {
            ItemManager.Instance.AddStatusEffect(new CustomStatusEffect(se, fixReference: true));
        }

        /// <summary>
        /// Retrieves the definition associated with the given name.
        /// </summary>
        public static VikingStatusEffectDefinition? GetDefinition(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            _definitions.TryGetValue(name, out var def);
            return def;
        }

        /// <summary>
        /// Retrieves the definition associated with the given stable hash.
        /// </summary>
        public static VikingStatusEffectDefinition? GetDefinition(int nameHash)
        {
            _hashToDefinition.TryGetValue(nameHash, out var def);
            return def;
        }

        /// <summary>
        /// Retrieves the prefab instance of the registered status effect.
        /// </summary>
        public static VikingStatusEffect? GetStatusEffect(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            _prefabs.TryGetValue(name, out var se);
            return se;
        }

        /// <summary>
        /// Returns all registered status effect definitions.
        /// </summary>
        public static IReadOnlyCollection<VikingStatusEffectDefinition> GetAllDefinitions() => _definitions.Values.ToArray();

        /// <summary>
        /// Applies an independent slice of a registered status effect to a character.
        /// If the status effect is already active, adds a new slice with its own timer and value.
        /// </summary>
        public static StatusEffect? ApplySlice(
            Character character,
            string effectName,
            float sliceValue,
            float duration = -1f,
            Character? source = null,
            object? customData = null)
        {
            if (character == null) return null;

            var seman = character.GetSEMan();
            if (seman == null) return null;

            // Immunity check for Bleed
            if (string.Equals(effectName, CanonicalStatusEffects.BleedId, StringComparison.OrdinalIgnoreCase))
            {
                if (!Combat.Defense.EntityDefenseRegistry.CanBleed(character))
                {
                    VikingLibPlugin.Log?.LogDebug($"[VikingLib] Character '{character.name}' is immune to bleeding; application rejected.");
                    return null;
                }

                // Scale bleed damage by character's BleedResistance
                float bleedRes = Stats.CharacterStatManager.Evaluate(character, Stats.CharacterStatType.ResistanceBleed);
                sliceValue *= bleedRes;
            }

            int hash = effectName.GetStableHashCode();
            var existing = seman.GetStatusEffect(hash);

            if (existing is VikingStatusEffect vse)
            {
                vse.AddSlice(sliceValue, duration, source, customData);
                return vse;
            }

            if (!_prefabs.TryGetValue(effectName, out var prefab))
            {
                VikingLibPlugin.Log?.LogWarning($"[VikingLib] Attempted to apply unregistered status effect '{effectName}'.");
                return null;
            }

            var applied = seman.AddStatusEffect(prefab, resetTime: true);
            if (applied == null)
            {
                applied = seman.GetStatusEffect(hash);
            }

            if (applied is VikingStatusEffect newVse && newVse.Definition.IsMultiSlice)
            {
                newVse.ClearSlices();
                newVse.AddSlice(sliceValue, duration, source, customData);
            }

            return applied;
        }

        /// <summary>
        /// Applies a registered status effect to a character.
        /// For multi-slice effects, applies slices with default parameters.
        /// </summary>
        public static StatusEffect? Apply(Character character, string effectName, int stacks = 1, float customDuration = -1f)
        {
            if (character == null) return null;

            var def = GetDefinition(effectName);
            if (def != null && def.IsMultiSlice)
            {
                float defaultVal = def.InitialShieldCapacity > 0f ? def.InitialShieldCapacity : def.DefaultSliceValue;
                float dur = customDuration >= 0f ? customDuration : (def.DefaultSliceDuration > 0f ? def.DefaultSliceDuration : def.BaseDuration);

                var applied = ApplySlice(character, effectName, defaultVal, dur);
                if (applied is VikingStatusEffect vse && stacks > 1)
                {
                    for (int s = 1; s < stacks; s++)
                    {
                        vse.AddSlice(defaultVal, dur);
                    }
                }
                return applied;
            }

            var seman = character.GetSEMan();
            if (seman == null) return null;

            if (!_prefabs.TryGetValue(effectName, out var prefab))
            {
                VikingLibPlugin.Log?.LogWarning($"[VikingLib] Attempted to apply unregistered status effect '{effectName}'.");
                return null;
            }

            // Apply base status effect
            var singleApplied = seman.AddStatusEffect(prefab, resetTime: true);
            if (singleApplied is VikingStatusEffect singleVse)
            {
                if (customDuration > 0f)
                {
                    singleVse.m_ttl = customDuration;
                }

                // If multiple stacks requested on initial apply, apply remaining stacks
                for (int s = 1; s < stacks; s++)
                {
                    singleVse.ResetTime();
                }
            }

            return singleApplied;
        }

        /// <summary>
        /// Removes a status effect by name from a character.
        /// </summary>
        public static bool Remove(Character character, string effectName)
        {
            if (character == null) return false;
            var seman = character.GetSEMan();
            if (seman == null) return false;

            return seman.RemoveStatusEffect(effectName.GetStableHashCode(), quiet: false);
        }

        /// <summary>
        /// Evaluates active status effects on a character and consumes charges matching the given trigger.
        /// </summary>
        public static int ConsumeCharges(Character? character, ChargeConsumeTrigger trigger, int count = 1)
        {
            if (character == null || trigger == ChargeConsumeTrigger.None) return 0;

            var seman = character.GetSEMan();
            if (seman == null) return 0;

            var activeEffects = seman.GetStatusEffects();
            if (activeEffects == null || activeEffects.Count == 0) return 0;

            int consumedTotal = 0;
            // Iterate backwards to support potential removal during iteration
            for (int i = activeEffects.Count - 1; i >= 0; i--)
            {
                if (i < activeEffects.Count && activeEffects[i] is VikingStatusEffect vse)
                {
                    if (vse.TryConsumeCharge(trigger, count))
                    {
                        consumedTotal++;
                    }
                }
            }

            return consumedTotal;
        }

        /// <summary>
        /// Routes damage through all active damage absorption shields on the target character.
        /// </summary>
        public static float AbsorbDamage(DamageContext ctx)
        {
            if (ctx.Target == null || ctx.IsPreview || ctx.IsCancelled) return 0f;
            if (ctx.GetTotalDamage() <= 0.001f) return 0f;

            var seman = ctx.Target.GetSEMan();
            if (seman == null) return 0f;

            var activeEffects = seman.GetStatusEffects();
            if (activeEffects == null || activeEffects.Count == 0) return 0f;

            float totalAbsorbed = 0f;
            for (int i = activeEffects.Count - 1; i >= 0; i--)
            {
                if (i < activeEffects.Count && activeEffects[i] is VikingStatusEffect vse && vse.CurrentShieldCapacity > 0.001f)
                {
                    float incoming = ctx.GetTotalDamage();
                    if (incoming <= 0.001f) break;

                    float absorbed = vse.AbsorbDamage(incoming);
                    if (absorbed > 0.001f)
                    {
                        ctx.AbsorbDamage(absorbed, vse.m_name);
                        totalAbsorbed += absorbed;
                    }
                }
            }

            return totalAbsorbed;
        }
    }
}
