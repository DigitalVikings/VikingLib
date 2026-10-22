using System;
using UnityEngine;

namespace VikingLib.StatusEffects
{
    /// <summary>
    /// Represents an independent slice within a multi-slice status effect.
    /// Supports individual duration timers, continuous intensity values, independent periodic tick intervals,
    /// and shortest-duration-first barrier depletion.
    /// </summary>
    public class StatusEffectSlice
    {
        private static long _globalSliceCounter;

        /// <summary>
        /// Unique identifier for this slice instance.
        /// </summary>
        public long Id { get; }

        /// <summary>
        /// Current value or remaining capacity of this slice.
        /// (e.g., remaining barrier health, remaining bleed damage, or bonus magnitude).
        /// </summary>
        public float Value { get; set; }

        /// <summary>
        /// Starting value assigned when this slice was created.
        /// </summary>
        public float InitialValue { get; internal set; }

        /// <summary>
        /// Remaining time-to-live in seconds (0 = indefinite until depleted or consumed).
        /// </summary>
        public float DurationRemaining { get; set; }

        /// <summary>
        /// Initial duration assigned to this slice in seconds.
        /// </summary>
        public float TotalDuration { get; internal set; }

        /// <summary>
        /// Interval between periodic ticks in seconds (0 = non-ticking).
        /// </summary>
        public float TickInterval { get; set; }

        /// <summary>
        /// Time remaining until the next periodic tick.
        /// </summary>
        public float TimeUntilNextTick { get; set; }

        /// <summary>
        /// The caster or attacker who applied this slice, if any.
        /// </summary>
        public Character? Source { get; }

        /// <summary>
        /// Arbitrary mod-author payload associated with this slice.
        /// </summary>
        public object? CustomData { get; set; }

        /// <summary>
        /// Returns true if this slice has a finite duration.
        /// </summary>
        public bool HasDuration => TotalDuration > 0f;

        /// <summary>
        /// Returns true if this slice's duration has expired.
        /// </summary>
        public bool IsExpired => HasDuration && DurationRemaining <= 0.0001f;

        public StatusEffectSlice(
            float value,
            float duration,
            float tickInterval = 0f,
            Character? source = null,
            object? customData = null)
        {
            Id = System.Threading.Interlocked.Increment(ref _globalSliceCounter);
            Value = value;
            InitialValue = value;
            DurationRemaining = Mathf.Max(0f, duration);
            TotalDuration = Mathf.Max(0f, duration);
            TickInterval = Mathf.Max(0f, tickInterval);
            TimeUntilNextTick = TickInterval;
            Source = source;
            CustomData = customData;
        }

        /// <summary>
        /// Advances the slice's timers by <paramref name="dt"/> and determines whether a periodic tick triggered.
        /// </summary>
        public bool Tick(float dt, out bool shouldTickDamage, out float tickDamage)
        {
            shouldTickDamage = false;
            tickDamage = 0f;

            if (HasDuration)
            {
                DurationRemaining = Mathf.Max(0f, DurationRemaining - dt);
            }

            if (TickInterval > 0f)
            {
                TimeUntilNextTick -= dt;
                if (TimeUntilNextTick <= 0f)
                {
                    TimeUntilNextTick += TickInterval;
                    shouldTickDamage = true;

                    float tickFraction = TotalDuration > 0f ? (TickInterval / TotalDuration) : 1.0f;
                    tickDamage = InitialValue * tickFraction;
                    Value = Mathf.Max(0f, Value - tickDamage);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Deducts up to <paramref name="amount"/> from this slice's remaining value.
        /// Returns the actual amount deducted.
        /// </summary>
        public float Deduct(float amount)
        {
            if (amount <= 0f || Value <= 0f) return 0f;
            float deducted = Mathf.Min(Value, amount);
            Value -= deducted;
            return deducted;
        }
    }
}
