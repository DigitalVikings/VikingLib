using HarmonyLib;
using VikingLib.Combat.Roll;
using VikingLib.Configuration;

namespace VikingLib.Patches
{
    /// <summary>
    /// Intercepts weapon damage rolling on Characters and Players to route calculations
    /// through VikingLib's DamageRollPipeline.
    /// </summary>
    [HarmonyPatch]
    public static class CharacterSkillRollPatch
    {
        [HarmonyPatch(typeof(Character), nameof(Character.GetRandomSkillFactor))]
        [HarmonyPrefix]
        public static bool PrefixCharacter(Character __instance, Skills.SkillType skill, ref float __result)
        {
            if (ModConfig.ModEnabled.Value && __instance != null)
            {
                __result = DamageRollPipeline.RollFactor(__instance, skill);
                return false;
            }
            return true;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.GetRandomSkillFactor))]
        [HarmonyPrefix]
        public static bool PrefixPlayer(Player __instance, Skills.SkillType skill, ref float __result)
        {
            if (ModConfig.ModEnabled.Value && __instance != null)
            {
                __result = DamageRollPipeline.RollFactor(__instance, skill);
                return false;
            }
            return true;
        }
    }
}
