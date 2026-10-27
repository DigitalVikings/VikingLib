using System;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using UnityEngine;
using VikingLib.Stats;

namespace VikingLib.Combat.Stagger
{
    /// <summary>
    /// Central manager for decoupled poise tracking, blunt poise damage accumulation,
    /// 3s combat linger window, 15%/s exponential decay, and artificial stun orchestration.
    /// </summary>
    public static class StaggerManager
    {
        private sealed class PoiseState
        {
            public float CurrentPoise;
            public float LastBluntHitTime = -100f;
            public float StaggerTimer;
            public float InitialStaggerDuration = 5.0f;
            public bool IsArtificialStun;
        }

        private static readonly int s_animatorTagStagger = ZSyncAnimation.GetHash("stagger");
        private static readonly int ZDO_PoiseRatio = "VL_PoiseRatio".GetStableHashCode();
        private static readonly int ZDO_StaggerDuration = "VL_StaggerDuration".GetStableHashCode();
        private static readonly int ZDO_StaggerEndTime = "VL_StaggerEndTime".GetStableHashCode();

        // ConditionalWeakTable automatically cleans up states when Character GameObjects are destroyed
        private static readonly ConditionalWeakTable<Character, PoiseState> _states = new();

        private static void SyncPoiseToZDO(Character character, float ratio)
        {
            var nview = CharacterStatsHelper.GetNView(character);
            if (nview != null && nview.IsValid() && nview.IsOwner())
            {
                var zdo = nview.GetZDO();
                if (zdo != null)
                {
                    float current = zdo.GetFloat(ZDO_PoiseRatio, 0f);
                    if (Mathf.Abs(current - ratio) > 0.005f || (ratio == 0f && current != 0f))
                    {
                        zdo.Set(ZDO_PoiseRatio, ratio);
                    }
                }
            }
        }

        private static void SyncStaggerTimerToZDO(Character character, float duration)
        {
            var nview = CharacterStatsHelper.GetNView(character);
            if (nview != null && nview.IsValid() && nview.IsOwner())
            {
                var zdo = nview.GetZDO();
                if (zdo != null)
                {
                    if (duration > 0f)
                    {
                        double now = ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : (double)Time.time;
                        long endMs = (long)((now + duration) * 1000.0);
                        zdo.Set(ZDO_StaggerDuration, duration);
                        zdo.Set(ZDO_StaggerEndTime, endMs);
                    }
                    else
                    {
                        zdo.Set(ZDO_StaggerDuration, 0f);
                        zdo.Set(ZDO_StaggerEndTime, 0L);
                    }
                }
            }
        }

        /// <summary>
        /// Adds poise damage to a character. Only blunt damage and parry impacts should feed into this method.
        /// Returns true if this application breaks the target's poise and triggers a stagger or artificial stun.
        /// </summary>
        public static bool AddPoiseDamage(Character target, float amount, Vector3 forceDir, Character? attacker)
        {
            if (target == null || amount <= 0.001f) return false;

            var state = _states.GetOrCreateValue(target);

            // Cannot build poise while already staggering or artificially stunned
            if (target.IsStaggering() || state.StaggerTimer > 0f)
            {
                return false;
            }

            state.CurrentPoise += amount;
            state.LastBluntHitTime = Time.time;

            float threshold = GetEffectiveThreshold(target);

            if (state.CurrentPoise >= threshold)
            {
                // Poise broke! Reset poise to 0 upon stagger
                state.CurrentPoise = 0f;
                SyncPoiseToZDO(target, 0f);

                TriggerStagger(target, forceDir, attacker);
                return true;
            }

            SyncPoiseToZDO(target, Mathf.Clamp01(state.CurrentPoise / threshold));
            return false;
        }

