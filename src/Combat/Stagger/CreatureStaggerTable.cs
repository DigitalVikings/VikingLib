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
                // Player uses 50% max HP symmetrical baseline, calibrated ~2.0s stagger window
                float hp = 50f;
                try { hp = character.GetMaxHealth(); } catch { }
                float playerThreshold = Mathf.Max(25f, hp * 0.5f);
                return new StaggerProfile(playerThreshold, 0.40f, true);
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
            float durationMult;
            if (isBoss)
            {
                // Smoothed boss sub-linear curve: ~7 * sqrt(HP), full 5.0s window
                fallbackThreshold = Mathf.Clamp(7.0f * Mathf.Sqrt(Mathf.Max(1f, baseHealth)), 120f, 950f);
                durationMult = 1.0f;
            }
            else
            {
                // Smoothed regular mob sub-linear curve: ~4.5 * sqrt(HP)
                fallbackThreshold = Mathf.Clamp(4.5f * Mathf.Sqrt(Mathf.Max(1f, baseHealth)), 10f, 200f);
                if (baseHealth <= 60f)
                {
                    durationMult = 0.40f; // Minor mob tier: ~2.0s
                }
                else if (baseHealth <= 300f)
                {
                    durationMult = 0.55f; // Standard mob tier: ~2.75s
                }
                else
                {
                    durationMult = 0.80f; // Elite / Heavy mob tier: ~4.0s
                }
            }

            var synthesized = new StaggerProfile(fallbackThreshold, durationMult, !isBoss);
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
            // 1. BOSSES (Smoothed sub-linear curve ~7 * sqrt(HP); full 5.0s artificial stun)
            // =========================================================================
            Register("Eikthyr", 120f, 1.0f, hasNativeAnimation: false);      // 500 HP (Artificial Stun)
            Register("gd_king", 350f, 1.0f, hasNativeAnimation: false);     // The Elder: 2500 HP (Artificial Stun)
            Register("Bonemass", 500f, 1.0f, hasNativeAnimation: false);   // 5000 HP (Artificial Stun)
            Register("Dragon", 600f, 1.0f, hasNativeAnimation: false);     // Moder: 7500 HP (Artificial Stun)
            Register("GoblinKing", 700f, 1.0f, hasNativeAnimation: false);  // Yagluth: 10000 HP (Artificial Stun)
            Register("SeekerQueen", 800f, 1.0f, hasNativeAnimation: false); // The Queen: 12500 HP (Artificial Stun)
            Register("Fader", 900f, 1.0f, hasNativeAnimation: false);       // 15000 HP (Artificial Stun)

            // =========================================================================
            // 2. REGULAR MOBS & ELITES (Tiered durations: Minor ~2.0s, Standard ~2.75s, Heavy ~4.0s)
            // =========================================================================
            // Meadows
            Register("Neck", 10f, 0.35f);                  // ~1.75s
            Register("Boar", 14f, 0.40f);                  // ~2.0s
            Register("Greyling", 12f, 0.40f);              // ~2.0s
            Register("Greydwarf", 26f, 0.50f);             // ~2.5s

            // Black Forest
            Register("Greydwarf_Shaman", 35f, 0.50f);      // ~2.5s
            Register("Greydwarf_Elite", 52f, 0.75f);       // ~3.75s (Elite)
            Register("Skeleton", 24f, 0.50f);              // ~2.5s
            Register("Skeleton_Poison", 40f, 0.55f);       // ~2.75s
            Register("Ghost", 32f, 0.50f);                 // ~2.5s
            Register("Troll", 110f, 0.80f, hasNativeAnimation: true); // ~4.0s (Heavy)

            // Swamp
            Register("Blob", 30f, 0.50f);                  // ~2.5s
            Register("BlobElite", 55f, 0.60f);             // ~3.0s
            Register("Draugr", 45f, 0.55f);                // ~2.75s
            Register("Draugr_Elite", 65f, 0.75f);          // ~3.75s (Elite)
            Register("Draugr_Ranged", 40f, 0.50f);         // ~2.5s
            Register("Leech", 32f, 0.45f);                 // ~2.25s
            Register("Surtling", 18f, 0.40f);              // ~2.0s
            Register("Wraith", 42f, 0.55f);                // ~2.75s
            Register("Abomination", 130f, 0.80f, hasNativeAnimation: true); // ~4.0s (Heavy)

            // Mountain
            Register("Wolf", 40f, 0.50f);                  // ~2.5s
            Register("Hatchling", 18f, 0.45f);             // ~2.25s
            Register("Fenring", 55f, 0.60f);               // ~3.0s
            Register("StoneGolem", 140f, 0.85f, hasNativeAnimation: false); // ~4.25s (Heavy, Artificial Stun)
            Register("Bat", 12f, 0.35f);                   // ~1.75s
            Register("Ulfv", 65f, 0.60f);                  // ~3.0s

            // Plains
            Register("Deathsquito", 12f, 0.35f);           // ~1.75s
            Register("Lox", 145f, 0.85f, hasNativeAnimation: true); // ~4.25s (Heavy)
            Register("Fuling", 58f, 0.55f);                // ~2.75s
            Register("Fuling_Berserker", 135f, 0.85f, hasNativeAnimation: true); // ~4.25s (Heavy)
            Register("Fuling_Shaman", 42f, 0.50f);         // ~2.5s
            Register("BlobTar", 42f, 0.55f);               // ~2.75s

            // Mistlands
            Register("Hare", 12f, 0.35f);                  // ~1.75s
            Register("Seeker", 68f, 0.60f);                // ~3.0s
            Register("SeekerBrood", 18f, 0.40f);           // ~2.0s
            Register("SeekerBrute", 175f, 0.85f, hasNativeAnimation: true); // ~4.25s (Heavy)
            Register("Gjall", 160f, 0.80f, hasNativeAnimation: true); // ~4.0s (Heavy)
            Register("Tick", 28f, 0.40f);                  // ~2.0s
            Register("Dvergr", 52f, 0.55f);                // ~2.75s
            Register("DvergrMage", 42f, 0.50f);            // ~2.5s

            // Ashlands
            Register("Charred_Melee", 56f, 0.55f);         // ~2.75s
            Register("Charred_Archer", 48f, 0.50f);        // ~2.5s
            Register("Charred_Mage", 52f, 0.50f);          // ~2.5s
            Register("FallenValkyrie", 150f, 0.85f, hasNativeAnimation: true); // ~4.25s (Heavy)
            Register("Morgen", 170f, 0.85f, hasNativeAnimation: true); // ~4.25s (Heavy)
            Register("Asksvin", 115f, 0.65f);              // ~3.25s
            Register("Volture", 28f, 0.45f);               // ~2.25s
        }
    }
}
