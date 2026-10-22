using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace VikingLib.Combat.Attacks
{
    /// <summary>
    /// Utility for extracting and exporting all weapon attack data from the game's ObjectDB
    /// into a structured JSON file for auditing and offline inspection.
    /// </summary>
    public static class WeaponAttackExtractor
    {
        [Serializable]
        public class AttackDataExport
        {
            public string PrefabName = "";
            public string LocalizedName = "";
            public string ItemType = "";
            public string SkillType = "";
            public Dictionary<string, float> BaseDamages = new();
            public PrimaryAttackExport PrimaryAttack = new();
            public SecondaryAttackExport? SecondaryAttack;
            public Dictionary<string, float> VikingLibStepMultipliers = new();
        }

        [Serializable]
        public class PrimaryAttackExport
        {
            public int ChainLevels;
            public float DamageMultiplier;
            public float LastChainMultiplier;
            public float ForceMultiplier;
            public float StaggerMultiplier;
            public string AttackAnimation = "";
        }

        [Serializable]
        public class SecondaryAttackExport
        {
            public float DamageMultiplier;
            public float ForceMultiplier;
            public float StaggerMultiplier;
            public string AttackAnimation = "";
        }

        /// <summary>
        /// Scans ObjectDB.m_items and writes VanillaWeaponAttacks.json to the specified path.
        /// </summary>
        public static string ExportAllWeapons(string? customPath = null)
        {
            if (ObjectDB.instance == null || ObjectDB.instance.m_items == null || ObjectDB.instance.m_items.Count == 0)
            {
                return "ObjectDB is not loaded or contains no items.";
            }

            string targetPath = customPath ?? Path.Combine(BepInEx.Paths.ConfigPath, "VanillaWeaponAttacks.json");
            var exports = new List<AttackDataExport>();

            foreach (var go in ObjectDB.instance.m_items)
            {
                if (go == null) continue;
                var itemDrop = go.GetComponent<ItemDrop>();
                if (itemDrop == null || itemDrop.m_itemData?.m_shared == null) continue;

                var shared = itemDrop.m_itemData.m_shared;
                bool isWeapon = shared.m_itemType == ItemDrop.ItemData.ItemType.OneHandedWeapon ||
                               shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon ||
                               shared.m_itemType == ItemDrop.ItemData.ItemType.Bow ||
                               shared.m_itemType == ItemDrop.ItemData.ItemType.Attach_Atgeir;

                if (!isWeapon && shared.m_damages.GetTotalDamage() <= 0f) continue;

                var entry = new AttackDataExport
                {
                    PrefabName = go.name,
                    LocalizedName = Localization.instance != null ? Localization.instance.Localize(shared.m_name) : shared.m_name,
                    ItemType = shared.m_itemType.ToString(),
                    SkillType = shared.m_skillType.ToString()
                };

                // Base damages
                var d = shared.m_damages;
                if (d.m_blunt > 0) entry.BaseDamages["Blunt"] = d.m_blunt;
                if (d.m_slash > 0) entry.BaseDamages["Slash"] = d.m_slash;
                if (d.m_pierce > 0) entry.BaseDamages["Pierce"] = d.m_pierce;
                if (d.m_chop > 0) entry.BaseDamages["Chop"] = d.m_chop;
                if (d.m_pickaxe > 0) entry.BaseDamages["Pickaxe"] = d.m_pickaxe;
                if (d.m_fire > 0) entry.BaseDamages["Fire"] = d.m_fire;
                if (d.m_frost > 0) entry.BaseDamages["Frost"] = d.m_frost;
                if (d.m_lightning > 0) entry.BaseDamages["Lightning"] = d.m_lightning;
                if (d.m_poison > 0) entry.BaseDamages["Poison"] = d.m_poison;
                if (d.m_spirit > 0) entry.BaseDamages["Spirit"] = d.m_spirit;

                // Primary attack
                if (shared.m_attack != null)
                {
                    entry.PrimaryAttack = new PrimaryAttackExport
                    {
                        ChainLevels = shared.m_attack.m_attackChainLevels,
                        DamageMultiplier = shared.m_attack.m_damageMultiplier,
                        LastChainMultiplier = shared.m_attack.m_lastChainDamageMultiplier,
                        ForceMultiplier = shared.m_attack.m_forceMultiplier,
                        StaggerMultiplier = shared.m_attack.m_staggerMultiplier,
                        AttackAnimation = shared.m_attack.m_attackAnimation ?? ""
                    };
                }

                // Secondary attack
                if (shared.m_secondaryAttack != null && !string.IsNullOrEmpty(shared.m_secondaryAttack.m_attackAnimation))
                {
                    entry.SecondaryAttack = new SecondaryAttackExport
                    {
                        DamageMultiplier = shared.m_secondaryAttack.m_damageMultiplier,
                        ForceMultiplier = shared.m_secondaryAttack.m_forceMultiplier,
                        StaggerMultiplier = shared.m_secondaryAttack.m_staggerMultiplier,
                        AttackAnimation = shared.m_secondaryAttack.m_attackAnimation
                    };
                }

                // VikingLib profile resolution
                var profile = WeaponAttackRegistry.GetProfile(itemDrop.m_itemData);
                foreach (var step in profile.Steps)
                {
                    entry.VikingLibStepMultipliers[step.Key] = step.Value.DefaultMultiplier;
                }

                exports.Add(entry);
            }

            // Write formatted JSON
            string json = SimpleJsonSerialize(exports);
            File.WriteAllText(targetPath, json);
            return $"Successfully exported {exports.Count} weapon attack profiles to: {targetPath}";
        }

        private static string SimpleJsonSerialize(List<AttackDataExport> list)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[");
            for (int i = 0; i < list.Count; i++)
            {
                var item = list[i];
                sb.AppendLine("  {");
                sb.AppendLine($"    \"PrefabName\": \"{item.PrefabName}\",");
                sb.AppendLine($"    \"LocalizedName\": \"{Escape(item.LocalizedName)}\",");
                sb.AppendLine($"    \"ItemType\": \"{item.ItemType}\",");
                sb.AppendLine($"    \"SkillType\": \"{item.SkillType}\",");
                sb.AppendLine($"    \"PrimaryChainLevels\": {item.PrimaryAttack.ChainLevels},");
                sb.AppendLine($"    \"PrimaryDamageMult\": {item.PrimaryAttack.DamageMultiplier:F2},");
                sb.AppendLine($"    \"PrimaryFinisherMult\": {item.PrimaryAttack.LastChainMultiplier:F2},");
                if (item.SecondaryAttack != null)
                {
                    sb.AppendLine($"    \"SecondaryDamageMult\": {item.SecondaryAttack.DamageMultiplier:F2},");
                }
                sb.AppendLine("    \"VikingLibSteps\": {");
                int sCount = 0;
                foreach (var s in item.VikingLibStepMultipliers)
                {
                    sCount++;
                    string comma = sCount < item.VikingLibStepMultipliers.Count ? "," : "";
                    sb.AppendLine($"      \"{s.Key}\": {s.Value:F2}{comma}");
                }
                sb.AppendLine("    }");
                string itemComma = i < list.Count - 1 ? "," : "";
                sb.AppendLine($"  }}{itemComma}");
            }
            sb.AppendLine("]");
            return sb.ToString();
        }

        private static string Escape(string s) => s.Replace("\"", "\\\"").Replace("\n", " ");
    }
}
