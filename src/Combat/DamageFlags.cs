using System;

namespace VikingLib.Combat
{
    /// <summary>
    /// Performance-critical 64-bit flags describing the delivery, classification, and metadata of a combat hit.
    /// </summary>
    [Flags]
    public enum DamageFlags : ulong
    {
        None            = 0,
        Melee           = 1UL << 0,
        Ranged          = 1UL << 1,
        Magic           = 1UL << 2,
        DoT             = 1UL << 3,
        AreaOfEffect    = 1UL << 4,
        Environmental   = 1UL << 5,
        CriticalHit     = 1UL << 6,
        Backstab        = 1UL << 7,
        TrueDamage      = 1UL << 8,
        IgnoresArmor    = 1UL << 9,
        CannotBeBlocked = 1UL << 10,
        CannotBeParried = 1UL << 11,
        CannotBeDodged  = 1UL << 12,
        CanTriggerProcs = 1UL << 13,
        Lifesteal       = 1UL << 14,
        StaggerBonus    = 1UL << 15,
        DeathDefied     = 1UL << 16,
        IsReflected     = 1UL << 17,
    }
}
