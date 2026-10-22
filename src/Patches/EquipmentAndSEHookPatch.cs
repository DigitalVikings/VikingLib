using HarmonyLib;
using VikingLib.Configuration;
using VikingLib.Stats;

namespace VikingLib.Patches
{
    /// <summary>
    /// Reactively synchronizes declarative 3-tier modifier tokens whenever equipment or status effects change.
    /// </summary>
    [HarmonyPatch]
    public static class EquipmentAndSEHookPatch
    {
        [HarmonyPatch(typeof(Player), "UpdateModifiers")]
        [HarmonyPostfix]
        public static void PostfixUpdateModifiers(Player __instance)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return;
            VanillaEquipmentAdapter.SyncEquipment(__instance);
            VanillaFoodAdapter.SyncFood(__instance);
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.EquipItem))]
        [HarmonyPostfix]
        public static void PostfixEquipItem(Humanoid __instance)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return;
            VanillaEquipmentAdapter.SyncEquipment(__instance);
            if (__instance is Player player) VanillaFoodAdapter.SyncFood(player);
        }

        [HarmonyPatch(typeof(Humanoid), nameof(Humanoid.UnequipItem))]
        [HarmonyPostfix]
        public static void PostfixUnequipItem(Humanoid __instance)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return;
            VanillaEquipmentAdapter.SyncEquipment(__instance);
            if (__instance is Player player) VanillaFoodAdapter.SyncFood(player);
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.AddStatusEffect), new[] { typeof(StatusEffect), typeof(bool), typeof(int), typeof(float), typeof(short) })]
        [HarmonyPostfix]
        public static void PostfixAddStatusEffect(SEMan __instance)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return;
            var character = CharacterStatsHelper.GetCharacter(__instance);
            if (character != null)
            {
                VanillaStatusEffectAdapter.SyncStatusEffects(character);
                if (character is Player player) VanillaFoodAdapter.SyncFood(player);
            }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.AddStatusEffect), new[] { typeof(int), typeof(bool), typeof(int), typeof(float), typeof(short) })]
        [HarmonyPostfix]
        public static void PostfixAddStatusEffectHash(SEMan __instance)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return;
            var character = CharacterStatsHelper.GetCharacter(__instance);
            if (character != null)
            {
                VanillaStatusEffectAdapter.SyncStatusEffects(character);
            }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.RemoveStatusEffect), new[] { typeof(StatusEffect), typeof(bool) })]
        [HarmonyPostfix]
        public static void PostfixRemoveStatusEffect(SEMan __instance)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return;
            var character = CharacterStatsHelper.GetCharacter(__instance);
            if (character != null)
            {
                VanillaStatusEffectAdapter.SyncStatusEffects(character);
            }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.RemoveStatusEffect), new[] { typeof(int), typeof(bool) })]
        [HarmonyPostfix]
        public static void PostfixRemoveStatusEffectHash(SEMan __instance)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return;
            var character = CharacterStatsHelper.GetCharacter(__instance);
            if (character != null)
            {
                VanillaStatusEffectAdapter.SyncStatusEffects(character);
            }
        }

        [HarmonyPatch(typeof(SEMan), nameof(SEMan.RemoveAllStatusEffects))]
        [HarmonyPostfix]
        public static void PostfixRemoveAllStatusEffects(SEMan __instance)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return;
            var character = CharacterStatsHelper.GetCharacter(__instance);
            if (character != null)
            {
                VanillaStatusEffectAdapter.SyncStatusEffects(character);
            }
        }
    }
}
