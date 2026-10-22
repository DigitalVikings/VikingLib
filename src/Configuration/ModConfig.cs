using BepInEx.Configuration;
using UnityEngine;
using VikingLib.Combat.Telemetry;

namespace VikingLib.Configuration
{
    public static class ModConfig
    {
        public static ConfigEntry<bool> ModEnabled { get; private set; } = null!;
        public static ConfigEntry<bool> CustomTooltipEnabled { get; private set; } = null!;
        public static ConfigEntry<KeyCode> TooltipExpandKey { get; private set; } = null!;
        public static ConfigEntry<TooltipExpandBehavior> TooltipExpandBehavior { get; private set; } = null!;
        public static bool IsTooltipToggleActive { get; set; } = false;
        public static ConfigEntry<DamageLoggingMode> LoggingMode { get; private set; } = null!;
        public static ConfigEntry<DamageLogFormat> LogFormat { get; private set; } = null!;
        public static ConfigEntry<bool> ShowCombatChatFeedback { get; private set; } = null!;
        public static ConfigEntry<float> DefaultPlayerCritChance { get; private set; } = null!;
        public static ConfigEntry<float> DefaultCritMultiplier { get; private set; } = null!;
        public static ConfigEntry<CleaveScalingMode> CleaveMode { get; private set; } = null!;
        public static ConfigEntry<bool> TerrainReducesCleaveDamage { get; private set; } = null!;
        public static ConfigEntry<ArmorMitigationModel> ArmorModel { get; private set; } = null!;
        public static ConfigEntry<float> ArmorScaleConstant { get; private set; } = null!;
        public static ConfigEntry<bool> EnableCharacterStats { get; private set; } = null!;
        public static ConfigEntry<float> HealthRegenCombatDelay { get; private set; } = null!;
        public static ConfigEntry<bool> ShowStatsInGlossary { get; private set; } = null!;
        public static ConfigEntry<float> GlossaryRefreshInterval { get; private set; } = null!;
        public static ConfigEntry<bool> StaggerOverhaulEnabled { get; private set; } = null!;
        public static ConfigEntry<StaggerBarDisplayMode> EnemyStaggerBarMode { get; private set; } = null!;
        public static ConfigEntry<float> PiercePrecisionMultiplier { get; private set; } = null!;
        public static ConfigEntry<float> SlashBleedPercent { get; private set; } = null!;
        public static ConfigEntry<float> StaggerLingerSeconds { get; private set; } = null!;
        public static ConfigEntry<float> StaggerDecayRate { get; private set; } = null!;
        public static ConfigEntry<float> StaggerDropoffSeconds { get; private set; } = null!;
        public static ConfigEntry<float> StaggerThresholdMultiplier { get; private set; } = null!;
        public static ConfigEntry<StaggerBarDisplayMode> EnemyStatusEffectsMode { get; private set; } = null!;
        public static ConfigEntry<bool> EnemyStatusEffectsShowText { get; private set; } = null!;

