using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using VikingLib.Combat;
using VikingLib.Configuration;

namespace VikingLib.Patches
{
    /// <summary>
    /// Intercepts Character combat damage via RPC_Damage to execute VikingLib's DamagePipeline.
    /// Neutralizes vanilla Valheim's native backstab and stagger double-mitigation.
    /// </summary>
    [HarmonyPatch(typeof(Character), "RPC_Damage")]
    public static class CharacterCombatPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Character __instance, HitData hit, out DamageContext? __state)
        {
            __state = null;

            if (!ModConfig.ModEnabled.Value || hit == null || __instance == null) return;

            // Only evaluate calculations on the authoritative network owner of the target
            if (!__instance.IsOwner()) return;

            // Honor vanilla death and immunity states
            if (__instance.GetHealth() <= 0f || __instance.IsDead() || __instance.IsTeleporting() || __instance.InCutscene())
                return;

            if (hit.m_dodgeable && __instance.IsDodgeInvincible())
            {
                DamagePipeline.RaiseDodge(__instance, hit.GetAttacker(), hit);
                return;
            }

            // Execute the VikingLib pipeline
            __state = DamagePipeline.ProcessCharacterHit(__instance, hit);

            if (__state.IsCancelled)
            {
                hit.m_damage = default;
            }

            // Neutralize vanilla's secondary backstab multiplication at IL_0396 in RPC_Damage
            hit.m_backstabBonus = 1.0f;
        }

        /// <summary>
        /// Neutralizes vanilla Valheim's hardcoded 2.0x stagger multiplier (ldc.r4 2) at IL_03DF in RPC_Damage,
        /// since VikingLib's DamagePipeline Phase 1 already evaluates and audits stagger vulnerability.
        /// </summary>
        public static float GetVanillaStaggerMultiplier()
        {
            return ModConfig.ModEnabled.Value ? 1.0f : 2.0f;
        }

        /// <summary>
        /// Controls whether vanilla Valheim's native stagger critical block (2.0x damage and crit sound/VFX)
        /// in RPC_Damage should trigger. When VikingLib's stagger overhaul is active, stagger is pure crowd
        /// control: it does not grant bonus damage or play crit feedback. Genuine critical hits from VikingLib
        /// stats or ambush strikes are handled in Postfix via DamageFlags.CriticalHit.
        /// </summary>
        public static bool ShouldTriggerVanillaStaggerCrit(Character? target)
        {
            if (target == null) return false;
            if (ModConfig.ModEnabled.Value && ModConfig.StaggerOverhaulEnabled.Value)
            {
                return false;
            }

            return target.IsStaggering();
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileRpcDamage(IEnumerable<CodeInstruction> instructions)
        {
            var isStaggeringMethod = AccessTools.Method(typeof(Character), nameof(Character.IsStaggering));
            var shouldTriggerHelper = AccessTools.Method(typeof(CharacterCombatPatch), nameof(ShouldTriggerVanillaStaggerCrit));
            var applyModMethod = AccessTools.Method(typeof(HitData), nameof(HitData.ApplyModifier), new[] { typeof(float) });
            var staggerHelper = AccessTools.Method(typeof(CharacterCombatPatch), nameof(GetVanillaStaggerMultiplier));

            var list = new List<CodeInstruction>(instructions);
            for (int i = 0; i < list.Count; i++)
            {
                // 1. Replace Character.IsStaggering() call with ShouldTriggerVanillaStaggerCrit(this)
                // Returning false skips both the 2.0x damage modifier and vanilla's m_critHitEffects.Create(...)
                if (list[i].Calls(isStaggeringMethod))
                {
                    var newInst = new CodeInstruction(OpCodes.Call, shouldTriggerHelper);
                    newInst.labels.AddRange(list[i].labels);
                    newInst.blocks.AddRange(list[i].blocks);
                    list[i] = newInst;
                }

                // 2. Defensive fallback: replace ldc.r4 2 before HitData.ApplyModifier with 1.0f
                if (i < list.Count - 1 &&
                    list[i].opcode == OpCodes.Ldc_R4 && Mathf.Approximately((float)list[i].operand, 2.0f) &&
                    list[i + 1].Calls(applyModMethod))
                {
                    list[i] = new CodeInstruction(OpCodes.Call, staggerHelper);
                }
            }
            return list;
        }

        [HarmonyPostfix]
        public static void Postfix(Character __instance, HitData hit, DamageContext? __state)
        {
            if (__state == null) return;

            // Spawn native critical strike visual and sound effects if hit was a critical strike
            if (__state.Flags.HasFlag(DamageFlags.CriticalHit))
            {
                Character? attacker = hit.GetAttacker();
                __instance.m_critHitEffects.Create(hit.m_point, Quaternion.identity, __instance.transform, 1f, -1,
                    attacker?.GetZDOID() ?? ZDOID.None);
            }

            DamagePipeline.FinalizeHit(__state);
        }
    }

    /// <summary>
    /// Intercepts Character.AddStaggerDamage to enforce the blunt-only poise buildup,
    /// trigger decoupled stagger / artificial stun via StaggerManager, and track CausedStagger telemetry.
    /// </summary>
    [HarmonyPatch(typeof(Character), "AddStaggerDamage")]
    public static class CharacterAddStaggerDamagePatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Character __instance, float damage, Vector3 forceDirection, HitData hit, ref bool __result)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.StaggerOverhaulEnabled.Value || __instance == null)
            {
                return true; // Fall back to vanilla
            }

            // Only evaluate on the authoritative network owner
            if (!__instance.IsOwner())
            {
                __result = false;
                return false;
            }

            if (__instance.GetHealth() <= 0f || __instance.IsDead())
            {
                __result = false;
                return false;
            }

            Character? attacker = hit?.GetAttacker();
            float poiseAmount;
            if (hit != null)
            {
                bool isEnemyAttacker = attacker == null || !attacker.IsPlayer();

                if (isEnemyAttacker && ModConfig.EnemyPhysicalInflictsStagger.Value)
                {
                    // Non-player enemies inflict poise damage with all physical damage types (Blunt, Slash, Pierce)
                    poiseAmount = hit.m_damage.GetTotalPhysicalDamage() * hit.m_staggerMultiplier;
                }
                else
                {
                    // Player attacks: blunt always inflicts poise; slash/pierce inflict poise only if their respective triad perk is disabled
                    float amount = hit.m_damage.m_blunt;

                    if (!ModConfig.EnableSlashBleed.Value)
                    {
                        amount += hit.m_damage.m_slash;
                    }

                    if (!ModConfig.EnablePiercePrecision.Value)
                    {
                        amount += hit.m_damage.m_pierce;
                    }

                    poiseAmount = amount * hit.m_staggerMultiplier;
                }
            }
            else
            {
                // When hit is null (e.g. from BlockAttack when a defender's block is overwhelmed)
                poiseAmount = damage;
            }

            if (poiseAmount <= 0.001f)
            {
                __result = false;
                return false;
            }

            bool brokePoise = Combat.Stagger.StaggerManager.AddPoiseDamage(__instance, poiseAmount, forceDirection, attacker);

            var ctx = DamagePipeline.CurrentContext;
            if (ctx != null)
            {
                float threshold = Combat.Stagger.StaggerManager.GetEffectiveThreshold(__instance);
                string targetName = __instance.GetHoverName();

                if (hit != null && ctx.Target == __instance)
                {
                    if (brokePoise)
                    {
                        ctx.CausedStagger = true;
                        ctx.Trace?.RecordStep(DamagePhase.PostHitProcs, "Poise Damage",
                            $"Inflicted {poiseAmount:F1} Poise damage -> STAGGERED ({targetName} poise broken: {threshold:F1}/{threshold:F1})", poiseAmount);
                    }
                    else
                    {
                        float currentPoise = Combat.Stagger.StaggerManager.GetCurrentPoise(__instance);
                        ctx.Trace?.RecordStep(DamagePhase.PostHitProcs, "Poise Damage",
                            $"Inflicted {poiseAmount:F1} Poise damage ({currentPoise:F1}/{threshold:F1} [{(currentPoise / threshold):P0}])", poiseAmount);
                    }
                }
                else if (hit == null)
                {
                    if (brokePoise)
                    {
                        ctx.Trace?.RecordStep(DamagePhase.TargetDefenses, "Guard Broken",
                            $"Block overwhelmed by {poiseAmount:F1} Poise damage -> STAGGERED ({targetName} poise broken: {threshold:F1}/{threshold:F1})", poiseAmount);
                    }
                    else
                    {
                        float currentPoise = Combat.Stagger.StaggerManager.GetCurrentPoise(__instance);
                        ctx.Trace?.RecordStep(DamagePhase.TargetDefenses, "Block Poise Absorbed",
                            $"Absorbed {poiseAmount:F1} Block Poise damage ({currentPoise:F1}/{threshold:F1} [{(currentPoise / threshold):P0}])", poiseAmount);
                    }
                }
            }

            __result = brokePoise;
            return false; // Skip vanilla linear clamp and native Stagger call
        }

        [HarmonyPostfix]
        public static void Postfix(Character __instance, bool __result)
        {
            // Fallback for vanilla mode: flag CausedStagger if vanilla broke poise
            if (__result && (!ModConfig.ModEnabled.Value || !ModConfig.StaggerOverhaulEnabled.Value))
            {
                var ctx = DamagePipeline.CurrentContext;
                if (ctx != null && ctx.Target == __instance)
                {
                    ctx.CausedStagger = true;
                }
            }
        }
    }

    /// <summary>
    /// Replaces vanilla linear poise decay with VikingLib's 3.0s linger window,
    /// 15%/s exponential decay, and artificial stun countdown.
    /// </summary>
    [HarmonyPatch(typeof(Character), "UpdateStagger")]
    public static class CharacterUpdateStaggerPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Character __instance, float dt)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.StaggerOverhaulEnabled.Value || __instance == null)
            {
                return true; // Fall back to vanilla linear decay
            }

            Combat.Stagger.StaggerManager.Update(__instance, dt);
            return false; // Skip vanilla linear decay
        }
    }

    /// <summary>
    /// Overrides Character.GetStaggerTreshold with decoupled poise thresholds from CreatureStaggerTable
    /// and scales with CharacterStatType.StaggerResistance.
    /// </summary>
    [HarmonyPatch(typeof(Character), "GetStaggerTreshold")]
    public static class CharacterGetStaggerTresholdPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Character __instance, ref float __result)
        {
            if (!ModConfig.ModEnabled.Value || __instance == null) return;

            if (ModConfig.StaggerOverhaulEnabled.Value)
            {
                __result = Combat.Stagger.StaggerManager.GetEffectiveThreshold(__instance);
                return;
            }

            // Legacy fallback
            float resistance = Stats.CharacterStatManager.GetStat(__instance, Stats.CharacterStatType.StaggerResistance);
            if (Mathf.Abs(resistance - 1f) > 0.001f)
            {
                __result = Mathf.Max(1f, __result * resistance);
            }
        }
    }

    /// <summary>
    /// Hooks Character.IsStaggering to report true while undergoing a stagger or artificial stun
    /// under VikingLib's uniform configurable duration, universally locking out movement, attacks,
    /// dodging, and jumps across the entire game engine.
    /// </summary>
    [HarmonyPatch(typeof(Character), nameof(Character.IsStaggering))]
    public static class CharacterIsStaggeringPatch
    {
        [HarmonyPostfix]
        public static void Postfix(Character __instance, ref bool __result)
        {
            if (__result || __instance == null) return;

            if (ModConfig.ModEnabled.Value && ModConfig.StaggerOverhaulEnabled.Value &&
                Combat.Stagger.StaggerManager.IsStaggering(__instance))
            {
                __result = true;
            }
        }
    }

    /// <summary>
    /// Guarantees that native stagger animations always trigger their animator clip
    /// and face the proper deflection direction without being suppressed by extended stagger timers.
    /// </summary>
    [HarmonyPatch(typeof(Character), "RPC_Stagger")]
    public static class CharacterRPC_StaggerPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(Character __instance, long sender, Vector3 forceDirection)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.StaggerOverhaulEnabled.Value || __instance == null)
                return true;

            if (forceDirection.magnitude > 0.01f)
            {
                forceDirection.y = 0f;
                if (forceDirection.sqrMagnitude > 0.0001f)
                {
                    __instance.transform.rotation = Quaternion.LookRotation(-forceDirection);
                }
            }

            var zanim = Stats.CharacterStatsHelper.GetZAnim(__instance);
            if (zanim != null)
            {
                zanim.SetSpeed(1f);
                zanim.SetTrigger("stagger");
            }

            return false;
        }
    }

    /// <summary>
    /// Resets poise and restores animator playback speed when a character dies.
    /// </summary>
    [HarmonyPatch(typeof(Character), "OnDeath")]
    public static class CharacterOnDeathPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Character __instance)
        {
            if (__instance != null)
            {
                Combat.Stagger.StaggerManager.Reset(__instance);
            }
        }
    }

    /// <summary>
    /// Resets poise and restores animator playback speed when a player dies (Player overrides Character.OnDeath).
    /// </summary>
    [HarmonyPatch(typeof(Player), "OnDeath")]
    public static class PlayerOnDeathPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Player __instance)
        {
            if (__instance != null)
            {
                Combat.Stagger.StaggerManager.Reset(__instance);
            }
        }
    }

    /// <summary>
    /// Resets poise and cleans up state when a character is destroyed or despawns.
    /// </summary>
    [HarmonyPatch(typeof(Character), "OnDestroy")]
    public static class CharacterOnDestroyPatch
    {
        [HarmonyPrefix]
        public static void Prefix(Character __instance)
        {
            if (__instance != null)
            {
                Combat.Stagger.StaggerManager.Reset(__instance);
            }
        }
    }

    /// <summary>
    /// Pauses monster AI updates while staggered or artificially stunned.
    /// </summary>
    [HarmonyPatch(typeof(BaseAI), nameof(BaseAI.UpdateAI))]
    public static class BaseAIUpdateAIPatch
    {
        [HarmonyPrefix]
        public static bool Prefix(BaseAI __instance)
        {
            if (!ModConfig.ModEnabled.Value || !ModConfig.StaggerOverhaulEnabled.Value || __instance == null)
                return true;

            var character = Stats.CharacterStatsHelper.GetCharacter(__instance);
            if (character != null && (Combat.Stagger.StaggerManager.IsStaggering(character) || character.IsStaggering()))
            {
                __instance.StopMoving();
                return false;
            }

            return true;
        }
    }

    /// <summary>
    /// Transpiles Humanoid.BlockAttack parrying to replace instantaneous stagger bypass with a
    /// calibrated Parry Poise Burst (BlockPower * ParryMultiplier * ParryPoiseMultiplier) fed into StaggerManager.
    /// </summary>
    [HarmonyPatch(typeof(Humanoid), "BlockAttack")]
    public static class HumanoidBlockAttackPatch
    {
        public static void ExecuteParryPoise(Character attacker, HitData hit, Humanoid defender)
        {
            if (attacker == null || hit == null || defender == null) return;

            if (!ModConfig.ModEnabled.Value || !ModConfig.StaggerOverhaulEnabled.Value)
            {
                // Vanilla fallback
                if (attacker.m_staggerWhenBlocked)
                {
                    attacker.Stagger(-hit.m_dir);
                }
                DamagePipeline.RaiseParry(defender, attacker, hit);
                return;
            }

            // Calculate Parry Poise Damage: BlockPower * ParryMultiplier * ParryPoiseMultiplier
            ItemDrop.ItemData? blocker = Stats.CharacterStatsHelper.GetCurrentBlocker(defender);
            float blockPower = blocker != null
                ? blocker.GetBlockPower(defender.GetSkillLevel(Skills.SkillType.Blocking))
                : 10f;
            float parryMult = blocker?.m_shared.m_timedBlockBonus ?? 1.5f;
            float statMult = Stats.CharacterStatManager.GetStat(defender, Stats.CharacterStatType.ParryPoiseMultiplier);
            float parryPoiseDamage = blockPower * parryMult * statMult;

            Vector3 forceDir = -hit.m_dir;
            bool broke = Combat.Stagger.StaggerManager.AddPoiseDamage(attacker, parryPoiseDamage, forceDir, defender);
            DamagePipeline.RaiseParry(defender, attacker, hit);

            var ctx = DamagePipeline.CurrentContext;
            if (ctx != null && ctx.Trace != null)
            {
                float threshold = Combat.Stagger.StaggerManager.GetEffectiveThreshold(attacker);
                string attackerName = attacker.GetHoverName();
                string blockerName = blocker?.m_shared?.m_name != null
                    ? (Localization.instance != null ? Localization.instance.Localize(blocker.m_shared.m_name) : blocker.m_shared.m_name)
                    : "Shield";

                if (broke)
                {
                    ctx.Trace.RecordStep(DamagePhase.TargetDefenses, "Parry Poise Counter",
                        $"Inflicted {parryPoiseDamage:F1} Parry Poise via {blockerName} -> STAGGERED ({attackerName} poise broken: {threshold:F1}/{threshold:F1})", parryPoiseDamage);
                }
                else
                {
                    float currentPoise = Combat.Stagger.StaggerManager.GetCurrentPoise(attacker);
                    ctx.Trace.RecordStep(DamagePhase.TargetDefenses, "Parry Poise Counter",
                        $"Inflicted {parryPoiseDamage:F1} Parry Poise via {blockerName} ({currentPoise:F1}/{threshold:F1} [{(currentPoise / threshold):P0}])", parryPoiseDamage);
                }
            }
        }

        [HarmonyTranspiler]
        public static IEnumerable<CodeInstruction> TranspileBlockAttack(IEnumerable<CodeInstruction> instructions)
        {
            var staggerMethod = AccessTools.Method(typeof(Character), nameof(Character.Stagger), new[] { typeof(Vector3) });
            var parryPoiseHelper = AccessTools.Method(typeof(HumanoidBlockAttackPatch), nameof(ExecuteParryPoise));
            var staggerWhenBlockedField = AccessTools.Field(typeof(Character), nameof(Character.m_staggerWhenBlocked));

            var list = new List<CodeInstruction>(instructions);
            for (int i = 0; i < list.Count - 6; i++)
            {
                // Find: ldarg.2, ldfld m_staggerWhenBlocked, brfalse(.s), ldarg.2, ldarg.1, ldfld m_dir, call op_UnaryNegation, callvirt Stagger
                if (list[i].opcode == OpCodes.Ldarg_2 &&
                    list[i + 1].LoadsField(staggerWhenBlockedField) &&
                    (list[i + 2].opcode == OpCodes.Brfalse_S || list[i + 2].opcode == OpCodes.Brfalse))
                {
                    int staggerIndex = -1;
                    for (int j = i + 3; j <= i + 10 && j < list.Count; j++)
                    {
                        if (list[j].Calls(staggerMethod))
                        {
                            staggerIndex = j;
                            break;
                        }
                    }

                    if (staggerIndex != -1)
                    {
                        var labels = list[i].labels;
                        int countToRemove = staggerIndex - i + 1;
                        list.RemoveRange(i, countToRemove);

                        var firstInst = new CodeInstruction(OpCodes.Ldarg_2);
                        firstInst.labels.AddRange(labels);

                        list.InsertRange(i, new[]
                        {
                            firstInst,
                            new CodeInstruction(OpCodes.Ldarg_1),
                            new CodeInstruction(OpCodes.Ldarg_0),
                            new CodeInstruction(OpCodes.Call, parryPoiseHelper)
                        });
                        break;
                    }
                }
            }
            return list;
        }
    }
}
