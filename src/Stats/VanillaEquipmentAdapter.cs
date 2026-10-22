using System.Collections.Generic;
using UnityEngine;
using VikingLib.Combat;
using VikingLib.SkillSystem;

namespace VikingLib.Stats
{
    /// <summary>
    /// Synchronizes native Valheim equipment modifiers and skill factors into declarative dual-space 3-tier tokens.
    /// </summary>
    public static class VanillaEquipmentAdapter
    {
        public static readonly object EquipmentOwnerKey = new();

        /// <summary>
        /// Scans equipped gear and player skills, synchronizing active 3-tier tokens on the character.
        /// </summary>
        public static void SyncEquipment(Humanoid humanoid)
        {
            if (humanoid == null) return;

            CharacterStatManager.RemoveModifiersByOwner(humanoid, EquipmentOwnerKey);

            var items = CharacterStatsHelper.GetEquippedItems(humanoid);

            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item?.m_shared == null) continue;

                var shared = item.m_shared;
                string itemName = string.IsNullOrEmpty(shared.m_name) ? "Equipment" : shared.m_name;
                if (itemName.StartsWith("$") && Localization.instance != null)
                {
                    itemName = Localization.instance.Localize(itemName);
                }

                // 0. Body Armor flat contribution:
                // Only wearable armor pieces contribute to passive body armor (Chest, Legs, Helmet, Shoulder, Utility, Trinket).
                if (IsWearableArmor(item))
                {
                    float armor = (Game.instance != null)
                        ? item.GetArmor()
                        : (shared.m_armor + Mathf.Max(0, item.m_quality - 1) * shared.m_armorPerLevel);

                    if (armor > 0.001f)
                    {
                        CharacterStatManager.AddModifier(humanoid,
                            StatModifier.Flat(CharacterStats.BodyArmor, armor, itemName, null, EquipmentOwnerKey));
                    }
                }

                // 1. Movement modifier (negative for heavy armor, e.g. -0.05)
                if (Mathf.Abs(shared.m_movementModifier) > 0.0001f)
                {
                    // Jog speed gets 1 + moveMod
                    CharacterStatManager.AddModifier(humanoid,
                        StatModifier.Increased(CharacterStatType.JogSpeed, shared.m_movementModifier, itemName, null, EquipmentOwnerKey));

                    // Run speed penalizes 1.5x more: 1 + 1.5 * moveMod
                    CharacterStatManager.AddModifier(humanoid,
                        StatModifier.Increased(CharacterStatType.RunSpeed, shared.m_movementModifier * 1.5f, itemName, null, EquipmentOwnerKey));
                }

                // 2. Specific equipment activity stamina channels
                // In vanilla, negative modifiers represent discounts (-0.20 = 20% discount).
                // In VikingLib's Asymptotic Efficiency engine, discounts map to positive efficiency additions.
                if (Mathf.Abs(shared.m_runStaminaModifier) > 0.0001f)
                {
                    float effBonus = -shared.m_runStaminaModifier;
                    CharacterStatManager.AddModifier(humanoid,
                        StatModifier.Increased(CharacterStatType.SprintEfficiency, effBonus, itemName, null, EquipmentOwnerKey));
                }

                if (Mathf.Abs(shared.m_jumpStaminaModifier) > 0.0001f)
                {
                    float effBonus = -shared.m_jumpStaminaModifier;
                    CharacterStatManager.AddModifier(humanoid,
                        StatModifier.Increased(CharacterStatType.JumpEfficiency, effBonus, itemName, null, EquipmentOwnerKey));
                }

                if (Mathf.Abs(shared.m_dodgeStaminaModifier) > 0.0001f)
                {
                    float effBonus = -shared.m_dodgeStaminaModifier;
                    CharacterStatManager.AddModifier(humanoid,
                        StatModifier.Increased(CharacterStatType.DodgeEfficiency, effBonus, itemName, null, EquipmentOwnerKey));
                }

                if (Mathf.Abs(shared.m_sneakStaminaModifier) > 0.0001f)
                {
                    float effBonus = -shared.m_sneakStaminaModifier;
                    CharacterStatManager.AddModifier(humanoid,
                        StatModifier.Increased(CharacterStatType.SneakEfficiency, effBonus, itemName, null, EquipmentOwnerKey));
                }

