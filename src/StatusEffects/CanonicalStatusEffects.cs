using VikingLib.Combat;
using VikingLib.Stats;

namespace VikingLib.StatusEffects
{
    /// <summary>
    /// Pre-baked suite of canonical status effects ready for immediate use by VikingLib and downstream mods.
    /// Fully aligned with VikingLib's multi-slice architecture and 3-tier mathematical engine.
    /// </summary>
    public static class CanonicalStatusEffects
    {
        public const string BleedId = "SE_Bleed";
        public const string VulnerableId = "SE_Vulnerable";
        public const string CrippleId = "SE_Cripple";
        public const string EmpoweredStrikeId = "SE_EmpoweredStrike";
        public const string BerserkId = "SE_Berserk";
        public const string FortifiedId = "SE_Fortified";
        public const string WardId = "SE_Ward";

        public static VikingStatusEffect Bleed { get; private set; } = null!;
        public static VikingStatusEffect Vulnerable { get; private set; } = null!;
        public static VikingStatusEffect Cripple { get; private set; } = null!;
        public static VikingStatusEffect EmpoweredStrike { get; private set; } = null!;
        public static VikingStatusEffect Berserk { get; private set; } = null!;
        public static VikingStatusEffect Fortified { get; private set; } = null!;
        public static VikingStatusEffect Ward { get; private set; } = null!;

        private static bool _initialized;

        /// <summary>
        /// Registers all canonical status effects into the VikingLib status effect registry.
        /// </summary>
        public static void Init()
        {
            if (_initialized) return;
            _initialized = true;

            // 1. SE_Bleed: Multi-slice DoT. 10s duration per slice, ticks every 2s (5 ticks total),
            // dealing 1/5 slice damage as pure Slash, ignoring armor. Bloodless creatures immune.
            Bleed = StatusEffectBuilder.Create(BleedId)
                .WithName("Bleeding")
                .WithTooltip("Suffering arterial bleeding, losing health over time. Ineffective against bloodless creatures.")
                .WithIcon(StatusEffectIconFactory.GetBleedIcon())
                .WithDuration(10.0f)
                .WithSlicePeriodicDamage(DamageType.Slash, defaultDamagePerSlice: 50.0f, tickIntervalSeconds: 2.0f, ignoresArmor: true)
                .BuildAndRegister();

            // 2. SE_Vulnerable: Multi-slice debuff. Continuous-value slices scale incoming Physical damage taken only via 1 + sum(slices).
            Vulnerable = StatusEffectBuilder.Create(VulnerableId)
                .WithName("Vulnerable")
                .WithTooltip("Defenses compromised! Incoming physical damage increased.")
                .WithIcon(StatusEffectIconFactory.GetVulnerableIcon())
                .AsMultiSlice(SliceAggregationMode.Sum, maxSlices: 0, defaultSliceValue: 0.01f, defaultDuration: 10.0f)
                .WithSliceResistance(CharacterStats.ResistancePhysical, v => 1.0f + v)
                .WithSliceIconFormat(SliceIconFormat.PositivePercent)
                .BuildAndRegister();

            // 3. SE_Cripple: Multi-slice debuff. Scales locomotion speeds and jump height via asymptotic efficiency: 1 / (1 + sum(slices)).
            Cripple = StatusEffectBuilder.Create(CrippleId)
                .WithName("Crippled")
                .WithTooltip("Locomotion severely impaired and jump apex reduced.")
                .WithIcon(StatusEffectIconFactory.GetCrippleIcon())
                .AsMultiSlice(SliceAggregationMode.Sum, maxSlices: 0, defaultSliceValue: 0.25f, defaultDuration: 8.0f)
                .WithSliceMultiplier(CharacterStats.RunSpeed, c => 1.0f / (1.0f + c))
                .WithSliceMultiplier(CharacterStats.JogSpeed, c => 1.0f / (1.0f + c))
                .WithSliceMultiplier(CharacterStats.WalkSpeed, c => 1.0f / (1.0f + c))
                .WithSliceMultiplier(CharacterStats.JumpHeight, c => 1.0f / (1.0f + c))
                .WithSliceIconFormat(SliceIconFormat.NegativePercent)
                .BuildAndRegister();

            // 4. SE_EmpoweredStrike: Multi-slice next-hit offensive buff. Slices add Tier 2 DamagePhysical. Consumed all at once on MeleeHitDelivered.
            EmpoweredStrike = StatusEffectBuilder.Create(EmpoweredStrikeId)
                .WithName("Empowered Strike")
                .WithTooltip("The next melee attack lands with devastating momentum.")
                .WithIcon(StatusEffectIconFactory.GetEmpoweredStrikeIcon())
                .AsMultiSlice(SliceAggregationMode.Sum, maxSlices: 0, defaultSliceValue: 0.50f, defaultDuration: 15.0f)
                .WithSliceIncreased(CharacterStats.DamagePhysical, scale: 1.0f)
                .WithSliceConsumeAllOnTrigger(ChargeConsumeTrigger.MeleeHitDelivered)
                .WithSliceIconFormat(SliceIconFormat.PositivePercent)
                .BuildAndRegister();

            // 5. SE_Berserk: Single-instance buff (10s TTL, RefreshDurationOnly).
            // +25% melee damage, +10% attack speed, but -30% armor efficiency.
            Berserk = StatusEffectBuilder.Create(BerserkId)
                .WithName("Berserk Rage")
                .WithTooltip("Unbridled bloodlust: +25% melee damage and +10% attack speed, but armor efficiency reduced by 30%.")
                .WithIcon(StatusEffectIconFactory.GetBerserkIcon())
                .WithDuration(10.0f)
                .WithMaxStacks(1, StackPolicy.RefreshDurationOnly)
                .WithIncreasedModifier(CharacterStats.DamageMelee, percentPerStack: 0.25f)
                .WithIncreasedModifier(CharacterStats.AttackSpeed, percentPerStack: 0.10f)
                .WithIncreasedModifier(CharacterStats.ArmorEfficiency, percentPerStack: -0.30f)
                .BuildAndRegister();

            // 6. SE_Fortified: Multi-slice next-hit defensive analogue. Slices add Tier 2 ArmorEfficiency (60s default TTL).
            // Consumed all at once on DamageTaken in Phase 2.
            Fortified = StatusEffectBuilder.Create(FortifiedId)
                .WithName("Fortified")
                .WithTooltip("Defensive bulwark: increased armor efficiency on the next incoming hit.")
                .WithIcon(StatusEffectIconFactory.GetFortifiedIcon())
                .AsMultiSlice(SliceAggregationMode.Sum, maxSlices: 0, defaultSliceValue: 0.30f, defaultDuration: 60.0f)
                .WithSliceIncreased(CharacterStats.ArmorEfficiency, scale: 1.0f)
                .WithSliceConsumeAllOnTrigger(ChargeConsumeTrigger.DamageTaken)
                .WithSliceIconFormat(SliceIconFormat.PositivePercent)
                .BuildAndRegister();

            // 7. SE_Ward: Multi-slice damage absorption barrier. Incoming damage depletes the slice with shortest remaining duration first.
            Ward = StatusEffectBuilder.Create(WardId)
                .WithName("Aegis Ward")
                .WithTooltip("Protective barrier absorbing incoming damage before health loss.")
                .WithIcon(StatusEffectIconFactory.GetWardIcon())
                .WithSliceShield(defaultCapacityPerSlice: 100.0f, defaultDuration: 30.0f)
                .BuildAndRegister();
        }
    }
}
