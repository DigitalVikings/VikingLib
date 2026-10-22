namespace VikingLib.SkillSystem
{
    /// <summary>
    /// Granular developer API for toggling and isolating native Valheim skill influences.
    /// Allows Type A mods (skill reward / perk overhauls) and Type C mods (custom progression / XP systems)
    /// to disable specific or all vanilla skill effects so the game behaves predictably regardless of skill levels.
    /// </summary>
    public static class VanillaSkillIntegration
    {
        /// <summary>
        /// Controls native weapon skill clumsiness scaling on damage rolls ([0.25..0.55] -> [0.85..1.00]).
        /// When false, weapon rolls default to fixed baseline roll bounds ([FixedDamageRollLowerBound .. FixedDamageRollUpperBound]).
        /// </summary>
        public static bool EnableDamageRollScaling { get; set; } = true;

        /// <summary>
        /// Lower bound multiplier for fixed weapon damage roll when native skill scaling is disabled (defaults to 0.25f).
        /// </summary>
        public static float FixedDamageRollLowerBound { get; set; } = 0.25f;

        /// <summary>
        /// Upper bound multiplier for fixed weapon damage roll when native skill scaling is disabled (defaults to 0.40f).
        /// </summary>
        public static float FixedDamageRollUpperBound { get; set; } = 0.40f;

        /// <summary>
        /// Lower bound multiplier for creature / non-player damage rolls (defaults to 0.75f).
        /// </summary>
        public static float CreatureDamageRollLowerBound { get; set; } = 0.75f;

        /// <summary>
        /// Upper bound multiplier for creature / non-player damage rolls (defaults to 1.00f).
        /// </summary>
        public static float CreatureDamageRollUpperBound { get; set; } = 1.00f;

        /// <summary>
        /// Controls native attack cost discounts from weapon skills (Stamina -33%, Eitr -33%, Health -33%).
        /// When false, attacks cost standard baseline values regardless of skill level.
        /// </summary>
        public static bool EnableAttackCostScaling { get; set; } = true;

        /// <summary>
        /// Controls native locomotion stamina discounts (Run up to -50%, Jump -33%, Dodge -50%, Sneak -75%, Swim -60%).
        /// When false, actions consume baseline stamina regardless of skill level.
        /// </summary>
        public static bool EnableLocomotionStaminaScaling { get; set; } = true;

        /// <summary>
        /// Controls native locomotion physical speed and jump force scaling (Run speed +25%, Jump height +40%).
        /// When false, speeds and jump height remain at baseline regardless of skill level.
        /// </summary>
        public static bool EnableLocomotionSpeedScaling { get; set; } = true;

        /// <summary>
        /// Controls native blocking skill bonus (+50% Block Power).
        /// When false, shields and weapons use base block power regardless of Blocking skill.
        /// </summary>
        public static bool EnableBlockPowerScaling { get; set; } = true;

        /// <summary>
        /// Controls native ranged action duration scaling (Bow draw time -80%, Crossbow reload time -80%).
        /// When false, draw and reload times remain at baseline regardless of skill level.
        /// </summary>
        public static bool EnableRangedDrawScaling { get; set; } = true;

        /// <summary>
        /// Controls native sneak stealth visibility and noise reduction.
        /// When false, stealth detection stays at baseline regardless of Sneak skill.
        /// </summary>
        public static bool EnableStealthScaling { get; set; } = true;

        /// <summary>
        /// Controls native build and craft tool scaling (hammer build stamina, placed piece durability).
        /// When false, building actions use baseline stamina and durability regardless of skill level.
        /// </summary>
        public static bool EnableBuildAndCraftScaling { get; set; } = true;

        /// <summary>
        /// Controls native and modded gathering/crafting drop yield scaling (Crafting, Cooking, Farming, Mining, Woodcutting).
        /// When false, gathering and crafting actions yield standard 1.0x baseline amounts regardless of skill level.
        /// </summary>
        public static bool EnableDropYieldScaling { get; set; } = true;

        /// <summary>
        /// Bulk helper to disable all combat-related skill scaling (damage rolls, attack costs, block power, ranged draw).
        /// </summary>
        public static void DisableCombatScaling()
        {
            EnableDamageRollScaling = false;
            EnableAttackCostScaling = false;
            EnableBlockPowerScaling = false;
            EnableRangedDrawScaling = false;
        }

        /// <summary>
        /// Bulk helper to disable all locomotion-related skill scaling (stamina discounts and speed/jump scaling).
        /// </summary>
        public static void DisableLocomotionScaling()
        {
            EnableLocomotionStaminaScaling = false;
            EnableLocomotionSpeedScaling = false;
            EnableStealthScaling = false;
        }

        /// <summary>
        /// Bulk helper to disable all native skill scaling across all combat, locomotion, defense, crafting, and drop systems.
        /// The game behaves 100% identically regardless of player skill levels (until custom perks/modifiers are registered).
        /// </summary>
        public static void DisableAll()
        {
            DisableCombatScaling();
            DisableLocomotionScaling();
            EnableBuildAndCraftScaling = false;
            EnableDropYieldScaling = false;
        }

        /// <summary>
        /// Resets all channels back to native vanilla defaults.
        /// </summary>
        public static void ResetToDefaults()
        {
            EnableDamageRollScaling = true;
            FixedDamageRollLowerBound = 0.25f;
            FixedDamageRollUpperBound = 0.40f;
            CreatureDamageRollLowerBound = 0.75f;
            CreatureDamageRollUpperBound = 1.00f;
            EnableAttackCostScaling = true;
            EnableLocomotionStaminaScaling = true;
            EnableLocomotionSpeedScaling = true;
            EnableBlockPowerScaling = true;
            EnableRangedDrawScaling = true;
            EnableStealthScaling = true;
            EnableBuildAndCraftScaling = true;
            EnableDropYieldScaling = true;
        }
    }
}
