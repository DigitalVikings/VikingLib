namespace VikingLib.Combat
{
    /// <summary>
    /// The discrete sequential execution phases of the VikingLib Damage Pipeline.
    /// </summary>
    public enum DamagePhase
    {
        /// <summary>
        /// Phase 0: Target filtering, iframes, dodge verification, hit cancellation.
        /// </summary>
        PreCheck = 0,

        /// <summary>
        /// Phase 1: Attacker buffs, weapon scaling, skills, critical strike roll, backstab multipliers.
        /// </summary>
        AttackerModifiers = 1,

        /// <summary>
        /// Phase 2: Target defensive perks, damage reduction stances, unyielding buffs.
        /// </summary>
        TargetDefenses = 2,

        /// <summary>
        /// Phase 3: Defensive mitigation including block, granular armor mitigation, and continuous resistances.
        /// </summary>
        Mitigation = 3,

        /// <summary>
        /// Phase 4: Damage absorption barriers (e.g. Eitr shield, magical ward bubbles) before health deduction.
        /// </summary>
        Absorption = 4,

        /// <summary>
        /// Phase 5: Final calculation, fatal damage prevention ("death defiance"), and hit payload finalization.
        /// </summary>
        Execution = 5,

        /// <summary>
        /// Phase 6: Post-hit effects, lifesteal healing, thorns retaliation, and combat telemetry broadcast.
        /// </summary>
        PostHitProcs = 6
    }
}
