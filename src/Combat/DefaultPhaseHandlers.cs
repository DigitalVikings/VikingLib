using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using VikingLib.Combat.Attacks;
using VikingLib.Combat.Roll;
using VikingLib.Configuration;
using VikingLib.Stats;

namespace VikingLib.Combat
{
    /// <summary>
    /// Implements and registers VikingLib's default vanilla-equivalent and core combat phase handlers
    /// (cleave, pickaxe chunk split, proficiencies, crits, stealth ambush, block/parry, armor,
    /// continuous resistances, damage absorption barriers, resource procs, and physical triad lacerations).
    /// </summary>
    public static class DefaultPhaseHandlers
    {
        private static HarmonyLib.AccessTools.FieldRef<Character, float>? _backstabTimeRef;
        internal static HarmonyLib.AccessTools.FieldRef<Character, float> BackstabTimeRef =>
            _backstabTimeRef ??= HarmonyLib.AccessTools.FieldRefAccess<Character, float>("m_backstabTime");

        private sealed class ResourceTriggerCooldowns
        {
            public float LastKillTime = -100f;
            public float LastDamageTakenTime = -100f;
            public float LastCauseStaggerTime = -100f;
        }

        private static readonly ConditionalWeakTable<Character, ResourceTriggerCooldowns> _resourceCooldowns = new();
        private const float ResourceTriggerIcdSeconds = 1.0f;

        private enum ResourceTrigger
        {
            Kill,
            DamageTaken,
            CauseStagger
        }

        private static bool TryConsumeResourceTrigger(Character character, ResourceTrigger trigger)
        {
            var cds = _resourceCooldowns.GetOrCreateValue(character);
            float now = Time.time;
            switch (trigger)
            {
                case ResourceTrigger.Kill:
                    if (now - cds.LastKillTime < ResourceTriggerIcdSeconds) return false;
                    cds.LastKillTime = now;
                    return true;

                case ResourceTrigger.DamageTaken:
                    if (now - cds.LastDamageTakenTime < ResourceTriggerIcdSeconds) return false;
                    cds.LastDamageTakenTime = now;
                    return true;

                case ResourceTrigger.CauseStagger:
                    if (now - cds.LastCauseStaggerTime < ResourceTriggerIcdSeconds) return false;
                    cds.LastCauseStaggerTime = now;
                    return true;

                default:
                    return false;
            }
        }