        /// <summary>
        /// Frame update for poise decay and stagger / artificial stun timer countdown.
        /// Replaces vanilla Character.UpdateStagger.
        /// </summary>
        public static void Update(Character character, float dt)
        {
            if (character == null || dt <= 0f) return;

            if (!_states.TryGetValue(character, out var state)) return;

            // 1. Stagger Timer Countdown (Uniform duration across native clips and artificial stuns)
            if (state.StaggerTimer > 0f)
            {
                state.StaggerTimer -= dt;
                if (state.StaggerTimer <= 0f)
                {
                    state.StaggerTimer = 0f;
                    SyncStaggerTimerToZDO(character, 0f);

                    var zanim = CharacterStatsHelper.GetZAnim(character);
                    zanim?.SetSpeed(1f);

                    if (state.IsArtificialStun)
                    {
                        state.IsArtificialStun = false;
                    }
                    return;
                }

                // Smooth dynamic speed curve for native stagger animations
                if (!state.IsArtificialStun)
                {
                    UpdateNativeStaggerAnimation(character, state);
                }
                return;
            }

            // 2. Poise Decay (linger window, followed by exponential decay)
            if (state.CurrentPoise > 0f)
            {
                float timeSinceHit = Time.time - state.LastBluntHitTime;
                float dropoff = Configuration.ModConfig.StaggerDropoffSeconds?.Value ?? 30.0f;

                // Hard combat dropoff: if untouched for 30s, drop all poise and evict state
                if (timeSinceHit >= dropoff)
                {
                    state.CurrentPoise = 0f;
                    SyncPoiseToZDO(character, 0f);
                    _states.Remove(character);
                    return;
                }

                float linger = Configuration.ModConfig.StaggerLingerSeconds?.Value ?? 3.0f;
                if (timeSinceHit >= linger)
                {
                    float decayRate = Configuration.ModConfig.StaggerDecayRate?.Value ?? 0.15f;
                    float decayFactor = Mathf.Max(0f, 1.0f - (decayRate * dt));
                    state.CurrentPoise *= decayFactor;

                    if (state.CurrentPoise < 0.1f)
                    {
                        state.CurrentPoise = 0f;
                        SyncPoiseToZDO(character, 0f);
                        _states.Remove(character);
                    }
                    else
                    {
                        float threshold = GetEffectiveThreshold(character);
                        SyncPoiseToZDO(character, threshold > 0.001f ? Mathf.Clamp01(state.CurrentPoise / threshold) : 0f);
                    }
                }
            }
            else if (state.StaggerTimer <= 0f)
            {
                // No active poise or stagger: evict immediately
                SyncPoiseToZDO(character, 0f);
                _states.Remove(character);
            }
        }

        /// <summary>
        /// Returns the current poise percentage [0.0, 1.0] for HUD meter rendering.
        /// Evaluates local poise on the owning client, or reads the network-synchronized ZDO float on remote clients.
        /// </summary>
        public static float GetPoisePercentage(Character character)
        {
            if (character == null) return 0f;

            if (_states.TryGetValue(character, out var state))
            {
                float threshold = GetEffectiveThreshold(character);
                if (threshold <= 0.001f) return 0f;
                return Mathf.Clamp01(state.CurrentPoise / threshold);
            }

            // Remote client fallback: query owner-authoritative ZDO float
            var nview = CharacterStatsHelper.GetNView(character);
            if (nview != null && nview.IsValid())
            {
                var zdo = nview.GetZDO();
                if (zdo != null)
                {
                    return zdo.GetFloat(ZDO_PoiseRatio, 0f);
                }
            }

            return 0f;
        }

        /// <summary>
        /// Returns the current accumulated poise damage points on a character.
        /// </summary>
        public static float GetCurrentPoise(Character character)
        {
            if (character != null && _states.TryGetValue(character, out var state))
            {
                return state.CurrentPoise;
            }
            return 0f;
        }

