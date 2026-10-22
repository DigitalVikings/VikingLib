using System;
using System.Collections.Generic;
using UnityEngine;
using VikingLib.Combat.Attacks;
using VikingLib.Configuration;
using VikingLib.SkillSystem;

namespace VikingLib.Combat.Roll
{
    /// <summary>
    /// Handler delegate for inspecting or modifying weapon roll bounds.
    /// </summary>
    public delegate void DamageRollHandler(DamageRollContext context);

    /// <summary>
    /// Extensible pipeline for evaluating weapon damage roll bounds.
    /// Establishes initial bounds [1.0, 1.0], applies composable scaling multipliers (vanilla skill or custom),
    /// and performs a single uniform roll between the resulting lower and upper bounds.
    /// </summary>
    public static class DamageRollPipeline
    {
        private class PrioritizedRollHandler
        {
            public int Priority { get; }
            public DamageRollHandler Handler { get; }

            public PrioritizedRollHandler(int priority, DamageRollHandler handler)
            {
                Priority = priority;
                Handler = handler;
            }
        }

        private static readonly List<PrioritizedRollHandler> _handlers = new();

        /// <summary>
        /// The active skill scaling provider. Can be replaced by custom mod implementations of ISkillScalingProvider.
        /// </summary>
        public static ISkillScalingProvider SkillScalingProvider { get; set; } = new VanillaSkillScalingProvider();

        /// <summary>
        /// Toggles whether skill scaling is active. When false, damage roll bounds are not scaled by skill.
        /// </summary>
        public static bool EnableSkillScaling { get; set; } = true;

        /// <summary>
        /// The most recent damage roll context evaluated, available for telemetry and debugging.
        /// </summary>
        public static DamageRollContext? LastRoll { get; private set; }

        static DamageRollPipeline()
        {
            RegisterDefaultHandlers();
        }

        /// <summary>
        /// Clears all registered roll handlers.
        /// </summary>
        public static void ClearHandlers()
        {
            _handlers.Clear();
        }

        /// <summary>
        /// Resets the pipeline handlers and restores default vanilla skill scaling.
        /// </summary>
        public static void ResetToDefaults()
        {
            _handlers.Clear();
            SkillScalingProvider = new VanillaSkillScalingProvider();
            EnableSkillScaling = true;
            RegisterDefaultHandlers();
        }

        /// <summary>
        /// Registers a handler for adjusting damage roll bounds.
        /// Lower priority numbers execute earlier. Default vanilla skill scaling runs at priority 100.
        /// </summary>
        public static void RegisterHandler(int priority, DamageRollHandler handler)
        {
            if (handler == null) return;
            _handlers.Add(new PrioritizedRollHandler(priority, handler));
            _handlers.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        }

        /// <summary>
        /// Registers the active skill scaling provider at priority 100 as composable multipliers.
        /// </summary>
        private static void RegisterDefaultHandlers()
        {
            RegisterHandler(priority: 100, ctx =>
            {
                if (!EnableSkillScaling || SkillScalingProvider == null) return;

                bool isPlayer = ctx.Attacker != null ? ctx.Attacker.IsPlayer() : (Player.m_localPlayer != null);

                // When using the default vanilla provider, respect VanillaSkillIntegration channel for player clumsiness scaling
                if (SkillScalingProvider is VanillaSkillScalingProvider && !VanillaSkillIntegration.EnableDamageRollScaling)
                {
                    if (!isPlayer)
                    {
                        // Creatures / mobs do not have player weapon skill clumsiness; they roll [CreatureDamageRollLowerBound .. CreatureDamageRollUpperBound]
                        ctx.LowerBound *= VanillaSkillIntegration.CreatureDamageRollLowerBound;
                        ctx.UpperBound *= VanillaSkillIntegration.CreatureDamageRollUpperBound;
                        ctx.RecordStep("Creature Variance", $"Scale Lower x{VanillaSkillIntegration.CreatureDamageRollLowerBound:F2}, Upper x{VanillaSkillIntegration.CreatureDamageRollUpperBound:F2}");
                        return;
                    }

                    ctx.LowerBound *= VanillaSkillIntegration.FixedDamageRollLowerBound;
                    ctx.UpperBound *= VanillaSkillIntegration.FixedDamageRollUpperBound;
                    ctx.RecordStep("Fixed Roll Baseline", $"Scale Lower x{VanillaSkillIntegration.FixedDamageRollLowerBound:F2}, Upper x{VanillaSkillIntegration.FixedDamageRollUpperBound:F2}");
                    return;
                }

                SkillScalingProvider.CalculateBounds(ctx, out float lowerMult, out float upperMult);
                ctx.LowerBound *= lowerMult;
                ctx.UpperBound *= upperMult;

                if (isPlayer)
                {
                    float skillLevel = ctx.Attacker != null
                        ? ctx.Attacker.GetSkillLevel(ctx.SkillType)
                        : (Player.m_localPlayer != null ? Player.m_localPlayer.GetSkillLevel(ctx.SkillType) : 0f);
                    string skillName = ctx.SkillType != Skills.SkillType.None
                        ? $"{ctx.SkillType} Skill ({skillLevel:F0})"
                        : "Player Skill";
                    ctx.RecordStep(skillName, $"Scale Lower x{lowerMult:F2}, Upper x{upperMult:F2}");
                }
                else
                {
                    ctx.RecordStep("Creature Variance", $"Scale Lower x{lowerMult:F2}, Upper x{upperMult:F2}");
                }
            });
        }

