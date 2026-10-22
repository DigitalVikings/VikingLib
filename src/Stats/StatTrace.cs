using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace VikingLib.Stats
{
    /// <summary>
    /// Itemized diagnostic audit breakdown capturing the 3-tier mathematical calculation of a character stat.
    /// </summary>
    public class StatTrace
    {
        public CharacterStatType StatType { get; set; }
        public string? CustomStatKey { get; set; }
        public float BaseValue { get; set; }
        public List<(string Source, float Value)> FlatModifiers { get; } = new();
        public float FlatSum { get; set; }
        public float BaseWithFlat => BaseValue + FlatSum;

        public List<(string Source, float Value)> IncreasedModifiers { get; } = new();
        public float IncreasedSum { get; set; }
        public float AdditiveFactor => Mathf.Max(0f, 1f + IncreasedSum);

        public List<(string Source, float Value)> Multipliers { get; } = new();
        public float MultiplierProduct { get; set; } = 1f;

        public float FinalValue { get; set; }

        public string ToFormattedString()
        {
            var sb = new StringBuilder();
            string header = !string.IsNullOrEmpty(CustomStatKey) ? CustomStatKey! : StatType.ToString();
            sb.AppendLine($"<color=orange>{header}</color>: <color=yellow>{FinalValue:F1}</color>");
            sb.AppendLine($"  ├── Base Value: {BaseValue:F1}");

            if (FlatModifiers.Count > 0)
            {
                sb.AppendLine($"  ├── Tier 1 (Flat): {(FlatSum >= 0 ? "+" : "")}{FlatSum:F1} -> ({BaseWithFlat:F1})");
                foreach (var (src, val) in FlatModifiers)
                {
                    sb.AppendLine($"  │   └── {src}: {(val >= 0 ? "+" : "")}{val:F1}");
                }
            }

            if (IncreasedModifiers.Count > 0)
            {
                sb.AppendLine($"  ├── Tier 2 (Additive %): {(IncreasedSum >= 0 ? "+" : "")}{IncreasedSum * 100f:F1}% -> (x{AdditiveFactor:F2})");
                foreach (var (src, val) in IncreasedModifiers)
                {
                    sb.AppendLine($"  │   └── {src}: {(val >= 0 ? "+" : "")}{val * 100f:F1}%");
                }
            }

            if (Multipliers.Count > 0)
            {
                sb.AppendLine($"  ├── Tier 3 (Multipliers): x{MultiplierProduct:F2}");
                foreach (var (src, val) in Multipliers)
                {
                    sb.AppendLine($"  │   └── {src}: x{val:F2}");
                }
            }

            sb.Append($"  └── Final: <color=yellow>{FinalValue:F2}</color>");
            return sb.ToString();
        }
    }
}
