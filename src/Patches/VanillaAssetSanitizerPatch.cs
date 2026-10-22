using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace VikingLib.Patches
{
    /// <summary>
    /// Overrides and neutralizes legacy rogue armor data in native vanilla Valheim item prefabs.
    /// In vanilla assets, Iron Gate assigned dummy or prototype armor values (e.g. 100-300 on shields,
    /// 10-20 on weapons, 10 on tools, 10 on utility/trinkets) that vanilla ignored because Player.GetBodyArmor
    /// hardcoded only checking chest, leg, helmet, and shoulder slots.
    /// </summary>
    [HarmonyPatch]
    public static class VanillaAssetSanitizerPatch
    {
        private static readonly HashSet<int> SanitizedHashes = new();

        private static readonly HashSet<string> VanillaUtilityItems = new(StringComparer.OrdinalIgnoreCase)
        {
            "BeltStrength",
            "$item_beltstrength",
            "Wishbone",
            "$item_wishbone",
            "Demister",
            "$item_demister",
            "IceShoes",
            "$item_iceshoes",
            "IceSkates",
            "$item_iceskates"
        };

        [HarmonyPatch(typeof(ObjectDB), "Awake")]
        [HarmonyPostfix]
        public static void PostfixAwake(ObjectDB __instance)
        {
            SanitizePrefabs(__instance);
        }

        [HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
        [HarmonyPostfix]
        public static void PostfixCopyOtherDB(ObjectDB __instance)
        {
            SanitizePrefabs(__instance);
        }

        /// <summary>
        /// Scans ObjectDB items and strips rogue armor stats from native vanilla items.
        /// </summary>
        public static void SanitizePrefabs(ObjectDB? db)
        {
            if (db == null || db.m_items == null || db.m_items.Count == 0) return;

            int sanitizedCount = 0;
            for (int i = 0; i < db.m_items.Count; i++)
            {
                var prefab = db.m_items[i];
                if (prefab == null) continue;

                int hash = prefab.name.GetStableHashCode();
                if (SanitizedHashes.Contains(hash)) continue;

                var itemDrop = prefab.GetComponent<ItemDrop>();
                if (itemDrop?.m_itemData?.m_shared == null) continue;

                var shared = itemDrop.m_itemData.m_shared;

                if (ShouldStripArmor(prefab.name, shared))
                {
                    if (shared.m_armor > 0.001f || shared.m_armorPerLevel > 0.001f)
                    {
                        shared.m_armor = 0f;
                        shared.m_armorPerLevel = 0f;
                        sanitizedCount++;
                    }
                }

                SanitizedHashes.Add(hash);
            }

            if (sanitizedCount > 0)
            {
                VikingLibPlugin.Log.LogInfo($"Sanitized {sanitizedCount} native prefabs with rogue body armor data in ObjectDB.");
            }
        }

        private static bool ShouldStripArmor(string prefabName, ItemDrop.ItemData.SharedData shared)
        {
            var type = shared.m_itemType;

            // 1. Never strip canonical wearable armor pieces
            if (type == ItemDrop.ItemData.ItemType.Helmet ||
                type == ItemDrop.ItemData.ItemType.Chest ||
                type == ItemDrop.ItemData.ItemType.Legs ||
                type == ItemDrop.ItemData.ItemType.Shoulder)
            {
                return false;
            }

            // 2. Native vanilla utility items & trinkets that inherited constructor default m_armor = 10f
            if (type == ItemDrop.ItemData.ItemType.Utility)
            {
                return VanillaUtilityItems.Contains(prefabName) ||
                       (!string.IsNullOrEmpty(shared.m_name) && VanillaUtilityItems.Contains(shared.m_name));
            }

            if (type == ItemDrop.ItemData.ItemType.Trinket)
            {
                return prefabName.StartsWith("Trinket", StringComparison.OrdinalIgnoreCase) ||
                       (!string.IsNullOrEmpty(shared.m_name) && shared.m_name.StartsWith("$item_trinket", StringComparison.OrdinalIgnoreCase));
            }

            // 3. All non-armor equipment categories (Shields, Weapons, Tools, Torches, Ammo, Materials, etc.)
            // Shields have defense in m_blockPower (Block Armor); their m_armor in native assets is legacy placeholder.
            // Weapons and tools (hammer, hoe) similarly inherited dummy armor values never read by vanilla.
            return true;
        }
    }
}
