using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using VikingLib.Combat;
using VikingLib.Combat.Attacks;
using VikingLib.Configuration;
using VikingLib.Stats;

namespace VikingLib.UI.Tooltip
{
    /// <summary>
    /// Extensible engine for generating and customizing weapon tooltips.
    /// Provides a clean, compact Base Tooltip at-a-glance, and an expandable
    /// Tabular Attack Matrix ("Combat Dossier") with common combat properties.
    /// </summary>
    public static class TooltipEngine
    {
        /// <summary>
        /// Delegate for downstream mods to append custom badge rows or trait descriptions to weapon tooltips.
        /// </summary>
        public delegate IEnumerable<string>? CustomTooltipLineProvider(ItemTooltipContext context);

        private sealed class PrioritizedLineProvider
        {
            public int Priority { get; }
            public CustomTooltipLineProvider Provider { get; }

            public PrioritizedLineProvider(int priority, CustomTooltipLineProvider provider)
            {
                Priority = priority;
                Provider = provider;
            }
        }

        private static readonly List<ITooltipSection> _sections = new();
        private static readonly List<PrioritizedLineProvider> _customLineProviders = new();

        public static void RegisterSection(ITooltipSection section)
        {
            if (section == null) return;
            _sections.Add(section);
            _sections.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        /// <summary>
        /// Registers a callback for downstream mods to inject custom formatted lines or trait badges into weapon tooltips.
        /// </summary>
        /// <param name="provider">Callback receiving ItemTooltipContext and returning lines of text.</param>
        /// <param name="priority">Ordering priority (lower numbers render earlier).</param>
        public static void RegisterLineProvider(CustomTooltipLineProvider provider, int priority = 100)
        {
            if (provider == null) return;
            _customLineProviders.Add(new PrioritizedLineProvider(priority, provider));
            _customLineProviders.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        /// <summary>
        /// Registers a callback for downstream mods to inject custom formatted lines or trait badges into weapon tooltips.
        /// </summary>
        /// <param name="provider">Callback receiving item and player, returning lines of text.</param>
        /// <param name="priority">Ordering priority (lower numbers render earlier).</param>
        public static void RegisterLineProvider(Func<ItemDrop.ItemData?, Player?, IEnumerable<string>?> provider, int priority = 100)
        {
            if (provider == null) return;
            RegisterLineProvider(ctx => provider(ctx.Item, ctx.Player), priority);
        }

        /// <summary>
        /// Determines whether an item is a weapon or attacking tool eligible for custom tooltips.
        /// Pure shields are explicitly excluded so their primary defensive stats remain vanilla.
        /// </summary>
        public static bool IsWeaponOrTool(ItemDrop.ItemData? item)
        {
            if (item?.m_shared == null) return false;

            // Pure shields are handled via native shield format
            if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield) return false;

            // Weapons
            if (item.m_shared.m_itemType is ItemDrop.ItemData.ItemType.OneHandedWeapon
                or ItemDrop.ItemData.ItemType.TwoHandedWeapon
                or ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft
                or ItemDrop.ItemData.ItemType.Bow)
            {
                return true;
            }

            // Harvesting tools with attacks or damage
            if (item.m_shared.m_itemType is ItemDrop.ItemData.ItemType.Tool or ItemDrop.ItemData.ItemType.Torch)
            {
                if (item.m_shared.m_attack != null &&
                    (item.m_shared.m_damages.GetTotalDamage() > 0 ||
                     item.m_shared.m_damages.m_chop > 0 ||
                     item.m_shared.m_damages.m_pickaxe > 0))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Determines whether the extended combat dossier is currently active (Hold or Toggle).
        /// </summary>
        public static bool IsExpanded()
        {
            if (ModConfig.TooltipExpandBehavior.Value == TooltipExpandBehavior.Toggle)
            {
                return ModConfig.IsTooltipToggleActive;
            }
            return Input.GetKey(ModConfig.TooltipExpandKey.Value);
        }

        /// <summary>
        /// Generates the complete tooltip for a weapon or tool, switching between
        /// compact base view and the full extended combat dossier.
        /// </summary>
        public static string FormatFullTooltip(ItemDrop.ItemData item, int qualityLevel, bool crafting, float worldLevel)
        {
            if (item?.m_shared == null) return "";

            return IsExpanded()
                ? FormatExtendedCombatDossier(item, qualityLevel, crafting, worldLevel)
                : FormatBaseWeaponTooltip(item, qualityLevel, crafting, worldLevel);
        }

        /// <summary>
        /// Compact, clean base tooltip: identity, utility, primary_1 practical damage, and expand hint.
        /// Secondary stats (block, parry, knockback, backstab, stamina) are relegated to the extended view.
        /// </summary>
        private static string FormatBaseWeaponTooltip(ItemDrop.ItemData item, int qualityLevel, bool crafting, float worldLevel)
        {
            var sb = new StringBuilder();

            // 1. Description / flavor text
            string desc = item.m_shared.m_description;
            if (!string.IsNullOrEmpty(desc))
            {
                sb.Append(LocalizeSafe(desc));
                sb.Append("\n"); // Blank line before stats
            }

            // 2. Quality
            if (item.m_shared.m_maxQuality > 1)
            {
                sb.Append($"\n$item_quality: <color=orange>{qualityLevel}</color>");
            }

            // 4. Weight
            sb.Append($"\n$item_weight: <color=orange>{item.GetWeight():0.0}</color>");

            // 5. Crafter Name
            if (!string.IsNullOrEmpty(item.m_crafterName))
            {
                sb.Append($"\n$item_crafter: <color=orange>{item.m_crafterName}</color>");
            }

            // 6. Durability
            float maxDurability = item.GetMaxDurability(qualityLevel);
            if (maxDurability > 0f)
            {
                if (crafting)
                {
                    sb.Append($"\n$item_durability: <color=orange>{maxDurability:0}</color>");
                }
                else
                {
                    sb.Append($"\n$item_durability: <color=orange>{item.m_durability:0}/{maxDurability:0}</color>");
                }
            }

            // 7. Repair Station Level
            if (item.m_shared.m_canBeReparied && !crafting && ObjectDB.instance != null)
            {
                var recipe = ObjectDB.instance.GetRecipe(item);
                if (recipe != null && recipe.m_minStationLevel > 0)
                {
                    sb.Append($"\n$item_repairlevel: <color=orange>{recipe.m_minStationLevel}</color>");
                }
            }

            // 6. Movement Speed Modifier
            if (Mathf.Abs(item.m_shared.m_movementModifier) > 0.001f)
            {
                string moveStr = (item.m_shared.m_movementModifier * 100f).ToString("+0;-0");
                sb.Append($"\n$item_movement_modifier: <color=orange>{moveStr}%</color>");
            }

            // 7. Equip Status Effect
            if (item.m_shared.m_equipStatusEffect != null)
            {
                sb.Append($"\n{item.m_shared.m_equipStatusEffect.GetTooltipString()}");
            }

            // 8. Primary Damage (evaluated practical range for primary_1)
            Character? attacker = GetLocalPlayerSafe();
            var baseDamages = item.GetDamage(qualityLevel, worldLevel);
            DamagePipeline.EvaluatePreviewDamages(
                attacker,
                item,
                baseDamages,
                item.m_shared.m_skillType,
                out var ranges,
                out _,
                out _,
                stepKey: "primary_1"
            );

            var combatTypes = ranges
                .Where(kvp => kvp.Value.Min > 0.01f || kvp.Value.Max > 0.01f)
                .ToList();

            foreach (var kvp in combatTypes)
            {
                string rangeStr = FormatRange(kvp.Value.Min, kvp.Value.Max);
                string typeLabel = GetLocalizedDamageType(kvp.Key);
                string typeColor = GetDamageTypeColor(kvp.Key);
                sb.Append($"\n{typeLabel}: <color={typeColor}>{rangeStr}</color>");

                if (ModConfig.StaggerOverhaulEnabled.Value)
                {
                    if (kvp.Key == DamageType.Slash)
                    {
                        float slashBleedPercent = ModConfig.SlashBleedPercent.Value;
                        if (attacker != null)
                        {
                            slashBleedPercent += CharacterStatManager.GetStat(attacker, CharacterStatType.SlashBleedPercent);
                        }

                        if (slashBleedPercent > 0.001f)
                        {
                            float minBleed = kvp.Value.Min * slashBleedPercent;
                            float maxBleed = kvp.Value.Max * slashBleedPercent;
                            string bleedStr = FormatRange(minBleed, maxBleed);
                            sb.Append($"\n    <color=#B91C1C>Bleed: +{bleedStr}</color>");
                        }
                    }
                    else if (kvp.Key == DamageType.Blunt)
                    {
                        float stagMult = item.m_shared.m_attack?.m_staggerMultiplier ?? 1.0f;
                        if (attacker != null)
                        {
                            stagMult *= CharacterStatManager.GetStat(attacker, CharacterStatType.StaggerDamage);
                        }

                        float minStag = kvp.Value.Min * stagMult;
                        float maxStag = kvp.Value.Max * stagMult;
                        string stagStr = FormatRange(minStag, maxStag);
                        sb.Append($"\n    <color=#F59E0B>Stagger: {stagStr}</color>");
                    }
                }
            }

            // Expand hint
            string keyLabel = GetKeyLabel(ModConfig.TooltipExpandKey.Value);
            string actionWord = ModConfig.TooltipExpandBehavior.Value == TooltipExpandBehavior.Toggle ? "Press" : "Hold";
            sb.Append($"\n\n<color=#888888>[{keyLabel}] {actionWord}: Combat Details</color>");

            // Custom section extensions
            var ctx = new ItemTooltipContext(item, qualityLevel, crafting, worldLevel, GetLocalPlayerSafe());
            foreach (var sec in _sections)
            {
                if (sec.ShouldRender(ctx))
                {
                    string secText = sec.Render(ctx);
                    if (!string.IsNullOrEmpty(secText))
                    {
                        sb.Append($"\n{secText}");
                    }
                }
            }

            return LocalizeSafe(sb.ToString());
        }

        private class AttackCardData
        {
            public string Title = "";
            public string StepKey = "";
            public Attack? NativeAttack;
            public float Cleave;
            public float Stamina;
            public float Eitr;
            public int Knockback;
            public float StaggerMultiplier;
            public string? OnHitStatusEffect;
            public float StatusEffectDuration;
            public Dictionary<DamageType, (float Min, float Max)> StepRanges = new();
        }

        /// <summary>
        /// Extended Combat Dossier (Replace Mode): Hides non-combat clutter to save vertical space.
        /// Displays Common Properties based on the primary_1 baseline at the top.
        /// Any additional attack profiles are rendered strictly as overrides.
        /// If there is only one attack profile, the attack matrix is omitted entirely.
        /// </summary>
        private static string FormatExtendedCombatDossier(ItemDrop.ItemData item, int qualityLevel, bool crafting, float worldLevel)
        {
            var sb = new StringBuilder();
            Character? attacker = GetLocalPlayerSafe();
            var profile = WeaponAttackRegistry.GetProfile(item);
            var baseDamages = item.GetDamage(qualityLevel, worldLevel);

            // Pre-evaluate all attack cards
            int chainLength = Math.Max(1, profile.PrimaryChainLength);
            var activeDamageTypes = GetActiveCombatTypes(baseDamages);
            var groups = GroupPrimarySteps(profile, chainLength, activeDamageTypes);

            var cards = new List<AttackCardData>();
            foreach (var grp in groups)
            {
                string stepKey = $"primary_{grp.StartStep}";
                string title;
                if (grp.StartStep == grp.EndStep)
                {
                    title = (chainLength == 1)
                        ? "Primary Attack"
                        : (grp.StartStep == chainLength ? $"Hit {grp.StartStep} (Combo Finisher)" : $"Hit {grp.StartStep}");
                }
                else
                {
                    title = $"Hit {grp.StartStep} - {grp.EndStep}";
                }

                DamagePipeline.EvaluatePreviewDamages(
                    attacker,
                    item,
                    baseDamages,
                    item.m_shared.m_skillType,
                    out var stepRanges,
                    out _,
                    out _,
                    stepKey: stepKey);

                var nat = item.m_shared.m_attack;
                float forceMult = (nat != null && nat.m_forceMultiplier > 0.01f) ? nat.m_forceMultiplier : 1.0f;
                float stagMult = (nat != null && nat.m_staggerMultiplier > 0.01f) ? nat.m_staggerMultiplier : 1.0f;

                float baseStam = nat?.m_attackStamina ?? 0f;
                float effStam = (baseStam > 0f && attacker != null)
                    ? CharacterStatManager.GetEffectiveWeaponAttackCost(baseStam, attacker, item, isSecondaryAttack: false, CharacterStatType.AttackStaminaEfficiency)
                    : baseStam;

                float baseEitr = nat?.m_attackEitr ?? 0f;
                float effEitr = (baseEitr > 0f && attacker != null)
                    ? CharacterStatManager.GetEffectiveWeaponAttackCost(baseEitr, attacker, item, isSecondaryAttack: false, CharacterStatType.AttackEitrEfficiency)
                    : baseEitr;

                var pStep = profile.GetStep(stepKey);
                cards.Add(new AttackCardData
                {
                    Title = title,
                    StepKey = stepKey,
                    NativeAttack = nat,
                    Cleave = pStep.Cleave,
                    Stamina = effStam,
                    Eitr = effEitr,
                    Knockback = Mathf.RoundToInt(item.m_shared.m_attackForce * forceMult),
                    StaggerMultiplier = stagMult,
                    OnHitStatusEffect = pStep.OnHitStatusEffect,
                    StatusEffectDuration = pStep.StatusEffectDuration,
                    StepRanges = stepRanges
                });
            }

            if (profile.HasSecondary && item.HaveSecondaryAttack())
            {
                DamagePipeline.EvaluatePreviewDamages(
                    attacker,
                    item,
                    baseDamages,
                    item.m_shared.m_skillType,
                    out var secRanges,
                    out _,
                    out _,
                    stepKey: "secondary");

                var nat = item.m_shared.m_secondaryAttack;
                float forceMult = nat.m_forceMultiplier > 0.01f ? nat.m_forceMultiplier : 1.0f;
                float stagMult = nat.m_staggerMultiplier > 0.01f ? nat.m_staggerMultiplier : 1.0f;

                float secBaseStam = nat.m_attackStamina;
                float effSecStam = (secBaseStam > 0f && attacker != null)
                    ? CharacterStatManager.GetEffectiveWeaponAttackCost(secBaseStam, attacker, item, isSecondaryAttack: true, CharacterStatType.AttackStaminaEfficiency)
                    : secBaseStam;

                float secBaseEitr = nat.m_attackEitr;
                float effSecEitr = (secBaseEitr > 0f && attacker != null)
                    ? CharacterStatManager.GetEffectiveWeaponAttackCost(secBaseEitr, attacker, item, isSecondaryAttack: true, CharacterStatType.AttackEitrEfficiency)
                    : secBaseEitr;

                var sStep = profile.GetStep("secondary");
                cards.Add(new AttackCardData
                {
                    Title = "Secondary Attack",
                    StepKey = "secondary",
                    NativeAttack = nat,
                    Cleave = sStep.Cleave,
                    Stamina = effSecStam,
                    Eitr = effSecEitr,
                    Knockback = Mathf.RoundToInt(item.m_shared.m_attackForce * forceMult),
                    StaggerMultiplier = stagMult,
                    OnHitStatusEffect = sStep.OnHitStatusEffect,
                    StatusEffectDuration = sStep.StatusEffectDuration,
                    StepRanges = secRanges
                });
            }

            var baseline = cards[0]; // primary_1 baseline

            // 1. Common Properties (Adheres strictly to primary_1)
            sb.Append("<size=85%>");
            sb.Append("<color=#FFAA00><b>Common Properties:</b></color>");

            // Handedness at the top of Common Properties
            bool isTwoHanded = item.m_shared.m_itemType is ItemDrop.ItemData.ItemType.TwoHandedWeapon
                or ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft
                or ItemDrop.ItemData.ItemType.Bow;
            string handStr = isTwoHanded ? "$item_twohanded" : "$item_onehanded";
            sb.Append($"\nType: <color=#C0C0C0>{handStr}</color>");

            float blockArmor = item.GetBlockPower(qualityLevel);
            float parryBonus = item.m_shared.m_timedBlockBonus;
            float effectiveParry = blockArmor * parryBonus;
            float backstab = item.m_shared.m_backstabBonus;

            if (blockArmor > 0f)
            {
                sb.Append($"\n$item_blockarmor: <color=orange>{blockArmor:0}</color>");
                if (parryBonus > 1.0f)
                {
                    sb.Append($"\n$item_parrybonus: <color=orange>{parryBonus:F1}x</color> (<color=yellow>{effectiveParry:0}</color>)");
                }
            }

            if (backstab > 1.0f)
            {
                float ambushBonus = backstab / 2.0f;
                sb.Append($"\n$item_backstab: <color=orange>{ambushBonus:F1}x</color>");
            }


            if (item.m_shared.m_equipStatusEffect != null)
            {
                sb.Append($"\nBuff: <color=orange>{item.m_shared.m_equipStatusEffect.m_name}</color>");
            }

            if (!string.IsNullOrEmpty(baseline.OnHitStatusEffect))
            {
                var def = StatusEffects.StatusEffectRegistry.GetDefinition(baseline.OnHitStatusEffect);
                string seName = !string.IsNullOrEmpty(def?.DisplayName) ? def.DisplayName : baseline.OnHitStatusEffect;
                sb.Append($"\nInflicts: <color=yellow>{seName}</color> ({baseline.StatusEffectDuration:0.#}s)");
            }

            // Damage (baseline primary_1)
            var baselineCombatRanges = baseline.StepRanges
                .Where(kvp => kvp.Value.Min > 0.01f || kvp.Value.Max > 0.01f)
                .ToList();

            if (baselineCombatRanges.Count > 0)
            {
                string dmgLabel = chainLength > 1
                    ? (groups[0].EndStep > 1 ? $"Damage (Hit 1 - {groups[0].EndStep})" : "Damage (Hit 1)")
                    : "Damage";

                if (baselineCombatRanges.Count == 1)
                {
                    var kvp = baselineCombatRanges[0];
                    string rangeStr = FormatRange(kvp.Value.Min, kvp.Value.Max);
                    string typeLabel = GetLocalizedDamageType(kvp.Key);
                    string typeColor = GetDamageTypeColor(kvp.Key);
                    sb.Append($"\n{dmgLabel}: <color={typeColor}>{rangeStr}</color> ({typeLabel})");

                    if (ModConfig.StaggerOverhaulEnabled.Value)
                    {
                        if (kvp.Key == DamageType.Slash)
                        {
                            float bleedPct = ModConfig.SlashBleedPercent.Value + (attacker != null ? CharacterStatManager.GetStat(attacker, CharacterStatType.SlashBleedPercent) : 0f);
                            if (bleedPct > 0.001f)
                            {
                                string bleedStr = FormatRange(kvp.Value.Min * bleedPct, kvp.Value.Max * bleedPct);
                                sb.Append($"\n    <color=#B91C1C>Bleed: +{bleedStr}</color>");
                            }
                        }
                        else if (kvp.Key == DamageType.Blunt)
                        {
                            float baseStag = item.m_shared.m_attack?.m_staggerMultiplier ?? 1.0f;
                            float stagMult = baseStag * (attacker != null ? CharacterStatManager.GetStat(attacker, CharacterStatType.StaggerDamage) : 1f);
                            string stagStr = FormatRange(kvp.Value.Min * stagMult, kvp.Value.Max * stagMult);
                            sb.Append($"\n    <color=#F59E0B>Stagger: {stagStr}</color>");
                        }
                    }
                }
                else
                {
                    sb.Append($"\n{dmgLabel}:");
                    foreach (var kvp in baselineCombatRanges)
                    {
                        string rangeStr = FormatRange(kvp.Value.Min, kvp.Value.Max);
                        string typeLabel = GetLocalizedDamageType(kvp.Key);
                        string typeColor = GetDamageTypeColor(kvp.Key);
                        sb.Append($"\n  {typeLabel}: <color={typeColor}>{rangeStr}</color>");

                        if (ModConfig.StaggerOverhaulEnabled.Value)
                        {
                            if (kvp.Key == DamageType.Slash)
                            {
                                float bleedPct = ModConfig.SlashBleedPercent.Value + (attacker != null ? CharacterStatManager.GetStat(attacker, CharacterStatType.SlashBleedPercent) : 0f);
                                if (bleedPct > 0.001f)
                                {
                                    string bleedStr = FormatRange(kvp.Value.Min * bleedPct, kvp.Value.Max * bleedPct);
                                    sb.Append($"\n    <color=#B91C1C>Bleed: +{bleedStr}</color>");
                                }
                            }
                            else if (kvp.Key == DamageType.Blunt)
                            {
                                float baseStag = item.m_shared.m_attack?.m_staggerMultiplier ?? 1.0f;
                                float stagMult = baseStag * (attacker != null ? CharacterStatManager.GetStat(attacker, CharacterStatType.StaggerDamage) : 1f);
                                string stagStr = FormatRange(kvp.Value.Min * stagMult, kvp.Value.Max * stagMult);
                                sb.Append($"\n    <color=#F59E0B>Stagger: {stagStr}</color>");
                            }
                        }
                    }
                }
            }

            // Bow properties if bow
            if (item.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow && item.m_shared.m_attack != null)
            {
                var nat = item.m_shared.m_attack;
                if (nat.m_drawDurationMin > 0f)
                {
                    float speed = attacker != null ? CharacterStatManager.GetStat(attacker, CharacterStatType.BowDrawSpeed) : 1f;
                    float drawDuration = nat.m_drawDurationMin / Mathf.Max(0.01f, speed);
                    sb.Append($"\nDraw Time: <color=yellow>{drawDuration:0.0}s</color>");
                }
                if (nat.m_drawStaminaDrain > 0f)
                {
                    float holdDrain = attacker != null
                        ? CharacterStatManager.GetEffectiveActionCost(nat.m_drawStaminaDrain, attacker, CharacterStatType.BowDrawStaminaEfficiency)
                        : nat.m_drawStaminaDrain;
                    sb.Append($"\nHold Stamina: <color=yellow>{holdDrain:0}/s</color>");
                }
                if (nat.m_projectileVel > 0f)
                {
                    sb.Append($"\nVelocity: <color=yellow>{nat.m_projectileVel:0}</color>");
                }
            }

            // Crossbow / reload properties
            if (item.m_shared.m_attack?.m_requiresReload == true)
            {
                var nat = item.m_shared.m_attack;
                if (nat.m_reloadTime > 0f)
                {
                    float speed = attacker != null ? CharacterStatManager.GetStat(attacker, CharacterStatType.CrossbowReloadSpeed) : 1f;
                    float reloadDuration = nat.m_reloadTime / Mathf.Max(0.01f, speed);
                    sb.Append($"\nReload Time: <color=yellow>{reloadDuration:0.0}s</color>");
                }
                if (nat.m_reloadStaminaDrain > 0f)
                {
                    float drain = attacker != null
                        ? CharacterStatManager.GetEffectiveActionCost(nat.m_reloadStaminaDrain, attacker, CharacterStatType.CrossbowReloadStaminaEfficiency)
                        : nat.m_reloadStaminaDrain;
                    sb.Append($"\nReload Stamina: <color=yellow>{drain:0}/s</color>");
                }
            }

            // Cleave (only shown for attacks that can actually cleave multi-target, not single-target projectiles/bows)
            bool canCleave = baseline.Cleave > 0.001f &&
                             (baseline.NativeAttack == null ||
                              (baseline.NativeAttack.m_attackType != Attack.AttackType.Projectile &&
                               baseline.NativeAttack.m_attackType != Attack.AttackType.TriggerProjectile &&
                               baseline.NativeAttack.m_attackProjectile == null)) &&
                             item.m_shared.m_itemType != ItemDrop.ItemData.ItemType.Bow &&
                             item.m_shared.m_skillType != Skills.SkillType.Bows &&
                             item.m_shared.m_skillType != Skills.SkillType.Crossbows;
            if (canCleave)
            {
                sb.Append($"\nCleave: <color=yellow>{baseline.Cleave * 100f:F0}%</color>");
            }

            // Stamina
            if (baseline.Stamina > 0.01f)
            {
                sb.Append($"\n$item_staminause: <color=yellow>{Mathf.RoundToInt(baseline.Stamina)}</color>");
            }

            // Eitr
            if (baseline.Eitr > 0.01f)
            {
                sb.Append($"\n$item_eitruse: <color=yellow>{Mathf.RoundToInt(baseline.Eitr)}</color>");
            }

            // Knockback
            if (baseline.Knockback > 0)
            {
                sb.Append($"\n$item_knockback: <color=yellow>{baseline.Knockback}</color>");
            }

            // 2. Attack Profiles Section (Overrides only! Omitted if there is only 1 attack profile)
            if (cards.Count > 1)
            {
                sb.Append("\n\n<color=#FFAA00><b>Attack Profiles:</b></color>");

                foreach (var card in cards.Skip(1))
                {
                    RenderAttackCardOverrides(sb, card, baseline, attacker);
                }
            }

            // 3. Downstream Custom Line Providers (Traits, Rites, and Modifiers)
            if (_customLineProviders.Count > 0)
            {
                var tooltipCtx = new ItemTooltipContext(item, qualityLevel, crafting, worldLevel, attacker as Player);
                var customLines = new List<string>();

                for (int i = 0; i < _customLineProviders.Count; i++)
                {
                    var lines = _customLineProviders[i].Provider(tooltipCtx);
                    if (lines != null)
                    {
                        foreach (var line in lines)
                        {
                            if (!string.IsNullOrWhiteSpace(line))
                            {
                                customLines.Add(line);
                            }
                        }
                    }
                }

                if (customLines.Count > 0)
                {
                    sb.Append("\n\n<color=#FFAA00><b>Traits & Modifiers:</b></color>");
                    foreach (var line in customLines)
                    {
                        sb.Append($"\n{line}");
                    }
                }
            }

            // 4. Footer Hint
            string keyLabel = GetKeyLabel(ModConfig.TooltipExpandKey.Value);
            string actionWord = ModConfig.TooltipExpandBehavior.Value == TooltipExpandBehavior.Toggle ? "Press" : "Hold";
            sb.Append($"\n\n<color=#888888>[{keyLabel}] {actionWord}: Standard View</color>");

            sb.Append("</size>");

            return LocalizeSafe(sb.ToString());
        }

        /// <summary>
        /// Renders an individual attack card displaying ONLY overrides relative to the primary_1 baseline.
        /// Stagger and Bleed are displayed directly underneath Damage when it differs from regular baseline damage.
        /// </summary>
        private static void RenderAttackCardOverrides(
            StringBuilder sb,
            AttackCardData card,
            AttackCardData baseline,
            Character? attacker)
        {
            var cardCombatRanges = card.StepRanges
                .Where(kvp => kvp.Value.Min > 0.01f || kvp.Value.Max > 0.01f)
                .ToList();
            var baselineCombatRanges = baseline.StepRanges
                .Where(kvp => kvp.Value.Min > 0.01f || kvp.Value.Max > 0.01f)
                .ToList();

            // 1. Card Title
            sb.Append($"\n\n<color=#FFD700>Attack: {card.Title}</color>");

            // 2. Damage line (if different from baseline)
            bool damageDiffers = cardCombatRanges.Count != baselineCombatRanges.Count;
            if (!damageDiffers)
            {
                for (int i = 0; i < cardCombatRanges.Count; i++)
                {
                    if (cardCombatRanges[i].Key != baselineCombatRanges[i].Key ||
                        !Mathf.Approximately(cardCombatRanges[i].Value.Min, baselineCombatRanges[i].Value.Min) ||
                        !Mathf.Approximately(cardCombatRanges[i].Value.Max, baselineCombatRanges[i].Value.Max))
                    {
                        damageDiffers = true;
                        break;
                    }
                }
            }

            bool staggerRendered = false;

            if (damageDiffers && cardCombatRanges.Count > 0)
            {
                if (cardCombatRanges.Count == 1)
                {
                    var kvp = cardCombatRanges[0];
                    string rangeStr = FormatRange(kvp.Value.Min, kvp.Value.Max);
                    string typeLabel = GetLocalizedDamageType(kvp.Key);
                    string typeColor = GetDamageTypeColor(kvp.Key);
                    sb.Append($"\n  Damage: <color={typeColor}>{rangeStr}</color> ({typeLabel})");

                    if (ModConfig.StaggerOverhaulEnabled.Value)
                    {
                        if (kvp.Key == DamageType.Slash)
                        {
                            float bleedPct = ModConfig.SlashBleedPercent.Value + (attacker != null ? CharacterStatManager.GetStat(attacker, CharacterStatType.SlashBleedPercent) : 0f);
                            if (bleedPct > 0.001f)
                            {
                                string bleedStr = FormatRange(kvp.Value.Min * bleedPct, kvp.Value.Max * bleedPct);
                                sb.Append($"\n    <color=#B91C1C>Bleed: +{bleedStr}</color>");
                            }
                        }
                        else if (kvp.Key == DamageType.Blunt)
                        {
                            float stagMult = card.StaggerMultiplier * (attacker != null ? CharacterStatManager.GetStat(attacker, CharacterStatType.StaggerDamage) : 1f);
                            string stagStr = FormatRange(kvp.Value.Min * stagMult, kvp.Value.Max * stagMult);
                            sb.Append($"\n    <color=#F59E0B>Stagger: {stagStr}</color>");
                            staggerRendered = true;
                        }
                    }
                }
                else
                {
                    sb.Append("\n  Damage:");
                    foreach (var kvp in cardCombatRanges)
                    {
                        string rangeStr = FormatRange(kvp.Value.Min, kvp.Value.Max);
                        string typeLabel = GetLocalizedDamageType(kvp.Key);
                        string typeColor = GetDamageTypeColor(kvp.Key);
                        sb.Append($"\n    {typeLabel}: <color={typeColor}>{rangeStr}</color>");

                        if (ModConfig.StaggerOverhaulEnabled.Value)
                        {
                            if (kvp.Key == DamageType.Slash)
                            {
                                float bleedPct = ModConfig.SlashBleedPercent.Value + (attacker != null ? CharacterStatManager.GetStat(attacker, CharacterStatType.SlashBleedPercent) : 0f);
                                if (bleedPct > 0.001f)
                                {
                                    string bleedStr = FormatRange(kvp.Value.Min * bleedPct, kvp.Value.Max * bleedPct);
                                    sb.Append($"\n      <color=#B91C1C>Bleed: +{bleedStr}</color>");
                                }
                            }
                            else if (kvp.Key == DamageType.Blunt)
                            {
                                float stagMult = card.StaggerMultiplier * (attacker != null ? CharacterStatManager.GetStat(attacker, CharacterStatType.StaggerDamage) : 1f);
                                string stagStr = FormatRange(kvp.Value.Min * stagMult, kvp.Value.Max * stagMult);
                                sb.Append($"\n      <color=#F59E0B>Stagger: {stagStr}</color>");
                                staggerRendered = true;
                            }
                        }
                    }
                }
            }

            // 3. Stagger line: displayed right underneath damage ONLY when different from regular damage / baseline!
            if (!staggerRendered)
            {
                if (ModConfig.StaggerOverhaulEnabled.Value)
                {
                    if (!Mathf.Approximately(card.StaggerMultiplier, baseline.StaggerMultiplier))
                    {
                        var bluntKvp = cardCombatRanges.FirstOrDefault(k => k.Key == DamageType.Blunt);
                        if (bluntKvp.Value.Max > 0.01f)
                        {
                            float stagMult = card.StaggerMultiplier * (attacker != null ? CharacterStatManager.GetStat(attacker, CharacterStatType.StaggerDamage) : 1f);
                            sb.Append($"\n  Stagger: <color=#F59E0B>{FormatRange(bluntKvp.Value.Min * stagMult, bluntKvp.Value.Max * stagMult)}</color>");
                        }
                    }
                }
                else
                {
                    if (card.StaggerMultiplier > 1.01f || !Mathf.Approximately(card.StaggerMultiplier, baseline.StaggerMultiplier))
                    {
                        var staggerCombatRanges = cardCombatRanges
                            .Where(k => k.Key != DamageType.Chop && k.Key != DamageType.Pickaxe)
                            .ToList();
                        if (staggerCombatRanges.Count > 0)
                        {
                            float totalMin = staggerCombatRanges.Sum(k => k.Value.Min);
                            float totalMax = staggerCombatRanges.Sum(k => k.Value.Max);
                            sb.Append($"\n  Stagger: <color=yellow>{FormatRange(totalMin * card.StaggerMultiplier, totalMax * card.StaggerMultiplier)}</color>");
                        }
                    }
                }
            }

            // 4. Cleave override (only shown if secondary attack can cleave and differs from baseline)
            bool secCanCleave = card.Cleave > 0.001f &&
                                (card.NativeAttack == null ||
                                 (card.NativeAttack.m_attackType != Attack.AttackType.Projectile &&
                                  card.NativeAttack.m_attackType != Attack.AttackType.TriggerProjectile &&
                                  card.NativeAttack.m_attackProjectile == null));
            if (secCanCleave && !Mathf.Approximately(card.Cleave, baseline.Cleave))
            {
                sb.Append($"\n  Cleave: <color=yellow>{card.Cleave * 100f:F0}%</color>");
            }

            // 5. Stamina override
            if (card.Stamina > 0.01f && Mathf.RoundToInt(card.Stamina) != Mathf.RoundToInt(baseline.Stamina))
            {
                sb.Append($"\n  $item_staminause: <color=yellow>{Mathf.RoundToInt(card.Stamina)}</color>");
            }

            // 6. Eitr override
            if (card.Eitr > 0.01f && Mathf.RoundToInt(card.Eitr) != Mathf.RoundToInt(baseline.Eitr))
            {
                sb.Append($"\n  $item_eitruse: <color=yellow>{Mathf.RoundToInt(card.Eitr)}</color>");
            }

            // 7. Knockback override
            if (card.Knockback > 0 && card.Knockback != baseline.Knockback)
            {
                sb.Append($"\n  $item_knockback: <color=yellow>{card.Knockback}</color>");
            }

            // 8. On-Hit Status Effect override
            if (!string.IsNullOrEmpty(card.OnHitStatusEffect) && card.OnHitStatusEffect != baseline.OnHitStatusEffect)
            {
                var def = StatusEffects.StatusEffectRegistry.GetDefinition(card.OnHitStatusEffect);
                string seName = !string.IsNullOrEmpty(def?.DisplayName) ? def.DisplayName : card.OnHitStatusEffect;
                sb.Append($"\n  Inflicts: <color=yellow>{seName}</color> ({card.StatusEffectDuration:0.#}s)");
            }
        }

        /// <summary>
        /// Backward-compatibility helper for standalone damage string queries (e.g. recipes or crafting upgrades).
        /// </summary>
        public static string FormatDamageTooltip(ItemTooltipContext? context, HitData.DamageTypes baseDamages, Skills.SkillType skillType)
        {
            Character? attacker = context?.Player ?? GetLocalPlayerSafe();
            ItemDrop.ItemData? weapon = context?.Item;

            DamagePipeline.EvaluatePreviewDamages(
                attacker,
                weapon,
                baseDamages,
                skillType,
                out var ranges,
                out _,
                out _,
                stepKey: "primary_1");

            var combatTypes = ranges
                .Where(kvp => kvp.Value.Min > 0.01f || kvp.Value.Max > 0.01f)
                .ToList();

            var sb = new StringBuilder();
            foreach (var kvp in combatTypes)
            {
                string rangeStr = FormatRange(kvp.Value.Min, kvp.Value.Max);
                string typeLabel = GetLocalizedDamageType(kvp.Key);
                string typeColor = GetDamageTypeColor(kvp.Key);
                sb.Append($"\n{typeLabel}: <color={typeColor}>{rangeStr}</color>");
            }

            return sb.ToString();
        }

        private class StepGroup
        {
            public int StartStep;
            public int EndStep;
        }

        private static List<StepGroup> GroupPrimarySteps(WeaponAttackProfile profile, int chainLength, List<DamageType> activeTypes)
        {
            var groups = new List<StepGroup>();
            int currentStart = 1;

            for (int step = 2; step <= chainLength; step++)
            {
                string prevKey = $"primary_{step - 1}";
                string curKey = $"primary_{step}";

                bool identical = true;
                if (!Mathf.Approximately(profile.GetStep(prevKey).Cleave, profile.GetStep(curKey).Cleave))
                {
                    identical = false;
                }
                else if (profile.GetStep(prevKey).OverrideDamageType != profile.GetStep(curKey).OverrideDamageType)
                {
                    identical = false;
                }
                else
                {
                    foreach (var dt in activeTypes)
                    {
                        if (!Mathf.Approximately(profile.GetMultiplier(prevKey, dt), profile.GetMultiplier(curKey, dt)))
                        {
                            identical = false;
                            break;
                        }
                    }
                }

                if (!identical)
                {
                    groups.Add(new StepGroup { StartStep = currentStart, EndStep = step - 1 });
                    currentStart = step;
                }
            }

            groups.Add(new StepGroup { StartStep = currentStart, EndStep = chainLength });
            return groups;
        }

        private static List<DamageType> GetActiveCombatTypes(HitData.DamageTypes d)
        {
            var list = new List<DamageType>();
            if (d.m_blunt > 0) list.Add(DamageType.Blunt);
            if (d.m_slash > 0) list.Add(DamageType.Slash);
            if (d.m_pierce > 0) list.Add(DamageType.Pierce);
            if (d.m_chop > 0) list.Add(DamageType.Chop);
            if (d.m_pickaxe > 0) list.Add(DamageType.Pickaxe);
            if (d.m_fire > 0) list.Add(DamageType.Fire);
            if (d.m_frost > 0) list.Add(DamageType.Frost);
            if (d.m_lightning > 0) list.Add(DamageType.Lightning);
            if (d.m_poison > 0) list.Add(DamageType.Poison);
            if (d.m_spirit > 0) list.Add(DamageType.Spirit);
            if (d.m_damage > 0) list.Add(DamageType.TrueDamage);
            if (list.Count == 0) list.Add(DamageType.Blunt); // Fallback
            return list;
        }

        private static string FormatRange(float min, float max)
        {
            int minInt = Mathf.RoundToInt(min);
            int maxInt = Mathf.RoundToInt(max);
            return (minInt == maxInt) ? $"{minInt}" : $"{minInt} - {maxInt}";
        }

        public static string GetKeyLabel(KeyCode key) => key switch
        {
            KeyCode.LeftShift or KeyCode.RightShift => "Shift",
            KeyCode.LeftAlt or KeyCode.RightAlt => "Alt",
            KeyCode.LeftControl or KeyCode.RightControl => "Ctrl",
            _ => key.ToString()
        };

        public static string GetLocalizedDamageType(DamageType type)
        {
            string tag = type switch
            {
                DamageType.Blunt => "$inventory_blunt",
                DamageType.Slash => "$inventory_slash",
                DamageType.Pierce => "$inventory_pierce",
                DamageType.Chop => "$inventory_chop",
                DamageType.Pickaxe => "$inventory_pickaxe",
                DamageType.Fire => "$inventory_fire",
                DamageType.Frost => "$inventory_frost",
                DamageType.Lightning => "$inventory_lightning",
                DamageType.Poison => "$inventory_poison",
                DamageType.Spirit => "$inventory_spirit",
                _ => type.ToString()
            };

            string localized = LocalizeSafe(tag);
            if (type == DamageType.Chop)
            {
                localized += " <color=#888888>(Trees)</color>";
            }
            else if (type == DamageType.Pickaxe)
            {
                localized += " <color=#888888>(Mining)</color>";
            }
            return localized;
        }

        public static string GetDamageTypeColor(DamageType type) => type switch
        {
            DamageType.Blunt or DamageType.Slash or DamageType.Pierce or DamageType.Chop or DamageType.Pickaxe => "yellow",
            DamageType.Fire => "#FF7744",
            DamageType.Frost => "#88DDFF",
            DamageType.Lightning => "#C088FF",
            DamageType.Poison => "#66DD44",
            DamageType.Spirit => "#FFFFA0",
            DamageType.TrueDamage => "#FF4444",
            _ => "yellow"
        };

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static Player? GetLocalPlayerSafe()
        {
            try
            {
                return Player.m_localPlayer;
            }
            catch
            {
                return null;
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static string LocalizeSafe(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            try
            {
                return Localization.instance != null ? Localization.instance.Localize(text) : text;
            }
            catch
            {
                return text;
            }
        }
    }
}
