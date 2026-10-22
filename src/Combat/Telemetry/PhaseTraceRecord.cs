using System.Collections.Generic;
using VikingLib.Configuration;

namespace VikingLib.Combat.Telemetry
{
    /// <summary>
    /// Holds structured 3-tier modification records for a single damage type during an attack phase.
    /// </summary>
    public class TierRecord
    {
        public DamageType Type { get; set; }
        public float BaseValue { get; set; }
        public float ResultingValue { get; set; }
        public float FlatSum { get; set; }
        public List<string> FlatSources { get; } = new();
        public float IncreasedSum { get; set; }
        public List<string> IncreasedSources { get; } = new();
        public float MultiplierProduct { get; set; } = 1f;
        public List<string> MultiplierSources { get; } = new();

        public bool HasModifiers => FlatSources.Count > 0 || IncreasedSources.Count > 0 || MultiplierSources.Count > 0;
    }

    /// <summary>
    /// Holds structured defensive mitigation records (armor and resistance) for a single damage type.
    /// Supports diagnostics for both LinearEHP and VanillaPiecewise armor models.
    /// </summary>
    public class MitigationRecord
    {
        public DamageType Type { get; set; }
        public float IncomingDamage { get; set; }
        public float MitigatedDamage { get; set; }

        // Armor State
        public bool HasArmorMitigation { get; set; }
        public ArmorMitigationModel ArmorModel { get; set; } = ArmorMitigationModel.LinearEHP;
        public float ArmorValue { get; set; }
        public float EffectiveArmor { get; set; }
        public float ArmorEfficiency { get; set; }
        public float ArmorPenetrationPercent { get; set; }
        public float ArmorPenetrationFlat { get; set; }
        public float ArmorReduction { get; set; }

        // Model Diagnostics
        public float ArmorScaleConstant { get; set; } = 60.0f; // K constant for LinearEHP
        public float LinearEhpTotalEfficiency { get; set; }    // (EffectiveArmor / K) + ArmorEfficiency
        public float ScaledPiecewiseArmor { get; set; }        // EffectiveArmor * (1 + ArmorEfficiency)
        public bool IsPiecewiseQuadratic { get; set; }         // True if IncomingDamage < 2 * ScaledArmor

        // Resistances
        public float ResistanceMultiplier { get; set; } = 1f;
        public string ResistanceLabel { get; set; } = "Normal";

        public bool HasMitigation => HasArmorMitigation || ArmorReduction > 0.01f || System.Math.Abs(ResistanceMultiplier - 1.0f) > 0.001f;
    }
}
