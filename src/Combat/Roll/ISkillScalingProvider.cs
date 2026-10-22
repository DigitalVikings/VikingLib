namespace VikingLib.Combat.Roll
{
    /// <summary>
    /// Contract for evaluating skill scaling curves on damage roll bounds.
    /// Downstream mods can implement this interface and assign it to <see cref="DamageRollPipeline.SkillScalingProvider"/>
    /// to completely customize or replace weapon skill scaling without touching Harmony patches.
    /// </summary>
    public interface ISkillScalingProvider
    {
        /// <summary>
        /// Calculates lower and upper bound multipliers based on attacker skills and combat context.
        /// </summary>
        /// <param name="context">The active damage roll context.</param>
        /// <param name="lowerMult">Output lower bound multiplier (e.g. 0.25f .. 0.85f).</param>
        /// <param name="upperMult">Output upper bound multiplier (e.g. 0.55f .. 1.00f).</param>
        void CalculateBounds(DamageRollContext context, out float lowerMult, out float upperMult);
    }
}