                if (Mathf.Abs(shared.m_swimStaminaModifier) > 0.0001f)
                {
                    float effBonus = -shared.m_swimStaminaModifier;
                    CharacterStatManager.AddModifier(humanoid,
                        StatModifier.Increased(CharacterStatType.SwimEfficiency, effBonus, itemName, null, EquipmentOwnerKey));
                }

                if (Mathf.Abs(shared.m_attackStaminaModifier) > 0.0001f)
                {
                    float effBonus = -shared.m_attackStaminaModifier;
                    CharacterStatManager.AddModifier(humanoid,
                        StatModifier.Increased(CharacterStatType.AttackStaminaEfficiency, effBonus, itemName, null, EquipmentOwnerKey));
                }

                if (Mathf.Abs(shared.m_blockStaminaModifier) > 0.0001f)
                {
                    float effBonus = -shared.m_blockStaminaModifier;
                    CharacterStatManager.AddModifier(humanoid,
                        StatModifier.Increased(CharacterStatType.BlockStaminaEfficiency, effBonus, itemName, null, EquipmentOwnerKey));
                }

                if (Mathf.Abs(shared.m_eitrRegenModifier) > 0.0001f)
                {
                    CharacterStatManager.AddModifier(humanoid,
                        StatModifier.Increased(CharacterStatType.EitrRegen, shared.m_eitrRegenModifier, itemName, null, EquipmentOwnerKey));
                }
            }

