using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using VikingLib.Combat;
using VikingLib.Configuration;
using VikingLib.Stats;

namespace VikingLib.Patches
{
    /// <summary>
    /// Thread/call context tracking for environmental resource drops (mining, logging).
    /// </summary>
    public static class DestructibleDropContext
    {
        public static Character? LastAttacker { get; set; }
        public static CharacterStatType ActiveYieldStat { get; set; } = CharacterStatType.MiningYield;
    }

    /// <summary>
    /// Intercepts environmental destructible damage (trees, logs, rocks, mining nodes)
    /// to route hits through VikingLib's DamagePipeline and apply dual-space drop yield budgets.
    /// </summary>
    [HarmonyPatch]
    public static class DestructibleCombatPatch
    {
        // --- TreeBase (Living Trees) ---
        [HarmonyPatch(typeof(TreeBase), "RPC_Damage")]
        [HarmonyPrefix]
        public static void PrefixTreeBase(TreeBase __instance, HitData hit, out DamageContext? __state)
        {
            __state = null;
            if (!ModConfig.ModEnabled.Value || hit == null || __instance == null) return;

            var nview = __instance.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid() && !nview.IsOwner()) return;

            DestructibleDropContext.LastAttacker = DamagePipeline.ResolveAttacker(hit) ?? Player.m_localPlayer;
            DestructibleDropContext.ActiveYieldStat = CharacterStatType.WoodcuttingYield;

            string cleanName = __instance.gameObject.name.Replace("(Clone)", "");
            string name = Localization.instance != null
                ? Localization.instance.Localize(cleanName)
                : cleanName;
            __state = DamagePipeline.ProcessDestructibleHit(__instance, hit, name);

            if (__state.IsCancelled)
            {
                hit.m_damage = default;
            }
        }

        [HarmonyPatch(typeof(TreeBase), "RPC_Damage")]
        [HarmonyPostfix]
        public static void PostfixTreeBase(DamageContext? __state)
        {
            DestructibleDropContext.LastAttacker = null;
            if (__state != null) DamagePipeline.FinalizeHit(__state);
        }

        // --- TreeLog (Fallen Logs) ---
        [HarmonyPatch(typeof(TreeLog), "RPC_Damage")]
        [HarmonyPrefix]
        public static void PrefixTreeLog(TreeLog __instance, HitData hit, out DamageContext? __state)
        {
            __state = null;
            if (!ModConfig.ModEnabled.Value || hit == null || __instance == null) return;

            var nview = __instance.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid() && !nview.IsOwner()) return;

            DestructibleDropContext.LastAttacker = DamagePipeline.ResolveAttacker(hit) ?? Player.m_localPlayer;
            DestructibleDropContext.ActiveYieldStat = CharacterStatType.WoodcuttingYield;

            string cleanName = __instance.gameObject.name.Replace("(Clone)", "");
            string name = Localization.instance != null
                ? Localization.instance.Localize(cleanName)
                : cleanName;
            __state = DamagePipeline.ProcessDestructibleHit(__instance, hit, name);

