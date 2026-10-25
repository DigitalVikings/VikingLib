using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;
using VikingLib.Combat.Attacks;
using VikingLib.Combat.Defense;
using VikingLib.Combat.Roll;
using VikingLib.Combat.Telemetry;
using VikingLib.Configuration;
using VikingLib.Patches;
using VikingLib.Stats;

namespace VikingLib.Combat
{
    /// <summary>
    /// Phase handler delegate for inspecting and modifying a combat hit.
    /// </summary>
    public delegate void DamagePhaseHandler(DamageContext context);

    /// <summary>
    /// Core calculation engine managing ordered phases, priority-sorted handlers, vanilla defaults,
    /// and diagnostic telemetry.
    /// </summary>
    public static class DamagePipeline
    {
        private class PrioritizedHandler
        {
            public int Priority { get; }
            public DamagePhaseHandler Handler { get; }

            public PrioritizedHandler(int priority, DamagePhaseHandler handler)
            {
                Priority = priority;
                Handler = handler;
            }
        }

        private static readonly Dictionary<DamagePhase, List<PrioritizedHandler>> _phaseHandlers = new();
        private static HarmonyLib.AccessTools.FieldRef<Character, float>? _backstabTimeRef;
        private static HarmonyLib.AccessTools.FieldRef<Character, float> BackstabTimeRef =>
            _backstabTimeRef ??= HarmonyLib.AccessTools.FieldRefAccess<Character, float>("m_backstabTime");

        /// <summary>
        /// Indicates whether VikingLib is currently executing a pipeline pass on the active thread.
        /// </summary>
        public static bool IsActive => CurrentContext != null;

        /// <summary>
        /// The currently active DamageContext being processed.
        /// </summary>
        public static DamageContext? CurrentContext { get; private set; }

        /// <summary>
        /// The significant DamageModifier calculated for the current hit, used for native damage text color.
        /// </summary>
        public static HitData.DamageModifier CurrentSignificantModifier { get; internal set; } = HitData.DamageModifier.Normal;

        /// <summary>
        /// The most recent damage trace captured, available for in-game inspection.
        /// </summary>
        public static DamageTrace? LastTrace { get; private set; }

        /// <summary>
        /// Event fired whenever a complete combat hit trace is recorded and finalized.
        /// </summary>
        public static event Action<DamageTrace>? OnHitTraced;

        /// <summary>
        /// Fired in Phase 2 (TargetDefenses) when a Character is about to take a hit. Subscribers may still
        /// add modifiers to the context (e.g. reactive armor) because this fires before Phase 2 modifiers resolve.
        /// Fires on the target's owning client only.
        /// </summary>
        public static event Action<DamageContext>? OnHitTaken;

        /// <summary>
        /// Fired at the end of Phase 5 (Execution) when the final damage would kill the target Character.
        /// Subscribers may call <see cref="DamageContext.DefyDeath"/> to survive at 1 HP ("Last Stand").
        /// </summary>
        public static event Action<DamageContext>? OnLethalHit;

        /// <summary>
        /// Fired in Phase 6 (PostHitProcs) after a hit killed its Character target. Only fires when an attacker is known.
        /// </summary>
        public static event Action<Character, Character, DamageContext>? OnKill;

        /// <summary>
        /// Fired in Phase 6 (PostHitProcs) when a hit breaks the target's poise and causes stagger.
        /// </summary>
        public static event Action<Character, Character, DamageContext>? OnCauseStagger;

        /// <summary>
        /// Fired in Phase 6 (PostHitProcs) when a hit targets an already-staggering victim.
        /// </summary>
        public static event Action<Character, Character, DamageContext>? OnStaggerHit;

        /// <summary>
        /// Fired in Phase 6 (PostHitProcs) when a hit lands a critical strike.
        /// </summary>
        public static event Action<Character, Character, DamageContext>? OnCriticalHit;

        /// <summary>
        /// Fired when an attack is successfully parried.
        /// </summary>
        public static event Action<Character, Character, HitData>? OnParry;

        /// <summary>
        /// Fired when an incoming attack is evaded via dodge invulnerability.
        /// </summary>
        public static event Action<Character, Character?, HitData>? OnDodge;

        /// <summary>
        /// Legacy event fired on stagger events. Prefer OnCauseStagger or OnStaggerHit.
        /// </summary>
        [Obsolete("Use OnCauseStagger or OnStaggerHit directly.")]
        public static event Action<Character, Character, DamageContext>? OnStagger;

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

        static DamagePipeline()
        {
            foreach (DamagePhase phase in Enum.GetValues(typeof(DamagePhase)))
            {
                _phaseHandlers[phase] = new List<PrioritizedHandler>();
            }

            RegisterDefaultHandlers();
        }