        public static void Init(ConfigFile config)
        {
            ModEnabled = Bind(
                config,
                "1 - General",
                "Enabled",
                true,
                "Master switch to enable or disable all features of VikingLib.",
                order: 100,
                isAdminOnly: true
            );

            CustomTooltipEnabled = Bind(
                config,
                "1 - General",
                "CustomTooltipEnabled",
                true,
                "If true, item tooltips display practical damage ranges calculated through VikingLib's modifier pipeline instead of vanilla's format.",
                order: 95,
                isAdminOnly: false
            );

            TooltipExpandKey = Bind(
                config,
                "1 - General",
                "TooltipExpandKey",
                KeyCode.LeftShift,
                "Key to hold to expand the attack and combo scaling breakdown table on weapon tooltips. Default is LeftShift.",
                order: 93,
                isAdminOnly: false
            );

            TooltipExpandBehavior = Bind(
                config,
                "1 - General",
                "TooltipExpandBehavior",
                Configuration.TooltipExpandBehavior.Hold,
                "Behavior of the TooltipExpandKey: 'Hold' displays the extended combat matrix while key is pressed down; 'Toggle' flips the view between standard and extended with each key press.",
                order: 92,
                isAdminOnly: false
            );

            LoggingMode = Bind(
                config,
                "2 - Telemetry",
                "DamageLoggingMode",
                DamageLoggingMode.PlayerOnly,
                "Combat inspection logging mode. PlayerOnly logs all hits involving the local player to the BepInEx console. AllCombat logs all hits. Off disables logging with zero overhead.",
                order: 90,
                isAdminOnly: false
            );

            LogFormat = Bind(
                config,
                "2 - Telemetry",
                "DamageLogFormat",
                DamageLogFormat.Tree,
                "Visual style for combat inspection telemetry. Tree = hierarchical breakdown with indented tier sub-nodes; Compact = dense one-line tier summaries; Audit = itemized receipt card.",
                order: 88,
                isAdminOnly: false
            );

            ShowCombatChatFeedback = Bind(
                config,
                "2 - Telemetry",
                "ShowCombatChatFeedback",
                false,
                "If true, shows a concise one-line damage summary in the top-left HUD message when the player deals damage.",
                order: 85,
                isAdminOnly: false
            );

            DefaultPlayerCritChance = Bind(
                config,
                "3 - Combat Defaults",
                "DefaultPlayerCritChance",
                0.0f,
                "Base critical strike chance for players (0.0 = 0%). Defaults to 0% to match vanilla. Can be raised if random crits are desired.",
                order: 80,
                acceptableValues: new AcceptableValueRange<float>(0f, 1f),
                isAdminOnly: true
            );

            DefaultCritMultiplier = Bind(
                config,
                "3 - Combat Defaults",
                "DefaultCritMultiplier",
                2.0f,
                "Base damage multiplier applied on a critical strike (e.g. 2.0 = 200% damage). Used for random crits, stagger punishes, and sneak attacks.",
                order: 75,
                acceptableValues: new AcceptableValueRange<float>(1.0f, 5.0f),
                isAdminOnly: true
            );

            CleaveMode = Bind(
                config,
                "3 - Combat Defaults",
                "CleaveScalingMode",
                CleaveScalingMode.Continuous,
                "Multi-target damage scaling formula. Continuous = 1 / (Cleave + (1 - Cleave) * N). Geometric = Target 0 takes 100%, Target 1 takes Cleave, Target 2 takes Cleave^2 in blade sweep order. Vanilla = legacy 4 / (3 * N).",
                order: 70,
                isAdminOnly: true
            );

            TerrainReducesCleaveDamage = Bind(
                config,
                "3 - Combat Defaults",
                "TerrainReducesCleaveDamage",
                true,
                "If true, melee attacks that collide with terrain/ground count the terrain as a hit target in multi-target scaling, reducing damage to subsequent targets in the swing (vanilla behavior). If false, terrain collisions still occur and log telemetry, but do not reduce damage to enemies or objects.",
                order: 68,
                isAdminOnly: true
            );

            ArmorModel = Bind(
                config,
                "3 - Combat Defaults",
                "ArmorMitigationModel",
                ArmorMitigationModel.LinearEHP,
                "Physical armor mitigation model. LinearEHP: Asymptotic linear effective health scaling (Damage = Incoming / (1 + Armor / K + Eff)). VanillaPiecewise: Native piecewise threshold scaling (Damage = Incoming^2 / (4*Armor) or Incoming - Armor).",
                order: 67,
                isAdminOnly: true
            );

            ArmorScaleConstant = Bind(
                config,
                "3 - Combat Defaults",
                "ArmorScaleConstant",
                60.0f,
                "Armor scale constant K used in LinearEHP mitigation mode (BaseEff = EffectiveArmor / K). Default 60 means 60 armor grants +100% effective HP against physical damage (50% damage reduction).",
                order: 66,
                acceptableValues: new AcceptableValueRange<float>(1.0f, 500.0f),
                isAdminOnly: true
            );

            EnableCharacterStats = Bind(
                config,
                "4 - Character Stats",
                "EnableCharacterStats",
                true,
                "Master switch for VikingLib's 3-Tier Character Stat Engine. When false, all character stats revert to 100% native vanilla mechanics.",
                order: 65,
                isAdminOnly: true
            );

            HealthRegenCombatDelay = Bind(
                config,
                "4 - Character Stats",
                "HealthRegenCombatDelay",
                15.0f,
                "Cooldown delay in seconds after taking health damage during which continuous health regeneration is completely paused. Blocks/parries that reduce damage to 0 do not trigger this cooldown.",
                order: 64,
                acceptableValues: new AcceptableValueRange<float>(0.0f, 60.0f),
                isAdminOnly: true
            );

            ShowStatsInGlossary = Bind(
                config,
                "4 - Character Stats",
                "ShowStatsInGlossary",
                true,
                "If true, adds a 'Character Stats' entry into Hugin's Compendium (TextsDialog), showing real-time character statistics and itemized 3-tier modifier breakdowns.",
                order: 63,
                isAdminOnly: false
            );

            GlossaryRefreshInterval = Bind(
                config,
                "4 - Character Stats",
                "GlossaryRefreshInterval",
                1.5f,
                "Refresh interval in seconds for the live Character Stats page in Hugin's Compendium.",
                order: 62,
                acceptableValues: new AcceptableValueRange<float>(0.2f, 10.0f),
                isAdminOnly: false
            );

            StaggerOverhaulEnabled = Bind(
                config,
                "5 - Stagger & Physical Triad",
                "StaggerOverhaulEnabled",
                true,
                "Master switch for the decoupled Stagger & Physical Damage Triad Overhaul. Blunt deals poise damage, Slash inflicts post-mitigation bleed, Pierce gains +18% damage, and stagger is pure crowd control (1.0x damage).",
                order: 60,
                isAdminOnly: true
            );

            EnemyStaggerBarMode = Bind(
                config,
                "5 - Stagger & Physical Triad",
                "EnemyStaggerBarMode",
                StaggerBarDisplayMode.AllEnemies,
                "Controls the display of the amber poise bar under enemy and boss health bars in EnemyHud.",
                order: 58,
                isAdminOnly: false
            );

            EnemyStatusEffectsMode = Bind(
                config,
                "4 - UI & Tooltips",
                "EnemyStatusEffectsMode",
                StaggerBarDisplayMode.AllEnemies,
                "Controls the display of active status effect chips (Bleed, Vulnerable, Crippled, Burning, etc.) under enemy and boss health bars in EnemyHud.",
                order: 60,
                isAdminOnly: false
            );

            EnemyStatusEffectsShowText = Bind(
                config,
                "4 - UI & Tooltips",
                "EnemyStatusEffectsShowText",
                true,
                "If true, displays values and timers on enemy status effect chips (e.g. remaining bleed damage, vulnerable percent, burning duration).",
                order: 59,
                isAdminOnly: false
            );

            PiercePrecisionMultiplier = Bind(
                config,
                "5 - Stagger & Physical Triad",
                "PiercePrecisionMultiplier",
                1.18f,
                "Innate Tier 3 damage multiplier applied across all piercing damage hits (1.18 = +18%).",
                order: 56,
                acceptableValues: new AcceptableValueRange<float>(1.0f, 3.0f),
                isAdminOnly: true
            );

            SlashBleedPercent = Bind(
                config,
                "5 - Stagger & Physical Triad",
                "SlashBleedPercent",
                0.25f,
                "Base percentage of post-mitigation slash damage applied as a 10-second bleeding slice (0.25 = 25%).",
                order: 54,
                acceptableValues: new AcceptableValueRange<float>(0.0f, 2.0f),
                isAdminOnly: true
            );

            StaggerLingerSeconds = Bind(
                config,
                "5 - Stagger & Physical Triad",
                "StaggerLingerSeconds",
                3.0f,
                "Duration in seconds before accumulated poise begins decaying after the last blunt hit.",
                order: 52,
                acceptableValues: new AcceptableValueRange<float>(0.5f, 10.0f),
                isAdminOnly: true
            );

            StaggerDecayRate = Bind(
                config,
                "5 - Stagger & Physical Triad",
                "StaggerDecayRate",
                0.15f,
                "Decay rate per second for accumulated poise after the linger period (0.15 = 15%/s).",
                order: 50,
                acceptableValues: new AcceptableValueRange<float>(0.01f, 1.0f),
                isAdminOnly: true
            );

            StaggerDropoffSeconds = Bind(
                config,
                "5 - Stagger & Physical Triad",
                "StaggerDropoffSeconds",
                30.0f,
                "Maximum time in seconds without taking blunt damage before all accumulated poise drops to zero completely.",
                order: 48,
                acceptableValues: new AcceptableValueRange<float>(5.0f, 120.0f),
                isAdminOnly: true
            );

            StaggerThresholdMultiplier = Bind(
                config,
                "5 - Stagger & Physical Triad",
                "StaggerThresholdMultiplier",
                1.0f,
                "Global scaling factor applied to all creature poise thresholds (0.5 = 50% threshold / easier stagger; 2.0 = 200% threshold / harder stagger).",
                order: 46,
                acceptableValues: new AcceptableValueRange<float>(0.1f, 5.0f),
                isAdminOnly: true
            );
        }

