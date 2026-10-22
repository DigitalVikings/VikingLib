using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using VikingLib.Combat.Attacks;
using VikingLib.Configuration;
using VikingLib.SkillSystem;
using VikingLib.Stats;

namespace VikingLib.Patches
{
    /// <summary>
    /// Surgical Harmony patches intercepting sprint, jump, dodge, sneak, block, and weapon attack resource costs
    /// using VikingLib's Dual-Space Asymptotic Efficiency Engine:
    /// <c>Effective Cost = (Base + Σ Flat) / (1 + Σ Efficiency) × Π Multipliers</c>.
    /// </summary>
    [HarmonyPatch]
    public static class ActivityStaminaPatch
    {
        private static readonly AccessTools.FieldRef<SEMan, Character> _semanCharacterRef =
            AccessTools.FieldRefAccess<SEMan, Character>("m_character");

        /// <summary>
        /// Replaces sprint stamina drain calculation in Player.CheckRun with asymptotic efficiency evaluation.
        /// </summary>
        [HarmonyPatch(typeof(Player), "CheckRun")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileCheckRun(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);

            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(Player), "m_skills")),
                new CodeMatch(OpCodes.Ldc_I4_S));

            if (matcher.IsValid)
            {
                var endMatcher = matcher.Clone();
                endMatcher.MatchForward(false,
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(SEMan), nameof(SEMan.ModifyRunStaminaDrain),
                        new[] { typeof(float), typeof(float).MakeByRefType(), typeof(Vector3), typeof(bool) })));

                if (endMatcher.IsValid)
                {
                    int count = (endMatcher.Pos - matcher.Pos) + 1;
                    var labels = matcher.Labels;
                    matcher.RemoveInstructions(count);
                    matcher.Insert(
                        new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ActivityStaminaPatch), nameof(GetRunStaminaDrainRate))) { labels = labels },
                        new CodeInstruction(OpCodes.Stloc_3)
                    );
                }
            }

            return matcher.InstructionEnumeration();
        }

        public static float GetRunStaminaDrainRate(Player player)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value)
            {
                float skillFactor = player.GetSkillFactor(Skills.SkillType.Run);
                float drain = player.m_runStaminaDrain * Mathf.Lerp(1f, 0.5f, skillFactor);
                drain = drain - drain * player.GetEquipmentMovementModifier() + drain * player.GetEquipmentRunStaminaModifier();
                player.GetSEMan()?.ModifyRunStaminaDrain(drain, ref drain, Vector3.zero, true);
                return drain;
            }

            return CharacterStatManager.GetEffectiveActionCost(player.m_runStaminaDrain, player, CharacterStatType.SprintEfficiency);
        }

        /// <summary>
        /// Replaces jump stamina cost calculation in Player.OnJump with asymptotic efficiency evaluation.
        /// </summary>
        [HarmonyPatch(typeof(Player), "OnJump")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileOnJump(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);

            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(Character), "m_jumpStaminaUsage")));

            if (matcher.IsValid)
            {
                var endMatcher = matcher.Clone();
                endMatcher.MatchForward(false,
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(SEMan), nameof(SEMan.ModifyJumpStaminaUsage),
                        new[] { typeof(float), typeof(float).MakeByRefType(), typeof(bool) })));

                if (endMatcher.IsValid)
                {
                    int count = (endMatcher.Pos - matcher.Pos) + 1;
                    var labels = matcher.Labels;
                    matcher.RemoveInstructions(count);
                    matcher.Insert(
                        new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ActivityStaminaPatch), nameof(GetJumpStaminaCost))) { labels = labels },
                        new CodeInstruction(OpCodes.Stloc_0)
                    );
                }
            }

            return matcher.InstructionEnumeration();
        }

        public static float GetJumpStaminaCost(Player player)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value)
            {
                float cost = player.m_jumpStaminaUsage - player.m_jumpStaminaUsage * player.GetEquipmentMovementModifier() + player.m_jumpStaminaUsage * player.GetEquipmentJumpStaminaModifier();
                player.GetSEMan()?.ModifyJumpStaminaUsage(cost, ref cost, true);
                return cost;
            }

            return CharacterStatManager.GetEffectiveActionCost(player.m_jumpStaminaUsage, player, CharacterStatType.JumpEfficiency);
        }

        /// <summary>
        /// Evaluates dodge stamina cost through asymptotic efficiency evaluation.
        /// </summary>
        [HarmonyPatch(typeof(Player), "GetDodgeStaminaUse")]
        [HarmonyPrefix]
        public static bool PrefixGetDodgeStaminaUse(Player __instance, ref float __result)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return true;

            __result = CharacterStatManager.GetEffectiveActionCost(__instance.m_dodgeStaminaUsage, __instance, CharacterStatType.DodgeEfficiency);
            return false;
        }

        /// <summary>
        /// Replaces sneak stamina drain calculation in Player.OnSneaking with asymptotic efficiency evaluation.
        /// </summary>
        [HarmonyPatch(typeof(Player), "OnSneaking")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileOnSneaking(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);

            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(SEMan), nameof(SEMan.ModifySneakStaminaUsage),
                    new[] { typeof(float), typeof(float).MakeByRefType(), typeof(bool) })));

            if (matcher.IsValid)
            {
                int endPos = matcher.Pos;
                matcher.Start();
                matcher.Advance(1);
                int count = (endPos - matcher.Pos) + 1;
                var labels = matcher.Labels;

                matcher.RemoveInstructions(count);
                matcher.Insert(
                    new CodeInstruction(OpCodes.Ldarg_1) { labels = labels },
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ActivityStaminaPatch), nameof(GetSneakStaminaDrain))),
                    new CodeInstruction(OpCodes.Stloc_2)
                );
            }

            return matcher.InstructionEnumeration();
        }

        public static float GetSneakStaminaDrain(Player player, float dt)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value)
            {
                float skillFactor = player.GetSkillFactor(Skills.SkillType.Sneak);
                float skillScale = Mathf.Lerp(1f, 0.25f, Mathf.Pow(skillFactor, 0.5f));
                float drain = dt * player.m_sneakStaminaDrain * skillScale;
                drain += drain * player.GetEquipmentSneakStaminaModifier();
                player.GetSEMan()?.ModifySneakStaminaUsage(drain, ref drain, true);
                return drain;
            }

            float rate = CharacterStatManager.GetEffectiveActionCost(player.m_sneakStaminaDrain, player, CharacterStatType.SneakEfficiency);
            return rate * dt;
        }

        /// <summary>
        /// Evaluates weapon attack stamina cost through asymptotic efficiency evaluation.
        /// </summary>
        [HarmonyPatch(typeof(Attack), "GetAttackStamina")]
        [HarmonyPrefix]
        public static bool PrefixGetAttackStamina(Attack __instance, ref float __result)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return true;
            if (__instance.m_attackStamina <= 0f)
            {
                __result = 0f;
                return false;
            }

            var character = HumanoidAttackHelper.GetCharacter(__instance);
            if (character == null) return true;

            float baseCost = __instance.m_attackStamina;
            var weapon = HumanoidAttackHelper.GetWeapon(__instance);
            bool isSecondary = (weapon?.m_shared?.m_secondaryAttack == __instance) ||
                               HumanoidAttackHelper.IsCurrentAttackSecondary(character as Humanoid);
            float finalCost = CharacterStatManager.GetEffectiveWeaponAttackCost(baseCost, character, weapon, isSecondary, CharacterStatType.AttackStaminaEfficiency);

            // Berserk mechanic: stamina return per missing HP
            if (__instance.m_staminaReturnPerMissingHP > 0f && character != null)
            {
                float missingHp = character.GetMaxHealth() - character.GetHealth();
                finalCost -= missingHp * __instance.m_staminaReturnPerMissingHP;
            }

            __result = Mathf.Max(0f, finalCost);
            return false;
        }

        [HarmonyPatch(typeof(Attack), nameof(Attack.GetAttackEitr), new[] { typeof(Character), typeof(ItemDrop.ItemData) })]
        [HarmonyPrefix]
        public static bool PrefixGetAttackEitr(Attack __instance, Character character, ItemDrop.ItemData weapon, ref float __result)
        {
            float baseCost = weapon?.m_shared?.m_attack?.m_attackEitr ?? __instance.m_attackEitr;
            if (baseCost <= 0f)
            {
                __result = 0f;
                return false;
            }

            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value)
            {
                if (!VanillaSkillIntegration.EnableAttackCostScaling)
                {
                    __result = baseCost;
                    return false;
                }
                return true;
            }

            bool isSecondary = (weapon?.m_shared?.m_secondaryAttack == __instance) ||
                               HumanoidAttackHelper.IsCurrentAttackSecondary(character as Humanoid);
            float finalCost = CharacterStatManager.GetEffectiveWeaponAttackCost(baseCost, character, weapon, isSecondary, CharacterStatType.AttackEitrEfficiency);

            __result = Mathf.Max(0f, finalCost);
            return false;
        }

        [HarmonyPatch(typeof(Attack), "GetAttackHealth")]
        [HarmonyPrefix]
        public static bool PrefixGetAttackHealth(Attack __instance, ref float __result)
        {
            if (__instance.m_attackHealth <= 0f && __instance.m_attackHealthPercentage <= 0f)
            {
                __result = 0f;
                return false;
            }

            var character = HumanoidAttackHelper.GetCharacter(__instance);
            float baseCost = __instance.m_attackHealth;
            if (character != null && __instance.m_attackHealthPercentage > 0f)
            {
                baseCost += character.GetHealth() * __instance.m_attackHealthPercentage / 100f;
            }

            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value)
            {
                if (!VanillaSkillIntegration.EnableAttackCostScaling)
                {
                    __result = baseCost;
                    return false;
                }
                return true;
            }

            var weapon = HumanoidAttackHelper.GetWeapon(__instance);
            bool isSecondary = (weapon?.m_shared?.m_secondaryAttack == __instance) ||
                               HumanoidAttackHelper.IsCurrentAttackSecondary(character as Humanoid);
            float finalCost = CharacterStatManager.GetEffectiveWeaponAttackCost(baseCost, character, weapon, isSecondary, CharacterStatType.AttackHealthEfficiency);

            __result = Mathf.Max(0f, finalCost);
            return false;
        }

        /// <summary>
        /// Replaces swim stamina drain calculation in Player.OnSwimming with asymptotic efficiency evaluation.
        /// </summary>
        [HarmonyPatch(typeof(Player), "OnSwimming")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileOnSwimming(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);

            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(Player), "m_skills")),
                new CodeMatch(OpCodes.Ldc_I4_S));

            if (matcher.IsValid)
            {
                var endMatcher = matcher.Clone();
                endMatcher.MatchForward(false,
                    new CodeMatch(OpCodes.Callvirt, AccessTools.Method(typeof(SEMan), nameof(SEMan.ModifySwimStaminaUsage),
                        new[] { typeof(float), typeof(float).MakeByRefType(), typeof(bool) })));

                if (endMatcher.IsValid)
                {
                    int count = (endMatcher.Pos - matcher.Pos) + 1;
                    var labels = matcher.Labels;
                    matcher.RemoveInstructions(count);
                    matcher.Insert(
                        new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ActivityStaminaPatch), nameof(GetSwimStaminaDrainRate))) { labels = labels },
                        new CodeInstruction(OpCodes.Stloc_1)
                    );
                }
            }

            return matcher.InstructionEnumeration();
        }

        public static float GetSwimStaminaDrainRate(Player player)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value)
            {
                float skillFactor = VanillaSkillIntegration.EnableLocomotionStaminaScaling ? player.GetSkillFactor(Skills.SkillType.Swim) : 0f;
                float drain = Mathf.Lerp(player.m_swimStaminaDrainMinSkill, player.m_swimStaminaDrainMaxSkill, skillFactor);
                drain += drain * player.GetEquipmentSwimStaminaModifier();
                player.GetSEMan()?.ModifySwimStaminaUsage(drain, ref drain, true);
                return drain;
            }

            return CharacterStatManager.GetEffectiveActionCost(player.m_swimStaminaDrainMinSkill, player, CharacterStatType.SwimEfficiency);
        }

        /// <summary>
        /// Intercepts blocking stamina cost evaluation in SEMan.ModifyBlockStaminaUsage.
        /// </summary>
        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ModifyBlockStaminaUsage))]
        [HarmonyPostfix]
        public static void PostfixModifyBlockStaminaUsage(SEMan __instance, float baseStaminaUse, ref float staminaUse)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return;

            var character = _semanCharacterRef(__instance);
            if (character == null) return;

            staminaUse = CharacterStatManager.GetEffectiveActionCost(baseStaminaUse, character, CharacterStatType.BlockStaminaEfficiency);
        }
    }
}