        /// <summary>
        /// Registers a handler for a specific pipeline phase with explicit priority.
        /// Lower priority numbers execute earlier. Standard default handlers run at priority 100.
        /// </summary>
        public static void RegisterHandler(DamagePhase phase, int priority, DamagePhaseHandler handler)
        {
            if (handler == null) return;
            var list = _phaseHandlers[phase];
            list.Add(new PrioritizedHandler(priority, handler));
            list.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        public static void RegisterTargetFilter(int priority, DamagePhaseHandler handler) =>
            RegisterHandler(DamagePhase.PreCheck, priority, handler);

        public static void RegisterAttackerModifier(int priority, DamagePhaseHandler handler) =>
            RegisterHandler(DamagePhase.AttackerModifiers, priority, handler);

        public static void RegisterTargetModifier(int priority, DamagePhaseHandler handler) =>
            RegisterHandler(DamagePhase.TargetDefenses, priority, handler);

        public static void RegisterMitigationHandler(int priority, DamagePhaseHandler handler) =>
            RegisterHandler(DamagePhase.Mitigation, priority, handler);

        public static void RegisterPostHitProc(int priority, DamagePhaseHandler handler) =>
            RegisterHandler(DamagePhase.PostHitProcs, priority, handler);

        /// <summary>
        /// Executes all registered handlers for a specific pipeline phase on the provided context.
        /// </summary>
        public static void ExecutePhaseHandlers(DamagePhase phase, DamageContext ctx)
        {
            if (ctx == null) return;
            ctx.CurrentPhase = phase;
            if (_phaseHandlers.TryGetValue(phase, out var handlers))
            {
                for (int i = 0; i < handlers.Count; i++)
                {
                    handlers[i].Handler(ctx);
                    if (ctx.IsCancelled) break;
                }
            }
        }

        /// <summary>
        /// Registers default vanilla-equivalent mechanics (crits, backstabs, stagger bonus, armor, resistances).
        /// </summary>
        private static void RegisterDefaultHandlers()
        {
            // Phase 1 Default: Multi-Target Cleave Scaling & Pickaxe Chunk Split
            RegisterHandler(DamagePhase.AttackerModifiers, priority: 50, ctx =>
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
            RegisterHandler(DamagePhase.AttackerModifiers, priority: 75, ctx =>
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
            RegisterHandler(DamagePhase.AttackerModifiers, priority: 80, ApplyPiercePrecision);

            // Phase 2 Default: Crits, Stealth Sneak Attack, and Stagger Punish
            RegisterHandler(DamagePhase.AttackerModifiers, priority: 100, ctx =>
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
            RegisterHandler(DamagePhase.TargetDefenses, priority: 50, ctx =>
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

                float preBlockDamage = ctx.GetTotalDamage();
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
                        // 100% absorption / deflection: completely neutralize all damage
                        damageBlocked = preBlockDamage;
                        foreach (DamageType dt in Enum.GetValues(typeof(DamageType)))
                        {
                            ctx.SetDamage(dt, 0f);
                        }
                    }
                    else
                    {
                        // Partial block: absorb effectiveBlockPower, remainder penetrates to body armor
                        damageBlocked = effectiveBlockPower;
                        float remainingRatio = Mathf.Max(0f, (preBlockDamage - effectiveBlockPower) / preBlockDamage);
                        foreach (DamageType dt in Enum.GetValues(typeof(DamageType)))
                        {
                            float current = ctx.GetDamage(dt);
                            if (current > 0f)
                            {
                                ctx.SetDamage(dt, current * remainingRatio);
                            }
                        }
                    }

                    ctx.WriteBackToHitData();
                    float postBlockDamage = ctx.GetTotalDamage();

                    ctx.Trace?.RecordStep(DamagePhase.TargetDefenses, label,
                        $"{preBlockDamage:F1} -> {postBlockDamage:F1} (-{damageBlocked:F1} blocked by {blockerName})", damageBlocked);
                }
            });

            // Phase 2 Default B: Defensive Procs & Resource Recovery On Damage Taken (3x3 Matrix with 1s ICD)
            RegisterHandler(DamagePhase.TargetDefenses, priority: 100, ctx =>
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
                    VikingLib.StatusEffects.StatusEffectRegistry.ConsumeCharges(ctx.Target, VikingLib.StatusEffects.ChargeConsumeTrigger.DamageTaken);
                }
            });

            // Phase 3 Default: Granular Armor & Continuous Resistances
            RegisterHandler(DamagePhase.Mitigation, priority: 100, ctx =>
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

                CurrentSignificantModifier = EntityDefenseRegistry.ToVanillaModifier(minResistMult);
                ctx.FlushMitigationTrace();

                // C. Damage Absorption Barriers (Shields)
                if (ctx.Target != null && !ctx.IsPreview && !ctx.IsCancelled)
                {
                    VikingLib.StatusEffects.StatusEffectRegistry.AbsorbDamage(ctx);
                }
            });

