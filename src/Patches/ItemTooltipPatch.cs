using System;
using HarmonyLib;
using UnityEngine;
using VikingLib.Configuration;
using VikingLib.UI.Tooltip;

namespace VikingLib.Patches
{
    /// <summary>
    /// Harmony patches intercepting weapon tooltip generation to display practical,
    /// modifier-pipeline-evaluated damage ranges instead of vanilla's theoretical numbers.
    /// </summary>
    [HarmonyPatch]
    public static class ItemTooltipPatch
    {
        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
            new Type[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) })]
        [HarmonyPrefix]
        public static bool PrefixGetTooltip(
            ItemDrop.ItemData item,
            int qualityLevel,
            bool crafting,
            float worldLevel,
            int stackOverride,
            bool appending,
            ref string __result)
        {
            if (ModConfig.ModEnabled.Value && ModConfig.CustomTooltipEnabled.Value && item != null)
            {
                ItemTooltipContext.Current = new ItemTooltipContext(item, qualityLevel, crafting, worldLevel, Player.m_localPlayer);

                if (TooltipEngine.IsWeaponOrTool(item))
                {
                    __result = TooltipEngine.FormatFullTooltip(item, qualityLevel, crafting, worldLevel);
                    return false; // Skip vanilla format completely for weapons!
                }
            }

            return true;
        }

        [HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip),
            new Type[] { typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool) })]
        [HarmonyPostfix]
        public static void PostfixGetTooltip()
        {
            ItemTooltipContext.Current = null;
        }

        [HarmonyPatch(typeof(HitData.DamageTypes), nameof(HitData.DamageTypes.GetTooltipString), new Type[] { typeof(Skills.SkillType) })]
        [HarmonyPrefix]
        public static bool PrefixDamageGetTooltipString(HitData.DamageTypes __instance, Skills.SkillType skillType, ref string __result)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.CustomTooltipEnabled.Value)
            {
                return true; // Defer to vanilla format
            }

            __result = TooltipEngine.FormatDamageTooltip(ItemTooltipContext.Current, __instance, skillType);
            return false; // Skip vanilla format
        }

        /// <summary>
        /// Instantly refreshes the inventory tooltip when the player presses or releases the expand key.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGui), "Update")]
        [HarmonyPostfix]
        public static void PostfixInventoryGuiUpdate(InventoryGui __instance)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.CustomTooltipEnabled.Value) return;

            bool triggered = false;

            if (Input.GetKeyDown(ModConfig.TooltipExpandKey.Value))
            {
                if (ModConfig.TooltipExpandBehavior.Value == TooltipExpandBehavior.Toggle)
                {
                    ModConfig.IsTooltipToggleActive = !ModConfig.IsTooltipToggleActive;
                }
                triggered = true;
            }
            else if (Input.GetKeyUp(ModConfig.TooltipExpandKey.Value))
            {
                if (ModConfig.TooltipExpandBehavior.Value == TooltipExpandBehavior.Hold)
                {
                    triggered = true;
                }
            }

            if (triggered && Player.m_localPlayer != null)
            {
                if (__instance.m_playerGrid != null)
                {
                    __instance.m_playerGrid.UpdateInventory(Player.m_localPlayer.GetInventory(), Player.m_localPlayer, null);
                }
                if (__instance.ContainerGrid != null && __instance.IsContainerOpen())
                {
                    __instance.ContainerGrid.UpdateInventory(__instance.ContainerGrid.GetInventory(), Player.m_localPlayer, null);
                }
            }
        }

        /// <summary>
        /// Resets the toggle state whenever the inventory is closed.
        /// </summary>
        [HarmonyPatch(typeof(InventoryGui), nameof(InventoryGui.Hide))]
        [HarmonyPostfix]
        public static void PostfixInventoryGuiHide()
        {
            ModConfig.IsTooltipToggleActive = false;
        }
    }
}