        /// <summary>
        /// Registers all standard default combat handlers into <see cref="DamagePipeline"/>.
        /// </summary>
        public static void RegisterAll()
        {
            // Phase 1 Default: Multi-Target Cleave Scaling & Pickaxe Chunk Split
            DamagePipeline.RegisterHandler(DamagePhase.AttackerModifiers, priority: 50, ctx =>
            {
                AttackStepModifiers? stepMods = null;
                int targetCount = 1;
                int targetIndex = 0;

                if (WeaponAttackRegistry.TryGetHitAttack(ctx.Hit, out var attackInfo))
                {
                    stepMods = attackInfo.Profile.GetStep(attackInfo.StepKey);
                    if (attackInfo.TargetCount > 0) targetCount = attackInfo.TargetCount;
                    targetIndex = attackInfo.TargetIndex;
                }
                else if (ctx.Attacker is Humanoid humanoid && !ctx.IsPreview)
                {
                    var weapon = humanoid.GetCurrentWeapon();
                    if (weapon != null)
                    {
                        var profile = WeaponAttackRegistry.GetProfile(weapon);
                        string stepKey = "primary_1";

                        if (DamageRollPipeline.LastRoll != null &&
                            DamageRollPipeline.LastRoll.Attacker == ctx.Attacker &&
                            !string.IsNullOrEmpty(DamageRollPipeline.LastRoll.AttackKey))
                        {
                            stepKey = DamageRollPipeline.LastRoll.AttackKey;
                        }
                        else
                        {
                            var curAtk = HumanoidAttackHelper.GetCurrentAttack(humanoid);
                            if (curAtk != null)
                            {
                                if (HumanoidAttackHelper.IsCurrentAttackSecondary(humanoid))
                                {
                                    stepKey = "secondary";
                                }
                                else
                                {
                                    int chainLevel = HumanoidAttackHelper.GetCurrentAttackChainLevel(curAtk);
                                    int step = chainLevel + 1;
                                    stepKey = $"primary_{step}";
                                }
                            }
                        }

                        stepMods = profile.GetStep(stepKey);
                    }
                }

                if (targetCount < 1) targetCount = 1;
                if (targetIndex < 0) targetIndex = 0;

                // Dedicated Pickaxe Split: When swinging a pickaxe into multiple rock/ore chunks,
                // divide pickaxe damage evenly between all affected chunks (1/N per chunk).
                if (attackInfo != null && attackInfo.IsPickaxeSpecial && attackInfo.PickaxeChunkCount > 1 && !ctx.IsPreview)
                {
                    float splitMult = 1.0f / attackInfo.PickaxeChunkCount;
                    ctx.AddMultiplier(splitMult, $"Pickaxe Split ({attackInfo.PickaxeChunkCount} chunks: {splitMult * 100f:F1}% damage)");
                }
                else if (targetCount > 1 && !ctx.IsPreview)
                {
                    // Multi-Target Cleave Scaling (Tier 3 Multiplier across all damage types)
                    float cleave = stepMods?.Cleave ?? 0.375f;
                    if (ctx.Attacker != null)
                    {
                        float cleaveEff = CharacterStatManager.GetStat(ctx.Attacker, CharacterStatType.CleaveEfficiency);
                        if (Mathf.Abs(cleaveEff - 1.0f) > 0.001f)
                        {
                            cleave = Mathf.Clamp01(cleave * cleaveEff);
                        }
                    }
                    float cleaveMult = 1.0f;

                    switch (ModConfig.CleaveMode.Value)
                    {
                        case CleaveScalingMode.Continuous:
                            // Continuous formula: 1 / (Cleave + (1 - Cleave) * N)
                            cleaveMult = cleave >= 1.0f
                                ? 1.0f
                                : 1.0f / (cleave + (1.0f - cleave) * targetCount);

                            if (Mathf.Abs(cleaveMult - 1.0f) > 0.001f)
                            {
                                ctx.AddMultiplier(cleaveMult, $"Multi-Target Cleave ({targetCount} targets, {cleave * 100f:F0}%: {cleaveMult:F2}x)");
                            }
                            break;

                        case CleaveScalingMode.Geometric:
                            // Blade-sweep geometric falloff: target k takes Cleave^k (Target 0 takes 100%)
                            cleaveMult = cleave >= 1.0f || targetIndex <= 0
                                ? 1.0f
                                : Mathf.Pow(cleave, targetIndex);

                            if (Mathf.Abs(cleaveMult - 1.0f) > 0.001f)
                            {
                                ctx.AddMultiplier(cleaveMult, $"Geometric Cleave (Target #{targetIndex + 1}/{targetCount}, {cleave * 100f:F0}%: {cleaveMult:F2}x)");
                            }
                            break;

                        case CleaveScalingMode.Vanilla:
                            // Vanilla fallback: If Cleave < 1.0f, apply 4 / (3 * N); if Cleave >= 1.0f, apply 1.0x
                            cleaveMult = cleave >= 1.0f
                                ? 1.0f
                                : 4.0f / (3.0f * targetCount);

                            if (Mathf.Abs(cleaveMult - 1.0f) > 0.001f)
                            {
                                ctx.AddMultiplier(cleaveMult, $"Vanilla Multi-Target ({targetCount} targets: {cleaveMult:F2}x)");
                            }
                            break;
                    }
                }
            });

            // Phase 2 Default: Global Offensive Proficiencies & Combat Archetypes
            DamagePipeline.RegisterHandler(DamagePhase.AttackerModifiers, priority: 75, ctx =>
            {
                if (ctx.Attacker == null) return;

                // A. Specific Damage Type Proficiencies
                if (ctx.GetDamage(DamageType.Slash) > 0f) ApplyStatTraceToContext(ctx, CharacterStatType.DamageSlashing, DamageType.Slash);
                if (ctx.GetDamage(DamageType.Blunt) > 0f) ApplyStatTraceToContext(ctx, CharacterStatType.DamageBlunt, DamageType.Blunt);
                if (ctx.GetDamage(DamageType.Pierce) > 0f) ApplyStatTraceToContext(ctx, CharacterStatType.DamagePiercing, DamageType.Pierce);
                if (ctx.GetDamage(DamageType.Chop) > 0f) ApplyStatTraceToContext(ctx, CharacterStatType.DamageChop, DamageType.Chop);
                if (ctx.GetDamage(DamageType.Pickaxe) > 0f) ApplyStatTraceToContext(ctx, CharacterStatType.DamagePickaxe, DamageType.Pickaxe);
                if (ctx.GetDamage(DamageType.Fire) > 0f) ApplyStatTraceToContext(ctx, CharacterStatType.DamageFire, DamageType.Fire);
                if (ctx.GetDamage(DamageType.Frost) > 0f) ApplyStatTraceToContext(ctx, CharacterStatType.DamageFrost, DamageType.Frost);
                if (ctx.GetDamage(DamageType.Lightning) > 0f) ApplyStatTraceToContext(ctx, CharacterStatType.DamageLightning, DamageType.Lightning);
                if (ctx.GetDamage(DamageType.Poison) > 0f) ApplyStatTraceToContext(ctx, CharacterStatType.DamagePoison, DamageType.Poison);
                if (ctx.GetDamage(DamageType.Spirit) > 0f) ApplyStatTraceToContext(ctx, CharacterStatType.DamageSpirit, DamageType.Spirit);

                // B. Group Damage Proficiencies
                if (ctx.GetTotalPhysicalDamage() > 0f)
                {
                    if (ctx.GetDamage(DamageType.Slash) > 0f) ApplyStatTraceToContext(ctx, CharacterStatType.DamagePhysical, DamageType.Slash);
                    if (ctx.GetDamage(DamageType.Blunt) > 0f) ApplyStatTraceToContext(ctx, CharacterStatType.DamagePhysical, DamageType.Blunt);
                    if (ctx.GetDamage(DamageType.Pierce) > 0f) ApplyStatTraceToContext(ctx, CharacterStatType.DamagePhysical, DamageType.Pierce);
                }

                if (ctx.GetTotalElementalDamage() > 0f)
                {
                    if (ctx.GetDamage(DamageType.Fire) > 0f) ApplyStatTraceToContext(ctx, CharacterStatType.DamageElemental, DamageType.Fire);
                    if (ctx.GetDamage(DamageType.Frost) > 0f) ApplyStatTraceToContext(ctx, CharacterStatType.DamageElemental, DamageType.Frost);
                    if (ctx.GetDamage(DamageType.Lightning) > 0f) ApplyStatTraceToContext(ctx, CharacterStatType.DamageElemental, DamageType.Lightning);
                }

                // C. Delivery & Grip Archetypes (Melee, Ranged, Magic, 1H, 2H)
                if (ctx.Flags.HasFlag(DamageFlags.Melee))
                {
                    ApplyStatTraceToContext(ctx, CharacterStatType.DamageMelee);

                    ItemDrop.ItemData? wep = null;
                    if (WeaponAttackRegistry.TryGetHitAttack(ctx.Hit, out var atkInfo)) wep = atkInfo.Weapon;
                    if (wep == null && ctx.Attacker is Humanoid humWep) wep = humWep.GetCurrentWeapon();

                    if (wep?.m_shared != null)
                    {
                        bool isTwoHanded = wep.m_shared.m_itemType is ItemDrop.ItemData.ItemType.TwoHandedWeapon
                                           or ItemDrop.ItemData.ItemType.TwoHandedWeaponLeft;
                        if (isTwoHanded)
                        {
                            ApplyStatTraceToContext(ctx, CharacterStatType.DamageTwoHanded);
                        }
                        else
                        {
                            ApplyStatTraceToContext(ctx, CharacterStatType.DamageOneHanded);
                        }
                    }
                }
                else if (ctx.Flags.HasFlag(DamageFlags.Ranged))
                {
                    ApplyStatTraceToContext(ctx, CharacterStatType.DamageRanged);
                }

                if (ctx.Flags.HasFlag(DamageFlags.Magic) || ctx.GetTotalElementalDamage() > 0f)
                {
                    ApplyStatTraceToContext(ctx, CharacterStatType.DamageMagic);
                }

                // D. Chain Step Techniques (Combo Finisher & Secondary)
                WeaponAttackRegistry.TryGetHitAttack(ctx.Hit, out var attackInfo);
                string stepKey = attackInfo?.StepKey ?? "";

                bool isSecondary = stepKey.Equals("secondary", StringComparison.OrdinalIgnoreCase) ||
                                   (ctx.Attacker is Humanoid hum && HumanoidAttackHelper.IsCurrentAttackSecondary(hum));

                if (isSecondary)
                {
                    ApplyStatTraceToContext(ctx, CharacterStatType.DamageSecondaryAttack);
                }

                bool isFinisher = stepKey.EndsWith("_3", StringComparison.OrdinalIgnoreCase) ||
                                  (attackInfo != null && attackInfo.DisplayName.IndexOf("Finisher", StringComparison.OrdinalIgnoreCase) >= 0);

                if (isFinisher)
                {
                    ApplyStatTraceToContext(ctx, CharacterStatType.DamageComboFinisher);
                }

                // E. Weapon Skill Damage Archetypes
                var skillType = ctx.Hit.m_skill;
                if (skillType == Skills.SkillType.None && ctx.Attacker is Humanoid attackerHum)
                {
                    var wep = attackerHum.GetCurrentWeapon();
                    if (wep?.m_shared != null) skillType = wep.m_shared.m_skillType;
                }

                var wepDamageStat = CharacterStatManager.GetWeaponDamageStat(skillType);
                if (wepDamageStat.HasValue)
                {
                    ApplyStatTraceToContext(ctx, wepDamageStat.Value);
                }

                // F. Stagger Power (Offensive Poise Damage)
                float staggerPower = CharacterStatManager.GetStat(ctx.Attacker, CharacterStatType.StaggerDamage);
                if (Mathf.Abs(staggerPower - 1f) > 0.001f)
                {
                    ctx.Hit.m_staggerMultiplier *= staggerPower;
                    ctx.Trace?.RecordStep(DamagePhase.AttackerModifiers, "Stagger Power", $"Poise damage scaled by {staggerPower:F2}x (Total: {ctx.Hit.m_staggerMultiplier:F2}x)", staggerPower);
                }

                // F2. Knockback Dealt (Push Force)
                if (!ctx.IsPreview && ctx.Hit.m_pushForce > 0f)
                {
                    float knockback = CharacterStatManager.GetStat(ctx.Attacker, CharacterStatType.KnockbackDealt);
                    if (Mathf.Abs(knockback - 1f) > 0.001f)
                    {
                        ctx.Hit.m_pushForce *= Mathf.Max(0f, knockback);
                        ctx.Trace?.RecordStep(DamagePhase.AttackerModifiers, "Knockback Dealt", $"Push force scaled by {knockback:F2}x (Total: {ctx.Hit.m_pushForce:F1})", knockback);
                    }
                }

                // G. Situational & Target Context Modifiers (Live Combat Only)
                if (!ctx.IsPreview)
                {
                    if (ctx.Target != null)
                    {
                        if (ctx.Target.IsBoss())
                        {
                            ApplyStatTraceToContext(ctx, CharacterStatType.DamageAgainstBosses);
                        }

                        if (ctx.Target.IsStaggering())
                        {
                            ApplyStatTraceToContext(ctx, CharacterStatType.DamageAgainstStaggered);
                        }

                        if (ctx.Target.IsFlying())
                        {
                            ApplyStatTraceToContext(ctx, CharacterStatType.DamageAgainstAirborne);
                        }

                        float targetHpPct = ctx.Target.GetHealthPercentage();
                        if (targetHpPct <= 0.35f)
                        {
                            ApplyStatTraceToContext(ctx, CharacterStatType.DamageExecute);
                        }
                        else if (targetHpPct >= 0.90f)
                        {
                            ApplyStatTraceToContext(ctx, CharacterStatType.DamageFirstStrike);
                        }
                    }

                    float attackerHpPct = ctx.Attacker.GetHealthPercentage();
                    if (attackerHpPct <= 0.35f)
                    {
                        ApplyStatTraceToContext(ctx, CharacterStatType.DamageBerserk);
                    }
                    else if (attackerHpPct >= 0.90f)
                    {
                        ApplyStatTraceToContext(ctx, CharacterStatType.DamageFlawless);
                    }

                    // Environmental & Positional Contexts
                    if (EnvMan.instance != null && EnvMan.IsNight())
                    {
                        ApplyStatTraceToContext(ctx, CharacterStatType.DamageAtNight);
                    }

                    if (!ctx.Attacker.IsOnGround())
                    {
                        ApplyStatTraceToContext(ctx, CharacterStatType.DamageWhileAirborne);
                    }

                    if (ctx.Target != null && IsAfflicted(ctx.Target))
                    {
                        ApplyStatTraceToContext(ctx, CharacterStatType.DamageAgainstAfflicted);
                    }
                }
            });

            // Phase 1: Physical Triad - Innate Pierce Precision Multiplier (+18% Tier 3)
            DamagePipeline.RegisterHandler(DamagePhase.AttackerModifiers, priority: 80, ApplyPiercePrecision);

            // Phase 2 Default: Crits, Stealth Sneak Attack, and Stagger Punish
            DamagePipeline.RegisterHandler(DamagePhase.AttackerModifiers, priority: 100, ctx =>
            {
                if (ctx.IsPreview) return;

                // 1. Initialize base crit stats from CharacterStatManager
                if (ctx.Attacker != null)
                {
                    ctx.CritChance = CharacterStatManager.GetStat(ctx.Attacker, CharacterStatType.CriticalStrikeChance);
                    ctx.CritMultiplier = CharacterStatManager.GetStat(ctx.Attacker, CharacterStatType.CriticalStrikeMultiplier);
                }
                else
                {
                    ctx.CritChance = 0f;
                    ctx.CritMultiplier = 2.0f;
                }

                // 2. Check Stagger Punish
                bool isStaggered = ctx.Target != null && ctx.Target.IsStaggering() && !ctx.Target.IsPlayer();
                if (isStaggered)
                {
                    ctx.Flags |= DamageFlags.StaggerBonus;
                    if (!ModConfig.StaggerOverhaulEnabled.Value)
                    {
                        ctx.Flags |= DamageFlags.CriticalHit;
                    }
                }

                // 3. Check Stealth Sneak Attack (Guaranteed critical strike + neutral sneak bonus)
                bool isSneakAttack = false;
                var baseAI = ctx.Target?.GetBaseAI();
                if (ctx.Target != null && baseAI != null && !baseAI.IsAlerted())
                {
                    float lastBackstab = BackstabTimeRef(ctx.Target);
                    if (ctx.Hit.m_backstabBonus > 1f && (Time.time - lastBackstab > 300f))
                    {
                        isSneakAttack = true;
                        ctx.Flags |= DamageFlags.Backstab;
                        ctx.Flags |= DamageFlags.CriticalHit;
                        BackstabTimeRef(ctx.Target) = Time.time;
                    }
                }

                // 4. Critical Strike Tier Calculation (Multi-Crit support)
                int critTiers = 0;
                bool guaranteedCrit = isSneakAttack || (isStaggered && !ModConfig.StaggerOverhaulEnabled.Value);
                if (guaranteedCrit)
                {
                    // Guaranteed at least Tier 1, plus additional over-crit tiers if critChance > 0
                    int extraTiers = Mathf.FloorToInt(ctx.CritChance);
                    float remainder = ctx.CritChance - extraTiers;
                    if (remainder > 0.0001f && UnityEngine.Random.value < remainder) extraTiers++;
                    critTiers = 1 + extraTiers;
                }
                else if (ctx.CritChance > 0.0001f)
                {
                    int guaranteed = Mathf.FloorToInt(ctx.CritChance);
                    float remainder = ctx.CritChance - guaranteed;
                    int extra = (remainder > 0.0001f && UnityEngine.Random.value < remainder) ? 1 : 0;
                    critTiers = guaranteed + extra;
                }

                // 5. Apply Critical Strike Multiplier
                if (critTiers > 0)
                {
                    ctx.Flags |= DamageFlags.CriticalHit;
                    float perTierBonus = ctx.CritMultiplier - 1.0f;
                    float effectiveMultiplier = 1.0f + (critTiers * perTierBonus);

                    string critReason = critTiers > 1
                        ? $"Multi-Crit (Tier {critTiers}: {effectiveMultiplier:F2}x)"
                        : (isStaggered
                            ? $"Stagger Punish Critical ({effectiveMultiplier:F1}x)"
                            : (isSneakAttack
                                ? $"Sneak Attack Critical ({effectiveMultiplier:F1}x)"
                                : $"Critical Strike ({effectiveMultiplier:F1}x)"));

                    ctx.AddMultiplier(effectiveMultiplier, critReason);
                }

                // 6. Apply Sneak Attack Proficiencies & Ambush Bonus
                if (isSneakAttack)
                {
                    ApplyStatTraceToContext(ctx, CharacterStatType.DamageSneak);

                    if (ctx.Hit.m_backstabBonus > 1.0f)
                    {
                        float ambushMult = ctx.Hit.m_backstabBonus / 2.0f;
                        if (ambushMult > 1.001f)
                        {
                            ctx.AddMultiplier(ambushMult, $"Stealth Ambush Bonus ({ambushMult:F2}x)");
                        }
                    }
                }
            });

            // Phase 2 Default A: Block & Parry Mitigation (runs at priority 50 before procs)
            DamagePipeline.RegisterHandler(DamagePhase.TargetDefenses, priority: 50, ctx =>
            {
                if (ctx.IsPreview || ctx.Target == null || ctx.IsCancelled) return;

                if (ctx.Flags.HasFlag(DamageFlags.CannotBeBlocked))
                {
                    ctx.Hit.m_blockable = false;
                    return;
                }

                if (!ctx.Hit.m_blockable) return;
                if (ctx.Target is not Humanoid humanoid) return;
                if (!humanoid.IsBlocking()) return;
                if (humanoid.IsDead() || humanoid.GetHealth() <= 0f) return;

                // Sync current pipeline damages to ctx.Hit.m_damage so BlockAttack operates on post-AttackerModifiers damage
                ctx.WriteBackToHitData();

                // Blockable damage includes physical (Blunt, Slash, Pierce) and elemental (Fire, Frost, Lightning, Poison, Spirit).
                // Excludes Chop and Pickaxe (structural/environment damage to which players and characters are immune).
                // Also excludes damage types to which the defender is completely immune (resistance <= 0),
                // so shield block power is not wasted absorbing damage that cannot harm the defender.
                float preBlockDamage = 0f;
                var blockableTypes = new List<DamageType>();
                foreach (DamageType dt in Enum.GetValues(typeof(DamageType)))
                {
                    if (dt == DamageType.Chop || dt == DamageType.Pickaxe) continue;
                    float dmg = ctx.GetDamage(dt);
                    if (dmg <= 0.001f) continue;

                    // If defender is immune to this damage type, don't spend shield block power on it
                    if (ctx.Defense != null && ctx.Defense.GetResistanceForType(dt) <= 0.0001f) continue;

                    preBlockDamage += dmg;
                    blockableTypes.Add(dt);
                }

                if (preBlockDamage <= 0.001f) return;

                // Check parry timing before BlockAttack processes it
                ItemDrop.ItemData? blocker = CharacterStatsHelper.GetCurrentBlocker(humanoid);
                float parryWindow = CharacterStatManager.GetStat(humanoid, CharacterStatType.ParryWindow);
                if (parryWindow <= 0.001f) parryWindow = 0.25f;
                float blockTimer = CharacterStatsHelper.GetBlockTimer(humanoid);
                bool isTimedParry = blocker != null && blocker.m_shared.m_timedBlockBonus > 1.0f 
                                    && blockTimer != -1f && blockTimer < parryWindow;

                Character? attacker = ctx.Attacker ?? ctx.Hit.GetAttacker();

                // Execute native Humanoid.BlockAttack (stamina drain, block/parry vfx, shield damage, attacker stagger)
                bool blocked = CharacterStatsHelper.BlockAttackInvoker(humanoid, ctx.Hit, attacker);

                // Prevent vanilla RPC_Damage from executing a redundant second BlockAttack
                ctx.Hit.m_blockable = false;

                if (blocked)
                {
                    float skillFactor = humanoid.GetSkillFactor(Skills.SkillType.Blocking);
                    float baseBlockPower = blocker != null ? blocker.GetBlockPower(skillFactor) : 10f;
                    float effectiveBlockPower;

                    if (isTimedParry && blocker != null)
                    {
                        float parryBonus = blocker.m_shared.m_timedBlockBonus;
                        float statParryMult = CharacterStatManager.GetStat(humanoid, CharacterStatType.ParryMultiplier);
                        effectiveBlockPower = baseBlockPower * parryBonus * statParryMult;
                    }
                    else
                    {
                        float statBlockMult = CharacterStatManager.GetStat(humanoid, CharacterStatType.BlockPowerMultiplier);
                        effectiveBlockPower = baseBlockPower * statBlockMult;
                    }

                    string blockerName = blocker?.m_shared?.m_name != null
                        ? (Localization.instance != null ? Localization.instance.Localize(blocker.m_shared.m_name) : blocker.m_shared.m_name)
                        : "Shield";
                    string label = isTimedParry ? "Parry Deflection" : "Block Mitigation";

                    float damageBlocked;
                    if (effectiveBlockPower >= preBlockDamage)
                    {
                        // 100% absorption / deflection: completely neutralize all blockable damage
                        damageBlocked = preBlockDamage;
                        for (int i = 0; i < blockableTypes.Count; i++)
                        {
                            ctx.SetDamage(blockableTypes[i], 0f);
                        }
                    }
                    else
                    {
                        // Partial block: absorb effectiveBlockPower from blockable damage, remainder penetrates to body armor
                        damageBlocked = effectiveBlockPower;
                        float remainingRatio = Mathf.Max(0f, (preBlockDamage - effectiveBlockPower) / preBlockDamage);
                        for (int i = 0; i < blockableTypes.Count; i++)
                        {
                            DamageType dt = blockableTypes[i];
                            float current = ctx.GetDamage(dt);
                            if (current > 0f)
                            {
                                ctx.SetDamage(dt, current * remainingRatio);
                            }
                        }
                    }

                    ctx.WriteBackToHitData();
                    float postBlockDamage = preBlockDamage - damageBlocked;

                    ctx.Trace?.RecordStep(DamagePhase.TargetDefenses, label,
                        $"{preBlockDamage:F1} -> {postBlockDamage:F1} (-{damageBlocked:F1} blocked by {blockerName})", damageBlocked);
                }
            });

            // Phase 2 Default B: Defensive Procs & Resource Recovery On Damage Taken (3x3 Matrix with 1s ICD)
            DamagePipeline.RegisterHandler(DamagePhase.TargetDefenses, priority: 100, ctx =>
            {
                if (ctx.IsPreview || ctx.Target == null || ctx.IsCancelled) return;
                if (ctx.GetTotalDamage() <= 0.001f) return;

                float hp = CharacterStatManager.GetStat(ctx.Target, CharacterStatType.HealthOnDamageTaken);
                float stam = CharacterStatManager.GetStat(ctx.Target, CharacterStatType.StaminaOnDamageTaken);
                float eitr = CharacterStatManager.GetStat(ctx.Target, CharacterStatType.EitrOnDamageTaken);

                if ((hp > 0f || stam > 0f || eitr > 0f) && TryConsumeResourceTrigger(ctx.Target, ResourceTrigger.DamageTaken))
                {
                    if (hp > 0f)
                    {
                        ctx.Target.Heal(hp);
                        ctx.Trace?.RecordStep(DamagePhase.TargetDefenses, "Health On Damage Taken", $"Restored +{hp:F1} HP to defender", hp);
                    }
                    if (stam > 0f) RestoreDefenderResource(ctx, stam, "Stamina On Damage Taken", stamina: true);
                    if (eitr > 0f) RestoreDefenderResource(ctx, eitr, "Eitr On Damage Taken", stamina: false);
                }

                // Status Effect Charge Consumption (DamageTaken)
                if (ctx.Target != null && !ctx.IsPreview)
                {
                    StatusEffects.StatusEffectRegistry.ConsumeCharges(ctx.Target, StatusEffects.ChargeConsumeTrigger.DamageTaken);
                }
            });

            // Phase 3 Default: Granular Armor & Continuous Resistances
            DamagePipeline.RegisterHandler(DamagePhase.Mitigation, priority: 100, ctx =>
            {
                // A. Granular Armor Mitigation
                if (!ctx.Flags.HasFlag(DamageFlags.IgnoresArmor) && !ctx.Flags.HasFlag(DamageFlags.TrueDamage))
                {
                    // Physical Armor & Armor Efficiency Suite
                    float bluntArmor = ctx.Defense.GetArmorForType(DamageType.Blunt);
                    float slashArmor = ctx.Defense.GetArmorForType(DamageType.Slash);
                    float pierceArmor = ctx.Defense.GetArmorForType(DamageType.Pierce);

                    float generalEff = ctx.Target != null ? CharacterStatManager.GetStat(ctx.Target, CharacterStatType.ArmorEfficiency) : 0f;
                    float bluntEff = generalEff + (ctx.Target != null ? CharacterStatManager.GetStat(ctx.Target, CharacterStatType.BluntArmorEfficiency) : 0f);
                    float slashEff = generalEff + (ctx.Target != null ? CharacterStatManager.GetStat(ctx.Target, CharacterStatType.SlashArmorEfficiency) : 0f);
                    float pierceEff = generalEff + (ctx.Target != null ? CharacterStatManager.GetStat(ctx.Target, CharacterStatType.PierceArmorEfficiency) : 0f);

                    if (bluntArmor > 0f) ctx.MitigateWithArmor(DamageType.Blunt, bluntArmor, bluntEff);
                    if (slashArmor > 0f) ctx.MitigateWithArmor(DamageType.Slash, slashArmor, slashEff);
                    if (pierceArmor > 0f) ctx.MitigateWithArmor(DamageType.Pierce, pierceArmor, pierceEff);

                    // Elemental / Magic Armor (if configured on target) - strictly NO elemental armor efficiency
                    float fireArmor = ctx.Defense.GetArmorForType(DamageType.Fire);
                    float frostArmor = ctx.Defense.GetArmorForType(DamageType.Frost);
                    float lightningArmor = ctx.Defense.GetArmorForType(DamageType.Lightning);

                    if (fireArmor > 0f) ctx.MitigateWithArmor(DamageType.Fire, fireArmor, 0f);
                    if (frostArmor > 0f) ctx.MitigateWithArmor(DamageType.Frost, frostArmor, 0f);
                    if (lightningArmor > 0f) ctx.MitigateWithArmor(DamageType.Lightning, lightningArmor, 0f);
                }

                // B. Continuous Resistances
                float maxResistDmg = 0f;
                float minResistMult = 1f;

                foreach (DamageType type in Enum.GetValues(typeof(DamageType)))
                {
                    if (type == DamageType.TrueDamage) continue;

                    float mult = ctx.Defense.GetResistanceForType(type);

                    // Safeguard: Living Characters universally ignore environmental utility damage (Chop/Pickaxe) unless explicitly registered
                    if (ctx.Target != null || ctx.DestructibleTarget is Character)
                    {
                        if (type == DamageType.Chop && !ctx.Defense.Resistances.ContainsKey(DamageType.Chop))
                        {
                            mult = 0f;
                        }
                        else if (type == DamageType.Pickaxe && !ctx.Defense.Resistances.ContainsKey(DamageType.Pickaxe))
                        {
                            mult = 0f;
                        }
                    }

                    // Combine with target's declarative ResistanceStat tokens if targeting a Character
                    if (ctx.Target != null)
                    {
                        var resStat = CharacterStatManager.GetResistanceStat(type);
                        if (resStat.HasValue)
                        {
                            mult *= CharacterStatManager.GetStat(ctx.Target, resStat.Value);
                        }

                        if (type.IsPhysical())
                        {
                            mult *= CharacterStatManager.GetStat(ctx.Target, CharacterStatType.ResistancePhysical);
                        }
                        else if (type.IsElemental())
                        {
                            mult *= CharacterStatManager.GetStat(ctx.Target, CharacterStatType.ResistanceElemental);
                        }
                    }

                    if (Mathf.Abs(mult - 1.0f) > 0.001f)
                    {
                        ctx.ApplyResistance(type, mult);
                    }

                    float dmg = ctx.GetDamage(type);
                    if (dmg > maxResistDmg)
                    {
                        maxResistDmg = dmg;
                        minResistMult = mult;
                    }
                }

                DamagePipeline.CurrentSignificantModifier = Defense.EntityDefenseRegistry.ToVanillaModifier(minResistMult);
                ctx.FlushMitigationTrace();

                // C. Damage Absorption Barriers (Shields)
                if (ctx.Target != null && !ctx.IsPreview && !ctx.IsCancelled)
                {
                    StatusEffects.StatusEffectRegistry.AbsorbDamage(ctx);
                }
            });

            // Phase 6 Default: Kill & Stagger Resource Restores (3x3 Matrix with 1s ICD)
            DamagePipeline.RegisterHandler(DamagePhase.PostHitProcs, priority: 100, ctx =>
            {
                if (ctx.IsPreview || ctx.Attacker == null) return;

                // A. On-Kill Resource Restores (Health, Stamina, Eitr)
                if (ctx.IsKillingBlow)
                {
                    float hp = CharacterStatManager.GetStat(ctx.Attacker, CharacterStatType.HealthOnKill);
                    float stam = CharacterStatManager.GetStat(ctx.Attacker, CharacterStatType.StaminaOnKill);
                    float eitr = CharacterStatManager.GetStat(ctx.Attacker, CharacterStatType.EitrOnKill);

                    if ((hp > 0f || stam > 0f || eitr > 0f) && TryConsumeResourceTrigger(ctx.Attacker, ResourceTrigger.Kill))
                    {
                        if (hp > 0f)
                        {
                            ctx.Attacker.Heal(hp);
                            ctx.Trace?.RecordStep(DamagePhase.PostHitProcs, "Health On Kill", $"Restored +{hp:F1} HP on kill", hp);
                        }
                        if (stam > 0f) RestoreAttackerResource(ctx, stam, "Stamina On Kill", stamina: true);
                        if (eitr > 0f) RestoreAttackerResource(ctx, eitr, "Eitr On Kill", stamina: false);
                    }
                }

                // B. On-CauseStagger Resource Restores (Health, Stamina, Eitr)
                if (ctx.CausedStagger && !ctx.IsKillingBlow)
                {
                    float hp = CharacterStatManager.GetStat(ctx.Attacker, CharacterStatType.HealthOnCauseStagger);
                    float stam = CharacterStatManager.GetStat(ctx.Attacker, CharacterStatType.StaminaOnCauseStagger);
                    float eitr = CharacterStatManager.GetStat(ctx.Attacker, CharacterStatType.EitrOnCauseStagger);

                    if ((hp > 0f || stam > 0f || eitr > 0f) && TryConsumeResourceTrigger(ctx.Attacker, ResourceTrigger.CauseStagger))
                    {
                        if (hp > 0f)
                        {
                            ctx.Attacker.Heal(hp);
                            ctx.Trace?.RecordStep(DamagePhase.PostHitProcs, "Health On Cause Stagger", $"Restored +{hp:F1} HP on cause stagger", hp);
                        }
                        if (stam > 0f) RestoreAttackerResource(ctx, stam, "Stamina On Cause Stagger", stamina: true);
                        if (eitr > 0f) RestoreAttackerResource(ctx, eitr, "Eitr On Cause Stagger", stamina: false);
                    }
                }

                // C. Status Effect Offensive Charge Consumption
                if (ctx.Flags.HasFlag(DamageFlags.Melee))
                {
                    StatusEffects.StatusEffectRegistry.ConsumeCharges(ctx.Attacker, StatusEffects.ChargeConsumeTrigger.MeleeHitDelivered);
                }
                if (ctx.Flags.HasFlag(DamageFlags.Ranged))
                {
                    StatusEffects.StatusEffectRegistry.ConsumeCharges(ctx.Attacker, StatusEffects.ChargeConsumeTrigger.RangedHitDelivered);
                }
                StatusEffects.StatusEffectRegistry.ConsumeCharges(ctx.Attacker, StatusEffects.ChargeConsumeTrigger.AnyHitDelivered);

                // D. Physical Triad - Post-Mitigation Slash Bleed
                ApplySlashBleed(ctx);

                // E. Attack Step On-Hit Status Effects
                ApplyStepOnHitStatusEffect(ctx);
            });
        }