            // Phase 6 Default: Kill & Stagger Resource Restores (3x3 Matrix with 1s ICD)
            RegisterHandler(DamagePhase.PostHitProcs, priority: 100, ctx =>
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
                    VikingLib.StatusEffects.StatusEffectRegistry.ConsumeCharges(ctx.Attacker, VikingLib.StatusEffects.ChargeConsumeTrigger.MeleeHitDelivered);
                }
                if (ctx.Flags.HasFlag(DamageFlags.Ranged))
                {
                    VikingLib.StatusEffects.StatusEffectRegistry.ConsumeCharges(ctx.Attacker, VikingLib.StatusEffects.ChargeConsumeTrigger.RangedHitDelivered);
                }
                VikingLib.StatusEffects.StatusEffectRegistry.ConsumeCharges(ctx.Attacker, VikingLib.StatusEffects.ChargeConsumeTrigger.AnyHitDelivered);

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

        private static void RestoreAttackerResource(DamageContext ctx, CharacterStatType statType, string label, bool stamina)
        {
            if (ctx.Attacker == null) return;
            float amount = CharacterStatManager.GetStat(ctx.Attacker, statType);
            RestoreAttackerResource(ctx, amount, label, stamina);
        }

        private static void RestoreDefenderResource(DamageContext ctx, float amount, string label, bool stamina)
        {
            if (amount <= 0f || ctx.Target is not Player player || !player.IsOwner()) return;

            if (stamina) player.AddStamina(amount);
            else player.AddEitr(amount);

            ctx.Trace?.RecordStep(DamagePhase.TargetDefenses, label, $"Restored +{amount:F1} {(stamina ? "stamina" : "eitr")}", amount);
        }

        /// <summary>
        /// Resolves the attacker character for a hit, falling back to local player if the ZDO matches or if local.
        /// </summary>
        public static Character? ResolveAttacker(HitData hit)
        {
            Character? attacker = hit.GetAttacker();
            if (attacker == null && Player.m_localPlayer != null)
            {
                if (hit.m_attacker.IsNone() || hit.m_attacker == Player.m_localPlayer.GetZDOID())
                {
                    attacker = Player.m_localPlayer;
                }
            }
            return attacker;
        }

        /// <summary>
        /// Executes the pipeline for a hit targeting a Character.
        /// </summary>
        public static DamageContext ProcessCharacterHit(Character target, HitData hit)
        {
            Character? attacker = ResolveAttacker(hit);
            DamageTrace? trace = ShouldLogTrace(attacker, target)
                ? new DamageTrace(attacker != null ? attacker.GetHoverName() : "Unknown", target.GetHoverName(), hit.m_damage)
                : null;

            PopulateWeaponTraceMetadata(trace, attacker, hit);

            var ctx = new DamageContext(hit, attacker, target, trace);
            CurrentContext = ctx;

            ExecutePipeline(ctx);
            return ctx;
        }

        /// <summary>
        /// Executes the pipeline for a hit targeting a piece or destructible.
        /// </summary>
        public static DamageContext ProcessPieceHit(WearNTear piece, HitData hit)
        {
            Character? attacker = ResolveAttacker(hit);
            DamageTrace? trace = ShouldLogTrace(attacker, null)
                ? new DamageTrace(attacker != null ? attacker.GetHoverName() : "Unknown", piece.gameObject.name, hit.m_damage)
                : null;

            PopulateWeaponTraceMetadata(trace, attacker, hit);

            var ctx = new DamageContext(hit, attacker, piece, trace);
            CurrentContext = ctx;

            ExecutePipeline(ctx);
            return ctx;
        }

        /// <summary>
        /// Executes the pipeline for a hit targeting an environmental destructible (TreeBase, TreeLog, Destructible, MineRock, MineRock5).
        /// </summary>
        public static DamageContext ProcessDestructibleHit(IDestructible destructible, HitData hit, string? targetDisplayName = null)
        {
            Character? attacker = ResolveAttacker(hit) ?? Player.m_localPlayer;
            string targetName = targetDisplayName ?? (destructible is UnityEngine.Component c ? c.gameObject.name : destructible.GetType().Name);

            DamageTrace? trace = ShouldLogTrace(attacker, null)
                ? new DamageTrace(attacker != null ? attacker.GetHoverName() : "Unknown", targetName, hit.m_damage)
                : null;

            PopulateWeaponTraceMetadata(trace, attacker, hit);

            var defense = EntityDefenseRegistry.GetDefense(destructible);
            var ctx = new DamageContext(hit, attacker, destructible, defense, trace);
            CurrentContext = ctx;

            ExecutePipeline(ctx);
            return ctx;
        }