        private static ConfigEntry<T> Bind<T>(
            ConfigFile config,
            string section,
            string key,
            T defaultValue,
            string description,
            int order = 0,
            AcceptableValueBase? acceptableValues = null,
            bool isAdminOnly = false)
        {
            var tags = new ConfigurationManagerAttributes
            {
                Order = order,
                Category = section,
                IsAdminOnly = isAdminOnly
            };
            var configDesc = new ConfigDescription(description, acceptableValues, tags);
            return config.Bind(section, key, defaultValue, configDesc);
        }
    }

    /// <summary>
    /// Governs how multi-target damage falloff is computed.
    /// </summary>
    public enum CleaveScalingMode
    {
        /// <summary>
        /// Continuous smooth scaling: DamagePerTarget(N) = 1 / (Cleave + (1 - Cleave) * N).
        /// </summary>
        Continuous,

        /// <summary>
        /// Geometric progression in blade sweep order: Target 0 takes 100%, Target 1 takes Cleave, Target 2 takes Cleave^2, etc.
        /// </summary>
        Geometric,

        /// <summary>
        /// Vanilla fallback: If Cleave &lt; 1.0f, applies vanilla 4 / (3 * N); if Cleave &gt;= 1.0f, applies 1.0x.
        /// </summary>
        Vanilla
    }