        /// <summary>
        /// Evaluates and applies any configured on-hit status effect defined on the attack step.
        /// </summary>
        public static void ApplyStepOnHitStatusEffect(DamageContext ctx)
        {
            if (ctx == null || ctx.Target == null || ctx.IsPreview || ctx.IsCancelled || ctx.IsKillingBlow) return;

            if (WeaponAttackRegistry.TryGetHitAttack(ctx.Hit, out var atkInfo) && atkInfo.Profile != null)
            {
                var step = atkInfo.Profile.GetStep(atkInfo.StepKey);
                if (!string.IsNullOrEmpty(step.OnHitStatusEffect))
                {
                    var applied = StatusEffects.StatusEffectRegistry.ApplySlice(
                        ctx.Target,
                        step.OnHitStatusEffect,
                        step.StatusEffectValue,
                        duration: step.StatusEffectDuration,
                        source: ctx.Attacker);

                    if (applied != null)
                    {
                        ctx.Trace?.RecordStep(DamagePhase.PostHitProcs, "Attack Step Status Effect",
                            $"Applied {step.OnHitStatusEffect} (val: {step.StatusEffectValue:F1}) for {step.StatusEffectDuration:F1}s", step.StatusEffectValue);
                    }
                }
            }
        }

        /// <summary>
        /// Evaluates and applies the physical triad Pierce Precision multiplier (Tier 3) to melee pierce damage on the context.
        /// Ranged attacks (Bows/Crossbows) and non-player enemies are excluded.
        /// </summary>
        public static void ApplyPiercePrecision(DamageContext ctx)
        {
            if (ctx == null || !ModConfig.StaggerOverhaulEnabled.Value || !ModConfig.EnablePiercePrecision.Value) return;

            // Only player attacks gain pierce precision (enemies do not get damage bonuses against players)
            if (ctx.Attacker == null || !ctx.Attacker.IsPlayer()) return;

            // Exclude ranged attacks (Bows, Crossbows) from melee pierce precision
            if (ctx.Hit != null)
            {
                bool isRanged = ctx.Hit.m_ranged ||
                                ctx.Hit.m_skill == Skills.SkillType.Bows ||
                                ctx.Hit.m_skill == Skills.SkillType.Crossbows;
                if (isRanged) return;

                if (WeaponAttackRegistry.TryGetHitAttack(ctx.Hit, out var atkInfo) && atkInfo.Weapon != null)
                {
                    if (atkInfo.Weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow)
                    {
                        return;
                    }
                }
            }

            if (ctx.GetDamage(DamageType.Pierce) > 0.001f)
            {
                float pierceMult = ModConfig.PiercePrecisionMultiplier.Value;
                if (Mathf.Abs(pierceMult - 1.0f) > 0.001f)
                {
                    ctx.AddMultiplier(pierceMult, $"Pierce Precision ({pierceMult:F2}x)", DamageType.Pierce);
                }
            }
        }