        /// <summary>
        /// Executes the pipeline for a hit colliding with terrain/ground, logging the absorbed cleave step.
        /// </summary>
        public static DamageContext ProcessTerrainHit(Character? attacker, HitData hit, string targetDisplayName = "Terrain (Ground)")
        {
            DamageTrace? trace = ShouldLogCombatTrace(attacker, null)
                ? new DamageTrace(attacker != null ? attacker.GetHoverName() : "Unknown", targetDisplayName, hit.m_damage)
                : null;

            PopulateWeaponTraceMetadata(trace, attacker, hit);

            var ctx = new DamageContext(hit, attacker, (IDestructible?)null, new EntityDefenseData(), trace);
            CurrentContext = ctx;

            ExecutePipeline(ctx);
            FinalizeHit(ctx);
            return ctx;
        }

        private static void PopulateWeaponTraceMetadata(DamageTrace? trace, Character? attacker, HitData hit)
        {
            if (trace == null || attacker == null) return;

            trace.AttackerLevel = attacker.GetLevel();
            if (!attacker.IsPlayer() && trace.AttackerLevel > 1)
            {
                trace.CreatureLevelMultiplier = 1f + (trace.AttackerLevel - 1) * 0.5f;
            }

            ItemDrop.ItemData? weapon = null;
            AttackStepModifiers? stepMods = null;

            if (WeaponAttackRegistry.TryGetHitAttack(hit, out var atkInfo))
            {
                weapon = atkInfo.Weapon;
                trace.AttackStepKey = atkInfo.StepKey;
                stepMods = atkInfo.Profile.GetStep(atkInfo.StepKey);
                if (atkInfo.IsPickaxeSpecial && atkInfo.PickaxeChunkCount > 1)
                {
                    trace.AttackDisplayName = $"{atkInfo.DisplayName} (Chunk #{atkInfo.TargetIndex + 1}/{atkInfo.PickaxeChunkCount})";
                }
                else if (atkInfo.TargetCount > 1)
                {
                    trace.AttackDisplayName = $"{atkInfo.DisplayName} (Target #{atkInfo.TargetIndex + 1}/{atkInfo.TargetCount})";
                }
                else
                {
                    trace.AttackDisplayName = atkInfo.DisplayName;
                }
            }
            else if (DamageRollPipeline.LastRoll != null && !string.IsNullOrEmpty(DamageRollPipeline.LastRoll.AttackDisplayName))
            {
                trace.AttackStepKey = DamageRollPipeline.LastRoll.AttackKey;
                trace.AttackDisplayName = DamageRollPipeline.LastRoll.AttackDisplayName;
            }

            if (weapon == null && attacker is Humanoid humanoid)
            {
                weapon = humanoid.GetCurrentWeapon();
            }

            if (weapon != null)
            {
                trace.WeaponName = Localization.instance != null
                    ? Localization.instance.Localize(weapon.m_shared.m_name)
                    : weapon.m_shared.m_name;
                trace.WeaponBaseDamage = weapon.GetDamage();
                trace.SkillType = weapon.m_shared.m_skillType;
                if (attacker is Player player)
                {
                    trace.SkillLevel = player.GetSkillLevel(trace.SkillType);
                }

                if (stepMods == null && !string.IsNullOrEmpty(trace.AttackStepKey))
                {
                    var profile = WeaponAttackRegistry.GetProfile(weapon);
                    stepMods = profile.GetStep(trace.AttackStepKey);
                }
            }

            // Scale WeaponBaseDamage by attack step multipliers so it accurately reflects the attack being performed
            if (stepMods != null && trace.WeaponBaseDamage != null)
            {
                var baseDmg = trace.WeaponBaseDamage.Value;
                AttackModifierPatch.ApplyAttackStepBaseDamage(ref baseDmg, stepMods);
                trace.WeaponBaseDamage = baseDmg;
            }

            // Align base physical damage types with the physical distribution of the incoming hit
            // (e.g. creature damage overrides or weapon damage conversions that mapped physical damage)
            if (trace.WeaponBaseDamage != null)
            {
                var baseDmg = trace.WeaponBaseDamage.Value;
                float hitPhys = hit.m_damage.m_blunt + hit.m_damage.m_slash + hit.m_damage.m_pierce;
                float basePhys = baseDmg.m_blunt + baseDmg.m_slash + baseDmg.m_pierce;

                if (hitPhys > 0.001f && basePhys > 0.001f)
                {
                    float hitBluntRatio = hit.m_damage.m_blunt / hitPhys;
                    float hitSlashRatio = hit.m_damage.m_slash / hitPhys;
                    float hitPierceRatio = hit.m_damage.m_pierce / hitPhys;

                    float baseBluntRatio = baseDmg.m_blunt / basePhys;
                    float baseSlashRatio = baseDmg.m_slash / basePhys;
                    float basePierceRatio = baseDmg.m_pierce / basePhys;

                    if (Mathf.Abs(hitBluntRatio - baseBluntRatio) > 0.01f ||
                        Mathf.Abs(hitSlashRatio - baseSlashRatio) > 0.01f ||
                        Mathf.Abs(hitPierceRatio - basePierceRatio) > 0.01f)
                    {
                        baseDmg.m_blunt = basePhys * hitBluntRatio;
                        baseDmg.m_slash = basePhys * hitSlashRatio;
                        baseDmg.m_pierce = basePhys * hitPierceRatio;
                        trace.WeaponBaseDamage = baseDmg;
                    }
                }
            }

            if (DamageRollPipeline.LastRoll != null && DamageRollPipeline.LastRoll.Attacker == attacker)
            {
                trace.RollLowerBound = DamageRollPipeline.LastRoll.LowerBound;
                trace.RollUpperBound = DamageRollPipeline.LastRoll.UpperBound;
                trace.SkillRollFactor = DamageRollPipeline.LastRoll.RolledFactor;
                trace.RollSteps.Clear();
                trace.RollSteps.AddRange(DamageRollPipeline.LastRoll.Steps);
            }
            else if (trace.WeaponBaseDamage != null)
            {
                float baseTotal = trace.WeaponBaseDamage.Value.GetTotalDamage();
                if (baseTotal > 0.01f)
                {
                    trace.SkillRollFactor = hit.GetTotalDamage() / baseTotal;
                }
            }
        }

