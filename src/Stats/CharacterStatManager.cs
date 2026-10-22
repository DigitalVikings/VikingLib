using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using VikingLib.Combat;
using VikingLib.Configuration;

namespace VikingLib.Stats
{
    /// <summary>
    /// Central manager for character statistics, declarative modifier tokens, and dual-space mathematical evaluation.
    /// </summary>
    public static class CharacterStatManager
    {
        private static readonly ConditionalWeakTable<Character, List<StatModifier>> _characterModifiers = new();
        private static readonly object _lock = new();

        /// <summary>
        /// Legacy alias forwarding to VanillaSkillIntegration.
        /// </summary>
        [System.Obsolete("Use VanillaSkillIntegration channels directly.")]
        public static bool EnableVanillaSkillModifiers => VikingLib.SkillSystem.VanillaSkillIntegration.EnableLocomotionStaminaScaling;

        /// <summary>
        /// Returns true if the stat supports Tier 1 (Flat) modifiers.
        /// Only stats with real physical units (HP, kg, meters, m/s, seconds, armor, flat resource restores) support flat modifiers.
        /// </summary>
        public static bool SupportsFlat(CharacterStatType statType) => statType switch
        {
            CharacterStatType.MaxHealth or
            CharacterStatType.MaxStamina or
            CharacterStatType.MaxEitr or
            CharacterStatType.MaxCarryWeight or
            CharacterStatType.HealthRegen or
            CharacterStatType.HealthRegenDelay or
            CharacterStatType.StaminaRegen or
            CharacterStatType.EitrRegen or
            CharacterStatType.StaminaRegenDelay or
            CharacterStatType.JumpHeight or
            CharacterStatType.WalkSpeed or
            CharacterStatType.CrouchSpeed or
            CharacterStatType.SwimSpeed or
            CharacterStatType.BodyArmor or
            CharacterStatType.ParryWindow or
            CharacterStatType.CriticalStrikeChance or
            CharacterStatType.HealthOnKill or
            CharacterStatType.StaminaOnKill or
            CharacterStatType.EitrOnKill or
            CharacterStatType.HealthOnDamageTaken or
            CharacterStatType.StaminaOnDamageTaken or
            CharacterStatType.EitrOnDamageTaken or
            CharacterStatType.HealthOnCauseStagger or
            CharacterStatType.StaminaOnCauseStagger or
            CharacterStatType.EitrOnCauseStagger => true,

            _ => false
        };

        /// <summary>
        /// Returns true if the stat supports Tier 2 (Additive Percentage) modifiers.
        /// BodyArmor is strictly flat-only (percentage scaling flows through ArmorEfficiency).
        /// Resistance stats only permit Tier 3 (Compounding Multipliers).
        /// </summary>
        public static bool SupportsAdditive(CharacterStatType statType) => statType switch
        {
            CharacterStatType.BodyArmor or
            CharacterStatType.ResistancePhysical or
            CharacterStatType.ResistanceElemental or
            CharacterStatType.ResistanceSlashing or
            CharacterStatType.ResistanceBlunt or
            CharacterStatType.ResistancePiercing or
            CharacterStatType.ResistanceFire or
            CharacterStatType.ResistanceFrost or
            CharacterStatType.ResistanceLightning or
            CharacterStatType.ResistancePoison or
            CharacterStatType.ResistanceSpirit or
            CharacterStatType.ResistanceChop or
            CharacterStatType.ResistancePickaxe or
            CharacterStatType.ResistanceFall or
            CharacterStatType.ResistanceBleed => false,

            _ => true
        };

        /// <summary>
        /// Returns true if the stat supports Tier 3 (Compounding Multipliers).
        /// BodyArmor is strictly flat-only (multiplier scaling flows through ArmorEfficiency).
        /// </summary>
        public static bool SupportsMultiplier(CharacterStatType statType) => statType switch
        {
            CharacterStatType.BodyArmor => false,

            _ => true
        };

        /// <summary>
        /// Registers a declarative stat modifier token for a character.
        /// </summary>
        public static void AddModifier(Character character, StatModifier modifier)
        {
            if (character is null || modifier is null) return;
            if (!IsModifierValid(modifier)) return;

            lock (_lock)
            {
                var list = _characterModifiers.GetOrCreateValue(character);
                list.Add(modifier);
            }
        }

        private static bool IsModifierValid(StatModifier modifier)
        {
            if (modifier.IsCustom)
            {
                if (StatRegistry.TryGet(modifier.CustomStatKey!, out var customToken) && customToken != null)
                {
                    if (modifier.Tier == ModifierTier.Flat && !customToken.PermitsFlat)
                    {
                        VikingLibPlugin.Log.LogError($"[VikingLib] Invalid Modifier Rejected! Custom stat '{modifier.CustomStatKey}' does not permit Tier 1 (Flat) modifiers from source '{modifier.Source}'.");
                        return false;
                    }
                    if (modifier.Tier == ModifierTier.IncreasedPercent && !customToken.PermitsAdditive)
                    {
                        VikingLibPlugin.Log.LogError($"[VikingLib] Invalid Modifier Rejected! Custom stat '{modifier.CustomStatKey}' does not permit Tier 2 (Additive Percentage) modifiers from source '{modifier.Source}'.");
                        return false;
                    }
                    if (modifier.Tier == ModifierTier.Multiplier && !customToken.PermitsMultiplier)
                    {
                        VikingLibPlugin.Log.LogError($"[VikingLib] Invalid Modifier Rejected! Custom stat '{modifier.CustomStatKey}' does not permit Tier 3 (Compounding Multiplier) modifiers from source '{modifier.Source}'.");
                        return false;
                    }
                }
                return true;
            }

            if (modifier.Tier == ModifierTier.Flat && !SupportsFlat(modifier.StatType))
            {
                VikingLibPlugin.Log.LogError(
                    $"[VikingLib] Invalid Modifier Rejected! Stat '{modifier.StatType}' does not support " +
                    $"Tier 1 (Flat) modifiers from source '{modifier.Source}' (+{modifier.Value}). " +
                    $"Use Tier 2 (IncreasedPercent) or Tier 3 (Multiplier)!"
                );
                return false;
            }

            if (modifier.Tier == ModifierTier.IncreasedPercent && !SupportsAdditive(modifier.StatType))
            {
                VikingLibPlugin.Log.LogError(
                    $"[VikingLib] Invalid Modifier Rejected! Stat '{modifier.StatType}' does not permit " +
                    $"Tier 2 (Additive Percentage) modifiers. Source: '{modifier.Source}' ({modifier.Value}). " +
                    (modifier.StatType == CharacterStatType.BodyArmor
                        ? "BodyArmor is flat-only; use ArmorEfficiency to scale armor effectiveness!"
                        : "Resistances only permit Tier 3 (Compounding Multipliers).")
                );
                return false;
            }

            if (modifier.Tier == ModifierTier.Multiplier && !SupportsMultiplier(modifier.StatType))
            {
                VikingLibPlugin.Log.LogError(
                    $"[VikingLib] Invalid Modifier Rejected! Stat '{modifier.StatType}' is flat-only and does not permit " +
                    $"Tier 3 (Compounding Multipliers). Source: '{modifier.Source}' ({modifier.Value}x). " +
                    $"Use ArmorEfficiency to scale armor effectiveness!"
                );
                return false;
            }

            return true;
        }

