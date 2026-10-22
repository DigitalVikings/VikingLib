using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace VikingLib.Combat.Attacks
{
    /// <summary>
    /// Central registry mapping weapon prefabs and items to their WeaponAttackProfile.
    /// Pre-populates canonical vanilla weapon categories, supports custom mod registrations,
    /// and dynamically generates fallback profiles from native Attack components.
    /// </summary>
    public static class WeaponAttackRegistry
    {
        private static readonly ConcurrentDictionary<string, WeaponAttackProfile> _registeredProfiles = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConditionalWeakTable<HitData, WeaponAttackInfo> _activeHitAttacks = new();

        static WeaponAttackRegistry()
        {
            RegisterCanonicalProfiles();
        }

        /// <summary>
        /// Registers or overrides a profile for a specific weapon prefab name or item name.
        /// </summary>
        public static void Register(string key, WeaponAttackProfile profile)
        {
            if (string.IsNullOrEmpty(key) || profile == null) return;
            _registeredProfiles[key] = profile;
        }

        /// <summary>
        /// Associates a HitData instance with runtime attack information.
        /// </summary>
        public static void RegisterHitAttack(HitData hit, WeaponAttackInfo info)
        {
            if (hit == null || info == null) return;
            _activeHitAttacks.Remove(hit);
            _activeHitAttacks.Add(hit, info);
        }

        /// <summary>
        /// Attempts to retrieve runtime attack information associated with a HitData instance.
        /// </summary>
        public static bool TryGetHitAttack(HitData? hit, out WeaponAttackInfo info)
        {
            info = null!;
            if (hit == null) return false;
            return _activeHitAttacks.TryGetValue(hit, out info);
        }

        /// <summary>
        /// Retrieves the attack profile for a weapon. If not explicitly registered,
        /// automatically synthesizes and caches a profile from the weapon's native Attack data.
        /// </summary>
        public static WeaponAttackProfile GetProfile(ItemDrop.ItemData? weapon)
        {
            if (weapon?.m_shared == null)
            {
                var neutral = new WeaponAttackProfile();
                neutral.AddStep("primary_1", 1.0f);
                return neutral;
            }

            WeaponAttackProfile? profile = null;

            // 1. Try lookup by prefab name
            string? prefabName = weapon.m_dropPrefab != null ? weapon.m_dropPrefab.name : null;
            if (!string.IsNullOrEmpty(prefabName) && _registeredProfiles.TryGetValue(prefabName, out profile))
            {
            }
            // 2. Try lookup by shared item name
            else
            {
                string itemName = weapon.m_shared.m_name;
                if (!string.IsNullOrEmpty(itemName) && _registeredProfiles.TryGetValue(itemName, out profile))
                {
                }
                // 3. Try lookup by skill category preset
                else
                {
                    string categoryKey = GetCategoryKey(weapon);
                    if (!string.IsNullOrEmpty(categoryKey) && _registeredProfiles.TryGetValue(categoryKey, out profile))
                    {
                    }
                    else
                    {
                        // 4. Dynamic synthesis from native Attack components
                        profile = SynthesizeFromNativeAttack(weapon);
                        string cacheKey = !string.IsNullOrEmpty(prefabName) ? prefabName : itemName;
                        if (!string.IsNullOrEmpty(cacheKey))
                        {
                            _registeredProfiles[cacheKey] = profile;
                        }
                    }
                }
            }

            // Invariant: If the physical weapon has no secondary attack animation in Valheim,
            // ensure the profile does not falsely provide a secondary step
            if (!weapon.HaveSecondaryAttack() && profile.HasSecondary)
            {
                var noSecProfile = new WeaponAttackProfile();
                foreach (var kvp in profile.Steps)
                {
                    if (kvp.Key.Equals("secondary", StringComparison.OrdinalIgnoreCase)) continue;
                    noSecProfile.AddStep(kvp.Key, kvp.Value.DefaultMultiplier, kvp.Value.Cleave);
                    foreach (var overrideKvp in kvp.Value.TypeMultipliers)
                    {
                        noSecProfile.GetStep(kvp.Key).SetMultiplier(overrideKvp.Key, overrideKvp.Value);
                    }
                }
                return noSecProfile;
            }

            return profile;
        }

        /// <summary>
        /// Synthesizes a WeaponAttackProfile by inspecting m_attack and m_secondaryAttack.
        /// </summary>
        public static WeaponAttackProfile SynthesizeFromNativeAttack(ItemDrop.ItemData weapon)
        {
            var profile = new WeaponAttackProfile();
            var primary = weapon.m_shared.m_attack;
            var secondary = weapon.m_shared.m_secondaryAttack;

            if (primary != null)
            {
                int chainLevels = primary.m_attackChainLevels;
                float baseMult = primary.m_damageMultiplier > 0.01f ? primary.m_damageMultiplier : 1.0f;
                float finisherMult = primary.m_lastChainDamageMultiplier > 0.01f ? primary.m_lastChainDamageMultiplier : 2.0f;
                bool isProj = primary.m_attackType == Attack.AttackType.Projectile ||
                              primary.m_attackType == Attack.AttackType.TriggerProjectile ||
                              primary.m_attackProjectile != null ||
                              weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Bow ||
                              weapon.m_shared.m_skillType == Skills.SkillType.Bows ||
                              weapon.m_shared.m_skillType == Skills.SkillType.Crossbows;
                float cleave = isProj ? 0f : (primary.m_lowerDamagePerHit ? 0.375f : 1.0f);

                if (chainLevels > 1)
                {
                    for (int i = 1; i < chainLevels; i++)
                    {
                        profile.AddStep($"primary_{i}", baseMult, cleave);
                    }
                    profile.AddStep($"primary_{chainLevels}", finisherMult, cleave);
                }
                else
                {
                    profile.AddStep("primary_1", baseMult, cleave);
                }
            }
            else
            {
                profile.AddStep("primary_1", 1.0f, 0.375f);
            }

            if (secondary != null && !string.IsNullOrEmpty(secondary.m_attackAnimation))
            {
                float secMult = secondary.m_damageMultiplier > 0.01f ? secondary.m_damageMultiplier : 1.0f;
                bool secIsProj = secondary.m_attackType == Attack.AttackType.Projectile ||
                                 secondary.m_attackType == Attack.AttackType.TriggerProjectile ||
                                 secondary.m_attackProjectile != null;
                float secCleave = secIsProj ? 0f : (secondary.m_lowerDamagePerHit ? 0.375f : 1.0f);
                profile.AddStep("secondary", secMult, secCleave);
            }

            return profile;
        }

        private static string GetCategoryKey(ItemDrop.ItemData weapon)
        {
            if (weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Torch)
            {
                return "category_torch";
            }

            return weapon.m_shared.m_skillType switch
            {
                Skills.SkillType.Swords => "category_sword",
                Skills.SkillType.Clubs => weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon ? "category_sledge" : "category_mace",
                Skills.SkillType.Knives => "category_knife",
                Skills.SkillType.Polearms => "category_atgeir",
                Skills.SkillType.Axes => weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.TwoHandedWeapon ? "category_battleaxe" : "category_axe",
                Skills.SkillType.Spears => "category_spear",
                Skills.SkillType.Bows => "category_bow",
                Skills.SkillType.Crossbows => "category_crossbow",
                Skills.SkillType.Unarmed => "category_unarmed",
                _ => ""
            };
        }

        private static void RegisterCanonicalProfiles()
        {
            // Swords: 3-hit combo (1.0x, 1.0x, 2.0x finisher), secondary 3.0x thrust
            var swordProfile = CreateProfile(3, finisherMult: 2.0f, secondaryMult: 3.0f, primaryCleave: 0.375f, secondaryCleave: 0.375f);
            _registeredProfiles["category_sword"] = swordProfile;

            // Maces: 3-hit combo (1.0x, 1.0x, 2.0x finisher), secondary 2.5x uppercut
            var maceProfile = CreateProfile(3, finisherMult: 2.0f, secondaryMult: 2.5f, primaryCleave: 0.375f, secondaryCleave: 0.375f);
            _registeredProfiles["category_mace"] = maceProfile;

            // Wooden Club has no secondary attack
            var clubProfile = CreateProfile(3, finisherMult: 2.0f, secondaryMult: 0f, primaryCleave: 0.375f, secondaryCleave: 0.375f);
            _registeredProfiles["Club"] = clubProfile;
            _registeredProfiles["$item_club"] = clubProfile;

            // Torches: 3-hit combo (1.0x, 1.0x, 2.0x finisher), no secondary attack
            var torchProfile = CreateProfile(3, finisherMult: 2.0f, secondaryMult: 0f, primaryCleave: 0.375f, secondaryCleave: 0.375f);
            _registeredProfiles["category_torch"] = torchProfile;
            _registeredProfiles["Torch"] = torchProfile;
            _registeredProfiles["$item_torch"] = torchProfile;
            _registeredProfiles["$item_torchmist"] = torchProfile;

            // Knives: 3-hit combo (1.0x, 1.0x, 2.0x finisher), secondary 3.0x leap
            var knifeProfile = CreateProfile(3, finisherMult: 2.0f, secondaryMult: 3.0f, primaryCleave: 0.375f, secondaryCleave: 0.375f);
            _registeredProfiles["category_knife"] = knifeProfile;

            // Atgeirs (Polearms): 3-hit combo (1.0x, 1.0x, 2.0x finisher), secondary 1.0x 360-spin (100% cleave)
            var atgeirProfile = CreateProfile(3, finisherMult: 2.0f, secondaryMult: 1.0f, primaryCleave: 0.375f, secondaryCleave: 1.0f);
            _registeredProfiles["category_atgeir"] = atgeirProfile;

            // Battleaxes (2H Axes): 3-hit combo (1.0x, 1.0x, 2.0x finisher), secondary 0.5x jab (with 4.0x stagger)
            var battleaxeProfile = CreateProfile(3, finisherMult: 2.0f, secondaryMult: 0.5f, primaryCleave: 0.375f, secondaryCleave: 0.375f);
            _registeredProfiles["category_battleaxe"] = battleaxeProfile;

            // 1H Axes: 3-hit combo (1.0x, 1.0x, 2.0x finisher), secondary 1.5x heavy strike
            var axeProfile = CreateProfile(3, finisherMult: 2.0f, secondaryMult: 1.5f, primaryCleave: 0.375f, secondaryCleave: 0.375f);
            _registeredProfiles["category_axe"] = axeProfile;

            // Spears: 1-hit (1.0x thrust), secondary 1.5x throw (single-target projectile)
            var spearProfile = CreateProfile(1, finisherMult: 1.0f, secondaryMult: 1.5f, primaryCleave: 0.375f, secondaryCleave: 0f);
            _registeredProfiles["category_spear"] = spearProfile;

            // Bows: 1-hit (1.0x single-target projectile)
            var bowProfile = CreateProfile(1, finisherMult: 1.0f, secondaryMult: 0f, primaryCleave: 0f, secondaryCleave: 0f);
            _registeredProfiles["category_bow"] = bowProfile;

            // Crossbows: 1-hit (1.0x single-target projectile)
            var crossbowProfile = CreateProfile(1, finisherMult: 1.0f, secondaryMult: 0f, primaryCleave: 0f, secondaryCleave: 0f);
            _registeredProfiles["category_crossbow"] = crossbowProfile;

            // Sledges: 1-hit (1.0x slam, 100% AoE cleave)
            var sledgeProfile = CreateProfile(1, finisherMult: 1.0f, secondaryMult: 0f, primaryCleave: 1.0f, secondaryCleave: 1.0f);
            _registeredProfiles["category_sledge"] = sledgeProfile;

            // Unarmed: 3-hit combo (1.0x, 1.0x, 2.0x finisher), secondary 2.0x kick
            var unarmedProfile = CreateProfile(3, finisherMult: 2.0f, secondaryMult: 2.0f, primaryCleave: 0.375f, secondaryCleave: 0.375f);
            _registeredProfiles["category_unarmed"] = unarmedProfile;
        }

        private static WeaponAttackProfile CreateProfile(
            int chainLevels,
            float finisherMult,
            float secondaryMult,
            float primaryCleave = 0.375f,
            float secondaryCleave = 0.375f)
        {
            var p = new WeaponAttackProfile();
            if (chainLevels > 1)
            {
                for (int i = 1; i < chainLevels; i++)
                {
                    p.AddStep($"primary_{i}", 1.0f, primaryCleave);
                }
                p.AddStep($"primary_{chainLevels}", finisherMult, primaryCleave);
            }
            else
            {
                p.AddStep("primary_1", 1.0f, primaryCleave);
            }

            if (secondaryMult > 0.01f)
            {
                p.AddStep("secondary", secondaryMult, secondaryCleave);
            }

            return p;
        }
    }
}