    /// <summary>
    /// Controls how the tooltip expand key triggers the combat matrix.
    /// </summary>
    public enum TooltipExpandBehavior
    {
        /// <summary>
        /// Extended combat matrix is displayed while the key is held down.
        /// </summary>
        Hold,

        /// <summary>
        /// Pressing the key toggles between standard view and the extended combat matrix.
        /// </summary>
        Toggle
    }

    /// <summary>
    /// Governs the mathematical armor mitigation model used in Phase 3 (Mitigation).
    /// </summary>
    public enum ArmorMitigationModel
    {
        /// <summary>
        /// Asymptotic linear effective health scaling: BaseEff = EffectiveArmor / ArmorScaleConstant.
        /// Mitigated = Incoming / (1 + BaseEff + ArmorEfficiency + TypeEfficiency).
        /// </summary>
        LinearEHP,

        /// <summary>
        /// Valheim piecewise threshold scaling: ScaledArmor = EffectiveArmor * (1 + ArmorEfficiency + TypeEfficiency).
        /// If Incoming &lt; 2 * ScaledArmor: Incoming^2 / (4 * ScaledArmor). Else: Incoming - ScaledArmor.
        /// </summary>
        VanillaPiecewise
    }

    /// <summary>
    /// Governs the display of the amber stagger/poise bar in EnemyHud.
    /// </summary>
    public enum StaggerBarDisplayMode
    {
        /// <summary>
        /// Amber poise meter is rendered under all enemy and boss health bars.
        /// </summary>
        AllEnemies,

        /// <summary>
        /// Amber poise meter is rendered exclusively under boss health bars.
        /// </summary>
        BossesOnly,

        /// <summary>
        /// Stagger bar rendering is completely disabled.
        /// </summary>
        Off
    }
}
