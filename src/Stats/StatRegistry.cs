using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace VikingLib.Stats
{
    /// <summary>
    /// Global registry for dynamic custom character statistics.
    /// Allows downstream mods to register and query custom stats with 3-tier math and glossary integration.
    /// </summary>
    public static class StatRegistry
    {
        private static readonly ConcurrentDictionary<string, CustomStatToken> _stats = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Registers a new custom character statistic.
        /// </summary>
        public static CustomStatToken Register(
            string key,
            string name,
            float defaultValue = 0f,
            bool permitsFlat = false,
            bool permitsAdditive = true,
            bool permitsMultiplier = true,
            string unit = "",
            string description = "")
        {
            var token = new CustomStatToken(key, name, defaultValue, permitsFlat, permitsAdditive, permitsMultiplier, unit, description);
            _stats[key] = token;
            return token;
        }

        /// <summary>
        /// Registers a custom physical stat with flat modifier support.
        /// </summary>
        public static CustomStatToken RegisterPhysical(string key, string name, string unit, float defaultValue = 0f, string description = "") =>
            Register(key, name, defaultValue, permitsFlat: true, permitsAdditive: true, permitsMultiplier: true, unit, description);

        /// <summary>
        /// Registers a custom multiplier stat (baseline 1.0x, Tier 2 and Tier 3 only).
        /// </summary>
        public static CustomStatToken RegisterMultiplier(string key, string name, string description = "") =>
            Register(key, name, defaultValue: 1.0f, permitsFlat: false, permitsAdditive: true, permitsMultiplier: true, unit: "x", description);

        /// <summary>
        /// Registers a custom efficiency stat (baseline 0.0, Tier 2 and Tier 3 only).
        /// </summary>
        public static CustomStatToken RegisterEfficiency(string key, string name, string description = "") =>
            Register(key, name, defaultValue: 0.0f, permitsFlat: false, permitsAdditive: true, permitsMultiplier: true, unit: "+%", description);

        /// <summary>
        /// Attempts to retrieve a registered custom stat token by key.
        /// </summary>
        public static bool TryGet(string key, out CustomStatToken? token)
        {
            return _stats.TryGetValue(key, out token);
        }

        /// <summary>
        /// Retrieves a registered custom stat token by key, or returns null if not registered.
        /// </summary>
        public static CustomStatToken? Get(string key)
        {
            _stats.TryGetValue(key, out var token);
            return token;
        }

        /// <summary>
        /// Returns all currently registered custom stat tokens.
        /// </summary>
        public static IReadOnlyCollection<CustomStatToken> GetAll() => _stats.Values.ToArray();
    }
}
