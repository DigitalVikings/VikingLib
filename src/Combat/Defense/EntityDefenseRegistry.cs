using System;
using System.Collections.Concurrent;

namespace VikingLib.Combat.Defense
{
    /// <summary>
    /// Centralized registry for base armor and continuous resistances across all creatures and objects.
    /// Provides data-driven registration with seamless automatic fallback to vanilla definitions.
    /// </summary>
    public static class EntityDefenseRegistry
    {
        private static readonly ConcurrentDictionary<string, EntityDefenseData> _registry = new(StringComparer.OrdinalIgnoreCase);

        private static readonly ConcurrentDictionary<string, byte> _bloodlessPrefabs = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Skeleton"] = 1,
            ["Skeleton_Poison"] = 1,
            ["Skeleton_NoArcher"] = 1,
            ["Skeleton_Hildir"] = 1,
            ["Ghost"] = 1,
            ["Wraith"] = 1,
            ["StoneGolem"] = 1,
            ["Blob"] = 1,
            ["BlobElite"] = 1,
            ["BlobTar"] = 1,
            ["Surtling"] = 1,
            ["Charred_Melee"] = 1,
            ["Charred_Archer"] = 1,
            ["Charred_Mage"] = 1,
            ["Charred_Twitcher"] = 1
        };

        /// <summary>
        /// Registers a prefab name as a bloodless entity immune to bleed status effects.
        /// </summary>
        public static void RegisterBloodless(string prefabName)
        {
            if (string.IsNullOrWhiteSpace(prefabName)) return;
            _bloodlessPrefabs[prefabName.Trim()] = 1;
        }

        /// <summary>
        /// Checks whether a prefab or creature name belongs to a bloodless entity.
        /// </summary>
        public static bool IsBloodless(string? nameOrPrefab)
        {
            if (string.IsNullOrWhiteSpace(nameOrPrefab)) return false;
            string clean = nameOrPrefab.Replace("(Clone)", "").Trim();
            return _bloodlessPrefabs.ContainsKey(clean);
        }