        /// <summary>
        /// Evaluates post-mitigation slash damage and inflicts a Multi-Slice Bleed status effect slice over 10s.
        /// Only player attacks inflict bleed lacerations; non-player enemies do not bleed the player.
        /// </summary>
        public static void ApplySlashBleed(DamageContext ctx)
        {
            if (ctx == null || !ModConfig.StaggerOverhaulEnabled.Value || !ModConfig.EnableSlashBleed.Value ||
                ctx.Target == null || ctx.IsPreview || ctx.IsCancelled || ctx.IsKillingBlow) return;

            // Only player attacks inflict slash bleed lacerations (enemies do not bleed the player)
            if (ctx.Attacker == null || !ctx.Attacker.IsPlayer()) return;

            float finalSlash = ctx.GetDamage(DamageType.Slash);
            if (finalSlash > 0.01f && Defense.EntityDefenseRegistry.CanBleed(ctx.Target))
            {
                float slashBleedPercent = ModConfig.SlashBleedPercent.Value;
                if (ctx.Attacker != null)
                {
                    slashBleedPercent += CharacterStatManager.GetStat(ctx.Attacker, CharacterStatType.SlashBleedPercent);
                }

                if (slashBleedPercent > 0.001f)
                {
                    float bleedAmount = finalSlash * slashBleedPercent;
                    var applied = StatusEffects.StatusEffectRegistry.ApplySlice(
                        ctx.Target,
                        StatusEffects.CanonicalStatusEffects.BleedId,
                        bleedAmount,
                        duration: 10f,
                        source: ctx.Attacker);

                    if (applied != null)
                    {
                        ctx.Trace?.RecordStep(DamagePhase.PostHitProcs, "Slash Bleed Laceration",
                            $"Applied {bleedAmount:F1} Bleed over 10s ({slashBleedPercent * 100f:F0}% of {finalSlash:F1} post-mitigation slash)", bleedAmount);
                    }
                }
            }
        }