        /// <summary>
        /// Atomically replaces every modifier registered under <paramref name="owner"/> with a new set.
        /// The removal and the registration happen inside a single lock acquisition, so a concurrent
        /// <see cref="Evaluate(Character?, CharacterStatType, float, out StatTrace)"/> never observes a frame
        /// where the owner's modifiers are missing (stats collapsing to baseline).
        /// Invalid modifiers (unsupported tier for the stat) are rejected individually and logged, like <see cref="AddModifier"/>.
        /// Modifiers in <paramref name="newModifiers"/> should carry <paramref name="owner"/> as their Owner so
        /// subsequent replacements and <see cref="RemoveModifiersByOwner"/> can find them.
        /// </summary>
        /// <returns>The number of previous modifiers removed.</returns>
        public static int ReplaceModifiersByOwner(Character character, object owner, IEnumerable<StatModifier>? newModifiers)
        {
            if (character is null || owner is null) return 0;

            // Validate and materialize outside the lock to keep the critical section minimal.
            var accepted = new List<StatModifier>();
            if (newModifiers != null)
            {
                foreach (var mod in newModifiers)
                {
                    if (mod != null && IsModifierValid(mod)) accepted.Add(mod);
                }
            }

            lock (_lock)
            {
                var list = _characterModifiers.GetOrCreateValue(character);
                int removed = list.RemoveAll(m => ReferenceEquals(m.Owner, owner) || Equals(m.Owner, owner));
                list.AddRange(accepted);
                return removed;
            }
        }

        /// <summary>
        /// Adds a Tier 1 (Flat) modifier for a physical stat.
        /// Compile-time restricted to <see cref="PhysicalStat"/>.
        /// </summary>
        public static void AddFlat(Character character, PhysicalStat stat, float amount, string source, Func<Character, bool>? condition = null, object? owner = null) =>
            AddModifier(character, StatModifier.Flat(stat, amount, source, condition, owner));

        /// <summary>
        /// Adds a Tier 2 (Additive Percentage) modifier to any stat token.
        /// </summary>
        public static void AddIncreased(Character character, StatToken stat, float percent, string source, Func<Character, bool>? condition = null, object? owner = null) =>
            AddModifier(character, StatModifier.Increased(stat, percent, source, condition, owner));

        /// <summary>
        /// Adds a Tier 3 (Compounding Multiplier) modifier to any stat token.
        /// </summary>
        public static void AddMultiplier(Character character, BaseStatToken stat, float multiplier, string source, Func<Character, bool>? condition = null, object? owner = null) =>
            AddModifier(character, StatModifier.Multiplier(stat, multiplier, source, condition, owner));

        /// <summary>
        /// Removes a specific stat modifier token from a character.
        /// </summary>
        public static bool RemoveModifier(Character character, StatModifier modifier)
        {
            if (character is null || modifier is null) return false;

            lock (_lock)
            {
                if (_characterModifiers.TryGetValue(character, out var list))
                {
                    return list.Remove(modifier);
                }
            }
            return false;
        }

        /// <summary>
        /// Removes all modifiers registered by a specific owner (e.g. an ItemData, StatusEffect, or mod key).
        /// </summary>
        public static int RemoveModifiersByOwner(Character character, object owner)
        {
            if (character is null || owner is null) return 0;

            lock (_lock)
            {
                if (_characterModifiers.TryGetValue(character, out var list))
                {
                    return list.RemoveAll(m => ReferenceEquals(m.Owner, owner) || Equals(m.Owner, owner));
                }
            }
            return 0;
        }

        /// <summary>
        /// Removes all modifiers registered from a specific source label.
        /// </summary>
        public static int RemoveModifiersFromSource(Character character, string source, CharacterStatType? statType = null)
        {
            if (character is null || string.IsNullOrEmpty(source)) return 0;

            lock (_lock)
            {
                if (_characterModifiers.TryGetValue(character, out var list))
                {
                    return list.RemoveAll(m => m.Source.Equals(source, StringComparison.OrdinalIgnoreCase) &&
                                               (!statType.HasValue || m.StatType == statType.Value));
                }
            }
            return 0;
        }

        /// <summary>
        /// Removes all modifiers registered under a custom stat from a specific source label.
        /// </summary>
        public static int RemoveModifiersFromSource(Character character, string source, string customKey)
        {
            if (character is null || string.IsNullOrEmpty(source) || string.IsNullOrEmpty(customKey)) return 0;

            lock (_lock)
            {
                if (_characterModifiers.TryGetValue(character, out var list))
                {
                    return list.RemoveAll(m => m.Source.Equals(source, StringComparison.OrdinalIgnoreCase) &&
                                               m.IsCustom && string.Equals(m.CustomStatKey, customKey, StringComparison.OrdinalIgnoreCase));
                }
            }
            return 0;
        }

