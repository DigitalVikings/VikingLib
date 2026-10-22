using System;

namespace VikingLib.StatusEffects
{
    /// <summary>
    /// Trigger conditions that consume charges on an active status effect.
    /// </summary>
    [Flags]
    public enum ChargeConsumeTrigger
    {
        None = 0,

        /// <summary>
        /// Consumed when the attacker successfully delivers a melee hit in Phase 6.
        /// </summary>
        MeleeHitDelivered = 1 << 0,

        /// <summary>
        /// Consumed when the attacker successfully delivers a ranged projectile hit in Phase 6.
        /// </summary>
        RangedHitDelivered = 1 << 1,

        /// <summary>
        /// Consumed when the attacker delivers any hit (melee or ranged).
        /// </summary>
        AnyHitDelivered = MeleeHitDelivered | RangedHitDelivered,

        /// <summary>
        /// Consumed when the character takes damage in Phase 2.
        /// </summary>
        DamageTaken = 1 << 2,

        /// <summary>
        /// Consumed when the character initiates an attack swing.
        /// </summary>
        AttackInitiated = 1 << 3,

        /// <summary>
        /// Consumed when the character successfully blocks or parries an incoming attack.
        /// </summary>
        BlockParry = 1 << 4
    }
}