        private static void RestoreAttackerResource(DamageContext ctx, float amount, string label, bool stamina)
        {
            if (amount <= 0f || ctx.Attacker is not Player player || !player.IsOwner()) return;

            if (stamina) player.AddStamina(amount);
            else player.AddEitr(amount);

            ctx.Trace?.RecordStep(DamagePhase.PostHitProcs, label, $"Restored +{amount:F1} {(stamina ? "stamina" : "eitr")}", amount);
        }

        private static void RestoreDefenderResource(DamageContext ctx, float amount, string label, bool stamina)
        {
            if (amount <= 0f || ctx.Target is not Player player || !player.IsOwner()) return;

            if (stamina) player.AddStamina(amount);
            else player.AddEitr(amount);

            ctx.Trace?.RecordStep(DamagePhase.TargetDefenses, label, $"Restored +{amount:F1} {(stamina ? "stamina" : "eitr")}", amount);
        }

        private static void ApplyStatTraceToContext(DamageContext ctx, CharacterStatType statType, DamageType? targetDamageType = null)
        {
            if (ctx.Attacker == null) return;
            var trace = CharacterStatManager.GetTrace(ctx.Attacker, statType);

            if (trace.FlatModifiers.Count == 0 && trace.IncreasedModifiers.Count == 0 && trace.Multipliers.Count == 0)
                return;

            foreach (var flat in trace.FlatModifiers)
            {
                ctx.AddFlat(flat.Value, flat.Source, targetDamageType);
            }
            foreach (var inc in trace.IncreasedModifiers)
            {
                ctx.AddIncreasedPercent(inc.Value, inc.Source, targetDamageType);
            }
            foreach (var mult in trace.Multipliers)
            {
                ctx.AddMultiplier(mult.Value, mult.Source, targetDamageType);
            }
        }

        private static bool IsAfflicted(Character? target)
        {
            if (target == null) return false;
            if (target.IsStaggering()) return true;

            var seman = target.GetSEMan();
            if (seman == null) return false;

            if (seman.HaveStatusEffect(SEMan.s_statusEffectBurning) ||
                seman.HaveStatusEffect(SEMan.s_statusEffectPoison) ||
                seman.HaveStatusEffect(SEMan.s_statusEffectFrost) ||
                seman.HaveStatusEffect(SEMan.s_statusEffectLightning) ||
                seman.HaveStatusEffect(SEMan.s_statusEffectSpirit) ||
                seman.HaveStatusEffect(SEMan.s_statusEffectTared) ||
                seman.HaveStatusEffect(SEMan.s_statusEffectFreezing))
            {
                return true;
            }

            var list = seman.GetStatusEffects();
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var se = list[i];
                    if (se is SE_Stats stats && (stats.m_speedModifier < 0f || stats.m_healthRegenMultiplier < 1f || stats.m_staminaRegenMultiplier < 1f))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
