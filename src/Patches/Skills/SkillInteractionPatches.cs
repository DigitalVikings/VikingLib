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
    /// Harmony patches governing non-combat interactions:
    /// building stamina/durability, crop/tree harvesting radius, crafting yields,
    /// foraging bonuses, cooking station yields, fishing stamina drain, and drop scaling.
    /// </summary>
    [HarmonyPatch]
    public static class SkillInteractionPatches
    {
        // --- 1. Building & Placement Scaling ---

        [HarmonyPatch(typeof(Player), "GetBuildStamina")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileGetBuildStamina(IEnumerable<CodeInstruction> instructions)
        {
            var targetMethod = AccessTools.Method(typeof(Character), nameof(Character.GetSkillFactor), new[] { typeof(global::Skills.SkillType) });
            var helperMethod = AccessTools.Method(typeof(SkillInteractionPatches), nameof(GetBuildSkillFactor));

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

        [HarmonyPatch(typeof(Player), "GetPlaceDurability")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileGetPlaceDurability(IEnumerable<CodeInstruction> instructions)
        {
            var targetMethod = AccessTools.Method(typeof(Character), nameof(Character.GetSkillFactor), new[] { typeof(global::Skills.SkillType) });
            var helperMethod = AccessTools.Method(typeof(SkillInteractionPatches), nameof(GetBuildSkillFactor));

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

        public static float GetBuildSkillFactor(Character character, global::Skills.SkillType skillType)
        {
            if (!VanillaSkillIntegration.EnableBuildAndCraftScaling) return 0f;
            return character?.GetSkillFactor(skillType) ?? 0f;
        }

        // --- 2. Harvesting Radius Scaling ---

        [HarmonyPatch(typeof(Attack), "DoMeleeAttack")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileDoMeleeAttack(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);
            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(Attack), "m_harvestRadiusMaxLevel")));

            if (matcher.IsValid)
            {
                matcher.MatchForward(false,
                    new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(Mathf), nameof(Mathf.Lerp), new[] { typeof(float), typeof(float), typeof(float) })));

                if (matcher.IsValid)
                {
                    matcher.SetInstruction(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SkillInteractionPatches), nameof(CalculateHarvestRadius))));
                }
            }

            return matcher.InstructionEnumeration();
        }

        [HarmonyPatch(typeof(Piece), "Awake")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspilePieceAwake(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);
            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(Piece), "m_harvestRadiusMaxLevel")));

            if (matcher.IsValid)
            {
                matcher.MatchForward(false,
                    new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(Mathf), nameof(Mathf.Lerp), new[] { typeof(float), typeof(float), typeof(float) })));

                if (matcher.IsValid)
                {
                    matcher.SetInstruction(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SkillInteractionPatches), nameof(CalculateHarvestRadius))));
                }
            }

            return matcher.InstructionEnumeration();
        }

        [HarmonyPatch(typeof(Piece), nameof(Piece.OnPlaced))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspilePieceOnPlaced(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);
            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(Piece), "m_harvestRadiusMaxLevel")));

            if (matcher.IsValid)
            {
                matcher.MatchForward(false,
                    new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(Mathf), nameof(Mathf.Lerp), new[] { typeof(float), typeof(float), typeof(float) })));

                if (matcher.IsValid)
                {
                    matcher.SetInstruction(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SkillInteractionPatches), nameof(CalculateHarvestRadius))));
                }
            }

            return matcher.InstructionEnumeration();
        }

        public static float CalculateHarvestRadius(float minRadius, float maxRadius, float skillFactor)
        {
            float factor = VanillaSkillIntegration.EnableBuildAndCraftScaling ? skillFactor : 0f;
            float radius = Mathf.Lerp(minRadius, maxRadius, factor);
            if (Player.m_localPlayer != null && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                float mod = CharacterStatManager.GetStat(Player.m_localPlayer, CharacterStatType.HarvestRadius);
                radius *= Mathf.Max(0.1f, mod);
            }
            return radius;
        }

        // --- 3. Crafting, Foraging, Cooking & Fishing Scaling ---

        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileDoCrafting(IEnumerable<CodeInstruction> instructions)
        {
            var targetMethod = AccessTools.Method(typeof(Character), nameof(Character.GetSkillFactor), new[] { typeof(global::Skills.SkillType) });
            var helperMethod = AccessTools.Method(typeof(SkillInteractionPatches), nameof(GetCraftingSkillFactor));

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

        [HarmonyPatch(typeof(InventoryGui), "UpdateRecipe")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileUpdateRecipe(IEnumerable<CodeInstruction> instructions)
        {
            var targetMethod = AccessTools.Method(typeof(Character), nameof(Character.GetSkillFactor), new[] { typeof(global::Skills.SkillType) });
            var helperMethod = AccessTools.Method(typeof(SkillInteractionPatches), nameof(GetCraftingSkillFactor));

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

        public static float GetCraftingSkillFactor(Character character, global::Skills.SkillType skillType)
        {
            if (character != null && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                float yield = CharacterStatManager.GetStat(character, CharacterStatType.CraftingYield);
                return (yield - 1.0f) / 0.25f;
            }

            if (!VanillaSkillIntegration.EnableDropYieldScaling && !VanillaSkillIntegration.EnableBuildAndCraftScaling) return 0f;
            return character?.GetSkillFactor(skillType) ?? 0f;
        }

        [HarmonyPatch(typeof(Pickable), nameof(Pickable.Interact))]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspilePickableInteract(IEnumerable<CodeInstruction> instructions)
        {
            var targetMethod = AccessTools.Method(typeof(Character), nameof(Character.GetSkillFactor), new[] { typeof(global::Skills.SkillType) });
            var helperMethod = AccessTools.Method(typeof(SkillInteractionPatches), nameof(GetForagingSkillFactor));

            var matcher = new CodeMatcher(instructions);
            matcher.MatchForward(false, new CodeMatch(OpCodes.Callvirt, targetMethod));
            if (matcher.IsValid)
            {
                matcher.Insert(new CodeInstruction(OpCodes.Ldarg_0));
                matcher.Advance(1);
                matcher.SetInstruction(new CodeInstruction(OpCodes.Call, helperMethod));
            }

            return matcher.InstructionEnumeration();
        }

        public static float GetForagingSkillFactor(Character character, global::Skills.SkillType skillType, Pickable pickable)
        {
            if (character != null && pickable != null && pickable.m_maxLevelBonusChance > 0f && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                float yield = CharacterStatManager.GetStat(character, CharacterStatType.ForagingYield);
                return (yield - 1.0f) / pickable.m_maxLevelBonusChance;
            }

            if (!VanillaSkillIntegration.EnableDropYieldScaling && !VanillaSkillIntegration.EnableBuildAndCraftScaling) return 0f;
            return character?.GetSkillFactor(skillType) ?? 0f;
        }

        [HarmonyPatch(typeof(CookingStation), "OnInteract")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileCookingStationOnInteract(IEnumerable<CodeInstruction> instructions)
        {
            var targetMethod = AccessTools.Method(typeof(Character), nameof(Character.GetSkillFactor), new[] { typeof(global::Skills.SkillType) });
            var helperMethod = AccessTools.Method(typeof(SkillInteractionPatches), nameof(GetCookingSkillFactor));

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

        public static float GetCookingSkillFactor(Character character, global::Skills.SkillType skillType)
        {
            if (character != null && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                float yield = CharacterStatManager.GetStat(character, CharacterStatType.CookingYield);
                return (yield - 1.0f) / 0.25f;
            }

            if (!VanillaSkillIntegration.EnableDropYieldScaling && !VanillaSkillIntegration.EnableBuildAndCraftScaling) return 0f;
            return character?.GetSkillFactor(skillType) ?? 0f;
        }

        [HarmonyPatch(typeof(FishingFloat), "FixedUpdate")]
        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileFishingFloatFixedUpdate(IEnumerable<CodeInstruction> instructions)
        {
            var matcher = new CodeMatcher(instructions);
            matcher.MatchForward(false,
                new CodeMatch(OpCodes.Ldfld, AccessTools.Field(typeof(FishingFloat), "m_hookedStaminaPerSecMaxSkill")));

            if (matcher.IsValid)
            {
                matcher.MatchForward(false,
                    new CodeMatch(OpCodes.Call, AccessTools.Method(typeof(Mathf), nameof(Mathf.Lerp), new[] { typeof(float), typeof(float), typeof(float) })));

                if (matcher.IsValid)
                {
                    matcher.SetInstruction(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(SkillInteractionPatches), nameof(CalculateFishingStaminaDrain))));
                }
            }

            return matcher.InstructionEnumeration();
        }

        public static float CalculateFishingStaminaDrain(float minDrain, float maxDrain, float skillFactor)
        {
            float factor = VanillaSkillIntegration.EnableBuildAndCraftScaling ? skillFactor : 0f;
            float drain = Mathf.Lerp(minDrain, maxDrain, factor);
            if (Player.m_localPlayer != null && ModConfig.ModEnabled.Value && ModConfig.EnableCharacterStats.Value)
            {
                drain = CharacterStatManager.GetEffectiveActionCost(drain, Player.m_localPlayer, CharacterStatType.FishingStaminaEfficiency);
            }
            return drain;
        }

        // --- 4. Monster Slay Loot Yield ---

        [HarmonyPatch(typeof(CharacterDrop), nameof(CharacterDrop.GenerateDropList))]
        [HarmonyPostfix]
        public static void PostfixGenerateDropList(CharacterDrop __instance, ref List<KeyValuePair<GameObject, int>> __result)
        {
            if (__result == null || __result.Count == 0) return;
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return;

            var player = Player.m_localPlayer;
            if (player == null) return;

            float yield = CharacterStatManager.GetStat(player, CharacterStatType.MonsterDropYield);
            if (Mathf.Abs(yield - 1.0f) <= 0.0001f) return;

            for (int i = 0; i < __result.Count; i++)
            {
                var entry = __result[i];
                if (entry.Key == null) continue;
                var itemDrop = entry.Key.GetComponent<ItemDrop>();
                if (itemDrop?.m_itemData != null && CharacterStatManager.CanScaleDrop(itemDrop.m_itemData))
                {
                    int scaled = CharacterStatManager.ResolveDropCount(yield, itemDrop.m_itemData, entry.Value, "Monster Slay", entry.Key.name);
                    __result[i] = new KeyValuePair<GameObject, int>(entry.Key, scaled);
                }
            }
        }

        // --- 5. Gathering, Cooking & Crafting Yield Telemetry ---

        private static readonly AccessTools.FieldRef<Pickable, bool> s_pickablePickedRef =
            AccessTools.FieldRefAccess<Pickable, bool>("m_picked");
        private static readonly AccessTools.FieldRef<InventoryGui, Recipe> s_craftRecipeRef =
            AccessTools.FieldRefAccess<InventoryGui, Recipe>("m_craftRecipe");
        private static readonly AccessTools.FieldRef<InventoryGui, bool> s_multiCraftingRef =
            AccessTools.FieldRefAccess<InventoryGui, bool>("m_multiCrafting");

        [HarmonyPatch(typeof(Pickable), "RPC_Pick")]
        [HarmonyPrefix]
        public static void PrefixPickableRPC_Pick(Pickable __instance, long sender, int bonus)
        {
            if (!ModConfig.ModEnabled.Value || ModConfig.LogDropYields == null || !ModConfig.LogDropYields.Value) return;
            if (__instance == null) return;
            var nview = __instance.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner() || s_pickablePickedRef(__instance)) return;
            var player = Player.m_localPlayer;
            if (player == null) return;

            float yield = CharacterStatManager.GetStat(player, CharacterStatType.ForagingYield);
            if (Mathf.Abs(yield - 1.0f) <= 0.001f) return;

            string rawName = __instance.m_itemPrefab != null ? __instance.m_itemPrefab.name : "Pickable";
            var drop = __instance.m_itemPrefab?.GetComponent<ItemDrop>();
            if (drop?.m_itemData?.m_shared?.m_name != null)
            {
                rawName = drop.m_itemData.m_shared.m_name;
            }
            string name = Localization.instance != null ? Localization.instance.Localize(rawName) : rawName;
            float chance = yield - 1.0f;
            string outcome = bonus > 0 ? $"SUCCESS (+{bonus})" : "MISSED";

            VikingLibPlugin.Log.LogInfo($"[VikingLib Yield] [Foraging] {name} (Yield: {yield:F2}x, Bonus Chance: {chance:P0} -> {outcome})");
        }

        [HarmonyPatch(typeof(CookingStation), "RPC_RemoveDoneItem")]
        [HarmonyPrefix]
        public static void PrefixCookingStationRPC_RemoveDoneItem(CookingStation __instance, long sender, Vector3 userPoint, int amount)
        {
            if (!ModConfig.ModEnabled.Value || ModConfig.LogDropYields == null || !ModConfig.LogDropYields.Value) return;
            if (__instance == null) return;
            var nview = __instance.GetComponent<ZNetView>();
            if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;
            var player = Player.m_localPlayer;
            if (player == null) return;

            float yield = CharacterStatManager.GetStat(player, CharacterStatType.CookingYield);
            if (Mathf.Abs(yield - 1.0f) <= 0.001f) return;

            float chance = yield - 1.0f;
            string outcome = amount > 1 ? $"SUCCESS (+{amount - 1})" : "MISSED";
            string stationName = Localization.instance != null ? Localization.instance.Localize(__instance.gameObject.name.Replace("(Clone)", "")) : __instance.gameObject.name;

            VikingLibPlugin.Log.LogInfo($"[VikingLib Yield] [Cooking] {stationName} (Yield: {yield:F2}x, Bonus Chance: {chance:P0} -> {outcome})");
        }

        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        [HarmonyPrefix]
        public static void PrefixDoCrafting(InventoryGui __instance, Player player, out (string Name, int PreCount, float Yield)? __state)
        {
            __state = null;
            if (!ModConfig.ModEnabled.Value || ModConfig.LogDropYields == null || !ModConfig.LogDropYields.Value) return;
            if (__instance == null || player == null) return;

            float yield = CharacterStatManager.GetStat(player, CharacterStatType.CraftingYield);
            if (Mathf.Abs(yield - 1.0f) <= 0.001f) return;

            var recipe = s_craftRecipeRef(__instance);
            if (recipe == null || recipe.m_item == null || recipe.m_item.m_itemData?.m_shared == null) return;
            if (recipe.m_item.m_itemData.m_shared.m_maxStackSize <= 1) return;

            string itemName = recipe.m_item.m_itemData.m_shared.m_name;
            int preCount = player.GetInventory()?.CountItems(itemName) ?? 0;
            __state = (itemName, preCount, yield);
        }

        [HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
        [HarmonyPostfix]
        public static void PostfixDoCrafting(InventoryGui __instance, Player player, (string Name, int PreCount, float Yield)? __state)
        {
            if (__state == null || player == null) return;
            var (rawName, preCount, yield) = __state.Value;
            int postCount = player.GetInventory()?.CountItems(rawName) ?? 0;
            int crafted = postCount - preCount;
            if (crafted <= 0) return;

            bool isMulti = s_multiCraftingRef(__instance);
            int multi = !isMulti ? 1 : __instance.m_multiCraftAmount;
            var recipe = s_craftRecipeRef(__instance);
            int numPerCraft = recipe?.m_amount ?? 1;
            int expected = multi * numPerCraft;
            int bonus = crafted - expected;

            string displayName = Localization.instance != null ? Localization.instance.Localize(rawName) : rawName;
            float chance = yield - 1.0f;
            string outcome = bonus > 0 ? $"SUCCESS (+{bonus})" : "MISSED";

            VikingLibPlugin.Log.LogInfo($"[VikingLib Yield] [Crafting] {displayName} x{expected} -> x{crafted} (Yield: {yield:F2}x, Bonus Chance: {chance:P0} -> {outcome})");
        }
    }
}
