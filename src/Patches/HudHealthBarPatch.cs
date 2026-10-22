using HarmonyLib;
using UnityEngine;
using VikingLib.Configuration;

namespace VikingLib.Patches
{
    /// <summary>
    /// Decouples Hud health bar rendering during continuous health regeneration.
    /// Disables m_smoothFill on Hud.m_healthBarSlow so that the red bar continuously advances
    /// in lockstep with live health, eliminating the frozen red bar and lingering yellow regeneration tip.
    /// Preserves native damage drain animations.
    /// </summary>
    [HarmonyPatch]
    public static class HudHealthBarPatch
    {
        private static readonly AccessTools.FieldRef<GuiBar, float> s_delayTimerRef =
            AccessTools.FieldRefAccess<GuiBar, float>("m_delayTimer");
        private static readonly AccessTools.FieldRef<GuiBar, float> s_valueRef =
            AccessTools.FieldRefAccess<GuiBar, float>("m_value");

        [HarmonyPatch(typeof(Hud), "Awake")]
        [HarmonyPostfix]
        public static void PostfixHudAwake(Hud __instance)
        {
            ApplySettings(__instance);
        }

        [HarmonyPatch(typeof(Hud), "UpdateHealth", new[] { typeof(Player) })]
        [HarmonyPrefix]
        public static void PrefixUpdateHealth(Hud __instance, Player player)
        {
            if (__instance == null || player == null) return;

            // If mod or stats are disabled, restore vanilla smoothFill behavior
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value)
            {
                if (__instance.m_healthBarSlow != null && !__instance.m_healthBarSlow.m_smoothFill)
                {
                    __instance.m_healthBarSlow.m_smoothFill = true;
                }
                return;
            }

            ApplySettings(__instance);

            // When health is static or increasing, clear any lingering delayTimer to keep the red bar advancing
            if (__instance.m_healthBarSlow != null && player.GetHealth() >= s_valueRef(__instance.m_healthBarSlow))
            {
                try
                {
                    if (s_delayTimerRef(__instance.m_healthBarSlow) > 0f)
                    {
                        s_delayTimerRef(__instance.m_healthBarSlow) = 0f;
                    }
                }
                catch { }
            }
        }

        private static void ApplySettings(Hud hud)
        {
            if (hud == null) return;

            if (hud.m_healthBarSlow != null && hud.m_healthBarSlow.m_smoothFill)
            {
                hud.m_healthBarSlow.m_smoothFill = false;
                try
                {
                    s_delayTimerRef(hud.m_healthBarSlow) = 0f;
                }
                catch { }
            }

            if (hud.m_healthBarFast != null && hud.m_healthBarFast.m_smoothFill)
            {
                hud.m_healthBarFast.m_smoothFill = false;
            }
        }
    }
}
