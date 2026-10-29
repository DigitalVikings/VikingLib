using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using VikingLib.Combat.Attacks;
using VikingLib.Configuration;
using VikingLib.SkillSystem;
using VikingLib.Stats;

namespace VikingLib.Patches.SkillChannels
{
    /// <summary>
    /// Harmony patches governing combat-related skill mechanics:
    /// ranged weapon draw times, reload duration, damage roll clamping,
    /// block power scaling, projectile spread, and weapon hold drains.
    /// </summary>
    [HarmonyPatch]
    public static class SkillCombatPatches
    {
        // --- 1. Ranged Draw & Reload Duration Scaling ---

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.GetAttackDrawPercentage))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileGetAttackDrawPercentage(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);
            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(Mathf), nameof(Mathf.Lerp), new[] { typeof(float), typeof(float), typeof(float) })));

            if (matcher.IsValid)
            {
                matcher.Insert(new CodeInstruction(OpCodes.Ldarg_0));
                matcher.Advance(1);
                matcher.SetInstruction(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SkillCombatPatches), nameof(CalculateBowDrawDuration))));
            }

            return matcher.InstructionEnumeration();
        }

        public static float CalculateBowDrawDuration(float minDuration, float maxDuration, float skillFactor, Humanoid humanoid)
        {
            if (humanoid != null && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                float speed = CharacterStatManager.GetStat(humanoid, CharacterStatType.BowDrawSpeed);
                return minDuration / Mathf.Max(0.01f, speed);
            }

            float factor = VanillaSkillIntegration.EnableRangedDrawScaling ? skillFactor : 0f;
            return Mathf.Lerp(minDuration, maxDuration, factor);
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetWeaponLoadingTime))]
        [HarmonyPrefix]
        public static bool PrefixGetWeaponLoadingTime(ItemDrop.ItemData __instance, ref float __result)
        {
            if (__instance.m_shared?.m_attack?.m_requiresReload != true)
            {
                __result = 1f;
                return false;
            }

            float reloadTime = __instance.m_shared.m_attack.m_reloadTime;
            if (Player.m_localPlayer != null && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                float reloadSpeed = CharacterStatManager.GetStat(Player.m_localPlayer, CharacterStatType.CrossbowReloadSpeed);
                __result = reloadTime / Mathf.Max(0.01f, reloadSpeed);
                return false;
            }

            if (VanillaSkillIntegration.EnableRangedDrawScaling && Player.m_localPlayer != null)
            {
                float factor = Player.m_localPlayer.GetSkillFactor(__instance.m_shared.m_skillType);
                reloadTime = Mathf.Lerp(reloadTime, reloadTime * 0.5f, factor);
            }

            __result = reloadTime;
            return false;
        }

        private static readonly AccessTools.FieldRef<Player, List<Player.MinorActionData>> _actionQueueRef =
            AccessTools.FieldRefAccess<Player, List<Player.MinorActionData>>("m_actionQueue");

        [HarmonyPatch(typeof(Player), "QueueReloadAction")]
        [HarmonyPostfix]
        public static void PostfixQueueReloadAction(Player __instance)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return;

            var queue = _actionQueueRef(__instance);
            if (queue == null || queue.Count == 0) return;

            var lastAction = queue[queue.Count - 1];
            if (lastAction != null && lastAction.m_type == Player.MinorActionData.ActionType.Reload)
            {
                if (lastAction.m_staminaDrain > 0f)
                {
                    lastAction.m_staminaDrain = CharacterStatManager.GetEffectiveActionCost(
                        lastAction.m_staminaDrain,
                        __instance,
                        CharacterStatType.CrossbowReloadStaminaEfficiency
                    );
                }
            }
        }

        // --- 2. Damage Roll Range Scaling ---

        [HarmonyPatch(typeof(global::Skills), nameof(global::Skills.GetRandomSkillRange))]
        [HarmonyPrefix]
        public static bool PrefixGetRandomSkillRange(ref float min, ref float max, global::Skills.SkillType skillType)
        {
            if (!VanillaSkillIntegration.EnableDamageRollScaling)
            {
                min = VanillaSkillIntegration.FixedDamageRollLowerBound;
                max = VanillaSkillIntegration.FixedDamageRollUpperBound;
                return false;
            }
            return true;
        }

        // --- 3. Block Power Skill Scaling ---

        [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileBlockAttack(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);
            var targetGetBlockPower = AccessTools.Method(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetBlockPower), new[] { typeof(float) });
            var targetGetSkillLevel = AccessTools.Method(typeof(Character), nameof(Character.GetSkillLevel), new[] { typeof(global::Skills.SkillType) });

            matcher.MatchForward(false, new CodeMatch(OpCodes.Callvirt, targetGetBlockPower));
            if (matcher.IsValid)
            {
                matcher.Insert(new CodeInstruction(OpCodes.Ldarg_0));
                matcher.Advance(1);
                matcher.SetInstruction(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SkillCombatPatches), nameof(GetEffectiveBlockPower))));
            }

            var list = new List<CodeInstruction>(matcher.InstructionEnumeration());
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Calls(targetGetSkillLevel))
                {
                    list[i] = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SkillCombatPatches), nameof(GetBlockSkillLevel)));
                }
            }
            return list;
        }

        public static float GetEffectiveBlockPower(ItemDrop.ItemData item, float skillFactor, Humanoid humanoid)
        {
            float baseBlock = item.GetBaseBlockPower(item.m_quality);
            if (ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                return baseBlock * CharacterStatManager.GetStat(humanoid, CharacterStatType.BlockPowerMultiplier);
            }

            float factor = VanillaSkillIntegration.EnableBlockPowerScaling ? skillFactor : 0f;
            return item.GetBlockPower(factor);
        }

        public static float GetBlockSkillLevel(Character character, global::Skills.SkillType skillType)
        {
            if (!VanillaSkillIntegration.EnableBlockPowerScaling) return 0f;
            return character.GetSkillLevel(skillType);
        }

        [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
        [HarmonyPostfix]
        public static void PostfixBlockAttack(Humanoid __instance, bool __result)
        {
            if (__result && ModConfig.ModEnabled.Value)
            {
                VikingLib.StatusEffects.StatusEffectRegistry.ConsumeCharges(__instance, VikingLib.StatusEffects.ChargeConsumeTrigger.BlockParry);
            }
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetBlockPowerTooltip))]
        [HarmonyPrefix]
        public static bool PrefixGetBlockPowerTooltip(ItemDrop.ItemData __instance, int quality, ref float __result)
        {
            if (Player.m_localPlayer != null && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                float baseBlock = __instance.GetBaseBlockPower(quality);
                __result = baseBlock * CharacterStatManager.GetStat(Player.m_localPlayer, CharacterStatType.BlockPowerMultiplier);
                return false;
            }

            if (!VanillaSkillIntegration.EnableBlockPowerScaling)
            {
                __result = __instance.GetBlockPower(quality, 0f);
                return false;
            }
            return true;
        }

        // --- 4. Projectile Precision & Weapon Hold Drains ---

        [HarmonyPatch(typeof(Attack), "FireProjectileBurst")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileFireProjectileBurst(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);
            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(Attack), "m_skillAccuracy")));

            if (matcher.IsValid)
            {
                matcher.MatchForward(false,
                    new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(Mathf), nameof(Mathf.Lerp), new[] { typeof(float), typeof(float), typeof(float) })));

                if (matcher.IsValid)
                {
                    matcher.Insert(new CodeInstruction(OpCodes.Ldarg_0));
                    matcher.Advance(1);
                    matcher.SetInstruction(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SkillCombatPatches), nameof(CalculateProjectileSpread))));
                }
            }

            return matcher.InstructionEnumeration();
        }

        public static float CalculateProjectileSpread(float minSpread, float maxSpread, float skillFactor, Attack attack)
        {
            float factor = VanillaSkillIntegration.EnableDamageRollScaling ? skillFactor : 0f;
            float spread = Mathf.Lerp(minSpread, maxSpread, factor);
            var character = HumanoidAttackHelper.GetCharacter(attack);
            if (character != null && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                float precisionMultiplier = CharacterStatManager.GetEffectiveSpreadMultiplier(character);
                spread *= precisionMultiplier;
            }
            return spread;
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetDrawStaminaDrain))]
        [HarmonyPrefix]
        public static bool PrefixGetDrawStaminaDrain(ItemDrop.ItemData __instance, ref float __result)
        {
            float drain = __instance.m_shared?.m_attack?.m_drawStaminaDrain ?? 0f;
            if (drain <= 0f)
            {
                __result = 0f;
                return false;
            }

            if (VanillaSkillIntegration.EnableAttackCostScaling && Player.m_localPlayer != null && __instance.m_shared != null)
            {
                float factor = Player.m_localPlayer.GetSkillFactor(__instance.m_shared.m_skillType);
                drain -= drain * 0.33f * factor;
            }

            if (Player.m_localPlayer != null && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                drain = CharacterStatManager.GetEffectiveActionCost(drain, Player.m_localPlayer, CharacterStatType.BowDrawStaminaEfficiency);
            }

            __result = Mathf.Max(0f, drain);
            return false;
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetDrawEitrDrain))]
        [HarmonyPrefix]
        public static bool PrefixGetDrawEitrDrain(ItemDrop.ItemData __instance, ref float __result)
        {
            float drain = __instance.m_shared?.m_attack?.m_drawEitrDrain ?? 0f;
            if (drain <= 0f)
            {
                __result = 0f;
                return false;
            }

            if (VanillaSkillIntegration.EnableAttackCostScaling && Player.m_localPlayer != null && __instance.m_shared != null)
            {
                float factor = Player.m_localPlayer.GetSkillFactor(__instance.m_shared.m_skillType);
                drain -= drain * 0.33f * factor;
            }

            if (Player.m_localPlayer != null && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                drain = CharacterStatManager.GetEffectiveActionCost(drain, Player.m_localPlayer, CharacterStatType.BowDrawEitrEfficiency);
            }

            __result = Mathf.Max(0f, drain);
            return false;
        }
    }
}
