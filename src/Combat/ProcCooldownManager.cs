using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace VikingLib.Combat
{
    /// <summary>
    /// Thread-safe manager for combat procs, reaction cooldowns, and on-hit internal timers.
    /// Supports automatic in-memory garbage collection on entity destruction, with optional
    /// persistent storage in <see cref="Player.m_customData"/> for long-duration cooldowns across relogs.
    /// </summary>
    public static class ProcCooldownManager
    {
        private const string CustomDataPrefix = "VL_CD_";
        private static readonly ConditionalWeakTable<Character, ConcurrentDictionary<string, float>> _memoryCooldowns = new();

        /// <summary>
        /// Attempts to trigger an action guarded by a cooldown.
        /// If cooled down, registers the current timestamp and returns true; otherwise returns false.
        /// </summary>
        /// <param name="character">The character initiating the proc.</param>
        /// <param name="key">Unique proc identifier (e.g. "CarapaceWard", "LastStand").</param>
        /// <param name="cooldownSeconds">Required cooldown duration in seconds.</param>
        /// <param name="persist">If true and character is a Player, persists the timestamp to Player.m_customData.</param>
        /// <returns>True if the proc successfully triggered; false if currently on cooldown.</returns>
        public static bool TryTrigger(Character character, string key, float cooldownSeconds, bool persist = false)
        {
            if (character == null || string.IsNullOrEmpty(key) || cooldownSeconds <= 0.001f)
            {
                return false;
            }

            // 1. Persistent cooldown path (saved in Player.m_customData using world epoch time)
            if (persist && character is Player player)
            {
                double now = GetCurrentEpochSeconds();
                string dataKey = CustomDataPrefix + key;

                if (player.m_customData.TryGetValue(dataKey, out string? timeStr) &&
                    double.TryParse(timeStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double lastEpoch))
                {
                    if (now - lastEpoch < cooldownSeconds)
                    {
                        return false;
                    }
                }

                player.m_customData[dataKey] = now.ToString("F1", CultureInfo.InvariantCulture);

                // Also update in-memory tracker for fast sub-frame lookup
                var map = _memoryCooldowns.GetOrCreateValue(character);
                map[key] = Time.time;
                return true;
            }

            // 2. High-performance in-memory session cooldown path
            var memMap = _memoryCooldowns.GetOrCreateValue(character);
            float currentTime = Time.time;

            if (memMap.TryGetValue(key, out float lastTriggerTime))
            {
                if (currentTime - lastTriggerTime < cooldownSeconds)
                {
                    return false;
                }
            }

            memMap[key] = currentTime;
            return true;
        }

        /// <summary>
        /// Returns the remaining cooldown duration in seconds. Returns 0 if ready.
        /// </summary>
        public static float GetRemainingCooldown(Character character, string key, float cooldownSeconds, bool persist = false)
        {
            if (character == null || string.IsNullOrEmpty(key) || cooldownSeconds <= 0.001f)
            {
                return 0f;
            }

            if (persist && character is Player player)
            {
                double now = GetCurrentEpochSeconds();
                string dataKey = CustomDataPrefix + key;

                if (player.m_customData.TryGetValue(dataKey, out string? timeStr) &&
                    double.TryParse(timeStr, NumberStyles.Float, CultureInfo.InvariantCulture, out double lastEpoch))
                {
                    double elapsed = now - lastEpoch;
                    if (elapsed < cooldownSeconds)
                    {
                        return (float)(cooldownSeconds - elapsed);
                    }
                }
                return 0f;
            }

            if (_memoryCooldowns.TryGetValue(character, out var map) && map.TryGetValue(key, out float lastTime))
            {
                float elapsed = Time.time - lastTime;
                if (elapsed < cooldownSeconds)
                {
                    return cooldownSeconds - elapsed;
                }
            }

            return 0f;
        }

        /// <summary>
        /// Manually resets or clears a cooldown for a character.
        /// </summary>
        public static void ResetCooldown(Character character, string key, bool persist = false)
        {
            if (character == null || string.IsNullOrEmpty(key)) return;

            if (_memoryCooldowns.TryGetValue(character, out var map))
            {
                map.TryRemove(key, out _);
            }

            if (persist && character is Player player)
            {
                player.m_customData.Remove(CustomDataPrefix + key);
            }
        }

        private static double GetCurrentEpochSeconds()
        {
            if (ZNet.instance != null)
            {
                return ZNet.instance.GetTimeSeconds();
            }
            return Time.time;
        }
    }
}