        private static void ExecutePipeline(DamageContext ctx)
        {
            if (ctx.Target != null)
            {
                ctx.TargetWasStaggering = ctx.Target.IsStaggering();
            }

            for (int p = (int)DamagePhase.PreCheck; p <= (int)DamagePhase.Execution; p++)
            {
                DamagePhase phase = (DamagePhase)p;
                ctx.CurrentPhase = phase;

                var handlers = _phaseHandlers[phase];
                for (int i = 0; i < handlers.Count; i++)
                {
                    handlers[i].Handler(ctx);
                    if (ctx.IsCancelled) break;
                }

                // Phase 2 hook: fired before modifiers resolve so reactive perks can still contribute modifiers.
                if (phase == DamagePhase.TargetDefenses && !ctx.IsCancelled && ctx.Target != null && !ctx.IsPreview)
                {
                    RaiseEvent(OnHitTaken, ctx);
                }

                ctx.ResolvePhaseModifiers();
                if (ctx.IsCancelled) break;

                // Phase 5 hook: damage is final here; subscribers may call ctx.DefyDeath().
                if (phase == DamagePhase.Execution && ctx.Target != null && !ctx.IsPreview)
                {
                    float health = ctx.Target.GetHealth();
                    if (health > 0f && ctx.GetTotalDamage() >= health)
                    {
                        ctx.IsLethal = true;
                        RaiseEvent(OnLethalHit, ctx);
                    }
                }
            }

            ctx.WriteBackToHitData();
        }

        private static void RaiseEvent(Action<DamageContext>? evt, DamageContext ctx)
        {
            if (evt == null) return;
            foreach (var d in evt.GetInvocationList())
            {
                try { ((Action<DamageContext>)d)(ctx); }
                catch (Exception ex) { VikingLibPlugin.Log.LogError($"[VikingLib] Combat event subscriber threw: {ex}"); }
            }
        }

        private static void RaiseEvent(Action<Character, Character, DamageContext>? evt, Character attacker, Character victim, DamageContext ctx)
        {
            if (evt == null) return;
            foreach (var d in evt.GetInvocationList())
            {
                try { ((Action<Character, Character, DamageContext>)d)(attacker, victim, ctx); }
                catch (Exception ex) { VikingLibPlugin.Log.LogError($"[VikingLib] Combat event subscriber threw: {ex}"); }
            }
        }

        internal static void RaiseParry(Character defender, Character attacker, HitData hit)
        {
            if (OnParry == null) return;
            foreach (var d in OnParry.GetInvocationList())
            {
                try { ((Action<Character, Character, HitData>)d)(defender, attacker, hit); }
                catch (Exception ex) { VikingLibPlugin.Log.LogError($"[VikingLib] OnParry subscriber threw: {ex}"); }
            }
        }

        internal static void RaiseDodge(Character defender, Character? attacker, HitData hit)
        {
            if (OnDodge == null) return;
            foreach (var d in OnDodge.GetInvocationList())
            {
                try { ((Action<Character, Character?, HitData>)d)(defender, attacker, hit); }
                catch (Exception ex) { VikingLibPlugin.Log.LogError($"[VikingLib] OnDodge subscriber threw: {ex}"); }
            }
        }

