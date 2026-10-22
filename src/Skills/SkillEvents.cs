using System;

namespace VikingLib.SkillSystem
{
    /// <summary>
    /// Event argument container providing cancellation capability for interceptable skill events.
    /// </summary>
    public class SkillCancelEventArgs : EventArgs
    {
        public bool Cancel { get; set; } = false;
    }

    /// <summary>
    /// Lifecycle and progression events for skill leveling, XP gain, and death penalties.
    /// Used by Type A mods to award custom perks on skill level changes,
    /// and by Type C mods to intercept native XP and death penalties.
    /// </summary>
    public static class SkillEvents
    {
        /// <summary>
        /// [For Type A Mods]
        /// Fired whenever a player's skill level increases or updates (e.g. on level up or stats sync).
        /// Ideal for attaching declarative 3-Tier perks or modifiers to the player.
        /// </summary>
        public static event Action<Player, Skills.SkillType, float>? OnSkillLevelChanged;

        /// <summary>
        /// [For Type C Mods]
        /// Intercepts native Character.RaiseSkill calls.
        /// Setting args.Cancel = true suppresses native XP gain, level-up banners, and game telemetry,
        /// allowing the overhaul mod to redirect XP into its custom class/attribute engine.
        /// </summary>
        public static event Action<Character, Skills.SkillType, float, SkillCancelEventArgs>? OnRaiseSkill;

        /// <summary>
        /// [For Type C Mods]
        /// Intercepts native death penalty skill loss in Skills.OnDeath / LowerAllSkills.
        /// Setting args.Cancel = true suppresses native skill drain on player death.
        /// </summary>
        public static event Action<Player, SkillCancelEventArgs>? OnDeathPenalty;

        internal static void InvokeSkillLevelChanged(Player player, Skills.SkillType skill, float level)
        {
            OnSkillLevelChanged?.Invoke(player, skill, level);
        }

        internal static bool ShouldRaiseSkill(Character character, Skills.SkillType skill, float value)
        {
            if (OnRaiseSkill == null) return true;
            var args = new SkillCancelEventArgs();
            OnRaiseSkill.Invoke(character, skill, value, args);
            return !args.Cancel;
        }

        internal static bool ShouldApplyDeathPenalty(Player player)
        {
            if (OnDeathPenalty == null) return true;
            var args = new SkillCancelEventArgs();
            OnDeathPenalty.Invoke(player, args);
            return !args.Cancel;
        }
    }
}
