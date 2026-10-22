namespace VikingLib.Stats
{
    /// <summary>
    /// Supported character statistics managed by VikingLib's Dual-Space 3-Tier Stat Engine.
    /// <para>
    /// <b>MATHEMATICAL NOTE FOR MOD DEVELOPERS:</b><br/>
    /// VikingLib employs three distinct mathematical evaluation models depending on stat category:
    /// </para>
    /// <list type="bullet">
    /// <item>
    /// <term><b>1. Standard 3-Tier Engine</b> (Vitals, Speeds, Precision, Multipliers, Yields):</term>
    /// <description><br/>
    /// <c>Evaluated = (Base + Σ Flat) × max(0, 1 + Σ Increased%) × Π Multipliers</c><br/>
    /// Used for linear, physical, and multiplicative properties.
    /// </description>
    /// </item>
    /// <item>
    /// <term><b>2. Asymptotic Efficiency Engine</b> (Action Resource Costs &amp; Drains):</term>
    /// <description><br/>
    /// <c>Effective Cost = Base Cost / ((1 + Σ Tier 2 Efficiency%) × Π Tier 3 Multipliers)</c><br/>
    /// In this engine, standard 3-tier modifiers are evaluated on the <b>efficiency denominator</b> (identical to speed scaling):<br/>
    /// In both Tier 2 and Tier 3, <b>higher values are always better</b>:<br/>
    /// <list type="bullet">
    /// <item><b>Tier 2 Additive %</b>: Additive efficiency bonuses (+50% efficiency = 1.5x efficiency factor).</item>
    /// <item><b>Tier 3 Multipliers</b>: Compounding efficiency multipliers (1.5x multiplier = +50% more efficiency, 2.0x = doubles efficiency).</item>
    /// </list>
    /// Costs scale smoothly and asymptotically towards zero without ever hitting 0 or flipping negative!
    /// </description>
    /// </item>
    /// <item>
    /// <term><b>3. Continuous Resistance Engine</b> (Damage Resistances &amp; Vulnerabilities):</term>
    /// <description><br/>
    /// <c>Damage Taken Factor = Base Factor (1.0x) × Π Tier 3 Multipliers</c><br/>
    /// Governed by <see cref="ResistanceStat"/> where only Tier 3 Compounding Multipliers are permitted.<br/>
    /// (e.g. 0.5x Poison Resistance mead × 0.8x perk = 0.40x damage taken).
    /// </description>
    /// </item>
    /// </list>
    /// </summary>
    public enum CharacterStatType
    {
        // --- Pools & Maximums (Standard 3-Tier, units: points/kg) ---
        MaxHealth,
        MaxStamina,
        MaxEitr,
        MaxCarryWeight,

        // --- Food Effectiveness (Standard 3-Tier, Tier 2 Additive % only, default 1.0x) ---
        FoodHealthEffectiveness,
        FoodHealthRegenEffectiveness,
        FoodStaminaEffectiveness,
        FoodEitrEffectiveness,

        // --- Regeneration Rates (Standard 3-Tier, units: points/sec) ---
        HealthRegen,
        StaminaRegen,
        EitrRegen,

        // --- Recovery Delays & Timers (Standard 3-Tier, units: seconds) ---
        HealthRegenDelay,
        StaminaRegenDelay,

        // --- Locomotion & Impulse Speeds (Standard 3-Tier) ---
        JumpHeight,                    // Jump apex height in METERS (base ~1.7m for player; kinetic transform v = v0 * sqrt(h / 1.7))
        JogSpeed,                      // Multiplier on jog speed (default 1.0x)
        RunSpeed,                      // Multiplier on sprint speed (default 1.0x)
        WalkSpeed,                     // Walk speed in meters/second (base 5.0 m/s)
        CrouchSpeed,                   // Crouch speed in meters/second (base 2.0 m/s)
        SwimSpeed,                     // Swim speed in meters/second (base 2.0 m/s)

        // --- Mount Mechanics (Decoupled, Standard 3-Tier / Clamped) ---
        RidingSpeed,                   // Multiplier on mount run speed (default 1.0x; uncapped for cavalry gear/perks)
        RidingStaminaEfficiency,       // Asymptotic efficiency rating on mount sprint/swim drain (default 0.0; drain = base / (1 + eff))
        RidingControl,                 // Turning obedience and acceleration factor (clamped 0.0 .. 1.0)

        // --- Defense & Guard (Standard 3-Tier) ---
        BodyArmor,
        BlockPowerMultiplier,
        ParryMultiplier,
        ParryWindow,
        StaggerResistance,             // Poise stability multiplier on stagger threshold (base 1.0x, Tier 2 & 3 only)

        // --- Continuous Damage Resistances (Tier 3 Multipliers ONLY, default 1.0x = 100% Damage Taken) ---
        ResistancePhysical,            // Umbrella resistance multiplying incoming Blunt, Slash, and Pierce
        ResistanceElemental,           // Umbrella resistance multiplying incoming Fire, Frost, and Lightning
        ResistanceSlashing,
        ResistanceBlunt,
        ResistancePiercing,
        ResistanceFire,
        ResistanceFrost,
        ResistanceLightning,
        ResistancePoison,
        ResistanceSpirit,
        ResistanceChop,
        ResistancePickaxe,
        ResistanceBleed,

        // --- Global Offensive Damage Types (Standard 3-Tier, default 1.0x) ---
        DamageSlashing,
        DamageBlunt,
        DamagePiercing,
        DamageChop,
        DamagePickaxe,
        DamageFire,
        DamageFrost,
        DamageLightning,
        DamagePoison,
        DamageSpirit,

        // --- Combat Delivery, Grip & Archetypes (Standard 3-Tier) ---
        DamagePhysical,
        DamageElemental,
        DamageMelee,
        DamageRanged,
        DamageMagic,                   // Universal magic damage umbrella across elemental and blood magic
        DamageOneHanded,               // Multiplier for 1H melee weapon attacks
        DamageTwoHanded,               // Multiplier for 2H melee weapon attacks

        // --- Attack Styles & Techniques (Standard 3-Tier) ---
        AttackSpeed,                   // Multiplier on weapon attack animation playback speed (base 1.0x, Tier 2 & 3 only)
        DamageSneak,
        CriticalStrikeChance,          // Multi-Crit supported: >1.0 rolls guaranteed crit tiers + fractional roll
        CriticalStrikeMultiplier,      // Critical damage multiplier per tier (default 1.5x)
        CleaveEfficiency,
        DamageComboFinisher,
        DamageSecondaryAttack,
        StaggerDamage,                 // Multiplier on poise damage dealt / HitData.m_staggerMultiplier (base 1.0x, Tier 2 & 3 only)

        // --- Situational & Target Context Modifiers (Standard 3-Tier, default 1.0x) ---
        DamageAgainstBosses,           // Dealt when target.IsBoss() == true
        DamageAgainstStaggered,        // Dealt when target.IsStaggering() == true
        DamageAgainstAirborne,         // Dealt when target.IsFlying() == true (Drakes, Gjall, Bats)
        DamageExecute,                 // Dealt when target health percentage <= 35%
        DamageFirstStrike,             // Dealt when target health percentage >= 90%
        DamageBerserk,                 // Dealt when attacker health percentage <= 35%
        DamageFlawless,                // Dealt when attacker health percentage >= 90%

        // --- Weapon Skill Damage Archetypes (Standard 3-Tier, baseline 1.0x, Tier 2 & 3 only) ---
        DamageSwords,
        DamageKnives,
        DamageClubs,
        DamagePolearms,
        DamageSpears,
        DamageAxes,
        DamageBows,
        DamageCrossbows,
        DamageUnarmed,                 // Applies to bare hands and fist weapons (e.g. Flesh Rippers / Fenris claws)
        DamageElementalMagic,
        DamageBloodMagic,

        // --- Weapon Skill Attack Speed Archetypes (Standard 3-Tier, baseline 1.0x, Tier 2 & 3 only) ---
        AttackSpeedSwords,
        AttackSpeedKnives,
        AttackSpeedClubs,
        AttackSpeedPolearms,
        AttackSpeedSpears,
        AttackSpeedAxes,
        AttackSpeedBows,
        AttackSpeedCrossbows,
        AttackSpeedUnarmed,
        AttackSpeedElementalMagic,
        AttackSpeedBloodMagic,

        // --- Action Resource Costs & Drains (ASYMPTOTIC EFFICIENCY ENGINE) ---
        // Formula: Effective Cost = Base Cost / ((1 + Σ Tier 2 Efficiency%) * Π Tier 3 Multipliers)
        SprintEfficiency,              // Efficiency rating for running stamina drain (base 0.0)
        JumpEfficiency,                // Efficiency rating for jumping stamina cost (base 0.0)
        DodgeEfficiency,               // Efficiency rating for dodge roll stamina cost (base 0.0)
        SneakEfficiency,               // Efficiency rating for sneak stamina drain (base 0.0)
        SwimEfficiency,                // Efficiency rating for swim stamina drain (base 0.0)
        AttackStaminaEfficiency,       // Efficiency rating for weapon attack stamina cost (base 0.0)
        AttackEitrEfficiency,          // Efficiency rating for magic attack Eitr cost (base 0.0)
        AttackHealthEfficiency,        // Efficiency rating for blood magic health cost (base 0.0)
        BowDrawStaminaEfficiency,      // Efficiency rating for holding bow draw stamina drain (base 0.0)
        BowDrawEitrEfficiency,         // Efficiency rating for holding staff draw Eitr drain (base 0.0)
        FishingStaminaEfficiency,      // Efficiency rating for fishing reel-in stamina drain (base 0.0)
        BlockStaminaEfficiency,        // Efficiency rating for blocking attack stamina cost (base 0.0)

        // --- Delivery & Grip Action Efficiencies (Asymptotic Denominator Scaling, base 0.0) ---
        MeleeStaminaEfficiency,
        RangedStaminaEfficiency,
        OneHandedStaminaEfficiency,
        TwoHandedStaminaEfficiency,
        SecondaryAttackStaminaEfficiency,

        // --- Per-Weapon Action Efficiencies (Asymptotic Denominator Scaling, base 0.0) ---
        SwordsEfficiency,
        KnivesEfficiency,
        ClubsEfficiency,
        PolearmsEfficiency,
        SpearsEfficiency,
        AxesEfficiency,
        BowsEfficiency,
        CrossbowReloadStaminaEfficiency, // Efficiency rating for crossbow reload stamina drain (base 0.0)
        UnarmedEfficiency,               // Applies to bare hands and fist weapons (e.g. Flesh Rippers / Fenris claws)
        ElementalMagicEitrEfficiency,    // Efficiency rating for elemental magic Eitr cost (base 0.0)
        BloodMagicHealthEfficiency,      // Efficiency rating for blood magic Health cost (base 0.0)
        BloodMagicEitrEfficiency,        // Efficiency rating for blood magic Eitr cost (base 0.0)

        // --- Action Timing, Precision & Stealth (Standard 3-Tier, Transformed) ---
        BowDrawSpeed,                  // Multiplier on bow draw speed (draw duration = base / speed)
        CrossbowReloadSpeed,           // Multiplier on crossbow reload speed (reload time = base / speed)
        ProjectilePrecision,           // Precision rating (spread angle = base spread / precision; higher is tighter)
        Concealment,                   // Stealth concealment rating (visibility target = base vis / concealment; higher is sneakier)

        // --- Gathering, Crafting & Loot Yields (Standard 3-Tier, 1.0x = 100% Base) ---
        // Multi-Drop Budget: Floor(Yield) guaranteed drops + (Random < Frac(Yield) ? 1 : 0) bonus drop
        CraftingYield,                 // Total yield on crafted items (base 1.0x; 1.20x = 1 guaranteed + 20% extra)
        CookingYield,                  // Total yield on cooked food (base 1.0x; 1.20x = 1 guaranteed + 20% extra)
        ForagingYield,                 // Total yield on harvested pickables/crops (base 1.0x; 1.20x = 1 guaranteed + 20% extra)
        MiningYield,                   // Total yield on mined mineral/rock chunks (base 1.0x)
        WoodcuttingYield,              // Total yield on felled wood/logs (base 1.0x)
        MonsterDropYield,              // Total yield on slain creature loot (base 1.0x)
        HarvestRadius,                 // Multiplier on scythe and farming piece interaction radius (base 1.0x)

        // --- On-Hit / Combat Trigger Resource Recovery (3x3 Matrix, Physical, Tier 1 Flat supported, base 0) ---
        HealthOnKill,                  // Flat HP restored to the attacker on a killing blow (base 0)
        StaminaOnKill,                 // Flat stamina restored to the attacker on a killing blow (base 0)
        EitrOnKill,                    // Flat Eitr restored to the attacker on a killing blow (base 0)
        HealthOnDamageTaken,           // Flat HP restored to the defender when taking damage (base 0)
        StaminaOnDamageTaken,          // Flat stamina restored to the defender when taking damage (base 0)
        EitrOnDamageTaken,             // Flat Eitr restored to the defender when taking damage (base 0)
        HealthOnCauseStagger,          // Flat HP restored to the attacker when a hit breaks target poise / staggers (base 0)
        StaminaOnCauseStagger,         // Flat stamina restored to the attacker when a hit breaks target poise / staggers (base 0)
        EitrOnCauseStagger,            // Flat Eitr restored to the attacker when a hit breaks target poise / staggers (base 0)

        // --- Home Tools, Survival & Combat Reach (Milestone 2) ---
        ToolStaminaEfficiency,         // Efficiency rating on Hammer/Hoe/Cultivator stamina costs (base 0.0; cost = base / (1 + eff))
        FoodDurationMultiplier,        // Multiplier on how long food lasts (base 1.0x; 1.5x = burns 33% slower)
        DurabilityEfficiency,          // Efficiency rating on equipment durability wear (base 0.0; wear = base / (1 + eff))
        KnockbackDealt,                // Multiplier on HitData.m_pushForce dealt (base 1.0x)
        MeleeReach,                    // Multiplier on melee Attack.m_attackRange (base 1.0x)
        ResistanceFall,                // Fall damage taken multiplier (Tier 3 ONLY, base 1.0x = 100% taken)

        // --- Mounts & Vehicles (Milestone 3) ---
        ShipSailSpeed,                 // Multiplier on ship wind sail force factor while at helm (base 1.0x)
        ShipRudderSpeed,               // Multiplier on ship paddle and reverse force while at helm (base 1.0x)
        CartWeightEfficiency,          // Efficiency rating reducing cargo inertia while pulling cart (base 0.0; factor = base / (1 + eff))

        // --- Contextual Combat Modifiers (Milestone 4, Standard 3-Tier, default 1.0x) ---
        DamageAtNight,                 // Dealt while EnvMan.IsNight() is true
        DamageWhileAirborne,           // Dealt while the attacker is off the ground (jumping/falling)
        DamageAgainstAfflicted,        // Dealt when the target suffers a harmful status effect (burning, poison, frost, tarred, wet-debuffs, staggered)

        // --- Armor Efficiency Suite (Milestone 5, Asymptotic Denominator / Piecewise Scaling, base 0.0) ---
        ArmorEfficiency,               // Umbrella physical armor efficiency scaling Blunt, Slash, Pierce
        BluntArmorEfficiency,          // Extra physical armor efficiency against Blunt damage
        SlashArmorEfficiency,          // Extra physical armor efficiency against Slash damage
        PierceArmorEfficiency,         // Extra physical armor efficiency against Pierce damage

        // --- Stagger & Physical Triad Overhaul ---
        SlashBleedPercent,             // Additive percentage bonus to slash bleed fraction (base 0.0, added to 0.25 base)
        ParryPoiseMultiplier,          // Multiplier on poise damage dealt by successful parries (base 1.0x)

        // --- Backward-Compatibility Aliases ---
        HomeToolsEfficiency = ToolStaminaEfficiency,
        DurabilityLossMultiplier = DurabilityEfficiency,
        ShipSpeed = ShipSailSpeed,
        CartWeightMultiplier = CartWeightEfficiency,
        HealthRestoreOnKill = HealthOnKill,
        StaminaRestoreOnKill = StaminaOnKill,
        EitrRestoreOnKill = EitrOnKill,
        StaminaRestoreOnStagger = StaminaOnCauseStagger,
        CrossbowsEfficiency = CrossbowReloadStaminaEfficiency,
        ElementalMagicEfficiency = ElementalMagicEitrEfficiency,
        BloodMagicEfficiency = BloodMagicHealthEfficiency,
        BleedResistance = ResistanceBleed,
        BleedDamagePercent = SlashBleedPercent,
        BleedDamageMultiplier = SlashBleedPercent
    }
}