        /// <summary>
        /// Checks whether a character is bloodless (e.g. Skeletons, Ghosts, Stone Golems, Blobs, Elementals).
        /// </summary>
        public static bool IsBloodless(Character character)
        {
            if ((object)character == null) return false;

            // 1. Check GameObject prefab name
            try
            {
                if (character.gameObject != null)
                {
                    string prefab = global::Utils.GetPrefabName(character.gameObject);
                    if (IsBloodless(prefab)) return true;
                }
            }
            catch { /* In offline testing, gameObject may be uninitialized */ }

            // 2. Check character.m_name
            if (IsBloodless(character.m_name)) return true;

            // 3. Check character.name
            try
            {
                if (IsBloodless(character.name)) return true;
            }
            catch { }

            // 4. Check slash immunity fallback
            try
            {
                if (character.GetDamageModifiers().m_slash == HitData.DamageModifier.Immune)
                {
                    return true;
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// Determines whether a character is eligible to suffer bleeding damage.
        /// Returns false for bloodless creatures or characters with BleedResistance &lt;= 0.001.
        /// </summary>
        public static bool CanBleed(Character character)
        {
            if ((object)character == null) return false;
            try { if (character.IsDead()) return false; } catch { }
            if (IsBloodless(character)) return false;

            float bleedRes = Stats.CharacterStatManager.Evaluate(character, Stats.CharacterStatType.ResistanceBleed);
            return bleedRes > 0.001f;
        }

        /// <summary>
        /// Registers or updates base defensive statistics for a specific prefab name.
        /// </summary>
        public static void Register(string prefabName, EntityDefenseData defenseData)
        {
            if (string.IsNullOrWhiteSpace(prefabName) || defenseData == null) return;
            _registry[prefabName.Trim()] = defenseData;
        }

        /// <summary>
        /// Attempts to get registered defense data for a prefab name.
        /// </summary>
        public static bool TryGet(string prefabName, out EntityDefenseData data)
        {
            return _registry.TryGetValue(prefabName, out data!);
        }

        /// <summary>
        /// Resolves the effective defense data for a Character (Player or Monster).
        /// Merges any registered overrides on top of the character's live vanilla damage modifiers.
        /// </summary>
        public static EntityDefenseData GetDefense(Character character)
        {
            if (character == null) return new EntityDefenseData();

            string cleanName = global::Utils.GetPrefabName(character.gameObject);

            if (_registry.TryGetValue(cleanName, out var registered))
            {
                // Return a copy with live character state and registered overrides layered on top of vanilla baseline
                return CloneWithLiveCharacterState(registered, character);
            }

            // Fallback: Construct live defense data from vanilla properties
            var fallback = new EntityDefenseData();

            if (character.IsPlayer())
            {
                fallback.PhysicalArmor = character.GetBodyArmor();
            }
            else if (Game.m_worldLevel > 0 && Game.instance != null)
            {
                fallback.PhysicalArmor = Game.m_worldLevel * Game.instance.m_worldLevelEnemyBaseAC;
            }

            // Populate resistances from vanilla DamageModifiers
            try
            {
                PopulateFromVanillaModifiers(fallback, character.GetDamageModifiers());
            }
            catch { }

            return fallback;
        }

        /// <summary>
        /// Resolves the effective defense data for a piece or destructible object.
        /// </summary>
        public static EntityDefenseData GetDefense(WearNTear wearNTear)
        {
            if (wearNTear == null) return new EntityDefenseData();

            var data = new EntityDefenseData();
            try { PopulateFromVanillaModifiers(data, wearNTear.m_damages); } catch { }

            string cleanName = global::Utils.GetPrefabName(wearNTear.gameObject);
            if (_registry.TryGetValue(cleanName, out var registered))
            {
                MergeOverrides(data, registered);
            }

            return data;
        }

        /// <summary>
        /// Resolves the effective defense data for any IDestructible entity (TreeBase, TreeLog, Destructible, MineRock, MineRock5).
        /// </summary>
        public static EntityDefenseData GetDefense(IDestructible destructible)
        {
            if (destructible == null) return new EntityDefenseData();

            if (destructible is Character character) return GetDefense(character);
            if (destructible is WearNTear wearNTear) return GetDefense(wearNTear);

            var data = new EntityDefenseData();
            HitData.DamageModifiers? mods = null;

            if (destructible is TreeBase tb) mods = tb.m_damageModifiers;
            else if (destructible is TreeLog tl) mods = tl.m_damages;
            else if (destructible is global::Destructible d) mods = d.m_damages;
            else if (destructible is MineRock mr) mods = mr.m_damageModifiers;
            else if (destructible is MineRock5 mr5) mods = mr5.m_damageModifiers;

            if (mods.HasValue)
            {
                try { PopulateFromVanillaModifiers(data, mods.Value); } catch { }
            }

            if (destructible is UnityEngine.Component comp)
            {
                string cleanName = global::Utils.GetPrefabName(comp.gameObject);
                if (_registry.TryGetValue(cleanName, out var registered))
                {
                    MergeOverrides(data, registered);
                }
            }

            return data;
        }

        /// <summary>
        /// Converts a vanilla discrete DamageModifier enum to a continuous float multiplier.
        /// </summary>
        public static float ToFloatMultiplier(HitData.DamageModifier mod) => mod switch
        {
            HitData.DamageModifier.Immune => 0f,
            HitData.DamageModifier.VeryResistant => 0.25f,
            HitData.DamageModifier.Resistant => 0.5f,
            HitData.DamageModifier.Normal => 1.0f,
            HitData.DamageModifier.Weak => 1.5f,
            HitData.DamageModifier.VeryWeak => 2.0f,
            HitData.DamageModifier.Ignore => 0f,
            _ => 1.0f
        };

        /// <summary>
        /// Maps an arbitrary continuous float multiplier to the closest vanilla DamageModifier enum
        /// for floating damage text coloring and native hit feedback.
        /// </summary>
        public static HitData.DamageModifier ToVanillaModifier(float multiplier)
        {
            if (multiplier <= 0.05f) return HitData.DamageModifier.Immune;
            if (multiplier <= 0.35f) return HitData.DamageModifier.VeryResistant;
            if (multiplier <= 0.75f) return HitData.DamageModifier.Resistant;
            if (multiplier >= 1.75f) return HitData.DamageModifier.VeryWeak;
            if (multiplier >= 1.25f) return HitData.DamageModifier.Weak;
            return HitData.DamageModifier.Normal;
        }

        private static void PopulateFromVanillaModifiers(EntityDefenseData data, HitData.DamageModifiers mods)
        {
            data.SetResistance(DamageType.Blunt, ToFloatMultiplier(mods.m_blunt));
            data.SetResistance(DamageType.Slash, ToFloatMultiplier(mods.m_slash));
            data.SetResistance(DamageType.Pierce, ToFloatMultiplier(mods.m_pierce));
            data.SetResistance(DamageType.Chop, ToFloatMultiplier(mods.m_chop));
            data.SetResistance(DamageType.Pickaxe, ToFloatMultiplier(mods.m_pickaxe));
            data.SetResistance(DamageType.Fire, ToFloatMultiplier(mods.m_fire));
            data.SetResistance(DamageType.Frost, ToFloatMultiplier(mods.m_frost));
            data.SetResistance(DamageType.Lightning, ToFloatMultiplier(mods.m_lightning));
            data.SetResistance(DamageType.Poison, ToFloatMultiplier(mods.m_poison));
            data.SetResistance(DamageType.Spirit, ToFloatMultiplier(mods.m_spirit));
        }

        private static EntityDefenseData Clone(EntityDefenseData source)
        {
            var copy = new EntityDefenseData(source.PhysicalArmor, source.MagicArmor);
            foreach (var kvp in source.TypeArmorOverrides) copy.TypeArmorOverrides[kvp.Key] = kvp.Value;
            foreach (var kvp in source.Resistances) copy.Resistances[kvp.Key] = kvp.Value;
            return copy;
        }

        private static EntityDefenseData CloneWithLiveCharacterState(EntityDefenseData registered, Character character)
        {
            var data = new EntityDefenseData();

            // 1. Establish base armor
            if (character.IsPlayer())
            {
                data.PhysicalArmor = registered.PhysicalArmor + character.GetBodyArmor();
                data.MagicArmor = registered.MagicArmor;
            }
            else
            {
                data.PhysicalArmor = registered.PhysicalArmor;
                data.MagicArmor = registered.MagicArmor;
                if (data.PhysicalArmor <= 0f && Game.m_worldLevel > 0 && Game.instance != null)
                {
                    data.PhysicalArmor = Game.m_worldLevel * Game.instance.m_worldLevelEnemyBaseAC;
                }
            }

            // 2. Populate baseline resistances from the live character's vanilla modifiers (including Chop: Ignore, Pickaxe: Ignore, Spirit, etc.)
            try
            {
                PopulateFromVanillaModifiers(data, character.GetDamageModifiers());
            }
            catch { }

            // 3. Layer registered overrides on top of the vanilla baseline
            MergeOverrides(data, registered);

            return data;
        }

        private static void MergeOverrides(EntityDefenseData target, EntityDefenseData overrides)
        {
            if (overrides.PhysicalArmor > 0f) target.PhysicalArmor = overrides.PhysicalArmor;
            if (overrides.MagicArmor > 0f) target.MagicArmor = overrides.MagicArmor;

            foreach (var kvp in overrides.TypeArmorOverrides)
            {
                target.TypeArmorOverrides[kvp.Key] = kvp.Value;
            }
            foreach (var kvp in overrides.Resistances)
            {
                target.Resistances[kvp.Key] = kvp.Value;
            }
        }
    }
}
