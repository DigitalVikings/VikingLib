using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;
using VikingLib.Combat;
using VikingLib.Combat.Defense;
using VikingLib.Configuration;
using VikingLib.Patches;
using VikingLib.Stats;

namespace VikingLib.UI
{
    /// <summary>
    /// Injects a live 'Character Stats' topic into Hugin's Compendium (TextsDialog),
    /// providing real-time stat breakdowns and dual-space modifier auditing directly in-game.
    /// </summary>
    [HarmonyPatch]
    public static class CharacterStatsGlossaryPanel
    {
        public const string TopicName = "Character Stats";
        private static float _lastRefreshTime;
        private const float RefreshInterval = 0.5f;

        /// <summary>
        /// Delegate for downstream mods to provide formatted key-value pairs for a custom Compendium category.
        /// </summary>
        public delegate IEnumerable<(string Label, string FormattedValue)>? CustomGlossaryCategoryProvider(Player player);

        private sealed class PrioritizedCategoryProvider
        {
            public string CategoryTitle { get; }
            public int Priority { get; }
            public CustomGlossaryCategoryProvider Provider { get; }

            public PrioritizedCategoryProvider(string categoryTitle, int priority, CustomGlossaryCategoryProvider provider)
            {
                CategoryTitle = categoryTitle;
                Priority = priority;
                Provider = provider;
            }
        }

        private static readonly List<PrioritizedCategoryProvider> _customCategories = new();

        /// <summary>
        /// Registers a structured custom category for downstream mods to display in Hugin's Compendium under Character Stats.
        /// Applies sparse pruning (the category is omitted if the provider yields no entries).
        /// </summary>
        /// <param name="categoryTitle">Category header title (e.g. "Rested Rites", "Survival Vitals").</param>
        /// <param name="provider">Callback receiving Player and returning key-value rows.</param>
        /// <param name="priority">Ordering priority (lower numbers render earlier).</param>
        public static void RegisterCustomCategory(string categoryTitle, CustomGlossaryCategoryProvider provider, int priority = 100)
        {
            if (string.IsNullOrEmpty(categoryTitle) || provider == null) return;
            _customCategories.Add(new PrioritizedCategoryProvider(categoryTitle, priority, provider));
            _customCategories.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        /// <summary>
        /// Registers a custom category provider that returns pre-formatted lines directly.
        /// Empty categories are automatically sparse-pruned.
        /// </summary>
        public static void RegisterCustomCategory(string categoryTitle, Func<Player, IEnumerable<string>?> lineProvider, int priority = 100)
        {
            if (string.IsNullOrEmpty(categoryTitle) || lineProvider == null) return;
            RegisterCustomCategory(categoryTitle, player =>
            {
                var lines = lineProvider(player);
                if (lines == null) return null;
                var list = new List<(string Label, string FormattedValue)>();
                foreach (var line in lines)
                {
                    list.Add((line, string.Empty));
                }
                return list;
            }, priority);
        }

        /// <summary>
        /// Hook for downstream mods (e.g. VikingClimate) to inject dedicated formatted sections into Hugin's Compendium.
        /// </summary>
        public static event Action<Player, StringBuilder>? OnFormatCustomSections;

        [HarmonyPatch(typeof(TextsDialog), "UpdateTextsList")]
        [HarmonyPostfix]
        public static void PostfixUpdateTextsList(TextsDialog __instance)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.ShowStatsInGlossary.Value) return;

            var player = Player.m_localPlayer;
            string text = FormatCharacterStats(player);

            var info = new TextsDialog.TextInfo(TopicName, text);
            CharacterStatsHelper.GetTexts(__instance)?.Add(info);
        }

