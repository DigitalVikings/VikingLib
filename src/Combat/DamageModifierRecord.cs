namespace VikingLib.Combat
{
    /// <summary>
    /// Represents a single mathematical modification registered dynamically during a pipeline phase.
    /// </summary>
    public readonly struct DamageModifierRecord
    {
        public DamageType? Type { get; }
        public ModifierTier Tier { get; }
        public float Value { get; }
        public string Source { get; }

        public DamageModifierRecord(ModifierTier tier, float value, string source, DamageType? type = null)
        {
            Tier = tier;
            Value = value;
            Source = source;
            Type = type;
        }

        public override string ToString() =>
            $"[{Tier}] {(Type.HasValue ? Type.Value.ToString() : "All")}: {Value:F2} from '{Source}'";
    }
}
