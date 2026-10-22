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
            public float StunTimer;
            public bool IsArtificialStun;
        }

        private static readonly int ZDO_PoiseRatio = "VL_PoiseRatio".GetStableHashCode();

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

        /// <summary>
        /// Adds poise damage to a character. Only blunt damage and parry impacts should feed into this method.
        /// Returns true if this application breaks the target's poise and triggers a stagger or artificial stun.
        /// </summary>
        public static bool AddPoiseDamage(Character target, float amount, Vector3 forceDir, Character? attacker)
        {
            if (target == null || amount <= 0.001f) return false;

            var state = _states.GetOrCreateValue(target);

            // Cannot build poise while already staggering or artificially stunned
            if (target.IsStaggering() || state.IsArtificialStun)
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
        /// Frame update for poise decay and artificial stun timer countdown.
        /// Replaces vanilla Character.UpdateStagger.
        /// </summary>
        public static void Update(Character character, float dt)
        {
            if (character == null || dt <= 0f) return;

            if (!_states.TryGetValue(character, out var state)) return;

            // 1. Artificial Stun Countdown (for bosses/mobs lacking native stagger animations)
            if (state.IsArtificialStun)
            {
                state.StunTimer -= dt;
                if (state.StunTimer <= 0f)
                {
                    state.IsArtificialStun = false;
                    state.StunTimer = 0f;

                    // Restore animator playback
                    var zanim = CharacterStatsHelper.GetZAnim(character);
                    if (zanim != null)
                    {
                        zanim.SetSpeed(1f);
                    }
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
            else if (!state.IsArtificialStun)
            {
                // No active poise or stun: evict immediately
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
        /// Returns true if the character is currently undergoing an artificial stun (AI paused, animator speed frozen).
        /// </summary>
        public static bool IsArtificiallyStunned(Character character)
        {
            if (character != null && _states.TryGetValue(character, out var state))
            {
                return state.IsArtificialStun;
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
                _states.Remove(character);
                SyncPoiseToZDO(character, 0f);
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

            if (profile.HasNativeAnimation)
            {
                // Native Unity Animator stagger clip
                target.Stagger(forceDir);

                var zanim = CharacterStatsHelper.GetZAnim(target);
                if (Mathf.Abs(profile.DurationMultiplier - 1.0f) > 0.01f && zanim != null)
                {
                    zanim.SetSpeed(1.0f / profile.DurationMultiplier);
                }
            }
            else
            {
                // Artificial Stun: For bosses and golems lacking native stagger animations (Elder, Bonemass, Moder)
                var state = _states.GetOrCreateValue(target);
                state.IsArtificialStun = true;
                state.StunTimer = 2.5f * profile.DurationMultiplier;

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
            }
        }
    }
}
