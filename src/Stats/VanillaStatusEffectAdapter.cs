using System.Collections.Generic;
using UnityEngine;
using VikingLib.Combat;

namespace VikingLib.Stats
{
    /// <summary>
    /// Synchronizes native Valheim SEMan active status effects into commutative 3-tier declarative tokens.
    /// Eliminates vanilla's order-of-application pathology (e.g. Rested + Wet giving 1.70 vs 1.85).
    /// </summary>
    public static class VanillaStatusEffectAdapter
    {
        public static readonly object StatusEffectOwnerKey = new();

        /// <summary>
        /// Scans active status effects on the character's SEMan, synchronizing active 3-tier tokens.
        /// </summary>
        public static void SyncStatusEffects(Character character)
        {
            if (character == null) return;

            CharacterStatManager.RemoveModifiersByOwner(character, StatusEffectOwnerKey);

            var seman = character.GetSEMan();
            if (seman == null) return;

            var activeEffects = seman.GetStatusEffects();
            if (activeEffects == null) return;

            for (int i = 0; i < activeEffects.Count; i++)
            {
                var effect = activeEffects[i];
                if (effect is not SE_Stats se) continue;

                string seName = string.IsNullOrEmpty(se.m_name) ? se.GetType().Name : se.m_name;

                // 1. Regeneration Multipliers (normalized into Tier 2 Additive %: mult - 1.0)
                if (Mathf.Abs(se.m_staminaRegenMultiplier - 1.0f) > 0.0001f)
                {
                    float percent = se.m_staminaRegenMultiplier - 1.0f;
                    CharacterStatManager.AddModifier(character,
                        StatModifier.Increased(CharacterStatType.StaminaRegen, percent, seName, null, StatusEffectOwnerKey));
                }

                if (Mathf.Abs(se.m_eitrRegenMultiplier - 1.0f) > 0.0001f)
                {
                    float percent = se.m_eitrRegenMultiplier - 1.0f;
                    CharacterStatManager.AddModifier(character,
                        StatModifier.Increased(CharacterStatType.EitrRegen, percent, seName, null, StatusEffectOwnerKey));
                }

                if (Mathf.Abs(se.m_healthRegenMultiplier - 1.0f) > 0.0001f)
                {
                    float percent = se.m_healthRegenMultiplier - 1.0f;
                    CharacterStatManager.AddModifier(character,
                        StatModifier.Increased(CharacterStatType.HealthRegen, percent, seName, null, StatusEffectOwnerKey));
                }

                // 2. Locomotion Speed Modifiers
                if (Mathf.Abs(se.m_speedModifier) > 0.0001f)
                {
                    CharacterStatManager.AddModifier(character,
                        StatModifier.Increased(CharacterStatType.JogSpeed, se.m_speedModifier, seName, null, StatusEffectOwnerKey));
                    CharacterStatManager.AddModifier(character,
                        StatModifier.Increased(CharacterStatType.RunSpeed, se.m_speedModifier, seName, null, StatusEffectOwnerKey));
                    CharacterStatManager.AddModifier(character,
                        StatModifier.Increased(CharacterStatType.WalkSpeed, se.m_speedModifier, seName, null, StatusEffectOwnerKey));
                    CharacterStatManager.AddModifier(character,
                        StatModifier.Increased(CharacterStatType.CrouchSpeed, se.m_speedModifier, seName, null, StatusEffectOwnerKey));
                }

                if (Mathf.Abs(se.m_swimSpeedModifier) > 0.0001f)
                {
                    CharacterStatManager.AddModifier(character,
                        StatModifier.Increased(CharacterStatType.SwimSpeed, se.m_swimSpeedModifier, seName, null, StatusEffectOwnerKey));
                }

                // 3. Carry Weight (Tier 1 Flat)
                if (Mathf.Abs(se.m_addMaxCarryWeight) > 0.0001f)
                {
                    CharacterStatManager.AddModifier(character,
                        StatModifier.Flat(CharacterStats.MaxCarryWeight, se.m_addMaxCarryWeight, seName, null, StatusEffectOwnerKey));
                }

                // 4. Activity Stamina Efficiency Modifiers
                // In vanilla, negative modifiers represent discounts (-0.20 = 20% discount).
                // In VikingLib's Asymptotic Efficiency engine, discounts map to positive efficiency additions.
                if (Mathf.Abs(se.m_runStaminaDrainModifier) > 0.0001f)
                {
                    float eff = -se.m_runStaminaDrainModifier;
                    CharacterStatManager.AddModifier(character,
                        StatModifier.Increased(CharacterStatType.SprintEfficiency, eff, seName, null, StatusEffectOwnerKey));
                }

                if (Mathf.Abs(se.m_jumpStaminaUseModifier) > 0.0001f)
                {
                    float eff = -se.m_jumpStaminaUseModifier;
                    CharacterStatManager.AddModifier(character,
                        StatModifier.Increased(CharacterStatType.JumpEfficiency, eff, seName, null, StatusEffectOwnerKey));
                }

                if (Mathf.Abs(se.m_dodgeStaminaUseModifier) > 0.0001f)
                {
                    float eff = -se.m_dodgeStaminaUseModifier;
                    CharacterStatManager.AddModifier(character,
                        StatModifier.Increased(CharacterStatType.DodgeEfficiency, eff, seName, null, StatusEffectOwnerKey));
                }

                if (Mathf.Abs(se.m_attackStaminaUseModifier) > 0.0001f)
                {
                    float eff = -se.m_attackStaminaUseModifier;
                    CharacterStatManager.AddModifier(character,
                        StatModifier.Increased(CharacterStatType.AttackStaminaEfficiency, eff, seName, null, StatusEffectOwnerKey));
                }

                if (Mathf.Abs(se.m_blockStaminaUseModifier) > 0.0001f)
                {
                    float eff = -se.m_blockStaminaUseModifier;
                    CharacterStatManager.AddModifier(character,
                        StatModifier.Increased(CharacterStatType.BlockStaminaEfficiency, eff, seName, null, StatusEffectOwnerKey));
                }

                // Flat block stamina modification: map -discount to efficiency
                if (Mathf.Abs(se.m_blockStaminaUseFlatValue) > 0.0001f)
                {
                    float eff = -se.m_blockStaminaUseFlatValue / 10f;
                    CharacterStatManager.AddModifier(character,
                        StatModifier.Increased(CharacterStats.BlockStaminaEfficiency, eff, seName, null, StatusEffectOwnerKey));
                }

                if (Mathf.Abs(se.m_sneakStaminaUseModifier) > 0.0001f)
                {
                    float eff = -se.m_sneakStaminaUseModifier;
                    CharacterStatManager.AddModifier(character,
                        StatModifier.Increased(CharacterStatType.SneakEfficiency, eff, seName, null, StatusEffectOwnerKey));
                }

                if (Mathf.Abs(se.m_swimStaminaUseModifier) > 0.0001f)
                {
                    float eff = -se.m_swimStaminaUseModifier;
                    CharacterStatManager.AddModifier(character,
                        StatModifier.Increased(CharacterStatType.SwimEfficiency, eff, seName, null, StatusEffectOwnerKey));
                }
            }
        }
    }
}
