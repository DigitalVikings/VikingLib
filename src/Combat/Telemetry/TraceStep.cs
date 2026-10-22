namespace VikingLib.Combat.Telemetry
{
    /// <summary>
    /// Represents a discrete modification step recorded during damage pipeline execution.
    /// </summary>
    public class TraceStep
    {
        public DamagePhase Phase { get; }
        public string Source { get; }
        public string Operation { get; }
        public float Delta { get; }
        public string Details { get; }

        public TraceStep(DamagePhase phase, string source, string operation, float delta, string details = "")
        {
            Phase = phase;
            Source = source;
            Operation = operation;
            Delta = delta;
            Details = details;
        }

        public override string ToString()
        {
            if (string.IsNullOrEmpty(Details))
            {
                return $"[{Phase}] {Source}: {Operation} (Delta: {Delta:F2})";
            }
            return $"[{Phase}] {Source}: {Operation} ({Details})";
        }
    }
}