        /// <summary>
        /// Returns all active modifier tokens for a character and stat type.
        /// </summary>
        public static List<StatModifier> GetModifiers(Character character, CharacterStatType statType)
        {
            var result = new List<StatModifier>();
            if (character is null) return result;

            lock (_lock)
            {
                if (_characterModifiers.TryGetValue(character, out var list))
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        var mod = list[i];
                        if (mod.StatType == statType)
                        {
                            result.Add(mod);
                        }
                    }
                }
            }
            return result;
        }

        /// <summary>
        /// Evaluates a stat using the Standard 3-Tier Mathematical Engine:
        /// <para>
        /// <c>Final = (Base + Σ Flat) × max(0, 1 + Σ Increased%) × Π Multipliers</c>
        /// </para>
        /// Used for linear, physical, and multiplicative properties.
        /// (For action resource drains, see <see cref="GetEffectiveActionCost"/> for the asymptotic efficiency model).
        /// </summary>
        public static float Evaluate(Character? character, CharacterStatType statType, float baseValue, out StatTrace trace)
        {
            trace = new StatTrace
            {
                StatType = statType,
                BaseValue = baseValue
            };

            float flatSum = 0f;
            float increasedSum = 0f;
            float multiplierProduct = 1f;

            if (character is not null)
            {
                lock (_lock)
                {
                    if (_characterModifiers.TryGetValue(character, out var list))
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            var mod = list[i];
                            if (mod.StatType != statType) continue;

                            // Food effectiveness channels only accept Tier 2 additive percent modifiers
                            if (statType is CharacterStatType.FoodHealthEffectiveness
                                or CharacterStatType.FoodHealthRegenEffectiveness
                                or CharacterStatType.FoodStaminaEffectiveness
                                or CharacterStatType.FoodEitrEffectiveness)
                            {
                                if (mod.Tier != ModifierTier.IncreasedPercent) continue;
                            }

                            // Evaluate dynamic condition if present
                            if (mod.Condition != null && !mod.Condition(character)) continue;

                            switch (mod.Tier)
                            {
                                case ModifierTier.Flat:
                                    if (!SupportsFlat(statType)) continue;
                                    flatSum += mod.Value;
                                    trace.FlatModifiers.Add((mod.Source, mod.Value));
                                    break;

                                case ModifierTier.IncreasedPercent:
                                    if (!SupportsAdditive(statType)) continue;
                                    increasedSum += mod.Value;
                                    trace.IncreasedModifiers.Add((mod.Source, mod.Value));
                                    break;

                                case ModifierTier.Multiplier:
                                    multiplierProduct *= mod.Value;
                                    trace.Multipliers.Add((mod.Source, mod.Value));
                                    break;
                            }
                        }
                    }
                }
            }

            trace.FlatSum = flatSum;
            trace.IncreasedSum = increasedSum;
            trace.MultiplierProduct = multiplierProduct;

            float finalValue;
            if (IsEfficiencyStat(statType))
            {
                // Asymptotic Efficiency Engine:
                // Effective Cost = Base Cost / ((1 + Tier 2 Efficiency%) * Tier 3 Multipliers)
                // Total Efficiency Factor = (1 + flatSum + increasedSum) * multiplierProduct
                // Net Efficiency Rating = Total Efficiency Factor - 1.0
                // In both Tier 2 and Tier 3, higher values are always better!
                float effFactor = (1f + flatSum + increasedSum) * multiplierProduct;
                finalValue = effFactor - 1f;
            }
            else
            {
                float baseWithFlat = baseValue + flatSum;
                float additiveFactor = Mathf.Max(0f, 1f + increasedSum);
                finalValue = baseWithFlat * additiveFactor * multiplierProduct;
            }

            trace.FinalValue = finalValue;
            return finalValue;
        }

        /// <summary>
        /// Evaluates a stat and returns the final calculated float value.
        /// </summary>
        public static float Evaluate(Character? character, CharacterStatType statType, float baseValue) =>
            Evaluate(character, statType, baseValue, out _);

        /// <summary>
        /// Computes the default baseline and evaluates the final stat for a character.
        /// </summary>
        public static float Evaluate(Character? character, CharacterStatType statType) =>
            GetStat(character, statType);

        /// <summary>
        /// Computes the default baseline and evaluates the final stat for a character.
        /// </summary>
        public static float GetStat(Character? character, CharacterStatType statType)
        {
            float baseVal = GetBaseValue(character, statType);
            return Evaluate(character, statType, baseVal);
        }

        /// <summary>
        /// Computes the default baseline, evaluates the final stat, and returns the diagnostic trace.
        /// </summary>
        public static StatTrace GetTrace(Character? character, CharacterStatType statType)
        {
            float baseVal = GetBaseValue(character, statType);
            Evaluate(character, statType, baseVal, out var trace);
            return trace;
        }

        /// <summary>
        /// Evaluates a dynamic custom stat for a character with 3-tier math and diagnostic trace.
        /// </summary>
        public static float Evaluate(Character? character, string customKey, float baseValue, out StatTrace trace)
        {
            trace = new StatTrace
            {
                StatType = (CharacterStatType)(-1),
                CustomStatKey = customKey,
                BaseValue = baseValue
            };

            float flatSum = 0f;
            float increasedSum = 0f;
            float multiplierProduct = 1f;

            if (character != null && !string.IsNullOrEmpty(customKey))
            {
                lock (_lock)
                {
                    if (_characterModifiers.TryGetValue(character, out var list))
                    {
                        for (int i = 0; i < list.Count; i++)
                        {
                            var mod = list[i];
                            if (!mod.IsCustom || !string.Equals(mod.CustomStatKey, customKey, StringComparison.OrdinalIgnoreCase)) continue;

                            // Evaluate dynamic condition if present
                            if (mod.Condition != null && !mod.Condition(character)) continue;

                            switch (mod.Tier)
                            {
                                case ModifierTier.Flat:
                                    flatSum += mod.Value;
                                    trace.FlatModifiers.Add((mod.Source, mod.Value));
                                    break;

                                case ModifierTier.IncreasedPercent:
                                    increasedSum += mod.Value;
                                    trace.IncreasedModifiers.Add((mod.Source, mod.Value));
                                    break;

                                case ModifierTier.Multiplier:
                                    multiplierProduct *= mod.Value;
                                    trace.Multipliers.Add((mod.Source, mod.Value));
                                    break;
                            }
                        }
                    }
                }
            }

            trace.FlatSum = flatSum;
            trace.IncreasedSum = increasedSum;
            trace.MultiplierProduct = multiplierProduct;

            float baseWithFlat = baseValue + flatSum;
            float additiveFactor = Mathf.Max(0f, 1f + increasedSum);
            float finalValue = baseWithFlat * additiveFactor * multiplierProduct;

            trace.FinalValue = finalValue;
            return finalValue;
        }

        /// <summary>
        /// Evaluates a dynamic custom stat and returns the calculated float value.
        /// </summary>
        public static float Evaluate(Character? character, string customKey, float baseValue) =>
            Evaluate(character, customKey, baseValue, out _);

        /// <summary>
        /// Evaluates a dynamic custom stat for a character, defaulting to registered default or specified fallback.
        /// </summary>
        public static float GetStat(Character? character, string customKey, float defaultValue = 0f)
        {
            if (StatRegistry.TryGet(customKey, out var token) && token != null)
            {
                defaultValue = token.DefaultValue;
            }
            return Evaluate(character, customKey, defaultValue);
        }

        /// <summary>
        /// Evaluates a dynamic custom stat for a character and returns the diagnostic trace.
        /// </summary>
        public static StatTrace GetTrace(Character? character, string customKey, float defaultValue = 0f)
        {
            if (StatRegistry.TryGet(customKey, out var token) && token != null)
            {
                defaultValue = token.DefaultValue;
            }
            Evaluate(character, customKey, defaultValue, out var trace);
            return trace;
        }

        // =========================================================================
        // DUAL-SPACE TRANSFORMATION LAYER (DEVELOPER SPACE -> NATIVE ENGINE SPACE)
        // =========================================================================

        /// <summary>
        /// Transforms evaluated JumpHeight (in meters of apex height, base 1.7m) into physical takeoff velocity (m/s).
        /// Follows the kinetic relation: <c>v = v0 * sqrt(targetHeight / baseHeight)</c>.
        /// </summary>
        public static float GetEffectiveJumpVelocity(Character character)
        {
            if (character == null) return 10f;
            const float baseHeight = 1.7f;
            float targetHeight = Mathf.Max(0.1f, GetStat(character, CharacterStatType.JumpHeight));
            float baseVelocity = character.m_jumpForce;
            if (baseVelocity <= 0.001f) baseVelocity = 10f;
            return baseVelocity * Mathf.Sqrt(targetHeight / baseHeight);
        }

        /// <summary>
        /// Transforms evaluated ProjectilePrecision (base 1.0x) into native projectile spread angle multiplier.
        /// Higher precision reduces scatter: <c>SpreadMultiplier = 1 / Precision</c>.
        /// </summary>
        public static float GetEffectiveSpreadMultiplier(Character character)
        {
            if (character == null) return 1f;
            float precision = Mathf.Max(0.01f, GetStat(character, CharacterStatType.ProjectilePrecision));
            return 1f / precision;
        }

        /// <summary>
        /// Transforms evaluated Concealment (base 1.0x) into native AI detection visibility factor.
        /// Higher concealment reduces player visibility: <c>VisibilityFactor = 1 / Concealment</c>.
        /// </summary>
        public static float GetEffectiveStealthFactor(Player player)
        {
            if (player == null) return 1f;
            float concealment = Mathf.Max(0.01f, GetStat(player, CharacterStatType.Concealment));
            return 1f / concealment;
        }

        /// <summary>
        /// Returns true if the given stat is an action resource efficiency rating.
        /// </summary>
        public static bool IsEfficiencyStat(CharacterStatType statType) => statType switch
        {
            CharacterStatType.SprintEfficiency or
            CharacterStatType.JumpEfficiency or
            CharacterStatType.DodgeEfficiency or
            CharacterStatType.SneakEfficiency or
            CharacterStatType.SwimEfficiency or
            CharacterStatType.AttackStaminaEfficiency or
            CharacterStatType.AttackEitrEfficiency or
            CharacterStatType.AttackHealthEfficiency or
            CharacterStatType.BowDrawStaminaEfficiency or
            CharacterStatType.BowDrawEitrEfficiency or
            CharacterStatType.FishingStaminaEfficiency or
            CharacterStatType.BlockStaminaEfficiency or
            CharacterStatType.RidingStaminaEfficiency or
            CharacterStatType.MeleeStaminaEfficiency or
            CharacterStatType.RangedStaminaEfficiency or
            CharacterStatType.OneHandedStaminaEfficiency or
            CharacterStatType.TwoHandedStaminaEfficiency or
            CharacterStatType.SecondaryAttackStaminaEfficiency or
            CharacterStatType.SwordsEfficiency or
            CharacterStatType.KnivesEfficiency or
            CharacterStatType.ClubsEfficiency or
            CharacterStatType.PolearmsEfficiency or
            CharacterStatType.SpearsEfficiency or
            CharacterStatType.AxesEfficiency or
            CharacterStatType.BowsEfficiency or
            CharacterStatType.CrossbowReloadStaminaEfficiency or
            CharacterStatType.UnarmedEfficiency or
            CharacterStatType.ElementalMagicEitrEfficiency or
            CharacterStatType.BloodMagicHealthEfficiency or
            CharacterStatType.BloodMagicEitrEfficiency or
            CharacterStatType.HomeToolsEfficiency => true,
            _ => false
        };

        /// <summary>
        /// Maps a Valheim weapon skill type to its corresponding VikingLib weapon efficiency stat.
        /// </summary>
        public static CharacterStatType? GetWeaponEfficiencyStat(Skills.SkillType skillType) => skillType switch
        {
            Skills.SkillType.Swords => CharacterStatType.SwordsEfficiency,
            Skills.SkillType.Knives => CharacterStatType.KnivesEfficiency,
            Skills.SkillType.Clubs => CharacterStatType.ClubsEfficiency,
            Skills.SkillType.Polearms => CharacterStatType.PolearmsEfficiency,
            Skills.SkillType.Spears => CharacterStatType.SpearsEfficiency,
            Skills.SkillType.Axes => CharacterStatType.AxesEfficiency,
            Skills.SkillType.Bows => CharacterStatType.BowsEfficiency,
            Skills.SkillType.Crossbows => CharacterStatType.CrossbowReloadStaminaEfficiency,
            Skills.SkillType.Unarmed => CharacterStatType.UnarmedEfficiency,
            Skills.SkillType.ElementalMagic => CharacterStatType.ElementalMagicEitrEfficiency,
            Skills.SkillType.BloodMagic => CharacterStatType.BloodMagicHealthEfficiency,
            _ => null
        };

        /// <summary>
        /// Maps a damage type to its corresponding VikingLib continuous resistance stat.
        /// </summary>
        public static CharacterStatType? GetResistanceStat(DamageType type) => type switch
        {
            DamageType.Slash => CharacterStatType.ResistanceSlashing,
            DamageType.Blunt => CharacterStatType.ResistanceBlunt,
            DamageType.Pierce => CharacterStatType.ResistancePiercing,
            DamageType.Fire => CharacterStatType.ResistanceFire,
            DamageType.Frost => CharacterStatType.ResistanceFrost,
            DamageType.Lightning => CharacterStatType.ResistanceLightning,
            DamageType.Poison => CharacterStatType.ResistancePoison,
            DamageType.Spirit => CharacterStatType.ResistanceSpirit,
            DamageType.Chop => CharacterStatType.ResistanceChop,
            DamageType.Pickaxe => CharacterStatType.ResistancePickaxe,
            _ => null
        };

        /// <summary>
        /// Maps a Valheim weapon skill type to its corresponding VikingLib weapon damage multiplier stat.
        /// </summary>
        public static CharacterStatType? GetWeaponDamageStat(Skills.SkillType skillType) => skillType switch
        {
            Skills.SkillType.Swords => CharacterStatType.DamageSwords,
            Skills.SkillType.Knives => CharacterStatType.DamageKnives,
            Skills.SkillType.Clubs => CharacterStatType.DamageClubs,
            Skills.SkillType.Polearms => CharacterStatType.DamagePolearms,
            Skills.SkillType.Spears => CharacterStatType.DamageSpears,
            Skills.SkillType.Axes => CharacterStatType.DamageAxes,
            Skills.SkillType.Bows => CharacterStatType.DamageBows,
            Skills.SkillType.Crossbows => CharacterStatType.DamageCrossbows,
            Skills.SkillType.Unarmed => CharacterStatType.DamageUnarmed,
            Skills.SkillType.ElementalMagic => CharacterStatType.DamageElementalMagic,
            Skills.SkillType.BloodMagic => CharacterStatType.DamageBloodMagic,
            _ => null
        };

        /// <summary>
        /// Maps a Valheim weapon skill type to its corresponding VikingLib weapon attack speed multiplier stat.
        /// </summary>
        public static CharacterStatType? GetWeaponAttackSpeedStat(Skills.SkillType skillType) => skillType switch
        {
            Skills.SkillType.Swords => CharacterStatType.AttackSpeedSwords,
            Skills.SkillType.Knives => CharacterStatType.AttackSpeedKnives,
            Skills.SkillType.Clubs => CharacterStatType.AttackSpeedClubs,
            Skills.SkillType.Polearms => CharacterStatType.AttackSpeedPolearms,
            Skills.SkillType.Spears => CharacterStatType.AttackSpeedSpears,
            Skills.SkillType.Axes => CharacterStatType.AttackSpeedAxes,
            Skills.SkillType.Bows => CharacterStatType.AttackSpeedBows,
            Skills.SkillType.Crossbows => CharacterStatType.AttackSpeedCrossbows,
            Skills.SkillType.Unarmed => CharacterStatType.AttackSpeedUnarmed,
            Skills.SkillType.ElementalMagic => CharacterStatType.AttackSpeedElementalMagic,
            Skills.SkillType.BloodMagic => CharacterStatType.AttackSpeedBloodMagic,
            _ => null
        };

        /// <summary>
        /// Evaluates an action resource cost or drain using the Asymptotic Efficiency Engine:
        /// <para>
        /// <c>Effective Cost = Base Cost / ((1 + Σ Tier 2 Efficiency%) × Π Tier 3 Multipliers)</c>
        /// </para>
        /// <para>
        /// Standard 3-tier modifiers are evaluated on the efficiency denominator (identical to speed scaling):<br/>
        /// Higher values are always better in both Tier 2 and Tier 3.<br/>
        /// +100% Efficiency cleanly halves the drain (cost / 2.0). Costs scale asymptotically towards zero.
        /// </para>
        /// </summary>
        public static float GetEffectiveActionCost(float baseCost, Character? character, CharacterStatType efficiencyStat)
        {
            if (character == null || baseCost <= 0.0001f) return baseCost;

            float netEfficiency = GetStat(character, efficiencyStat);
            float efficiencyFactor = Mathf.Max(0.01f, 1f + netEfficiency);
            float effectiveCost = baseCost / efficiencyFactor;
            return Mathf.Max(0.01f, effectiveCost);
        }

        /// <summary>
        /// Evaluates a weapon attack resource cost (stamina, eitr, health) combining universal attack efficiency,
        /// delivery efficiency, grip efficiency, weapon family efficiency, and attack style efficiency asymptotically.
        /// </summary>
        public static float GetEffectiveWeaponAttackCost(
            float baseCost,
            Character? character,
            ItemDrop.ItemData? weapon,
            bool isSecondaryAttack,
            CharacterStatType primaryResourceEffStat)
        {
            if (character == null || baseCost <= 0.0001f) return baseCost;

            // 1. Primary resource efficiency (AttackStaminaEfficiency, AttackEitrEfficiency, or AttackHealthEfficiency)
            float totalEfficiency = GetStat(character, primaryResourceEffStat);

            bool isHomeTool = primaryResourceEffStat == CharacterStatType.AttackStaminaEfficiency &&
                              weapon?.m_shared != null &&
                              weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Tool;
            if (isHomeTool)
            {
                totalEfficiency += GetStat(character, CharacterStatType.HomeToolsEfficiency);
            }

            if (weapon?.m_shared != null && !isHomeTool)
            {
                var shared = weapon.m_shared;
                var skillType = shared.m_skillType;

                // 2. Weapon Family Efficiency
                if (primaryResourceEffStat == CharacterStatType.AttackStaminaEfficiency)
                {
                    var familyStat = GetWeaponEfficiencyStat(skillType);
                    if (familyStat.HasValue) totalEfficiency += GetStat(character, familyStat.Value);
                }
                else if (primaryResourceEffStat == CharacterStatType.AttackEitrEfficiency)
                {
                    if (skillType == Skills.SkillType.ElementalMagic)
                        totalEfficiency += GetStat(character, CharacterStatType.ElementalMagicEitrEfficiency);
                    else if (skillType == Skills.SkillType.BloodMagic)
                        totalEfficiency += GetStat(character, CharacterStatType.BloodMagicEitrEfficiency);
                }
                else if (primaryResourceEffStat == CharacterStatType.AttackHealthEfficiency)
                {
                    if (skillType == Skills.SkillType.BloodMagic)
                        totalEfficiency += GetStat(character, CharacterStatType.BloodMagicHealthEfficiency);
                }

                // 3. Delivery & Grip (primarily applies to stamina costs)
                if (primaryResourceEffStat == CharacterStatType.AttackStaminaEfficiency)
                {
                    bool isRanged = shared.m_itemType == ItemDrop.ItemData.ItemType.Bow ||
                                    skillType == Skills.SkillType.Bows ||
                                    skillType == Skills.SkillType.Crossbows;

                    if (isRanged)
                    {
                        totalEfficiency += GetStat(character, CharacterStatType.RangedStaminaEfficiency);
                    }
                    else
                    {
                        // Melee attacks
                        totalEfficiency += GetStat(character, CharacterStatType.MeleeStaminaEfficiency);

                        bool isTwoHanded = shared.m_itemType is ItemDrop.ItemData.ItemType.TwoHandedWeapon
                                           or ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft;

                        if (isTwoHanded)
                        {
                            totalEfficiency += GetStat(character, CharacterStatType.TwoHandedStaminaEfficiency);
                        }
                        else
                        {
                            totalEfficiency += GetStat(character, CharacterStatType.OneHandedStaminaEfficiency);
                        }
                    }
                }
            }

            // 4. Attack Style (Secondary Attack)
            if (isSecondaryAttack && primaryResourceEffStat == CharacterStatType.AttackStaminaEfficiency)
            {
                totalEfficiency += GetStat(character, CharacterStatType.SecondaryAttackStaminaEfficiency);
            }

            // In Asymptotic Efficiency Engine: Effective Cost = Base / (1 + totalEfficiency)
            float efficiencyFactor = Mathf.Max(0.01f, 1f + totalEfficiency);
            float effectiveCost = baseCost / efficiencyFactor;
            return Mathf.Max(0.01f, effectiveCost);
        }

        /// <summary>
        /// Backward-compatible overload for evaluating weapon attack costs.
        /// </summary>
        public static float GetEffectiveWeaponAttackCost(float baseCost, Character? character, CharacterStatType globalEfficiencyStat, Skills.SkillType skillType)
        {
            if (character == null || baseCost <= 0.0001f) return baseCost;

            float globalEff = GetStat(character, globalEfficiencyStat);
            var weaponEffStat = GetWeaponEfficiencyStat(skillType);
            float weaponEff = weaponEffStat.HasValue ? GetStat(character, weaponEffStat.Value) : 0f;

            float totalEfficiency = globalEff + weaponEff;
            float efficiencyFactor = Mathf.Max(0.01f, 1f + totalEfficiency);
            float effectiveCost = baseCost / efficiencyFactor;
            return Mathf.Max(0.01f, effectiveCost);
        }

        /// <summary>
        /// Determines if an item drop can be safely duplicated according to native Valheim loot rules.
        /// Guarantees that quest items, unique gear, and non-scaled item types are never duplicated.
        /// </summary>
        public static bool CanScaleDrop(ItemDrop.ItemData? itemData)
        {
            if (itemData == null || itemData.m_shared == null) return false;
            if (itemData.m_shared.m_questItem) return false;
            if (Game.instance != null && Game.instance.m_nonScaledDropTypes != null)
            {
                if (Game.instance.m_nonScaledDropTypes.Contains(itemData.m_shared.m_itemType))
                    return false;
            }
            return true;
        }

        /// <summary>
        /// Resolves final item drop count using a multi-drop roll budget based on evaluated Yield (base 1.0x / 100%).
        /// <para>
        /// Formula: <c>Floor(Yield) guaranteed drops + (Random.value &lt; Frac(Yield) ? 1 : 0) bonus drop</c>.<br/>
        /// E.g., 1.20x grants 1 guaranteed + 20% chance for an extra drop; 2.50x grants 2 guaranteed + 50% for 3rd.
        /// </para>
        /// </summary>
        public static int ResolveDropCount(float yieldMultiplier, ItemDrop.ItemData? itemData, int baseCount = 1)
        {
            if (baseCount <= 0) return 0;
            if (!CanScaleDrop(itemData)) return baseCount;

            float totalBudget = baseCount * Mathf.Max(0f, yieldMultiplier);
            int guaranteed = Mathf.FloorToInt(totalBudget);
            float remainder = totalBudget - guaranteed;

            int extra = (remainder > 0.0001f && UnityEngine.Random.value < remainder) ? 1 : 0;
            return guaranteed + extra;
        }

        /// <summary>
        /// Evaluates multi-tier critical strike damage multiplier supporting uncapped Over-Crit (>100% crit chance).
        /// <para>
        /// Every whole 1.0 (100%) guarantees a crit tier, and remaining fractional chance rolls for an additional tier:
        /// <c>Multiplier = 1.0 + (Tiers) × (critMultiplier - 1.0)</c>.
        /// </para>
        /// </summary>
        public static float CalculateCritMultiplier(float critChance, float critMultiplier)
        {
            if (critChance <= 0.0001f || critMultiplier <= 1.0001f) return 1.0f;

            int guaranteedTiers = Mathf.FloorToInt(critChance);
            float remainder = critChance - guaranteedTiers;
            int bonusTier = (remainder > 0.0001f && UnityEngine.Random.value < remainder) ? 1 : 0;
            int totalTiers = guaranteedTiers + bonusTier;

            if (totalTiers <= 0) return 1.0f;

            float perTierBonus = critMultiplier - 1.0f;
            return 1.0f + (totalTiers * perTierBonus);
        }

        // =========================================================================
        // BASELINE EVALUATION
        // =========================================================================

        /// <summary>
        /// Computes native baseline values for each stat type.
        /// </summary>
        public static float GetBaseValue(Character? character, CharacterStatType statType)
        {
            if (character is null) return 0f;

            var player = character as Player;

            switch (statType)
            {
                // --- Pools & Maximums (True biological baselines; foods added via Tier 1 Flat tokens) ---
                case CharacterStatType.MaxHealth:
                    return player != null ? player.m_baseHP : character.GetMaxHealth();

                case CharacterStatType.MaxStamina:
                    return player != null ? player.m_baseStamina : character.GetMaxStamina();

                case CharacterStatType.MaxEitr:
                    return player != null ? 0f : character.GetMaxEitr();

                case CharacterStatType.MaxCarryWeight:
                    return player != null ? player.m_maxCarryWeight : 0f;

                // --- Food Effectiveness (defaults to 1.0x / 100%) ---
                case CharacterStatType.FoodHealthEffectiveness:
                case CharacterStatType.FoodHealthRegenEffectiveness:
                case CharacterStatType.FoodStaminaEffectiveness:
                case CharacterStatType.FoodEitrEffectiveness:
                    return 1.0f;

                // --- Regeneration Rates (per second) ---
                case CharacterStatType.HealthRegen:
                    if (player != null) return 0f; // Biological base; food regen added via Tier 1 Flat tokens
                    float creatureMax = character.GetMaxHealth();
                    float regenTime = Mathf.Max(1f, character.m_regenAllHPTime);
                    return creatureMax / regenTime;

                case CharacterStatType.StaminaRegen:
                    if (player != null)
                    {
                        float maxStam = Mathf.Max(1f, player.GetMaxStamina());
                        float curStam = player.GetStamina();
                        float dynamic = player.m_staminaRegen + (1f - (curStam / maxStam)) * player.m_staminaRegen * player.m_staminaRegenTimeMultiplier;
                        return dynamic;
                    }
                    return 0f;

                case CharacterStatType.EitrRegen:
                    if (player != null)
                    {
                        float maxEitr = Mathf.Max(1f, player.GetMaxEitr());
                        float curEitr = player.GetEitr();
                        float dynamic = player.m_eiterRegen + (1f - (curEitr / maxEitr)) * player.m_eiterRegen;
                        return dynamic;
                    }
                    return 0f;

                case CharacterStatType.HealthRegenDelay:
                    return player != null ? Configuration.ModConfig.HealthRegenCombatDelay.Value : 0f;

                case CharacterStatType.StaminaRegenDelay:
                    return player != null ? player.m_staminaRegenDelay : 0f;

                // --- Locomotion & Physics (Base height 1.7m; speeds in m/s or multiplier 1.0x) ---
                case CharacterStatType.JumpHeight:
                    return 1.7f;

                case CharacterStatType.JogSpeed:
                case CharacterStatType.RunSpeed:
                    return 1.0f;

                case CharacterStatType.WalkSpeed:
                    return character.m_walkSpeed;

                case CharacterStatType.CrouchSpeed:
                    return character.m_crouchSpeed;

                case CharacterStatType.SwimSpeed:
                    return character.m_swimSpeed;

                // --- Mount Mechanics (Decoupled) ---
                case CharacterStatType.RidingSpeed:
                    return 1.0f;

                case CharacterStatType.RidingControl:
                    return player != null && SkillSystem.VanillaSkillIntegration.EnableLocomotionSpeedScaling
                        ? player.GetSkillFactor(Skills.SkillType.Ride)
                        : 0f;

                // --- Defense & Guard ---
                case CharacterStatType.BodyArmor:
                    return 0f; // Gear pieces add Tier 1 Flat armor

                case CharacterStatType.BlockPowerMultiplier:
                case CharacterStatType.ParryMultiplier:
                case CharacterStatType.StaggerResistance:
                    return 1.0f;

                case CharacterStatType.ParryWindow:
                    return 0.25f;

                // --- Continuous Damage Resistances (default 1.0x / 100% Damage Taken) ---
                case CharacterStatType.ResistancePhysical:
                case CharacterStatType.ResistanceElemental:
                case CharacterStatType.ResistanceSlashing:
                case CharacterStatType.ResistanceBlunt:
                case CharacterStatType.ResistancePiercing:
                case CharacterStatType.ResistanceFire:
                case CharacterStatType.ResistanceFrost:
                case CharacterStatType.ResistanceLightning:
                case CharacterStatType.ResistancePoison:
                case CharacterStatType.ResistanceSpirit:
                case CharacterStatType.ResistanceChop:
                case CharacterStatType.ResistancePickaxe:
                case CharacterStatType.ResistanceFall:
                case CharacterStatType.ResistanceBleed:
                    return 1.0f;

                // --- Global Offensive Damage Types (default 1.0x / 100%) ---
                case CharacterStatType.AttackSpeed:
                case CharacterStatType.DamageSlashing:
                case CharacterStatType.DamageBlunt:
                case CharacterStatType.DamagePiercing:
                case CharacterStatType.DamageChop:
                case CharacterStatType.DamagePickaxe:
                case CharacterStatType.DamageFire:
                case CharacterStatType.DamageFrost:
                case CharacterStatType.DamageLightning:
                case CharacterStatType.DamagePoison:
                case CharacterStatType.DamageSpirit:
                case CharacterStatType.DamagePhysical:
                case CharacterStatType.DamageElemental:
                case CharacterStatType.DamageMelee:
                case CharacterStatType.DamageRanged:
                case CharacterStatType.DamageMagic:
                case CharacterStatType.DamageOneHanded:
                case CharacterStatType.DamageTwoHanded:
                case CharacterStatType.DamageSneak:
                case CharacterStatType.CleaveEfficiency:
                case CharacterStatType.DamageComboFinisher:
                case CharacterStatType.DamageSecondaryAttack:
                case CharacterStatType.StaggerDamage:
                case CharacterStatType.DamageAgainstBosses:
                case CharacterStatType.DamageAgainstStaggered:
                case CharacterStatType.DamageAgainstAirborne:
                case CharacterStatType.DamageExecute:
                case CharacterStatType.DamageFirstStrike:
                case CharacterStatType.DamageBerserk:
                case CharacterStatType.DamageFlawless:
                case CharacterStatType.DamageSwords:
                case CharacterStatType.DamageKnives:
                case CharacterStatType.DamageClubs:
                case CharacterStatType.DamagePolearms:
                case CharacterStatType.DamageSpears:
                case CharacterStatType.DamageAxes:
                case CharacterStatType.DamageBows:
                case CharacterStatType.DamageCrossbows:
                case CharacterStatType.DamageUnarmed:
                case CharacterStatType.DamageElementalMagic:
                case CharacterStatType.DamageBloodMagic:
                case CharacterStatType.AttackSpeedSwords:
                case CharacterStatType.AttackSpeedKnives:
                case CharacterStatType.AttackSpeedClubs:
                case CharacterStatType.AttackSpeedPolearms:
                case CharacterStatType.AttackSpeedSpears:
                case CharacterStatType.AttackSpeedAxes:
                case CharacterStatType.AttackSpeedBows:
                case CharacterStatType.AttackSpeedCrossbows:
                case CharacterStatType.AttackSpeedUnarmed:
                case CharacterStatType.AttackSpeedElementalMagic:
                case CharacterStatType.AttackSpeedBloodMagic:
                    return 1.0f;

                case CharacterStatType.CriticalStrikeChance:
                    return player != null ? Configuration.ModConfig.DefaultPlayerCritChance.Value : 0f;

                case CharacterStatType.CriticalStrikeMultiplier:
                    return player != null ? Configuration.ModConfig.DefaultCritMultiplier.Value : 1.5f;

                // --- Action Resource Costs & Drains (Efficiency Ratings, default 0.0) ---
                case CharacterStatType.SprintEfficiency:
                case CharacterStatType.JumpEfficiency:
                case CharacterStatType.DodgeEfficiency:
                case CharacterStatType.SneakEfficiency:
                case CharacterStatType.SwimEfficiency:
                case CharacterStatType.AttackStaminaEfficiency:
                case CharacterStatType.AttackEitrEfficiency:
                case CharacterStatType.AttackHealthEfficiency:
                case CharacterStatType.BowDrawStaminaEfficiency:
                case CharacterStatType.BowDrawEitrEfficiency:
                case CharacterStatType.FishingStaminaEfficiency:
                case CharacterStatType.BlockStaminaEfficiency:
                case CharacterStatType.RidingStaminaEfficiency:
                case CharacterStatType.MeleeStaminaEfficiency:
                case CharacterStatType.RangedStaminaEfficiency:
                case CharacterStatType.OneHandedStaminaEfficiency:
                case CharacterStatType.TwoHandedStaminaEfficiency:
                case CharacterStatType.SecondaryAttackStaminaEfficiency:
                case CharacterStatType.SwordsEfficiency:
                case CharacterStatType.KnivesEfficiency:
                case CharacterStatType.ClubsEfficiency:
                case CharacterStatType.PolearmsEfficiency:
                case CharacterStatType.SpearsEfficiency:
                case CharacterStatType.AxesEfficiency:
                case CharacterStatType.BowsEfficiency:
                case CharacterStatType.CrossbowReloadStaminaEfficiency:
                case CharacterStatType.UnarmedEfficiency:
                case CharacterStatType.ElementalMagicEitrEfficiency:
                case CharacterStatType.BloodMagicHealthEfficiency:
                case CharacterStatType.BloodMagicEitrEfficiency:
                case CharacterStatType.ToolStaminaEfficiency:
                case CharacterStatType.DurabilityEfficiency:
                case CharacterStatType.CartWeightEfficiency:
                case CharacterStatType.ArmorEfficiency:
                case CharacterStatType.BluntArmorEfficiency:
                case CharacterStatType.SlashArmorEfficiency:
                case CharacterStatType.PierceArmorEfficiency:
                case CharacterStatType.SlashBleedPercent:
                    return 0.0f;

                // --- Action Speeds, Precision & Stealth (default 1.0x) ---
                case CharacterStatType.BowDrawSpeed:
                case CharacterStatType.CrossbowReloadSpeed:
                case CharacterStatType.ProjectilePrecision:
                case CharacterStatType.Concealment:
                    return 1.0f;

                // --- Gathering, Crafting & Loot Yields (default 1.0x / 100% Base) ---
                case CharacterStatType.CraftingYield:
                case CharacterStatType.CookingYield:
                case CharacterStatType.ForagingYield:
                case CharacterStatType.MiningYield:
                case CharacterStatType.WoodcuttingYield:
                case CharacterStatType.MonsterDropYield:
                case CharacterStatType.HarvestRadius:
                case CharacterStatType.FoodDurationMultiplier:
                case CharacterStatType.KnockbackDealt:
                case CharacterStatType.MeleeReach:
                case CharacterStatType.ShipSailSpeed:
                case CharacterStatType.ShipRudderSpeed:
                case CharacterStatType.DamageAtNight:
                case CharacterStatType.DamageWhileAirborne:
                case CharacterStatType.DamageAgainstAfflicted:
                case CharacterStatType.ParryPoiseMultiplier:
                    return 1.0f;

                default:
                    return 0f;
            }
        }
    }
}