        /// <summary>
        /// Returns true if the character is currently undergoing a stagger or artificial stun.
        /// </summary>
        public static bool IsStaggering(Character character)
        {
            if (character == null) return false;

            if (_states.TryGetValue(character, out var state))
            {
                return state.StaggerTimer > 0f;
            }

            // Remote client fallback: query owner-authoritative ZDO end timestamp
            var nview = CharacterStatsHelper.GetNView(character);
            if (nview != null && nview.IsValid())
            {
                var zdo = nview.GetZDO();
                if (zdo != null)
                {
                    long endMs = zdo.GetLong(ZDO_StaggerEndTime, 0L);
                    if (endMs > 0L)
                    {
                        double now = ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : (double)Time.time;
                        return (endMs / 1000.0) > now;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the remaining stagger duration fraction [0.0, 1.0] while a character is staggered.
        /// Returns 0.0 if the character is not currently staggered.
        /// </summary>
        public static float GetStaggerFraction(Character character)
        {
            if (character == null) return 0f;

            if (_states.TryGetValue(character, out var state))
            {
                if (state.StaggerTimer > 0f && state.InitialStaggerDuration > 0.001f)
                {
                    return Mathf.Clamp01(state.StaggerTimer / state.InitialStaggerDuration);
                }
                return 0f;
            }

            // Remote client fallback: query owner-authoritative ZDO end timestamp
            var nview = CharacterStatsHelper.GetNView(character);
            if (nview != null && nview.IsValid())
            {
                var zdo = nview.GetZDO();
                if (zdo != null)
                {
                    float duration = zdo.GetFloat(ZDO_StaggerDuration, 0f);
                    long endMs = zdo.GetLong(ZDO_StaggerEndTime, 0L);
                    if (duration > 0.001f && endMs > 0L)
                    {
                        double now = ZNet.instance != null ? ZNet.instance.GetTimeSeconds() : (double)Time.time;
                        double remaining = (endMs / 1000.0) - now;
                        if (remaining > 0.0)
                        {
                            return Mathf.Clamp01((float)(remaining / duration));
                        }
                    }
                }
            }

            return 0f;
        }

        /// <summary>
        /// Returns true if the character is currently undergoing an artificial stun (AI paused, animator speed frozen).
        /// </summary>
        public static bool IsArtificiallyStunned(Character character)
        {
            if (character != null && _states.TryGetValue(character, out var state))
            {
                return state.IsArtificialStun && state.StaggerTimer > 0f;
            }
            return false;
        }

        /// <summary>
        /// Resets accumulated poise and stun states on a character (e.g. on death or world despawn).
        /// </summary>
        public static void Reset(Character character)
        {
            if (character != null)
            {
                if (_states.TryGetValue(character, out var state))
                {
                    var zanim = CharacterStatsHelper.GetZAnim(character);
                    zanim?.SetSpeed(1f);
                }
                _states.Remove(character);
                SyncPoiseToZDO(character, 0f);
                SyncStaggerTimerToZDO(character, 0f);
            }
        }

        public static float GetEffectiveThreshold(Character character)
        {
            float baseThreshold = CreatureStaggerTable.GetThreshold(character);
            float resistance = CharacterStatManager.GetStat(character, CharacterStatType.StaggerResistance);
            if (resistance <= 0.001f) resistance = 1.0f;
            float multiplier = Configuration.ModConfig.StaggerThresholdMultiplier?.Value ?? 1.0f;
            return Mathf.Max(1f, baseThreshold * resistance * multiplier);
        }

        private static void TriggerStagger(Character target, Vector3 forceDir, Character? attacker)
        {
            var profile = CreatureStaggerTable.GetProfile(target);
            var state = _states.GetOrCreateValue(target);

            // Set uniform stagger countdown timer calibrated by creature tier
            float baseDuration = Configuration.ModConfig.StaggerDurationSeconds?.Value ?? 5.0f;
            float totalDuration = Mathf.Max(0.1f, baseDuration * profile.DurationMultiplier);
            state.InitialStaggerDuration = totalDuration;
            state.StaggerTimer = totalDuration;

            SyncStaggerTimerToZDO(target, totalDuration);

            // Flash player HUD stagger bar if the target was a player
            if (target.IsPlayer())
            {
                Hud.instance?.StaggerBarFlash();
            }

            // Award adrenaline to attacker if player
            if (attacker is Player player)
            {
                float adrenaline = player.m_staggerEnemyAdrenaline * target.m_enemyAdrenalineMultiplier;
                if (adrenaline > 0f)
                {
                    if (attacker.IsOwner())
                    {
                        attacker.AddAdrenaline(adrenaline);
                    }
                    else
                    {
                        var nview = CharacterStatsHelper.GetNView(attacker);
                        if (nview != null && nview.IsValid())
                        {
                            nview.InvokeRPC("RPC_AddAdrenaline", adrenaline);
                        }
                    }
                }
            }

            bool hasNative = profile.HasNativeAnimation && !target.IsBoss();
            if (hasNative)
            {
                state.IsArtificialStun = false;

                // Native Unity Animator stagger clip
                target.Stagger(forceDir);
            }
            else
            {
                // Artificial Stun: For bosses and golems lacking native stagger animations (Eikthyr, Elder, Bonemass, Moder, etc.)
                state.IsArtificialStun = true;

                // Stop AI locomotion and attacks
                var baseAI = target.GetBaseAI();
                if (baseAI != null)
                {
                    baseAI.StopMoving();
                }

                // Freeze animator
                var zanim = CharacterStatsHelper.GetZAnim(target);
                if (zanim != null)
                {
                    zanim.SetSpeed(0f);
                }

                // Play sensory feedback
                if (target.m_critHitEffects != null)
                {
                    target.m_critHitEffects.Create(target.GetCenterPoint(), Quaternion.identity, target.transform, 1f, -1);
                }

                // Emit floating STAGGERED text above target head
                DamageText.instance?.ShowText(
                    DamageText.TextType.Bonus,
                    target.GetTopPoint() + Vector3.up * 0.5f,
                    "STAGGERED"
                );
            }
        }

        private static void UpdateNativeStaggerAnimation(Character character, PoiseState state)
        {
            var zanim = CharacterStatsHelper.GetZAnim(character);
            var anim = CharacterStatsHelper.GetAnimator(character);
            if (zanim == null || anim == null) return;

            var stateInfo = anim.GetCurrentAnimatorStateInfo(0);
            if (stateInfo.tagHash == s_animatorTagStagger)
            {
                float u = Mathf.Clamp01(stateInfo.normalizedTime % 1f);
                float clipLength = stateInfo.length > 0.05f ? stateInfo.length : 1.25f;
                float ratio = Mathf.Max(1f, state.InitialStaggerDuration / clipLength);

                // Closed-form harmonic smooth dip:
                // Starts at 1.0 at u=0, dips smoothly to 1/(2*ratio - 1) at u=0.5, returns smoothly to 1.0 at u=1.0
                float sinVal = Mathf.Sin(Mathf.PI * u);
                float speed = 1.0f / (1.0f + 2.0f * (ratio - 1.0f) * (sinVal * sinVal));

                zanim.SetSpeed(Mathf.Clamp(speed, 0.05f, 1.0f));
            }
            else
            {
                // If animator transitioned out or is in a different state, restore full speed
                if (anim.speed < 0.99f)
                {
                    zanim.SetSpeed(1f);
                }
            }
        }
    }
}
