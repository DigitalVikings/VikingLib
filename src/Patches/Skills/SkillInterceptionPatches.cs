using System;
using System.Collections.Generic;
using HarmonyLib;
using VikingLib.SkillSystem;
using VikingLib.Stats;

namespace VikingLib.Patches.SkillChannels
{
    /// <summary>
    /// Harmony patches governing skill acquisition, XP interception, death penalties,
    /// level-up notifications, and corrupt skill save sanitization.
    /// </summary>
    [HarmonyPatch]
    public static class SkillInterceptionPatches
    {
        // --- 1. Skill Acquisition & XP Interception ---

        [HarmonyPatch(typeof(global::Skills), nameof(global::Skills.RaiseSkill))]
        [HarmonyPrefix]
        public static bool PrefixRaiseSkill(global::Skills __instance, global::Skills.SkillType skillType, float factor)
        {
            var player = __instance.GetComponent<Player>();
            if (player != null && !SkillEvents.ShouldRaiseSkill(player, skillType, factor))
            {
                return false; // Cancel native XP gain
            }
            return true;
        }

        [HarmonyPatch(typeof(Player), nameof(Player.OnSkillLevelup))]
        [HarmonyPostfix]
        public static void PostfixOnSkillLevelup(Player __instance, global::Skills.SkillType skill, float level)
        {
            SkillEvents.InvokeSkillLevelChanged(__instance, skill, level);
            VanillaEquipmentAdapter.SyncEquipment(__instance);
        }

        // --- 2. Death Penalty Interception ---

        [HarmonyPatch(typeof(global::Skills), nameof(global::Skills.OnDeath))]
        [HarmonyPrefix]
        public static bool PrefixOnDeath(global::Skills __instance)
        {
            var player = __instance.GetComponent<Player>();
            if (player != null && !SkillEvents.ShouldApplyDeathPenalty(player))
            {
                return false; // Cancel native skill loss on death
            }
            return true;
        }

        // --- 2b. Corrupt & Unregistered Skill Defense & Save Sanitizer ---

        private static readonly AccessTools.FieldRef<global::Skills, Dictionary<global::Skills.SkillType, global::Skills.Skill>> _skillDataRef =
            AccessTools.FieldRefAccess<global::Skills, Dictionary<global::Skills.SkillType, global::Skills.Skill>>("m_skillData");

        private static readonly System.Reflection.MethodInfo _getSkillDefMethod =
            AccessTools.Method(typeof(global::Skills), "GetSkillDef", new[] { typeof(global::Skills.SkillType) });

        [HarmonyPatch(typeof(global::Skills), nameof(global::Skills.Save))]
        [HarmonyPrefix]
        public static void PrefixSaveSkills(global::Skills __instance)
        {
            var dict = _skillDataRef(__instance);
            if (dict == null) return;

            List<global::Skills.SkillType>? invalidKeys = null;
            foreach (var kvp in dict)
            {
                if (kvp.Value == null || kvp.Value.m_info == null)
                {
                    invalidKeys ??= new List<global::Skills.SkillType>();
                    invalidKeys.Add(kvp.Key);
                }
            }

            if (invalidKeys != null)
            {
                foreach (var k in invalidKeys)
                {
                    dict.Remove(k);
                }
            }
        }

        [HarmonyPatch(typeof(global::Skills), "GetSkill")]
        [HarmonyPrefix]
        public static bool PrefixGetSkill(global::Skills __instance, global::Skills.SkillType skillType, ref global::Skills.Skill __result)
        {
            var dict = _skillDataRef(__instance);
            if (dict != null && dict.TryGetValue(skillType, out var skill))
            {
                if (skill != null && skill.m_info != null)
                {
                    __result = skill;
                    return false;
                }
                dict.Remove(skillType);
            }

            var skillDef = (global::Skills.SkillDef?)_getSkillDefMethod.Invoke(__instance, new object[] { skillType });
            if (skillDef == null)
            {
                var fallbackDef = new global::Skills.SkillDef { m_skill = skillType };
                __result = new global::Skills.Skill(fallbackDef);
                return false;
            }

            return true;
        }
    }
}
