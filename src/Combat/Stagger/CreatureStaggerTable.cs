using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace VikingLib.Combat.Stagger
{
    /// <summary>
    /// Centralized registry of decoupled creature poise and stagger profiles.
    /// Maps creature prefabs to calibrated poise thresholds, animation duration multipliers, and native animation availability.
    /// </summary>
    public static class CreatureStaggerTable
    {
        private static readonly ConcurrentDictionary<string, StaggerProfile> _profiles =
            new(StringComparer.OrdinalIgnoreCase);

        static CreatureStaggerTable()
        {
            InitDefaults();
        }

        /// <summary>
        /// Registers or overrides a stagger profile for a specific creature prefab.
        /// </summary>
        public static void Register(string prefabName, float poiseThreshold, float durationMultiplier = 1.0f, bool hasNativeAnimation = true)
        {
            if (string.IsNullOrEmpty(prefabName)) return;
            string clean = CleanPrefabName(prefabName);
            _profiles[clean] = new StaggerProfile(poiseThreshold, durationMultiplier, hasNativeAnimation);
        }

        /// <summary>
        /// Attempts to retrieve a registered StaggerProfile by prefab name.
        /// </summary>
        public static bool TryGetProfile(string prefabName, out StaggerProfile profile)
        {
            if (string.IsNullOrEmpty(prefabName))
            {
                profile = null!;
                return false;
            }
            string clean = CleanPrefabName(prefabName);
            return _profiles.TryGetValue(clean, out profile);
        }

        /// <summary>
        /// Retrieves the registered StaggerProfile for a prefab name, or null if unregistered.
        /// </summary>
        public static StaggerProfile? GetProfile(string prefabName)
        {
            return TryGetProfile(prefabName, out var profile) ? profile : null;
        }

        /// <summary>
        /// Retrieves the registered or synthesized StaggerProfile for a character.
        /// </summary>
        public static StaggerProfile GetProfile(Character? character)
        {
            if (character is null) return new StaggerProfile(50f);

            bool isPlayer = false;
            try
            {
                isPlayer = character.IsPlayer();
            }
            catch { }

            if (isPlayer)
            {
                // Player uses 50% max HP symmetrical baseline
                float hp = 50f;
                try { hp = character.GetMaxHealth(); } catch { }
                float playerThreshold = Mathf.Max(25f, hp * 0.5f);
                return new StaggerProfile(playerThreshold, 1.0f, true);
            }

            string rawName = "";
            try
            {
                if (character.gameObject != null)
                {
                    rawName = global::Utils.GetPrefabName(character.gameObject);
                }
            }
            catch { }

            if (string.IsNullOrEmpty(rawName))
            {
                rawName = character.m_name;
            }

            if (string.IsNullOrEmpty(rawName))
            {
                try
                {
                    rawName = character.name;
                }
                catch { }
            }

            string clean = CleanPrefabName(rawName ?? "");
            if (_profiles.TryGetValue(clean, out var profile))
            {
                return profile;
            }

            // Synthesize fallback for unlisted or modded creatures
            float baseHealth = 100f;
            bool isBoss = false;
            try
            {
                baseHealth = character.GetMaxHealth();
                isBoss = character.IsBoss();
            }
            catch { }

            float fallbackThreshold;
            if (isBoss)
            {
                // Smoothed boss sub-linear curve: ~7 * sqrt(HP)
                fallbackThreshold = Mathf.Clamp(7.0f * Mathf.Sqrt(Mathf.Max(1f, baseHealth)), 120f, 950f);
            }
            else
            {
                // Smoothed regular mob sub-linear curve: ~4.5 * sqrt(HP)
                fallbackThreshold = Mathf.Clamp(4.5f * Mathf.Sqrt(Mathf.Max(1f, baseHealth)), 10f, 200f);
            }

            var synthesized = new StaggerProfile(fallbackThreshold, 1.0f, !isBoss);
            _profiles[clean] = synthesized;
            return synthesized;
        }

        /// <summary>
        /// Returns the base decoupled poise threshold for a character.
        /// </summary>
        public static float GetThreshold(Character? character)
        {
            return GetProfile(character).PoiseThreshold;
        }

        private static string CleanPrefabName(string rawName)
        {
            string clean = rawName;
            int cloneIndex = clean.IndexOf("(Clone)", StringComparison.OrdinalIgnoreCase);
            if (cloneIndex >= 0) clean = clean.Substring(0, cloneIndex);
            return clean.Trim();
        }

        private static void InitDefaults()
        {
            // =========================================================================
            // 1. BOSSES (Smoothed sub-linear curve ~7 * sqrt(HP); artificial stun)
            // =========================================================================
            Register("Eikthyr", 120f, 1.0f, hasNativeAnimation: false);      // 500 HP (Artificial Stun)
            Register("gd_king", 350f, 1.0f, hasNativeAnimation: false);     // The Elder: 2500 HP (Artificial Stun)
            Register("Bonemass", 500f, 1.0f, hasNativeAnimation: false);   // 5000 HP (Artificial Stun)
            Register("Dragon", 600f, 1.0f, hasNativeAnimation: false);     // Moder: 7500 HP (Artificial Stun)
            Register("GoblinKing", 700f, 1.0f, hasNativeAnimation: false);  // Yagluth: 10000 HP (Artificial Stun)
            Register("SeekerQueen", 800f, 1.0f, hasNativeAnimation: false); // The Queen: 12500 HP (Artificial Stun)
            Register("Fader", 900f, 1.0f, hasNativeAnimation: false);       // 15000 HP (Artificial Stun)

            // =========================================================================
            // 2. REGULAR MOBS & ELITES (Smoothed sub-linear curve ~4.5 * sqrt(HP))
            // =========================================================================
            // Meadows
            Register("Neck", 10f);
            Register("Boar", 14f);
            Register("Greydwarf", 26f);

            // Black Forest
            Register("Greydwarf_Shaman", 35f);
            Register("Greydwarf_Elite", 52f);
            Register("Skeleton", 24f);
            Register("Skeleton_Poison", 40f);
            Register("Ghost", 32f);
            Register("Troll", 110f, 1.0f, hasNativeAnimation: true);

            // Swamp
            Register("Blob", 30f);
            Register("BlobElite", 55f);
            Register("Draugr", 45f);
            Register("Draugr_Elite", 65f);
            Register("Draugr_Ranged", 40f);
            Register("Leech", 32f);
            Register("Surtling", 18f);
            Register("Wraith", 42f);
            Register("Abomination", 130f, 1.0f, hasNativeAnimation: true);

            // Mountain
            Register("Wolf", 40f);
            Register("Hatchling", 18f);
            Register("Fenring", 55f);
            Register("StoneGolem", 140f, 1.0f, hasNativeAnimation: false); // Stone Golem lacks native stagger clip
            Register("Bat", 12f);
            Register("Ulfv", 65f);

            // Plains
            Register("Deathsquito", 12f);
            Register("Lox", 145f, 1.0f, hasNativeAnimation: true);
            Register("Fuling", 58f);
            Register("Fuling_Berserker", 135f, 1.0f, hasNativeAnimation: true);
            Register("Fuling_Shaman", 42f);
            Register("BlobTar", 42f);

            // Mistlands
            Register("Hare", 12f);
            Register("Seeker", 68f);
            Register("SeekerBrood", 18f);
            Register("SeekerBrute", 175f, 1.0f, hasNativeAnimation: true);
            Register("Gjall", 160f, 1.0f, hasNativeAnimation: true);
            Register("Tick", 28f);
            Register("Dvergr", 52f);
            Register("DvergrMage", 42f);

            // Ashlands
            Register("Charred_Melee", 56f);
            Register("Charred_Archer", 48f);
            Register("Charred_Mage", 52f);
            Register("FallenValkyrie", 150f, 1.0f, hasNativeAnimation: true);
            Register("Morgen", 170f, 1.0f, hasNativeAnimation: true);
            Register("Asksvin", 115f);
            Register("Volture", 28f);
        }
    }
}
