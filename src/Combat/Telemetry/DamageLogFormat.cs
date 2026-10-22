namespace VikingLib.Combat.Telemetry
{
    /// <summary>
    /// Visual presentation style for combat telemetry logs.
    /// </summary>
    public enum DamageLogFormat
    {
        /// <summary>
        /// Hierarchical tree with indented sub-nodes for each active 3-tier step and mitigation factor.
        /// </summary>
        Tree,

        /// <summary>
        /// High-density tree with one-line tier summaries.
        /// </summary>
        Compact,

        /// <summary>
        /// Tabular step-by-step combat audit card.
        /// </summary>
        Audit
    }
}
