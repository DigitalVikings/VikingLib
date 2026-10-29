using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using VikingLib.Configuration;
using VikingLib.SkillSystem;
using VikingLib.Stats;

namespace VikingLib.Patches.SkillChannels
{
    /// <summary>
    /// Harmony patches governing player locomotion, jumping physics,
    /// mount stamina/riding controls, and stealth concealment scaling.
    /// </summary>
    [HarmonyPatch]
    public static class SkillLocomotionPatches
    {
        // --- 1. Jump Height Physics & Mount Locomotion Scaling ---

        [HarmonyPatch(typeof(Character), nameof(Character.Jump))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileJump(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);
            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldc_R4, 1f),
                new CodeMatch(OpCodes.Ldloc_2),
                new CodeMatch(OpCodes.Ldc_R4, 0.4f),
                new CodeMatch(OpCodes.Mul),
                new CodeMatch(OpCodes.Add),
                new CodeMatch(OpCodes.Stloc_S));

            if (matcher.IsValid)
            {
                var labels = matcher.Labels;
                matcher.RemoveInstructions(5);
                matcher.Insert(
                    new CodeInstruction(OpCodes.Ldarg_0) { labels = labels },
                    new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SkillLocomotionPatches), nameof(GetJumpMultiplier)))
                );
            }

            return matcher.InstructionEnumeration();
        }

        public static float GetJumpMultiplier(Character character)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value)
            {
                float skillFactor = VanillaSkillIntegration.EnableLocomotionSpeedScaling && character.GetSkills() != null
                    ? character.GetSkills().GetSkillFactor(global::Skills.SkillType.Jump)
                    : 0f;
                return 1f + skillFactor * 0.4f;
            }

            float baseVelocity = Mathf.Max(0.01f, character.m_jumpForce);
            float targetVelocity = CharacterStatManager.GetEffectiveJumpVelocity(character);
            return targetVelocity / baseVelocity;
        }

        [HarmonyPatch(typeof(Sadle), "ApplyControlls")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileSadleApplyControlls(IEnumerable<CodeInstruction> instructions)
        {
            var targetMethod = AccessTools.Method(typeof(global::Skills), nameof(global::Skills.GetSkillFactor), new[] { typeof(global::Skills.SkillType) });
            var helperMethod = AccessTools.Method(typeof(SkillLocomotionPatches), nameof(GetRideSkillFactor));

            var list = new List<CodeInstruction>(instructions);
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Calls(targetMethod))
                {
                    list[i] = new CodeInstruction(OpCodes.Call, helperMethod);
                    break;
                }
            }
            return list;
        }

        public static float GetRideSkillFactor(global::Skills skills, global::Skills.SkillType skillType)
        {
            if (Player.m_localPlayer != null && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                return Mathf.Clamp01(CharacterStatManager.GetStat(Player.m_localPlayer, CharacterStatType.RidingControl));
            }

            if (!VanillaSkillIntegration.EnableLocomotionSpeedScaling) return 0f;
            return skills.GetSkillFactor(skillType);
        }

        [HarmonyPatch(typeof(Character), "GetRunSpeedFactor")]
        [HarmonyPostfix]
        public static void PostfixGetRunSpeedFactor(Character __instance, ref float __result)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return;
            if (__instance.HaveRider())
            {
                var rider = (Player.m_localPlayer != null && Player.m_localPlayer.IsRiding()) ? Player.m_localPlayer : null;
                if (rider != null)
                {
                    __result = CharacterStatManager.GetStat(rider, CharacterStatType.RidingSpeed);
                }
            }
        }

        [HarmonyPatch(typeof(Sadle), "UpdateRiding")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileSadleUpdateRiding(IEnumerable<CodeInstruction> instructions)
        {
            var targetMethod = AccessTools.Method(typeof(Mathf), nameof(Mathf.Lerp), new[] { typeof(float), typeof(float), typeof(float) });
            var matcher = new CodeMatcher(instructions);
            matcher.MatchForward(false, new CodeMatch(OpCodes.Call, targetMethod));

            if (matcher.IsValid)
            {
                matcher.Insert(new CodeInstruction(OpCodes.Ldarg_0));
                matcher.Advance(1);
                matcher.SetInstruction(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SkillLocomotionPatches), nameof(GetMountStaminaDrainFactor))));
            }

            return matcher.InstructionEnumeration();
        }

        public static float GetMountStaminaDrainFactor(float a, float b, float skill, Sadle sadle)
        {
            if (ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                var rider = (sadle.HaveValidUser() && Player.m_localPlayer != null && Player.m_localPlayer.IsRiding())
                    ? Player.m_localPlayer
                    : null;
                if (rider != null)
                {
                    float eff = Mathf.Max(0f, CharacterStatManager.GetStat(rider, CharacterStatType.RidingStaminaEfficiency));
                    return 1f / (1f + eff);
                }
            }
            return Mathf.Lerp(a, b, skill);
        }

        // --- 2. Stealth Concealment Scaling ---

        [HarmonyPatch(typeof(Player), "UpdateStealth")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileUpdateStealth(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);
            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(Mathf), nameof(Mathf.Lerp), new[] { typeof(float), typeof(float), typeof(float) })),
                new CodeMatch(OpCodes.Stfld, AccessTools.Field(typeof(Player), "m_stealthFactorTarget")));

            if (matcher.IsValid)
            {
                matcher.Insert(new CodeInstruction(OpCodes.Ldarg_0));
                matcher.Advance(1);
                matcher.SetInstruction(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SkillLocomotionPatches), nameof(CalculateStealthFactorTarget))));
            }

            return matcher.InstructionEnumeration();
        }

        public static float CalculateStealthFactorTarget(float minFactor, float maxFactor, float skillFactor, Player player)
        {
            float factor = VanillaSkillIntegration.EnableStealthScaling ? skillFactor : 0f;
            float target = Mathf.Lerp(minFactor, maxFactor, factor);
            if (player != null && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                float stealthMultiplier = CharacterStatManager.GetEffectiveStealthFactor(player);
                target *= stealthMultiplier;
            }
            return target;
        }
    }
}
