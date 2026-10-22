using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using VikingLib.Configuration;
using VikingLib.SkillSystem;
using VikingLib.Stats;

namespace VikingLib.Patches
{
    /// <summary>
    /// Surgical Harmony patches intercepting jog, run, and crouch movement speed calculations.
    /// </summary>
    [HarmonyPatch]
    public static class LocomotionSpeedPatch
    {
        /// <summary>
        /// Evaluates jog speed factor through 3-tier modifiers for Character and Player overrides.
        /// </summary>
        [HarmonyPatch(typeof(Character), "GetJogSpeedFactor")]
        [HarmonyPrefix]
        public static bool PrefixCharacterGetJogSpeedFactor(Character __instance, ref float __result)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return true;

            __result = CharacterStatManager.GetStat(__instance, CharacterStatType.JogSpeed);
            return false;
        }

        [HarmonyPatch(typeof(Player), "GetJogSpeedFactor")]
        [HarmonyPrefix]
        public static bool PrefixPlayerGetJogSpeedFactor(Player __instance, ref float __result)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return true;

            __result = CharacterStatManager.GetStat(__instance, CharacterStatType.JogSpeed);
            return false;
        }

        /// <summary>
        /// Evaluates run speed factor through 3-tier modifiers for Character and Player overrides.
        /// </summary>
        [HarmonyPatch(typeof(Character), "GetRunSpeedFactor")]
        [HarmonyPrefix]
        public static bool PrefixCharacterGetRunSpeedFactor(Character __instance, ref float __result)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return true;

            __result = CharacterStatManager.GetStat(__instance, CharacterStatType.RunSpeed);
            return false;
        }

        [HarmonyPatch(typeof(Player), "GetRunSpeedFactor")]
        [HarmonyPrefix]
        public static bool PrefixPlayerGetRunSpeedFactor(Player __instance, ref float __result)
        {
            if (!VanillaSkillIntegration.EnableLocomotionSpeedScaling && (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value))
            {
                __result = 1f * (1f + __instance.GetEquipmentMovementModifier() * 1.5f);
                return false;
            }

            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return true;

            __result = CharacterStatManager.GetStat(__instance, CharacterStatType.RunSpeed);
            return false;
        }

        /// <summary>
        /// Intercepts crouch / sneak speed assignment in Character.UpdateWalking.
        /// </summary>
        [HarmonyPatch(typeof(Character), "UpdateWalking")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileUpdateWalking(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);

            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(Character), "m_crouchSpeed")));

            if (matcher.IsValid)
            {
                matcher.Set(OpCodes.Call, AccessTools.Method(typeof(LocomotionSpeedPatch), nameof(GetCrouchSpeed)));
            }

            return matcher.InstructionEnumeration();
        }

        public static float GetCrouchSpeed(Character character)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value)
            {
                return character.m_crouchSpeed;
            }

            return CharacterStatManager.GetStat(character, CharacterStatType.CrouchSpeed);
        }

        /// <summary>
        /// Neutralizes native SEMan.ApplyStatusEffectSpeedMods to prevent double-dipping,
        /// since speed modifiers are evaluated cleanly as 3-tier tokens across jog, run, and crouch.
        /// </summary>
        [HarmonyPatch(typeof(SEMan), nameof(SEMan.ApplyStatusEffectSpeedMods))]
        [HarmonyPrefix]
        public static bool PrefixApplyStatusEffectSpeedMods()
        {
            if (ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                return false;
            }
            return true;
        }
    }
}