        /// <summary>
        /// Evaluates all prioritized roll handlers to compute the deterministic lower and upper bounds
        /// for an attacker and weapon without executing a random roll. Ideal for tooltips and previews.
        /// </summary>
        public static DamageRollContext EvaluateBounds(Character? attacker, Skills.SkillType skillType, ItemDrop.ItemData? weapon = null)
        {
            if (weapon == null && attacker is Humanoid humanoid)
            {
                weapon = humanoid.GetCurrentWeapon();
            }

            var ctx = new DamageRollContext(attacker, weapon, skillType);

            if (attacker is Humanoid hum)
            {
                var curAtk = HumanoidAttackHelper.GetCurrentAttack(hum);
                if (curAtk != null)
                {
                    if (HumanoidAttackHelper.IsCurrentAttackSecondary(hum))
                    {
                        ctx.AttackKey = "secondary";
                        ctx.AttackDisplayName = "Secondary Attack";
                    }
                    else
                    {
                        int chainLevel = HumanoidAttackHelper.GetCurrentAttackChainLevel(curAtk);
                        int step = chainLevel + 1;
                        ctx.AttackKey = $"primary_{step}";
                        bool isFinisher = curAtk.m_attackChainLevels > 0 && chainLevel == curAtk.m_attackChainLevels - 1;
                        ctx.AttackDisplayName = isFinisher
                            ? $"Combo Finisher (Hit {step}/{curAtk.m_attackChainLevels})"
                            : $"Primary Hit {step}";
                    }
                }
            }

            // Execute all handlers in priority order
            for (int i = 0; i < _handlers.Count; i++)
            {
                _handlers[i].Handler(ctx);
            }

            // Enforce mathematical invariants
            if (ctx.LowerBound < 0f || ctx.UpperBound < 0f || ctx.LowerBound > ctx.UpperBound)
            {
                ctx.LowerBound = Mathf.Max(0f, ctx.LowerBound);
                ctx.UpperBound = Mathf.Max(0f, ctx.UpperBound);
                if (ctx.LowerBound > ctx.UpperBound)
                {
                    ctx.LowerBound = ctx.UpperBound;
                }
                ctx.RecordStep("Bounds Clamping", "Clamp to valid range [Lower <= Upper]");
            }

            return ctx;
        }

        /// <summary>
        /// Executes the roll pipeline to compute the final damage factor for an attack.
        /// </summary>
        public static float RollFactor(Character? attacker, Skills.SkillType skillType, ItemDrop.ItemData? weapon = null)
        {
            var ctx = EvaluateBounds(attacker, skillType, weapon);

            // Perform uniform roll between lower and upper bounds
            if (Mathf.Approximately(ctx.LowerBound, ctx.UpperBound))
            {
                ctx.RolledFactor = ctx.LowerBound;
            }
            else
            {
                ctx.RolledFactor = Mathf.Lerp(ctx.LowerBound, ctx.UpperBound, UnityEngine.Random.value);
            }

            LastRoll = ctx;
            return ctx.RolledFactor;
        }
    }
}