            if (__state.IsCancelled)
            {
                hit.m_damage = default;
            }
        }

        [HarmonyPatch(typeof(TreeLog), "RPC_Damage")]
        [HarmonyPostfix]
        public static void PostfixTreeLog(DamageContext? __state)
        {
            DestructibleDropContext.LastAttacker = null;
            if (__state != null) DamagePipeline.FinalizeHit(__state);
        }

        // --- Destructible (Stumps, Bushes, Crates, Small Rocks) ---
        [HarmonyPatch(typeof(Destructible), "RPC_Damage")]
        [HarmonyPrefix]
        public static void PrefixDestructible(Destructible __instance, HitData hit, out DamageContext? __state)
        {
            __state = null;
            if (!ModConfig.ModEnabled.Value || hit == null || __instance == null) return;

            var nview = __instance.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid() && !nview.IsOwner()) return;

            DestructibleDropContext.LastAttacker = DamagePipeline.ResolveAttacker(hit) ?? Player.m_localPlayer;
            DestructibleDropContext.ActiveYieldStat = __instance.m_destructibleType == DestructibleType.Tree
                ? CharacterStatType.WoodcuttingYield
                : CharacterStatType.MiningYield;

            string cleanName = __instance.gameObject.name.Replace("(Clone)", "");
            string name = Localization.instance != null
                ? Localization.instance.Localize(cleanName)
                : cleanName;
            __state = DamagePipeline.ProcessDestructibleHit(__instance, hit, name);

            if (__state.IsCancelled)
            {
                hit.m_damage = default;
            }
        }

        [HarmonyPatch(typeof(Destructible), "RPC_Damage")]
        [HarmonyPostfix]
        public static void PostfixDestructible(DamageContext? __state)
        {
            DestructibleDropContext.LastAttacker = null;
            if (__state != null) DamagePipeline.FinalizeHit(__state);
        }

        // --- MineRock5 (Segmented Ore Veins and Large Rocks) ---
        [HarmonyPatch(typeof(MineRock5), "RPC_Damage")]
        [HarmonyPrefix]
        public static void PrefixMineRock5(MineRock5 __instance, HitData hit, int hitAreaIndex, out DamageContext? __state)
        {
            __state = null;
            if (!ModConfig.ModEnabled.Value || hit == null || __instance == null) return;

            var nview = __instance.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid() && !nview.IsOwner()) return;

            DestructibleDropContext.LastAttacker = DamagePipeline.ResolveAttacker(hit) ?? Player.m_localPlayer;
            DestructibleDropContext.ActiveYieldStat = CharacterStatType.MiningYield;

            string name = GetMineRock5DisplayName(__instance, hitAreaIndex);
            __state = DamagePipeline.ProcessDestructibleHit(__instance, hit, name);

            if (__state.IsCancelled)
            {
                hit.m_damage = default;
            }
        }

        [HarmonyPatch(typeof(MineRock5), "RPC_Damage")]
        [HarmonyPostfix]
        public static void PostfixMineRock5(DamageContext? __state)
        {
            DestructibleDropContext.LastAttacker = null;
            if (__state != null) DamagePipeline.FinalizeHit(__state);
        }

        private static string GetMineRock5DisplayName(MineRock5 rock, int hitAreaIndex)
        {
            string name = "";
            if (Localization.instance != null && !string.IsNullOrEmpty(rock.m_name))
            {
                name = Localization.instance.Localize(rock.m_name);
            }

            if (string.IsNullOrEmpty(name))
            {
                name = rock.GetHoverText();
                if (!string.IsNullOrEmpty(name) && Localization.instance != null)
                {
                    name = Localization.instance.Localize(name);
                }
            }

            var nview = rock.GetComponent<ZNetView>();
            if ((string.IsNullOrEmpty(name) || name.Contains("m_meshFilter")) && nview != null && nview.IsValid() && nview.GetZDO() != null && ZNetScene.instance != null)
            {
                var prefab = ZNetScene.instance.GetPrefab(nview.GetZDO().GetPrefab());
                if (prefab != null)
                {
                    string prefabName = prefab.name.Replace("(Clone)", "");
                    name = Localization.instance != null ? Localization.instance.Localize(prefabName) : prefabName;
                }
            }

            if (string.IsNullOrEmpty(name) || name.Contains("m_meshFilter"))
            {
                name = "Ore Deposit";
            }

            return $"{name} (Chunk #{hitAreaIndex})";
        }

        // --- MineRock (Legacy Rock Formations) ---
        [HarmonyPatch(typeof(MineRock), "RPC_Hit")]
        [HarmonyPrefix]
        public static void PrefixMineRock(MineRock __instance, HitData hit, int hitAreaIndex, out DamageContext? __state)
        {
            __state = null;
            if (!ModConfig.ModEnabled.Value || hit == null || __instance == null) return;

            var nview = __instance.GetComponent<ZNetView>();
            if (nview != null && nview.IsValid() && !nview.IsOwner()) return;

            DestructibleDropContext.LastAttacker = DamagePipeline.ResolveAttacker(hit) ?? Player.m_localPlayer;
            DestructibleDropContext.ActiveYieldStat = CharacterStatType.MiningYield;

            string name = Localization.instance != null && !string.IsNullOrEmpty(__instance.m_name)
                ? Localization.instance.Localize(__instance.m_name)
                : global::Utils.GetPrefabName(__instance.gameObject);
            name = $"{name} (Area #{hitAreaIndex})";

            __state = DamagePipeline.ProcessDestructibleHit(__instance, hit, name);

            if (__state.IsCancelled)
            {
                hit.m_damage = default;
            }
        }

        [HarmonyPatch(typeof(MineRock), "RPC_Hit")]
        [HarmonyPostfix]
        public static void PostfixMineRock(DamageContext? __state)
        {
            DestructibleDropContext.LastAttacker = null;
            if (__state != null) DamagePipeline.FinalizeHit(__state);
        }

        // --- DropTable (Mining & Logging Drops Multi-Yield Scaling) ---
        [HarmonyPatch(typeof(DropTable), nameof(DropTable.GetDropList), new Type[] { })]
        [HarmonyPostfix]
        public static void PostfixGetDropList(DropTable __instance, ref List<GameObject> __result)
        {
            if (__result == null || __result.Count == 0) return;
            if (DestructibleDropContext.LastAttacker == null) return;
            if (!ModConfig.ModEnabled.Value || !ModConfig.EnableCharacterStats.Value) return;

            float yield = CharacterStatManager.GetStat(DestructibleDropContext.LastAttacker, DestructibleDropContext.ActiveYieldStat);
            if (Mathf.Abs(yield - 1.0f) <= 0.0001f) return;

            string source = DestructibleDropContext.ActiveYieldStat == CharacterStatType.WoodcuttingYield ? "Woodcutting" : "Mining";

            var counts = new Dictionary<GameObject, int>();
            var order = new List<GameObject>();
            for (int i = 0; i < __result.Count; i++)
            {
                var prefab = __result[i];
                if (prefab == null) continue;
                if (!counts.ContainsKey(prefab))
                {
                    counts[prefab] = 0;
                    order.Add(prefab);
                }
                counts[prefab]++;
            }

            var scaledList = new List<GameObject>();
            for (int i = 0; i < order.Count; i++)
            {
                var prefab = order[i];
                int baseCount = counts[prefab];
                var itemDrop = prefab.GetComponent<ItemDrop>();
                int count = CharacterStatManager.ResolveDropCount(yield, itemDrop?.m_itemData, baseCount, source, prefab.name);
                for (int k = 0; k < count; k++)
                {
                    scaledList.Add(prefab);
                }
            }
            __result = scaledList;
        }
    }
}
