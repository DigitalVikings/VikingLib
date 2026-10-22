using System.Collections.Generic;

namespace VikingLib.Combat.Defense
{
    /// <summary>
    /// Holds the defensive statistics (base armor and continuous float resistances) for an entity.
    /// </summary>
    public class EntityDefenseData
    {
        /// <summary>
        /// Base physical armor applied to Blunt, Slash, and Pierce damage.
        /// </summary>
        public float PhysicalArmor { get; set; }

        /// <summary>
        /// Base elemental / magic armor applied to Fire, Frost, Lightning, Poison, and Spirit damage.
        /// </summary>
        public float MagicArmor { get; set; }

        /// <summary>
        /// Optional per-damage-type armor overrides (e.g. extra Pierce armor).
        /// </summary>
        public Dictionary<DamageType, float> TypeArmorOverrides { get; } = new();

        /// <summary>
        /// Continuous float damage multipliers for each damage type (e.g. 0.5f for 50% damage taken, 1.5f for 150% damage taken).
        /// </summary>
        public Dictionary<DamageType, float> Resistances { get; } = new();

        public EntityDefenseData(float physicalArmor = 0f, float magicArmor = 0f)
        {
            PhysicalArmor = physicalArmor;
            MagicArmor = magicArmor;
        }

        /// <summary>
        /// Returns the effective armor for a specific damage type.
        /// </summary>
        public float GetArmorForType(DamageType type)
        {
            if (type == DamageType.TrueDamage) return 0f;

            if (TypeArmorOverrides.TryGetValue(type, out float overrideVal))
            {
                return overrideVal;
            }

            if (type.IsPhysical()) return PhysicalArmor;
            if (type.IsElemental() || type == DamageType.Poison || type == DamageType.Spirit) return MagicArmor;
            return 0f;
        }

        /// <summary>
        /// Returns the continuous resistance multiplier for a specific damage type (default 1.0f).
        /// </summary>
        public float GetResistanceForType(DamageType type)
        {
            if (type == DamageType.TrueDamage) return 1f;

            if (Resistances.TryGetValue(type, out float mult))
            {
                return mult;
            }
            return 1f;
        }

        /// <summary>
        /// Sets a specific continuous resistance multiplier.
        /// </summary>
        public EntityDefenseData SetResistance(DamageType type, float multiplier)
        {
            Resistances[type] = multiplier;
            return this;
        }

        /// <summary>
        /// Sets specific armor for a damage type.
        /// </summary>
        public EntityDefenseData SetTypeArmor(DamageType type, float armor)
        {
            TypeArmorOverrides[type] = armor;
            return this;
        }
    }
}
