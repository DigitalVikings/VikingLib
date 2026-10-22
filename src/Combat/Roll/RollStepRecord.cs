using System;

namespace VikingLib.Combat.Roll
{
    /// <summary>
    /// Immutable record capturing a single step or modifier applied to damage roll bounds.
    /// Tracks who performed the modification, what operation was applied, and the resulting bounds.
    /// </summary>
    public class RollStepRecord
    {
        public string Source { get; }
        public string Operation { get; }
        public float ResultingLower { get; }
        public float ResultingUpper { get; }

        public RollStepRecord(string source, string operation, float resultingLower, float resultingUpper)
        {
            Source = string.IsNullOrEmpty(source) ? "Unknown" : source;
            Operation = string.IsNullOrEmpty(operation) ? "" : operation;
            ResultingLower = resultingLower;
            ResultingUpper = resultingUpper;
        }

        public string Format(int padSource = 0)
        {
            string label = $"[{Source}]";
            if (padSource > 0)
            {
                label = label.PadRight(padSource);
            }

            if (Operation.StartsWith("Base", StringComparison.OrdinalIgnoreCase))
            {
                return $"{label} {Operation}";
            }
            return $"{label} {Operation} -> [{ResultingLower:F2} .. {ResultingUpper:F2}]";
        }

        public override string ToString() => Format();
    }
}