        /// <summary>
        /// Finalizes the hit: runs Phase 6 (PostHitProcs), logs telemetry, and cleans up context.
        /// </summary>
        public static void FinalizeHit(DamageContext ctx)
        {
            try
            {
                ctx.CurrentPhase = DamagePhase.PostHitProcs;

                if (ctx.Target != null && !ctx.IsPreview && !ctx.IsCancelled)
                {
                    ctx.IsKillingBlow = ctx.Target.IsDead() || ctx.Target.GetHealth() <= 0f;
                }

                var handlers = _phaseHandlers[DamagePhase.PostHitProcs];
                for (int i = 0; i < handlers.Count; i++)
                {
                    handlers[i].Handler(ctx);
                }
                ctx.ResolvePhaseModifiers();

                if (ctx.Target != null && ctx.Attacker != null && !ctx.IsPreview && !ctx.IsCancelled)
                {
                    if (ctx.Flags.HasFlag(DamageFlags.CriticalHit))
                    {
                        RaiseEvent(OnCriticalHit, ctx.Attacker, ctx.Target, ctx);
                    }

                    if (ctx.IsKillingBlow)
                    {
                        RaiseEvent(OnKill, ctx.Attacker, ctx.Target, ctx);
                    }
                    else
                    {
                        if (ctx.CausedStagger)
                        {
                            RaiseEvent(OnCauseStagger, ctx.Attacker, ctx.Target, ctx);
                            RaiseEvent(OnStagger, ctx.Attacker, ctx.Target, ctx); // Legacy compatibility
                        }

                        if (ctx.TargetWasStaggering)
                        {
                            RaiseEvent(OnStaggerHit, ctx.Attacker, ctx.Target, ctx);
                        }
                    }
                }

                if (ctx.Trace != null)
                {
                    LastTrace = ctx.Trace;
                    string report = ctx.Trace.ToFormattedReport();

                    // Print formatted report directly to BepInEx Log and Console
                    VikingLibPlugin.Log.LogInfo(report);

                    // Optionally display short summary in player in-game chat/message (only for tangible targets)
                    if (ModConfig.ShowCombatChatFeedback.Value && ctx.Attacker is Player &&
                        (ctx.Target != null || ctx.DestructibleTarget != null || ctx.TargetPiece != null))
                    {
                        string msg = $"[VikingLib] Dealt {ctx.GetTotalDamage():F1} dmg to {ctx.Trace.TargetName}";
                        if (ctx.Flags.HasFlag(DamageFlags.CriticalHit)) msg += " (CRIT!)";
                        MessageHud.instance?.ShowMessage(MessageHud.MessageType.TopLeft, msg);
                    }

                    OnHitTraced?.Invoke(ctx.Trace);
                }
            }
            finally
            {
                if (CurrentContext == ctx)
                {
                    CurrentContext = null;
                }
            }
        }

        public static bool ShouldLogCombatTrace(Character? attacker, Character? target)
        {
            var mode = ModConfig.LoggingMode.Value;
            if (mode == DamageLoggingMode.Off) return false;
            if (mode == DamageLoggingMode.AllCombat) return true;

            // PlayerOnly: check if local player is involved
            if (Player.m_localPlayer != null)
            {
                if (attacker == Player.m_localPlayer || target == Player.m_localPlayer)
                    return true;
            }
            return false;
        }

        private static bool ShouldLogTrace(Character? attacker, Character? target) => ShouldLogCombatTrace(attacker, target);

