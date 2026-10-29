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

        static DamagePipeline()
        {
            foreach (DamagePhase phase in Enum.GetValues(typeof(DamagePhase)))
            {
                _phaseHandlers[phase] = new List<PrioritizedHandler>();
            }

            DefaultPhaseHandlers.RegisterAll();
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
        /// Evaluates and applies any configured on-hit status effect defined on the attack step.
        /// </summary>
        public static void ApplyStepOnHitStatusEffect(DamageContext ctx) => DefaultPhaseHandlers.ApplyStepOnHitStatusEffect(ctx);

        /// <summary>
        /// Evaluates and applies the physical triad Pierce Precision multiplier (Tier 3) to melee pierce damage on the context.
        /// </summary>
        public static void ApplyPiercePrecision(DamageContext ctx) => DefaultPhaseHandlers.ApplyPiercePrecision(ctx);

        /// <summary>
        /// Evaluates post-mitigation slash damage and inflicts a Multi-Slice Bleed status effect slice over 10s.
        /// </summary>
        public static void ApplySlashBleed(DamageContext ctx) => DefaultPhaseHandlers.ApplySlashBleed(ctx);

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