            // 3. Player skill progression (Dual-Space Transformed Tokens)
            if (humanoid is Player player && player.GetSkills() != null)
            {
                // Run skill: reduces drain up to 50% (+100% efficiency), increases run speed up to +25%
                float runSkillFactor = player.GetSkillFactor(Skills.SkillType.Run);
                if (runSkillFactor > 0.0001f)
                {
                    if (VanillaSkillIntegration.EnableLocomotionStaminaScaling)
                    {
                        CharacterStatManager.AddModifier(humanoid,
                            StatModifier.Increased(CharacterStatType.SprintEfficiency, runSkillFactor, $"Run Skill ({Mathf.RoundToInt(runSkillFactor * 100f)})", null, EquipmentOwnerKey));
                    }

                    if (VanillaSkillIntegration.EnableLocomotionSpeedScaling)
                    {
                        CharacterStatManager.AddModifier(humanoid,
                            StatModifier.Increased(CharacterStatType.RunSpeed, 0.25f * runSkillFactor, $"Run Skill ({Mathf.RoundToInt(runSkillFactor * 100f)})", null, EquipmentOwnerKey));
                    }
                }

                // Dodge skill: reduces dodge stamina cost up to 50% (+100% efficiency)
                if (VanillaSkillIntegration.EnableLocomotionStaminaScaling)
                {
                    float dodgeSkillFactor = player.GetSkillFactor(Skills.SkillType.Dodge);
                    if (dodgeSkillFactor > 0.0001f)
                    {
                        CharacterStatManager.AddModifier(humanoid,
                            StatModifier.Increased(CharacterStatType.DodgeEfficiency, dodgeSkillFactor, $"Dodge Skill ({Mathf.RoundToInt(dodgeSkillFactor * 100f)})", null, EquipmentOwnerKey));
                    }

                    // Sneak skill: reduces sneak stamina drain up to 75% (+300% efficiency)
                    float sneakSkillFactor = player.GetSkillFactor(Skills.SkillType.Sneak);
                    if (sneakSkillFactor > 0.0001f)
                    {
                        float sneakPow = Mathf.Pow(sneakSkillFactor, 0.5f);
                        CharacterStatManager.AddModifier(humanoid,
                            StatModifier.Increased(CharacterStatType.SneakEfficiency, 3.0f * sneakPow, $"Sneak Skill ({Mathf.RoundToInt(sneakSkillFactor * 100f)})", null, EquipmentOwnerKey));
                    }

                    // Swim skill: reduces swim stamina drain up to 60% (+150% efficiency)
                    float swimSkillFactor = player.GetSkillFactor(Skills.SkillType.Swim);
                    if (swimSkillFactor > 0.0001f)
                    {
                        CharacterStatManager.AddModifier(humanoid,
                            StatModifier.Increased(CharacterStatType.SwimEfficiency, 1.5f * swimSkillFactor, $"Swim Skill ({Mathf.RoundToInt(swimSkillFactor * 100f)})", null, EquipmentOwnerKey));
                    }
                }

                // Stealth concealment: Sneak skill reduces detection visibility by up to 50% (+100% Concealment)
                if (VanillaSkillIntegration.EnableStealthScaling)
                {
                    float sneakSkillFactor = player.GetSkillFactor(Skills.SkillType.Sneak);
                    if (sneakSkillFactor > 0.0001f)
                    {
                        CharacterStatManager.AddModifier(humanoid,
                            StatModifier.Increased(CharacterStatType.Concealment, sneakSkillFactor, $"Sneak Skill ({Mathf.RoundToInt(sneakSkillFactor * 100f)})", null, EquipmentOwnerKey));
                    }
                }

                // Blocking skill: increases block armor up to +50%
                if (VanillaSkillIntegration.EnableBlockPowerScaling)
                {
                    float blockSkillFactor = player.GetSkillFactor(Skills.SkillType.Blocking);
                    if (blockSkillFactor > 0.0001f)
                    {
                        CharacterStatManager.AddModifier(humanoid,
                            StatModifier.Increased(CharacterStatType.BlockPowerMultiplier, 0.5f * blockSkillFactor, $"Blocking Skill ({Mathf.RoundToInt(blockSkillFactor * 100f)})", null, EquipmentOwnerKey));
                    }
                }

                // Jump skill: increases jump apex height up to +40% (+0.68m) and reduces jump stamina cost up to 33% (+50% efficiency)
                float jumpSkillFactor = player.GetSkillFactor(Skills.SkillType.Jump);
                if (jumpSkillFactor > 0.0001f)
                {
                    if (VanillaSkillIntegration.EnableLocomotionSpeedScaling)
                    {
                        CharacterStatManager.AddModifier(humanoid,
                            StatModifier.Increased(CharacterStatType.JumpHeight, 0.4f * jumpSkillFactor, $"Jump Skill ({Mathf.RoundToInt(jumpSkillFactor * 100f)})", null, EquipmentOwnerKey));
                    }

                    if (VanillaSkillIntegration.EnableLocomotionStaminaScaling)
                    {
                        CharacterStatManager.AddModifier(humanoid,
                            StatModifier.Increased(CharacterStatType.JumpEfficiency, 0.5f * jumpSkillFactor, $"Jump Skill ({Mathf.RoundToInt(jumpSkillFactor * 100f)})", null, EquipmentOwnerKey));
                    }
                }

                // Riding skill: uncoupled speed, stamina efficiency, and turning control
                if (VanillaSkillIntegration.EnableLocomotionSpeedScaling)
                {
                    float rideSkillFactor = player.GetSkillFactor(Skills.SkillType.Ride);
                    if (rideSkillFactor > 0.0001f)
                    {
                        CharacterStatManager.AddModifier(humanoid,
                            StatModifier.Increased(CharacterStatType.RidingSpeed, 0.25f * rideSkillFactor, $"Ride Skill ({Mathf.RoundToInt(rideSkillFactor * 100f)})", null, EquipmentOwnerKey));

                        CharacterStatManager.AddModifier(humanoid,
                            StatModifier.Increased(CharacterStatType.RidingStaminaEfficiency, 1.0f * rideSkillFactor, $"Ride Skill ({Mathf.RoundToInt(rideSkillFactor * 100f)})", null, EquipmentOwnerKey));
                    }
                }

                // Ranged skills: Bows & Crossbows draw speed and precision
                if (VanillaSkillIntegration.EnableRangedDrawScaling)
                {
                    float bowSkillFactor = player.GetSkillFactor(Skills.SkillType.Bows);
                    if (bowSkillFactor > 0.0001f)
                    {
                        // In vanilla, duration lerps from 1.0 down to 0.2: duration factor = 1 - 0.8 * skill.
                        // Speed factor = 1 / (1 - 0.8 * skill), so Increased% = (1 / (1 - 0.8 * skill)) - 1.
                        float bowSpeedBonus = (1f / Mathf.Max(0.01f, 1f - 0.8f * bowSkillFactor)) - 1f;
                        CharacterStatManager.AddModifier(humanoid,
                            StatModifier.Increased(CharacterStatType.BowDrawSpeed, bowSpeedBonus, $"Bows Skill ({Mathf.RoundToInt(bowSkillFactor * 100f)})", null, EquipmentOwnerKey));

                        // Bows skill tightens spread by up to 50% (+100% precision)
                        CharacterStatManager.AddModifier(humanoid,
                            StatModifier.Increased(CharacterStatType.ProjectilePrecision, bowSkillFactor, $"Bows Skill ({Mathf.RoundToInt(bowSkillFactor * 100f)})", null, EquipmentOwnerKey));
                    }

                    float xbowSkillFactor = player.GetSkillFactor(Skills.SkillType.Crossbows);
                    if (xbowSkillFactor > 0.0001f)
                    {
                        // In vanilla, reload duration lerps from 1.0 down to 0.5: duration factor = 1 - 0.5 * skill.
                        // Speed factor = 1 / (1 - 0.5 * skill), so Increased% = (1 / (1 - 0.5 * skill)) - 1.
                        float xbowSpeedBonus = (1f / Mathf.Max(0.01f, 1f - 0.5f * xbowSkillFactor)) - 1f;
                        CharacterStatManager.AddModifier(humanoid,
                            StatModifier.Increased(CharacterStatType.CrossbowReloadSpeed, xbowSpeedBonus, $"Crossbows Skill ({Mathf.RoundToInt(xbowSkillFactor * 100f)})", null, EquipmentOwnerKey));
                    }
                }

                // Weapon skill attack resource efficiency scaling (stamina/eitr/health discounts up to 33% = +49.3% efficiency)
                if (VanillaSkillIntegration.EnableAttackCostScaling)
                {
                    SyncWeaponSkillEfficiency(player, humanoid, Skills.SkillType.Swords, CharacterStats.SwordsEfficiency);
                    SyncWeaponSkillEfficiency(player, humanoid, Skills.SkillType.Knives, CharacterStats.KnivesEfficiency);
                    SyncWeaponSkillEfficiency(player, humanoid, Skills.SkillType.Clubs, CharacterStats.ClubsEfficiency);
                    SyncWeaponSkillEfficiency(player, humanoid, Skills.SkillType.Polearms, CharacterStats.PolearmsEfficiency);
                    SyncWeaponSkillEfficiency(player, humanoid, Skills.SkillType.Spears, CharacterStats.SpearsEfficiency);
                    SyncWeaponSkillEfficiency(player, humanoid, Skills.SkillType.Axes, CharacterStats.AxesEfficiency);
                    SyncWeaponSkillEfficiency(player, humanoid, Skills.SkillType.Bows, CharacterStats.BowsEfficiency);
                    SyncWeaponSkillEfficiency(player, humanoid, Skills.SkillType.Crossbows, CharacterStats.CrossbowsEfficiency);
                    SyncWeaponSkillEfficiency(player, humanoid, Skills.SkillType.Unarmed, CharacterStats.UnarmedEfficiency);
                    SyncWeaponSkillEfficiency(player, humanoid, Skills.SkillType.ElementalMagic, CharacterStats.ElementalMagicEfficiency);
                    SyncWeaponSkillEfficiency(player, humanoid, Skills.SkillType.BloodMagic, CharacterStats.BloodMagicEfficiency);
                }

                // Fishing reel stamina scaling
                if (VanillaSkillIntegration.EnableLocomotionStaminaScaling)
                {
                    float fishSkill = player.GetSkillFactor(Skills.SkillType.Fishing);
                    if (fishSkill > 0.0001f)
                    {
                        // In vanilla, reel stamina drain scales down by up to 50%
                        float fishEff = (1f / Mathf.Max(0.01f, 1f - 0.5f * fishSkill)) - 1f;
                        CharacterStatManager.AddModifier(humanoid,
                            StatModifier.Increased(CharacterStats.FishingStaminaEfficiency, fishEff, $"Fishing Skill ({Mathf.RoundToInt(fishSkill * 100f)})", null, EquipmentOwnerKey));
                    }
                }

                // Gathering & Crafting total yield multipliers (base 1.0x / 100%)
                if (VanillaSkillIntegration.EnableDropYieldScaling)
                {
                    float craftSkill = player.GetSkillFactor(Skills.SkillType.Crafting);
                    if (craftSkill > 0.0001f)
                    {
                        CharacterStatManager.AddModifier(humanoid,
                            StatModifier.Increased(CharacterStatType.CraftingYield, 0.25f * craftSkill, $"Crafting Skill ({Mathf.RoundToInt(craftSkill * 100f)})", null, EquipmentOwnerKey));
                    }

                    float cookSkill = player.GetSkillFactor(Skills.SkillType.Cooking);
                    if (cookSkill > 0.0001f)
                    {
                        CharacterStatManager.AddModifier(humanoid,
                            StatModifier.Increased(CharacterStatType.CookingYield, 0.25f * cookSkill, $"Cooking Skill ({Mathf.RoundToInt(cookSkill * 100f)})", null, EquipmentOwnerKey));
                    }

                    float farmSkill = player.GetSkillFactor(Skills.SkillType.Farming);
                    if (farmSkill > 0.0001f)
                    {
                        CharacterStatManager.AddModifier(humanoid,
                            StatModifier.Increased(CharacterStatType.ForagingYield, 0.25f * farmSkill, $"Farming Skill ({Mathf.RoundToInt(farmSkill * 100f)})", null, EquipmentOwnerKey));
                    }

                    float pickSkill = player.GetSkillFactor(Skills.SkillType.Pickaxes);
                    if (pickSkill > 0.0001f)
                    {
                        CharacterStatManager.AddModifier(humanoid,
                            StatModifier.Increased(CharacterStatType.MiningYield, 0.25f * pickSkill, $"Pickaxes Skill ({Mathf.RoundToInt(pickSkill * 100f)})", null, EquipmentOwnerKey));
                    }

                    float woodSkill = player.GetSkillFactor(Skills.SkillType.WoodCutting);
                    if (woodSkill > 0.0001f)
                    {
                        CharacterStatManager.AddModifier(humanoid,
                            StatModifier.Increased(CharacterStatType.WoodcuttingYield, 0.25f * woodSkill, $"Woodcutting Skill ({Mathf.RoundToInt(woodSkill * 100f)})", null, EquipmentOwnerKey));
                    }
                }
            }
        }

        private static void SyncWeaponSkillEfficiency(Player player, Humanoid humanoid, Skills.SkillType skillType, EfficiencyStat stat)
        {
            float skillFactor = player.GetSkillFactor(skillType);
            if (skillFactor > 0.0001f)
            {
                // In vanilla, weapon skills reduce stamina / eitr / health cost by up to 33% at level 100.
                // In VikingLib's Asymptotic Efficiency engine: eff = 1 / (1 - 0.33 * skill) - 1.
                float eff = (1f / Mathf.Max(0.01f, 1f - 0.33f * skillFactor)) - 1f;
                string name = skillType.ToString();
                CharacterStatManager.AddModifier(humanoid,
                    StatModifier.Increased(stat, eff, $"{name} Skill ({Mathf.RoundToInt(skillFactor * 100f)})", null, EquipmentOwnerKey));
            }
        }

        /// <summary>
        /// Determines if an item belongs to a wearable armor or accessory slot that can contribute passive body armor.
        /// Excludes held weapons, tools, shields (which use Block Power), torches, and consumables.
        /// </summary>
        public static bool IsWearableArmor(ItemDrop.ItemData? item)
        {
            if (item?.m_shared == null) return false;
            var type = item.m_shared.m_itemType;
            return type == ItemDrop.ItemData.ItemType.Chest ||
                   type == ItemDrop.ItemData.ItemType.Legs ||
                   type == ItemDrop.ItemData.ItemType.Helmet ||
                   type == ItemDrop.ItemData.ItemType.Shoulder ||
                   type == ItemDrop.ItemData.ItemType.Utility ||
                   type == ItemDrop.ItemData.ItemType.Trinket;
        }
    }
}