        /// <summary>
        /// Evaluates a weapon's resulting min and max damage range across all damage types
        /// by passing lower and upper bounds through Phase 1 (AttackerModifiers).
        /// Used by the tooltip system to display real-world practical damage ranges.
        /// </summary>
        public static void EvaluatePreviewDamages(
            Character? attacker,
            ItemDrop.ItemData? weapon,
            HitData.DamageTypes baseDamages,
            Skills.SkillType skillType,
            out Dictionary<DamageType, (float Min, float Max)> ranges,
            out float totalMin,
            out float totalMax,
            string stepKey = "primary_1")
        {
            ranges = new Dictionary<DamageType, (float Min, float Max)>();
            totalMin = 0f;
            totalMax = 0f;

            // 1. Evaluate deterministic roll bounds
            var rollCtx = VikingLib.Combat.Roll.DamageRollPipeline.EvaluateBounds(attacker, skillType, weapon);
            float lowerBound = rollCtx.LowerBound;
            float upperBound = rollCtx.UpperBound;

            // 2. Build min and max HitData scaled by roll bounds
            HitData minHit = new HitData { m_damage = ScaleDamageTypes(baseDamages, lowerBound), m_skill = skillType };
            if (attacker != null) minHit.SetAttacker(attacker);

            HitData maxHit = new HitData { m_damage = ScaleDamageTypes(baseDamages, upperBound), m_skill = skillType };
            if (attacker != null) maxHit.SetAttacker(attacker);

            // Register preview attack info and scale base damage by the attack step's multipliers
            if (weapon != null)
            {
                var profile = WeaponAttackRegistry.GetProfile(weapon);
                var stepMods = profile.GetStep(stepKey);
                AttackModifierPatch.ApplyAttackStepBaseDamage(ref minHit.m_damage, stepMods);
                AttackModifierPatch.ApplyAttackStepBaseDamage(ref maxHit.m_damage, stepMods);

                var info = new WeaponAttackInfo(stepKey, stepKey, weapon, profile);
                WeaponAttackRegistry.RegisterHitAttack(minHit, info);
                WeaponAttackRegistry.RegisterHitAttack(maxHit, info);
            }

            // 3. Create preview contexts
            var minCtx = new DamageContext(minHit, attacker, (Character?)null) { IsPreview = true, CurrentPhase = DamagePhase.AttackerModifiers };
            var maxCtx = new DamageContext(maxHit, attacker, (Character?)null) { IsPreview = true, CurrentPhase = DamagePhase.AttackerModifiers };

            // 4. Run Phase 1 AttackerModifiers handlers
            if (_phaseHandlers.TryGetValue(DamagePhase.AttackerModifiers, out var handlers))
            {
                for (int i = 0; i < handlers.Count; i++)
                {
                    try
                    {
                        handlers[i].Handler(minCtx);
                        handlers[i].Handler(maxCtx);
                    }
                    catch (Exception ex)
                    {
                        VikingLibPlugin.Log?.LogWarning($"[VikingLib Tooltip Preview] Handler exception: {ex.Message}");
                    }
                }
            }

            minCtx.ResolvePhaseModifiers();
            maxCtx.ResolvePhaseModifiers();

            // 5. Accumulate resulting ranges per damage type
            foreach (DamageType type in Enum.GetValues(typeof(DamageType)))
            {
                float minVal = minCtx.GetDamage(type);
                float maxVal = maxCtx.GetDamage(type);

                if (minVal > 0.01f || maxVal > 0.01f)
                {
                    ranges[type] = (minVal, maxVal);
                    totalMin += minVal;
                    totalMax += maxVal;
                }
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

        private static HitData.DamageTypes ScaleDamageTypes(HitData.DamageTypes dt, float factor)
        {
            var copy = dt.Clone();
            copy.m_blunt *= factor;
            copy.m_slash *= factor;
            copy.m_pierce *= factor;
            copy.m_chop *= factor;
            copy.m_pickaxe *= factor;
            copy.m_fire *= factor;
            copy.m_frost *= factor;
            copy.m_lightning *= factor;
            copy.m_poison *= factor;
            copy.m_spirit *= factor;
            copy.m_damage *= factor;
            return copy;
        }

        /// <summary>
        /// Dispatches a reflected retaliation hit back to the original attacker.
        /// The reflected hit undergoes standard mitigation (armor and resistance on the attacker)
        /// and is tagged with <see cref="DamageFlags.IsReflected"/> to prevent recursive reflection loops.
        /// </summary>
        /// <param name="defender">The character reflecting the hit.</param>
        /// <param name="attacker">The original attacking character receiving the reflected damage.</param>
        /// <param name="originalCtx">The original combat context being reflected.</param>
        /// <param name="ratio">Fraction of damage to reflect back (e.g. 0.50f for 50%).</param>
        /// <param name="overrideType">Optional specific damage type to reflect as. If null, reflects proportional to incoming damage.</param>
        /// <param name="source">Attribution label for telemetry audits.</param>
        /// <returns>True if the reflected hit was successfully dispatched; false otherwise.</returns>
        public static bool ReflectDamage(Character defender, Character attacker, DamageContext originalCtx, float ratio, DamageType? overrideType = null, string source = "Damage Reflection")
        {
            if (defender == null || attacker == null || originalCtx == null) return false;
            if (attacker.IsDead() || defender.IsDead()) return false;
            if ((originalCtx.Flags & DamageFlags.IsReflected) != 0) return false; // Prevent recursive reflection loops

            float totalDamage = originalCtx.GetTotalDamage();
            float toReflect = totalDamage * Mathf.Max(0f, ratio);
            if (toReflect <= 0.05f) return false;

            var hit = new HitData
            {
                m_point = attacker.GetCenterPoint(),
                m_dir = (attacker.transform.position - defender.transform.position).normalized,
                m_hitType = HitData.HitType.Undefined,
                m_attacker = defender.GetZDOID(),
                m_blockable = true,
                m_dodgeable = true,
                m_pushForce = 0f
            };

            if (overrideType.HasValue)
            {
                SetHitDamageByType(ref hit.m_damage, overrideType.Value, toReflect);
            }
            else
            {
                float factor = toReflect / totalDamage;
                hit.m_damage.m_blunt = originalCtx.GetDamage(DamageType.Blunt) * factor;
                hit.m_damage.m_slash = originalCtx.GetDamage(DamageType.Slash) * factor;
                hit.m_damage.m_pierce = originalCtx.GetDamage(DamageType.Pierce) * factor;
                hit.m_damage.m_fire = originalCtx.GetDamage(DamageType.Fire) * factor;
                hit.m_damage.m_frost = originalCtx.GetDamage(DamageType.Frost) * factor;
                hit.m_damage.m_lightning = originalCtx.GetDamage(DamageType.Lightning) * factor;
                hit.m_damage.m_poison = originalCtx.GetDamage(DamageType.Poison) * factor;
                hit.m_damage.m_spirit = originalCtx.GetDamage(DamageType.Spirit) * factor;
            }

            var attackInfo = new WeaponAttackInfo("reflected_retaliation", source)
            {
                Flags = DamageFlags.IsReflected | DamageFlags.CanTriggerProcs
            };
            Attacks.WeaponAttackRegistry.RegisterHitAttack(hit, attackInfo);

            attacker.Damage(hit);
            return true;
        }

        private static void SetHitDamageByType(ref HitData.DamageTypes dt, DamageType type, float amount)
        {
            switch (type)
            {
                case DamageType.Blunt: dt.m_blunt = amount; break;
                case DamageType.Slash: dt.m_slash = amount; break;
                case DamageType.Pierce: dt.m_pierce = amount; break;
                case DamageType.Chop: dt.m_chop = amount; break;
                case DamageType.Pickaxe: dt.m_pickaxe = amount; break;
                case DamageType.Fire: dt.m_fire = amount; break;
                case DamageType.Frost: dt.m_frost = amount; break;
                case DamageType.Lightning: dt.m_lightning = amount; break;
                case DamageType.Poison: dt.m_poison = amount; break;
                case DamageType.Spirit: dt.m_spirit = amount; break;
                case DamageType.TrueDamage: dt.m_damage = amount; break;
            }
        }

        /// <summary>
        /// Unified helper to apply a status effect on hit (supports both VikingLib multi-slice effects and native Valheim status effects).
        /// Automatically checks target biological immunities (e.g. CanBleed) and scales duration/magnitude by target elemental resistances.
        /// </summary>
        /// <param name="target">The target character to receive the status effect.</param>
        /// <param name="effectNameOrId">The status effect identifier or prefab name (e.g. "Vulnerable", "Bleed", "Frost", "Tarred").</param>
        /// <param name="value">Magnitude or slice value (e.g. absorption amount, bleed DPS, vulnerability fraction).</param>
        /// <param name="duration">Duration in seconds.</param>
        /// <param name="attacker">Optional attacker character for damage attribution or procs.</param>
        /// <summary>
        /// Overload accepting attacker first, target second: ApplyOnHitStatus(attacker, target, effectNameOrId, value, duration).
        /// </summary>
        public static bool ApplyOnHitStatus(Character? attacker, Character target, string effectNameOrId, float value, float duration)
            => ApplyOnHitStatus(target, effectNameOrId, value, duration, attacker);

        public static bool ApplyOnHitStatus(Character target, string effectNameOrId, float value, float duration, Character? attacker = null)
        {
            if (target == null || target.IsDead() || string.IsNullOrEmpty(effectNameOrId)) return false;

            // 1. Biological immunity check for Bleed
            if (effectNameOrId.Equals(VikingLib.StatusEffects.CanonicalStatusEffects.BleedId, StringComparison.OrdinalIgnoreCase))
            {
                if (!EntityDefenseRegistry.CanBleed(target)) return false;
                float bleedRes = CharacterStatManager.GetStat(target, CharacterStatType.ResistanceBleed);
                if (bleedRes <= 0.001f) return false;
                value *= bleedRes;
            }

            // 2. Multi-slice status effect branch
            if (VikingLib.StatusEffects.StatusEffectRegistry.GetDefinition(effectNameOrId) != null)
            {
                VikingLib.StatusEffects.StatusEffectRegistry.ApplySlice(target, effectNameOrId, value, duration);
                return true;
            }

            // 3. Native Valheim status effect branch
            var seman = target.GetSEMan();
            if (seman != null)
            {
                if (effectNameOrId.Equals("Frost", StringComparison.OrdinalIgnoreCase))
                {
                    float frostRes = CharacterStatManager.GetStat(target, CharacterStatType.ResistanceFrost);
                    if (frostRes <= 0.001f) return false;
                    duration *= frostRes;
                }
                else if (effectNameOrId.Equals("Poison", StringComparison.OrdinalIgnoreCase))
                {
                    float poisonRes = CharacterStatManager.GetStat(target, CharacterStatType.ResistancePoison);
                    if (poisonRes <= 0.001f) return false;
                    duration *= poisonRes;
                }

                var se = seman.AddStatusEffect(effectNameOrId.GetStableHashCode(), resetTime: true);
                if (se != null)
                {
                    if (duration > 0f) se.m_ttl = duration;
                    return true;
                }
            }

            return false;
        }
    }
}