        [HarmonyPatch(typeof(TextsDialog), "ShowText", typeof(TextsDialog.TextInfo))]
        [HarmonyPrefix]
        public static void PrefixShowText(TextsDialog __instance, TextsDialog.TextInfo text)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.ShowStatsInGlossary.Value) return;
            if (text == null || text.m_topic != TopicName) return;

            var player = Player.m_localPlayer;
            string updatedText = FormatCharacterStats(player);
            text.m_text = updatedText;
            _lastRefreshTime = Time.unscaledTime;
        }

        [HarmonyPatch(typeof(TextsDialog), "Update")]
        [HarmonyPostfix]
        public static void PostfixUpdate(TextsDialog __instance)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.ShowStatsInGlossary.Value) return;
            if (!__instance.gameObject.activeInHierarchy) return;

            float interval = ModConfig.GlossaryRefreshInterval.Value;
            if (interval <= 0.1f) interval = 1.5f;

            if (Time.unscaledTime - _lastRefreshTime < interval) return;

            var texts = CharacterStatsHelper.GetTexts(__instance);
            if (texts == null) return;

            // Find if Character Stats is the currently viewed topic
            TextsDialog.TextInfo? selected = null;
            for (int i = 0; i < texts.Count; i++)
            {
                var item = texts[i];
                if (item != null && item.m_topic == TopicName)
                {
                    if (item.m_selected != null && item.m_selected.activeSelf)
                    {
                        selected = item;
                        break;
                    }
                    if (__instance.m_textAreaTopic != null && __instance.m_textAreaTopic.text == TopicName)
                    {
                        selected = item;
                        break;
                    }
                }
            }

            if (selected == null) return;

            _lastRefreshTime = Time.unscaledTime;

            var player = Player.m_localPlayer;
            string updatedText = FormatCharacterStats(player);

            if (selected.m_text != updatedText)
            {
                selected.m_text = updatedText;
                if (__instance.m_textArea != null)
                {
                    float scrollPos = __instance.m_rightScrollbar != null ? __instance.m_rightScrollbar.value : 1f;
                    __instance.m_textArea.text = updatedText;
                    if (__instance.m_rightScrollbar != null)
                    {
                        __instance.m_rightScrollbar.value = scrollPos;
                    }
                }
            }
        }

        public static string FormatCharacterStats(Player? player)
        {
            if (player == null) return "No character currently active.";

            var sb = new StringBuilder();
            sb.AppendLine("<size=125%><color=#FFCC00><b>CHARACTER OVERVIEW</b></color></size>\n");

            // --- VITALS & MAXIMUMS ---
            sb.AppendLine("<color=orange><b>[ MAXIMUM ATTRIBUTES ]</b></color>");
            AppendStatLine(sb, player, CharacterStatType.MaxHealth, "Max Health", "HP");
            AppendStatLine(sb, player, CharacterStatType.MaxStamina, "Max Stamina", "pts");
            AppendStatLine(sb, player, CharacterStatType.MaxEitr, "Max Eitr", "pts");
            AppendStatLine(sb, player, CharacterStatType.MaxCarryWeight, "Max Carry Weight", "kg");

            AppendStatLineIfModified(sb, player, CharacterStatType.FoodHealthEffectiveness, "Food Health Effectiveness", "+%", 1.0f);
            AppendStatLineIfModified(sb, player, CharacterStatType.FoodHealthRegenEffectiveness, "Food HP Regen Effectiveness", "+%", 1.0f);
            AppendStatLineIfModified(sb, player, CharacterStatType.FoodStaminaEffectiveness, "Food Stamina Effectiveness", "+%", 1.0f);
            AppendStatLineIfModified(sb, player, CharacterStatType.FoodEitrEffectiveness, "Food Eitr Effectiveness", "+%", 1.0f);
            AppendStatLineIfModified(sb, player, CharacterStatType.FoodDurationMultiplier, "Food Duration", "x", 1.0f);
            sb.AppendLine();

            // --- CONTINUOUS REGENERATION ---
            sb.AppendLine("<color=orange><b>[ REGENERATION & RECOVERY ]</b></color>");
            AppendStatLine(sb, player, CharacterStatType.HealthRegen, "Health Regeneration", "HP/s");
            AppendStatLine(sb, player, CharacterStatType.HealthRegenDelay, "Combat Health Regen Delay", "sec");
            float remaining = PlayerStatsPatch.GetRemainingRegenDelay(player);
            if (remaining > 0.05f)
            {
                sb.AppendLine($"    <color=#FF9933>└─ Status: Paused in combat ({remaining:F1}s remaining)</color>");
            }
            AppendStatLine(sb, player, CharacterStatType.StaminaRegen, "Stamina Regeneration", "pts/s");
            AppendStatLine(sb, player, CharacterStatType.EitrRegen, "Eitr Regeneration", "pts/s");
            AppendStatLine(sb, player, CharacterStatType.StaminaRegenDelay, "Stamina Recovery Delay", "sec");
            sb.AppendLine();

            // --- DEFENSE & RESISTANCES ---
            sb.AppendLine("<color=orange><b>[ DEFENSE & RESISTANCES ]</b></color>");
            AppendStatLine(sb, player, CharacterStatType.BodyArmor, "Body Armor", "pts");
            AppendStatLineIfModified(sb, player, CharacterStatType.ArmorEfficiency, "Armor Efficiency", "+%", 0.0f);
            AppendStatLineIfModified(sb, player, CharacterStatType.BluntArmorEfficiency, "Blunt Armor Efficiency", "+%", 0.0f);
            AppendStatLineIfModified(sb, player, CharacterStatType.SlashArmorEfficiency, "Slash Armor Efficiency", "+%", 0.0f);
            AppendStatLineIfModified(sb, player, CharacterStatType.PierceArmorEfficiency, "Pierce Armor Efficiency", "+%", 0.0f);

            var defense = EntityDefenseRegistry.GetDefense(player);
            var activeResistances = new List<(string Name, float Mult, string Label, List<string> Sources)>();

            DamageType[] combatDamageTypes = new[]
            {
                DamageType.Blunt, DamageType.Slash, DamageType.Pierce,
                DamageType.Fire, DamageType.Frost, DamageType.Lightning,
                DamageType.Poison
            };

            foreach (var dt in combatDamageTypes)
            {
                float baseMult = defense.GetResistanceForType(dt);
                float statMult = 1.0f;
                var resStat = CharacterStatManager.GetResistanceStat(dt);
                if (resStat.HasValue) statMult *= CharacterStatManager.GetStat(player, resStat.Value);
                if (dt.IsPhysical()) statMult *= CharacterStatManager.GetStat(player, CharacterStatType.ResistancePhysical);
                else if (dt.IsElemental()) statMult *= CharacterStatManager.GetStat(player, CharacterStatType.ResistanceElemental);

                float finalMult = baseMult * statMult;
                if (Mathf.Abs(finalMult - 1.0f) > 0.001f)
                {
                    var sources = GetResistanceSources(player, dt);
                    if (resStat.HasValue)
                    {
                        var trace = CharacterStatManager.GetTrace(player, resStat.Value);
                        foreach (var m in trace.Multipliers)
                        {
                            sources.Add($"{m.Source} (x{m.Value:F2})");
                        }
                    }
                    string label = EntityDefenseRegistry.ToVanillaModifier(finalMult).ToString();
                    activeResistances.Add((dt.ToString(), finalMult, label, sources));
                }
            }

            if (activeResistances.Count > 0)
            {
                sb.AppendLine("• <b>Damage Resistances</b>:");
                for (int r = 0; r < activeResistances.Count; r++)
                {
                    var res = activeResistances[r];
                    sb.AppendLine($"    <color=yellow>{res.Name} Taken</color>: {res.Mult:F2}x ({res.Label})");
                    for (int i = 0; i < res.Sources.Count; i++)
                    {
                        string prefix = (i == res.Sources.Count - 1) ? "        <color=#A0A0A0>└─ " : "        <color=#A0A0A0>├─ ";
                        sb.AppendLine($"{prefix}[Tier 3 Multiplier] {res.Sources[i]}</color>");
                    }
                }
            }

            AppendStatLineIfModified(sb, player, CharacterStatType.BlockPowerMultiplier, "Block Power Multiplier", "x", 1.0f);
            AppendStatLineIfModified(sb, player, CharacterStatType.ParryMultiplier, "Parry Bonus Multiplier", "x", 1.0f);
            AppendStatLineIfModified(sb, player, CharacterStatType.ParryWindow, "Parry Timing Window", "sec", 0.25f);
            AppendStatLineIfModified(sb, player, CharacterStatType.StaggerResistance, "Stagger Resistance", "x", 1.0f);
            sb.AppendLine();

            // --- OFFENSIVE PROFICIENCIES (Categorized & Sparse) ---
            var offensiveSb = new StringBuilder();

            // 1. Delivery & Stance
            var deliverySb = new StringBuilder();
            AppendStatLineIfModified(deliverySb, player, CharacterStatType.DamageMelee, "Melee Damage Bonus", "x", 1.0f, "    ");
            AppendStatLineIfModified(deliverySb, player, CharacterStatType.DamageOneHanded, "One-Handed Damage Bonus", "x", 1.0f, "    ");
            AppendStatLineIfModified(deliverySb, player, CharacterStatType.DamageTwoHanded, "Two-Handed Damage Bonus", "x", 1.0f, "    ");
            AppendStatLineIfModified(deliverySb, player, CharacterStatType.DamageRanged, "Ranged Damage Bonus", "x", 1.0f, "    ");
            AppendStatLineIfModified(deliverySb, player, CharacterStatType.DamageMagic, "Magic Damage Bonus", "x", 1.0f, "    ");
            if (deliverySb.Length > 0)
            {
                offensiveSb.AppendLine("  <color=#FFE082><b>[ Delivery & Stance ]</b></color>");
                offensiveSb.Append(deliverySb.ToString());
            }

            // 2. Attack Styles & Techniques
            var styleSb = new StringBuilder();
            AppendStatLineIfModified(styleSb, player, CharacterStatType.AttackSpeed, "Attack Speed", "x", 1.0f, "    ");
            AppendStatLineIfModified(styleSb, player, CharacterStatType.CriticalStrikeChance, "Critical Strike Chance", "%", ModConfig.DefaultPlayerCritChance.Value, "    ");
            AppendStatLineIfModified(styleSb, player, CharacterStatType.CriticalStrikeMultiplier, "Critical Strike Multiplier", "x", ModConfig.DefaultCritMultiplier.Value, "    ");
            AppendStatLineIfModified(styleSb, player, CharacterStatType.DamageSecondaryAttack, "Secondary Attack Damage", "x", 1.0f, "    ");
            AppendStatLineIfModified(styleSb, player, CharacterStatType.DamageComboFinisher, "Combo Finisher Damage", "x", 1.0f, "    ");
            AppendStatLineIfModified(styleSb, player, CharacterStatType.DamageSneak, "Sneak Attack Damage", "x", 1.0f, "    ");
            AppendStatLineIfModified(styleSb, player, CharacterStatType.CleaveEfficiency, "Cleave Efficiency", "x", 1.0f, "    ");
            AppendStatLineIfModified(styleSb, player, CharacterStatType.StaggerDamage, "Stagger Power", "x", 1.0f, "    ");
            AppendStatLineIfModified(styleSb, player, CharacterStatType.KnockbackDealt, "Knockback Force", "x", 1.0f, "    ");
            AppendStatLineIfModified(styleSb, player, CharacterStatType.MeleeReach, "Melee Reach", "x", 1.0f, "    ");
            AppendStatLineIfModified(styleSb, player, CharacterStatType.BowDrawSpeed, "Bow Draw Speed", "x", 1.0f, "    ");
            AppendStatLineIfModified(styleSb, player, CharacterStatType.CrossbowReloadSpeed, "Crossbow Reload Speed", "x", 1.0f, "    ");
            AppendStatLineIfModified(styleSb, player, CharacterStatType.ProjectilePrecision, "Projectile Precision Rating", "x", 1.0f, "    ");
            if (styleSb.Length > 0)
            {
                offensiveSb.AppendLine("  <color=#FFE082><b>[ Attack Styles & Techniques ]</b></color>");
                offensiveSb.Append(styleSb.ToString());
            }

            // 2b. Combat Resource Recovery (3x3 Cartesian Matrix)
            var procSb = new StringBuilder();
            AppendStatLineIfModified(procSb, player, CharacterStatType.HealthOnKill, "Health On Kill", "HP", 0.0f, "    ");
            AppendStatLineIfModified(procSb, player, CharacterStatType.StaminaOnKill, "Stamina On Kill", "pts", 0.0f, "    ");
            AppendStatLineIfModified(procSb, player, CharacterStatType.EitrOnKill, "Eitr On Kill", "pts", 0.0f, "    ");
            AppendStatLineIfModified(procSb, player, CharacterStatType.HealthOnDamageTaken, "Health On Damage Taken", "HP", 0.0f, "    ");
            AppendStatLineIfModified(procSb, player, CharacterStatType.StaminaOnDamageTaken, "Stamina On Damage Taken", "pts", 0.0f, "    ");
            AppendStatLineIfModified(procSb, player, CharacterStatType.EitrOnDamageTaken, "Eitr On Damage Taken", "pts", 0.0f, "    ");
            AppendStatLineIfModified(procSb, player, CharacterStatType.HealthOnCauseStagger, "Health On Cause Stagger", "HP", 0.0f, "    ");
            AppendStatLineIfModified(procSb, player, CharacterStatType.StaminaOnCauseStagger, "Stamina On Cause Stagger", "pts", 0.0f, "    ");
            AppendStatLineIfModified(procSb, player, CharacterStatType.EitrOnCauseStagger, "Eitr On Cause Stagger", "pts", 0.0f, "    ");
            if (procSb.Length > 0)
            {
                offensiveSb.AppendLine("  <color=#FFE082><b>[ Combat Resource Recovery ]</b></color>");
                offensiveSb.Append(procSb.ToString());
            }

            // 3. Situational & Target Contexts
            var situationalSb = new StringBuilder();
            AppendStatLineIfModified(situationalSb, player, CharacterStatType.DamageAgainstBosses, "Boss Damage Bonus", "x", 1.0f, "    ");
            AppendStatLineIfModified(situationalSb, player, CharacterStatType.DamageAgainstStaggered, "Stagger Punish Bonus", "x", 1.0f, "    ");
            AppendStatLineIfModified(situationalSb, player, CharacterStatType.DamageAgainstAirborne, "Airborne Damage Bonus", "x", 1.0f, "    ");
            AppendStatLineIfModified(situationalSb, player, CharacterStatType.DamageExecute, "Execute Damage (Target HP <= 35%)", "x", 1.0f, "    ");
            AppendStatLineIfModified(situationalSb, player, CharacterStatType.DamageFirstStrike, "First Strike (Target HP >= 90%)", "x", 1.0f, "    ");
            AppendStatLineIfModified(situationalSb, player, CharacterStatType.DamageBerserk, "Berserk Damage (Self HP <= 35%)", "x", 1.0f, "    ");
            AppendStatLineIfModified(situationalSb, player, CharacterStatType.DamageFlawless, "Flawless Damage (Self HP >= 90%)", "x", 1.0f, "    ");
            AppendStatLineIfModified(situationalSb, player, CharacterStatType.DamageAtNight, "Night Damage Bonus", "x", 1.0f, "    ");
            AppendStatLineIfModified(situationalSb, player, CharacterStatType.DamageWhileAirborne, "Self-Airborne Damage Bonus", "x", 1.0f, "    ");
            AppendStatLineIfModified(situationalSb, player, CharacterStatType.DamageAgainstAfflicted, "Afflicted Target Damage", "x", 1.0f, "    ");
            if (situationalSb.Length > 0)
            {
                offensiveSb.AppendLine("  <color=#FFE082><b>[ Situational & Target Contexts ]</b></color>");
                offensiveSb.Append(situationalSb.ToString());
            }

            // 4. Weapon Families
            var wepSb = new StringBuilder();
            AppendStatLineIfModified(wepSb, player, CharacterStatType.DamageSwords, "Swords Damage", "x", 1.0f, "    ");
            AppendStatLineIfModified(wepSb, player, CharacterStatType.AttackSpeedSwords, "Swords Attack Speed", "x", 1.0f, "    ");
            AppendStatLineIfModified(wepSb, player, CharacterStatType.DamageKnives, "Knives Damage", "x", 1.0f, "    ");
            AppendStatLineIfModified(wepSb, player, CharacterStatType.AttackSpeedKnives, "Knives Attack Speed", "x", 1.0f, "    ");
            AppendStatLineIfModified(wepSb, player, CharacterStatType.DamageClubs, "Clubs Damage", "x", 1.0f, "    ");
            AppendStatLineIfModified(wepSb, player, CharacterStatType.AttackSpeedClubs, "Clubs Attack Speed", "x", 1.0f, "    ");
            AppendStatLineIfModified(wepSb, player, CharacterStatType.DamagePolearms, "Polearms Damage", "x", 1.0f, "    ");
            AppendStatLineIfModified(wepSb, player, CharacterStatType.AttackSpeedPolearms, "Polearms Attack Speed", "x", 1.0f, "    ");
            AppendStatLineIfModified(wepSb, player, CharacterStatType.DamageSpears, "Spears Damage", "x", 1.0f, "    ");
            AppendStatLineIfModified(wepSb, player, CharacterStatType.AttackSpeedSpears, "Spears Attack Speed", "x", 1.0f, "    ");
            AppendStatLineIfModified(wepSb, player, CharacterStatType.DamageAxes, "Axes Damage", "x", 1.0f, "    ");
            AppendStatLineIfModified(wepSb, player, CharacterStatType.AttackSpeedAxes, "Axes Attack Speed", "x", 1.0f, "    ");
            AppendStatLineIfModified(wepSb, player, CharacterStatType.DamageBows, "Bows Damage", "x", 1.0f, "    ");
            AppendStatLineIfModified(wepSb, player, CharacterStatType.AttackSpeedBows, "Bows Attack Speed", "x", 1.0f, "    ");
            AppendStatLineIfModified(wepSb, player, CharacterStatType.DamageCrossbows, "Crossbows Damage", "x", 1.0f, "    ");
            AppendStatLineIfModified(wepSb, player, CharacterStatType.AttackSpeedCrossbows, "Crossbows Attack Speed", "x", 1.0f, "    ");
            AppendStatLineIfModified(wepSb, player, CharacterStatType.DamageUnarmed, "Unarmed & Fist Damage", "x", 1.0f, "    ");
            AppendStatLineIfModified(wepSb, player, CharacterStatType.AttackSpeedUnarmed, "Unarmed Attack Speed", "x", 1.0f, "    ");
            AppendStatLineIfModified(wepSb, player, CharacterStatType.DamageElementalMagic, "Elemental Magic Damage", "x", 1.0f, "    ");
            AppendStatLineIfModified(wepSb, player, CharacterStatType.AttackSpeedElementalMagic, "Elemental Magic Attack Speed", "x", 1.0f, "    ");
            AppendStatLineIfModified(wepSb, player, CharacterStatType.DamageBloodMagic, "Blood Magic Damage", "x", 1.0f, "    ");
            AppendStatLineIfModified(wepSb, player, CharacterStatType.AttackSpeedBloodMagic, "Blood Magic Attack Speed", "x", 1.0f, "    ");
            if (wepSb.Length > 0)
            {
                offensiveSb.AppendLine("  <color=#FFE082><b>[ Weapon Families ]</b></color>");
                offensiveSb.Append(wepSb.ToString());
            }

            // 5. Damage Types & Archetypes
            var typeSb = new StringBuilder();
            AppendStatLineIfModified(typeSb, player, CharacterStatType.DamagePhysical, "Physical Damage Bonus", "x", 1.0f, "    ");
            AppendStatLineIfModified(typeSb, player, CharacterStatType.DamageSlashing, "Slashing Damage", "x", 1.0f, "    ");
            AppendStatLineIfModified(typeSb, player, CharacterStatType.DamageBlunt, "Blunt Damage", "x", 1.0f, "    ");
            AppendStatLineIfModified(typeSb, player, CharacterStatType.DamagePiercing, "Piercing Damage", "x", 1.0f, "    ");
            AppendStatLineIfModified(typeSb, player, CharacterStatType.DamageElemental, "Elemental Damage Bonus", "x", 1.0f, "    ");
            AppendStatLineIfModified(typeSb, player, CharacterStatType.DamageFire, "Fire Damage", "x", 1.0f, "    ");
            AppendStatLineIfModified(typeSb, player, CharacterStatType.DamageFrost, "Frost Damage", "x", 1.0f, "    ");
            AppendStatLineIfModified(typeSb, player, CharacterStatType.DamageLightning, "Lightning Damage", "x", 1.0f, "    ");
            AppendStatLineIfModified(typeSb, player, CharacterStatType.DamagePoison, "Poison Damage", "x", 1.0f, "    ");
            AppendStatLineIfModified(typeSb, player, CharacterStatType.DamageSpirit, "Spirit Damage", "x", 1.0f, "    ");
            AppendStatLineIfModified(typeSb, player, CharacterStatType.DamageChop, "Chop Damage", "x", 1.0f, "    ");
            AppendStatLineIfModified(typeSb, player, CharacterStatType.DamagePickaxe, "Pickaxe Damage", "x", 1.0f, "    ");
            if (typeSb.Length > 0)
            {
                offensiveSb.AppendLine("  <color=#FFE082><b>[ Damage Types & Archetypes ]</b></color>");
                offensiveSb.Append(typeSb.ToString());
            }

            if (offensiveSb.Length > 0)
            {
                sb.AppendLine("<color=orange><b>[ OFFENSIVE PROFICIENCIES ]</b></color>");
                sb.Append(offensiveSb.ToString());
                sb.AppendLine();
            }

            // --- ACTION RESOURCE EFFICIENCIES (Categorized & Sparse) ---
            var effSb = new StringBuilder();

            // 1. Combat & Attack Costs
            var combatEffSb = new StringBuilder();
            AppendStatLineIfModified(combatEffSb, player, CharacterStatType.AttackStaminaEfficiency, "Attack Stamina Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(combatEffSb, player, CharacterStatType.MeleeStaminaEfficiency, "Melee Stamina Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(combatEffSb, player, CharacterStatType.OneHandedStaminaEfficiency, "One-Handed Stamina Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(combatEffSb, player, CharacterStatType.TwoHandedStaminaEfficiency, "Two-Handed Stamina Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(combatEffSb, player, CharacterStatType.RangedStaminaEfficiency, "Ranged Stamina Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(combatEffSb, player, CharacterStatType.SecondaryAttackStaminaEfficiency, "Secondary Attack Stamina Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(combatEffSb, player, CharacterStatType.AttackEitrEfficiency, "Attack Eitr Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(combatEffSb, player, CharacterStatType.AttackHealthEfficiency, "Attack Health Efficiency", "+%", 0.0f, "    ");

            // Per-Weapon Efficiencies
            AppendStatLineIfModified(combatEffSb, player, CharacterStatType.SwordsEfficiency, "Swords Stamina Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(combatEffSb, player, CharacterStatType.KnivesEfficiency, "Knives Stamina Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(combatEffSb, player, CharacterStatType.ClubsEfficiency, "Clubs Stamina Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(combatEffSb, player, CharacterStatType.PolearmsEfficiency, "Polearms Stamina Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(combatEffSb, player, CharacterStatType.SpearsEfficiency, "Spears Stamina Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(combatEffSb, player, CharacterStatType.AxesEfficiency, "Axes Stamina Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(combatEffSb, player, CharacterStatType.BowsEfficiency, "Bows Stamina Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(combatEffSb, player, CharacterStatType.CrossbowReloadStaminaEfficiency, "Crossbow Reload Stamina Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(combatEffSb, player, CharacterStatType.UnarmedEfficiency, "Unarmed Stamina Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(combatEffSb, player, CharacterStatType.ElementalMagicEitrEfficiency, "Elemental Magic Eitr Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(combatEffSb, player, CharacterStatType.BloodMagicHealthEfficiency, "Blood Magic Health Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(combatEffSb, player, CharacterStatType.BloodMagicEitrEfficiency, "Blood Magic Eitr Efficiency", "+%", 0.0f, "    ");

            if (combatEffSb.Length > 0)
            {
                effSb.AppendLine("  <color=#FFE082><b>[ Combat & Attack Resource Costs ]</b></color>");
                effSb.Append(combatEffSb.ToString());
            }

            // 2. Locomotion & World Activities
            var locoEffSb = new StringBuilder();
            AppendStatLineIfModified(locoEffSb, player, CharacterStatType.SprintEfficiency, "Sprint Stamina Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(locoEffSb, player, CharacterStatType.JumpEfficiency, "Jump Stamina Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(locoEffSb, player, CharacterStatType.DodgeEfficiency, "Dodge Stamina Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(locoEffSb, player, CharacterStatType.SneakEfficiency, "Sneak Stamina Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(locoEffSb, player, CharacterStatType.SwimEfficiency, "Swim Stamina Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(locoEffSb, player, CharacterStatType.BowDrawStaminaEfficiency, "Bow Hold Stamina Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(locoEffSb, player, CharacterStatType.BowDrawEitrEfficiency, "Staff Hold Eitr Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(locoEffSb, player, CharacterStatType.FishingStaminaEfficiency, "Fishing Reel Stamina Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(locoEffSb, player, CharacterStatType.BlockStaminaEfficiency, "Block Stamina Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(locoEffSb, player, CharacterStatType.RidingStaminaEfficiency, "Riding Stamina Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(locoEffSb, player, CharacterStatType.ToolStaminaEfficiency, "Tool Stamina Efficiency", "+%", 0.0f, "    ");
            AppendStatLineIfModified(locoEffSb, player, CharacterStatType.DurabilityEfficiency, "Durability Efficiency", "+%", 0.0f, "    ");

            if (locoEffSb.Length > 0)
            {
                effSb.AppendLine("  <color=#FFE082><b>[ Locomotion & World Activities ]</b></color>");
                effSb.Append(locoEffSb.ToString());
            }

            if (effSb.Length > 0)
            {
                sb.AppendLine("<color=orange><b>[ ACTION RESOURCE EFFICIENCIES ]</b></color>");
                sb.Append(effSb.ToString());
                sb.AppendLine();
            }

            // --- LOCOMOTION SPEEDS & IMPULSE ---
            sb.AppendLine("<color=orange><b>[ LOCOMOTION & SPEEDS ]</b></color>");
            AppendVelocityStatLine(sb, player, CharacterStatType.JogSpeed, "Jog Speed", player.m_speed);
            AppendVelocityStatLine(sb, player, CharacterStatType.RunSpeed, "Sprint Speed", player.m_runSpeed);
            AppendStatLine(sb, player, CharacterStatType.WalkSpeed, "Walk Speed", "m/s");
            AppendStatLine(sb, player, CharacterStatType.CrouchSpeed, "Crouch Speed", "m/s");
            AppendStatLine(sb, player, CharacterStatType.SwimSpeed, "Swim Speed", "m/s");
            AppendStatLineIfModified(sb, player, CharacterStatType.JumpHeight, "Jump Apex Height", "m", 1.7f);
            AppendStatLineIfModified(sb, player, CharacterStatType.Concealment, "Stealth Concealment Rating", "x", 1.0f);
            AppendStatLineIfModified(sb, player, CharacterStatType.RidingSpeed, "Mount Run Speed Factor", "x", 1.0f);
            AppendStatLineIfModified(sb, player, CharacterStatType.RidingControl, "Mount Steering Obedience", "%", 0.0f);
            AppendStatLineIfModified(sb, player, CharacterStatType.ShipSailSpeed, "Ship Sail Speed (Wind)", "x", 1.0f);
            AppendStatLineIfModified(sb, player, CharacterStatType.ShipRudderSpeed, "Ship Rudder Speed (Paddle)", "x", 1.0f);
            AppendStatLineIfModified(sb, player, CharacterStatType.CartWeightEfficiency, "Cart Weight Efficiency", "+%", 0.0f);
            sb.AppendLine();

            // --- GATHERING, CRAFTING & YIELDS ---
            var utilitySb = new StringBuilder();
            AppendStatLineIfModified(utilitySb, player, CharacterStatType.HarvestRadius, "Harvest & Agriculture Radius", "x", 1.0f);
            AppendStatLineIfModified(utilitySb, player, CharacterStatType.CraftingYield, "Crafting Total Yield", "%", 1.0f);
            AppendStatLineIfModified(utilitySb, player, CharacterStatType.CookingYield, "Cooking Total Yield", "%", 1.0f);
            AppendStatLineIfModified(utilitySb, player, CharacterStatType.ForagingYield, "Foraging Total Yield", "%", 1.0f);
            AppendStatLineIfModified(utilitySb, player, CharacterStatType.MiningYield, "Mining Mineral Yield", "%", 1.0f);
            AppendStatLineIfModified(utilitySb, player, CharacterStatType.WoodcuttingYield, "Woodcutting Timber Yield", "%", 1.0f);
            AppendStatLineIfModified(utilitySb, player, CharacterStatType.MonsterDropYield, "Monster Slay Loot Yield", "%", 1.0f);

            if (utilitySb.Length > 0)
            {
                sb.AppendLine("<color=orange><b>[ GATHERING, CRAFTING & YIELDS ]</b></color>");
                sb.Append(utilitySb.ToString());
                sb.AppendLine();
            }

            // --- CUSTOM & MODDED STATS ---
            var customStats = StatRegistry.GetAll();
            if (customStats.Count > 0)
            {
                var customSb = new StringBuilder();
                foreach (var custom in customStats)
                {
                    var trace = CharacterStatManager.GetTrace(player, custom.Key, custom.DefaultValue);
                    bool hasModifiers = trace.FlatModifiers.Count > 0 || trace.IncreasedModifiers.Count > 0 || trace.Multipliers.Count > 0;
                    if (hasModifiers || Mathf.Abs(trace.FinalValue - custom.DefaultValue) > 0.001f)
                    {
                        AppendStatLineWithTrace(customSb, trace, custom.Name, custom.Unit);
                    }
                }

                if (customSb.Length > 0)
                {
                    sb.AppendLine("<color=orange><b>[ CUSTOM & MODDED STATS ]</b></color>");
                    sb.Append(customSb.ToString());
                    sb.AppendLine();
                }
            }

            // Structured custom categories registered by downstream mods
            if (_customCategories.Count > 0)
            {
                for (int i = 0; i < _customCategories.Count; i++)
                {
                    var cat = _customCategories[i];
                    try
                    {
                        var rows = cat.Provider(player);
                        if (rows != null)
                        {
                            var catSb = new StringBuilder();
                            foreach (var (lbl, val) in rows)
                            {
                                if (!string.IsNullOrWhiteSpace(lbl))
                                {
                                    if (string.IsNullOrEmpty(val))
                                    {
                                        catSb.AppendLine($"  {lbl}");
                                    }
                                    else
                                    {
                                        catSb.AppendLine($"  {lbl}: <color=orange>{val}</color>");
                                    }
                                }
                            }

                            if (catSb.Length > 0)
                            {
                                sb.AppendLine($"<color=orange><b>[ {cat.CategoryTitle.ToUpperInvariant()} ]</b></color>");
                                sb.Append(catSb.ToString());
                                sb.AppendLine();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        VikingLibPlugin.Log?.LogWarning($"Error rendering custom glossary category '{cat.CategoryTitle}': {ex.Message}");
                    }
                }
            }

            // Downstream modular section hook (e.g. VikingClimate)
            try
            {
                OnFormatCustomSections?.Invoke(player, sb);
            }
            catch (Exception ex)
            {
                VikingLibPlugin.Log?.LogError($"Error in glossary custom sections hook: {ex}");
            }

            return sb.ToString();
        }

        private static void AppendVelocityStatLine(StringBuilder sb, Player player, CharacterStatType statType, string label, float baseVelocity)
        {
            var trace = CharacterStatManager.GetTrace(player, statType);
            float finalVelocity = baseVelocity * trace.FinalValue;
            bool hasModifiers = trace.FlatModifiers.Count > 0 || trace.IncreasedModifiers.Count > 0 || trace.Multipliers.Count > 0;

            if (!hasModifiers)
            {
                sb.AppendLine($"• <b>{label}</b>: <color=yellow>{finalVelocity:F1} m/s</color>");
            }
            else
            {
                sb.AppendLine($"• <b>{label}</b>: <color=yellow>{finalVelocity:F1} m/s</color> <i>(Base: {baseVelocity:F1} m/s)</i>");

                var lines = new List<string>();
                foreach (var (src, val) in trace.FlatModifiers)
                {
                    string sign = val >= 0 ? "+" : "";
                    lines.Add($"[Tier 1 Flat] {src}: {sign}{val:F1}");
                }

                foreach (var (src, val) in trace.IncreasedModifiers)
                {
                    string sign = val >= 0 ? "+" : "";
                    lines.Add($"[Tier 2 Additive] {src}: {sign}{val * 100f:F1}%");
                }

                foreach (var (src, val) in trace.Multipliers)
                {
                    lines.Add($"[Tier 3 Multiplier] {src}: x{val:F2}");
                }

                for (int i = 0; i < lines.Count; i++)
                {
                    string prefix = (i == lines.Count - 1) ? "    <color=#A0A0A0>└─ " : "    <color=#A0A0A0>├─ ";
                    sb.AppendLine($"{prefix}{lines[i]}</color>");
                }
            }
        }

        private static void AppendStatLine(StringBuilder sb, Player player, CharacterStatType statType, string label, string unit)
        {
            var trace = CharacterStatManager.GetTrace(player, statType);
            AppendStatLineWithTrace(sb, trace, label, unit);
        }

        private static void AppendStatLineIfModified(StringBuilder sb, Player player, CharacterStatType statType, string label, string unit, float baseline, string indent = "")
        {
            var trace = CharacterStatManager.GetTrace(player, statType);
            bool hasModifiers = trace.FlatModifiers.Count > 0 || trace.IncreasedModifiers.Count > 0 || trace.Multipliers.Count > 0;
            if (!hasModifiers && Mathf.Abs(trace.FinalValue - baseline) < 0.001f)
            {
                return;
            }

            AppendStatLineWithTrace(sb, trace, label, unit, indent);
        }

        private static void AppendStatLineWithTrace(StringBuilder sb, StatTrace trace, string label, string unit, string indent = "")
        {
            float finalVal = trace.FinalValue;
            bool isEfficiency = IsEfficiencyStat(trace.StatType);

            string valueStr = unit switch
            {
                "x" => $"{finalVal:F2}x",
                "%" => $"{finalVal * 100f:F0}%",
                "+%" => $"{((isEfficiency ? finalVal : (finalVal - 1f)) * 100f):+0.0;-0.0;0}%",
                "m" => $"{finalVal:F2}m",
                _ => string.IsNullOrEmpty(unit) ? $"{finalVal:F1}" : $"{finalVal:F1} {unit}"
            };

            bool hasModifiers = trace.FlatModifiers.Count > 0 || trace.IncreasedModifiers.Count > 0 || trace.Multipliers.Count > 0;

            if (!hasModifiers)
            {
                sb.AppendLine($"{indent}• <b>{label}</b>: <color=yellow>{valueStr}</color>");
            }
            else
            {
                string baseStr = unit switch
                {
                    "x" => $"{trace.BaseValue:F2}x",
                    "%" => $"{trace.BaseValue * 100f:F0}%",
                    "+%" => "0%",
                    "m" => $"{trace.BaseValue:F2}m",
                    _ => string.IsNullOrEmpty(unit) ? $"{trace.BaseValue:F1}" : $"{trace.BaseValue:F1} {unit}"
                };

                sb.AppendLine($"{indent}• <b>{label}</b>: <color=yellow>{valueStr}</color> <i>(Base: {baseStr})</i>");

                var lines = new List<string>();
                foreach (var (src, val) in trace.FlatModifiers)
                {
                    string sign = val >= 0 ? "+" : "";
                    lines.Add($"[Tier 1 Flat] {src}: {sign}{val:F1}");
                }

                foreach (var (src, val) in trace.IncreasedModifiers)
                {
                    string sign = val >= 0 ? "+" : "";
                    if (isEfficiency)
                    {
                        lines.Add($"[Tier 2 Efficiency] {src}: {sign}{val * 100f:F1}%");
                    }
                    else
                    {
                        lines.Add($"[Tier 2 Additive] {src}: {sign}{val * 100f:F1}%");
                    }
                }

                foreach (var (src, val) in trace.Multipliers)
                {
                    lines.Add($"[Tier 3 Multiplier] {src}: x{val:F2}");
                }

                for (int i = 0; i < lines.Count; i++)
                {
                    string prefix = (i == lines.Count - 1) ? $"{indent}    <color=#A0A0A0>└─ " : $"{indent}    <color=#A0A0A0>├─ ";
                    sb.AppendLine($"{prefix}{lines[i]}</color>");
                }
            }
        }

        private static bool IsEfficiencyStat(CharacterStatType statType) => CharacterStatManager.IsEfficiencyStat(statType);

        private static List<string> GetResistanceSources(Player player, DamageType type)
        {
            var sources = new List<string>();
            var hitType = ToHitDataType(type);

            var equipped = CharacterStatsHelper.GetEquippedItems(player);
            for (int i = 0; i < equipped.Count; i++)
            {
                var item = equipped[i];
                if (item?.m_shared?.m_damageModifiers == null) continue;
                foreach (var mod in item.m_shared.m_damageModifiers)
                {
                    if (mod.m_type == hitType && mod.m_modifier != HitData.DamageModifier.Normal)
                    {
                        string name = item.m_shared.m_name ?? "Gear";
                        if (name.StartsWith("$") && Localization.instance != null)
                        {
                            name = Localization.instance.Localize(name);
                        }
                        float fMult = EntityDefenseRegistry.ToFloatMultiplier(mod.m_modifier);
                        sources.Add($"{name}: x{fMult:F2} ({mod.m_modifier})");
                    }
                }
            }

            var seMan = player.GetSEMan();
            if (seMan != null)
            {
                var effects = seMan.GetStatusEffects();
                for (int i = 0; i < effects.Count; i++)
                {
                    var se = effects[i];
                    if (se is SE_Stats seStats && seStats.m_mods != null)
                    {
                        foreach (var mod in seStats.m_mods)
                        {
                            if (mod.m_type == hitType && mod.m_modifier != HitData.DamageModifier.Normal)
                            {
                                string name = seStats.m_name;
                                if (string.IsNullOrEmpty(name)) name = seStats.name;
                                if (name.StartsWith("$") && Localization.instance != null)
                                {
                                    name = Localization.instance.Localize(name);
                                }
                                float fMult = EntityDefenseRegistry.ToFloatMultiplier(mod.m_modifier);
                                sources.Add($"{name}: x{fMult:F2} ({mod.m_modifier})");
                            }
                        }
                    }
                }
            }

            if (sources.Count == 0)
            {
                sources.Add("Innate Trait");
            }

            return sources;
        }

        private static HitData.DamageType ToHitDataType(DamageType type) => type switch
        {
            DamageType.Blunt => HitData.DamageType.Blunt,
            DamageType.Slash => HitData.DamageType.Slash,
            DamageType.Pierce => HitData.DamageType.Pierce,
            DamageType.Chop => HitData.DamageType.Chop,
            DamageType.Pickaxe => HitData.DamageType.Pickaxe,
            DamageType.Fire => HitData.DamageType.Fire,
            DamageType.Frost => HitData.DamageType.Frost,
            DamageType.Lightning => HitData.DamageType.Lightning,
            DamageType.Poison => HitData.DamageType.Poison,
            DamageType.Spirit => HitData.DamageType.Spirit,
            _ => HitData.DamageType.Physical
        };
    }
}
