using System;
using UnityEngine;

namespace VikingLib.Combat.Roll
{
    /// <summary>
    /// Mutable context passed through the DamageRollPipeline to establish, modify, and evaluate
    /// the weapon attack damage roll bounds before damage calculation occurs.
    /// </summary>
    public class DamageRollContext
    {
        public Character? Attacker { get; }
        public ItemDrop.ItemData? Weapon { get; }
        public Skills.SkillType SkillType { get; set; }

        /// <summary>
        /// Attack step key (e.g. "primary_1", "primary_3", "secondary").
        /// </summary>
        public string AttackKey { get; set; } = "primary_1";

        /// <summary>
        /// Human-readable label for this attack step (e.g. "Primary Hit 1", "Combo Finisher (Hit 3/3)").
        /// </summary>
        public string AttackDisplayName { get; set; } = "Primary Hit 1";

        /// <summary>
        /// Lower bound multiplier for the damage roll (defaults to 1.0f).
        /// </summary>
        public float LowerBound { get; set; } = 1.0f;

        /// <summary>
        /// Upper bound multiplier for the damage roll (defaults to 1.0f).
        /// </summary>
        public float UpperBound { get; set; } = 1.0f;

        /// <summary>
        /// The resulting factor rolled uniformly between LowerBound and UpperBound.
        /// </summary>
        public float RolledFactor { get; internal set; } = 1.0f;

        /// <summary>
        /// Diagnostic history of every modification made to the roll bounds.
        /// </summary>
        public System.Collections.Generic.List<RollStepRecord> Steps { get; } = new();

        public DamageRollContext(Character? attacker, ItemDrop.ItemData? weapon, Skills.SkillType skillType)
        {
            Attacker = attacker;
            Weapon = weapon;
            SkillType = skillType;
            RecordStep("Initial Attack Power", "Base [1.00 .. 1.00]");
        }

        /// <summary>
        /// Records an explicit step in the bounds pipeline for telemetry and auditing.
        /// </summary>
        public void RecordStep(string source, string operation)
        {
            Steps.Add(new RollStepRecord(source, operation, LowerBound, UpperBound));
        }

        /// <summary>
        /// Multiplies both the lower and upper bounds by a common factor.
        /// </summary>
        public void MultiplyBounds(float factor, string source = "")
        {
            LowerBound *= factor;
            UpperBound *= factor;
            if (!string.IsNullOrEmpty(source))
            {
                RecordStep(source, $"Scale bounds x{factor:F2}");
            }
        }

        /// <summary>
        /// Multiplies only the lower bound by a factor.
        /// </summary>
        public void MultiplyLower(float factor, string source = "")
        {
            LowerBound *= factor;
            if (!string.IsNullOrEmpty(source))
            {
                RecordStep(source, $"Scale lower x{factor:F2}");
            }
        }

        /// <summary>
        /// Multiplies only the upper bound by a factor.
        /// </summary>
        public void MultiplyUpper(float factor, string source = "")
        {
            UpperBound *= factor;
            if (!string.IsNullOrEmpty(source))
            {
                RecordStep(source, $"Scale upper x{factor:F2}");
            }
        }

        /// <summary>
        /// Explicitly sets both bounds to specific values.
        /// </summary>
        public void SetBounds(float lower, float upper, string source = "")
        {
            LowerBound = lower;
            UpperBound = upper;
            if (!string.IsNullOrEmpty(source))
            {
                RecordStep(source, $"Set bounds [{lower:F2} .. {upper:F2}]");
            }
        }

        /// <summary>
        /// Sets the roll to a deterministic constant value (LowerBound == UpperBound).
        /// </summary>
        public void SetConstant(float value, string source = "")
        {
            LowerBound = value;
            UpperBound = value;
            if (!string.IsNullOrEmpty(source))
            {
                RecordStep(source, $"Constant {value:F2}");
            }
        }

        /// <summary>
        /// Raises the lower bound to at least the specified minimum value (raising the damage floor).
        /// </summary>
        public void RaiseFloor(float minimum, string source = "")
        {
            LowerBound = Mathf.Max(LowerBound, minimum);
            if (!string.IsNullOrEmpty(source))
            {
                RecordStep(source, $"Raise floor to {minimum:F2}");
            }
        }
    }
}
