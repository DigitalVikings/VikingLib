namespace VikingLib.Stats
{
    /// <summary>
    /// Master registry of strongly-typed character statistic descriptors.
    /// <para>
    /// Provides compile-time safe access to all stats supported by VikingLib's Dual-Space Stat Engine.
    /// Physical stats support <c>.AddFlat()</c>, <c>.AddIncreased()</c>, and <c>.AddMultiplier()</c>.
    /// Multiplier and Efficiency stats prevent <c>.AddFlat()</c> at compile time.
    /// Resistance stats permit ONLY <c>.AddMultiplier()</c> at compile time.
    /// </para>
    /// </summary>
    public static class CharacterStats
    {
        // =========================================================================
        // 1. PHYSICAL STATS (Support Tier 1 Flat, Tier 2 Additive %, Tier 3 Multipliers)
        // =========================================================================

        /// <summary>Maximum health pool in HP. Biological baseline is 25 HP for players.</summary>
        public static readonly PhysicalStat MaxHealth =
            new(CharacterStatType.MaxHealth, "Max Health", "HP", "Maximum health pool points.");

        /// <summary>Maximum stamina pool in points. Biological baseline is 50 stamina for players.</summary>
        public static readonly PhysicalStat MaxStamina =
            new(CharacterStatType.MaxStamina, "Max Stamina", "pts", "Maximum stamina pool points.");

        /// <summary>Maximum Eitr pool in points. Biological baseline is 0 Eitr for players.</summary>
        public static readonly PhysicalStat MaxEitr =
            new(CharacterStatType.MaxEitr, "Max Eitr", "pts", "Maximum magic Eitr pool points.");

        /// <summary>Maximum carry weight capacity before encumbrance in kilograms (base 300 kg).</summary>
        public static readonly PhysicalStat MaxCarryWeight =
            new(CharacterStatType.MaxCarryWeight, "Max Carry Weight", "kg", "Maximum carry weight in kilograms.");

        /// <summary>Continuous health regeneration rate in HP per second (base 0 HP/s biological).</summary>
        public static readonly PhysicalStat HealthRegen =
            new(CharacterStatType.HealthRegen, "Health Regeneration", "HP/s", "Health regeneration in points per second.");

        /// <summary>Stamina regeneration rate in points per second (base 5.0 pts/s).</summary>
        public static readonly PhysicalStat StaminaRegen =
            new(CharacterStatType.StaminaRegen, "Stamina Regeneration", "pts/s", "Stamina regeneration in points per second.");

        /// <summary>Eitr regeneration rate in points per second (base 5.0 pts/s).</summary>
        public static readonly PhysicalStat EitrRegen =
            new(CharacterStatType.EitrRegen, "Eitr Regeneration", "pts/s", "Eitr regeneration in points per second.");

        /// <summary>Cooldown delay before continuous health begins regenerating after taking damage (default 15.0 sec).</summary>
        public static readonly PhysicalStat HealthRegenDelay =
            new(CharacterStatType.HealthRegenDelay, "Health Regen Delay", "sec", "Delay before continuous health begins regenerating after taking damage.");

        /// <summary>Delay before stamina begins regenerating after action expenditure (base 1.0 sec).</summary>
        public static readonly PhysicalStat StaminaRegenDelay =
            new(CharacterStatType.StaminaRegenDelay, "Stamina Regen Delay", "sec", "Delay before stamina begins regenerating.");

        /// <summary>Jump apex height in meters (base ~1.7m for players; kinetic relation v = v0 * sqrt(h / 1.7)).</summary>
        public static readonly PhysicalStat JumpHeight =
            new(CharacterStatType.JumpHeight, "Jump Apex Height", "m", "Jump apex height in meters.");

        /// <summary>Base walking locomotion velocity in meters per second (base 5.0 m/s).</summary>
        public static readonly PhysicalStat WalkSpeed =
            new(CharacterStatType.WalkSpeed, "Walk Speed", "m/s", "Walking locomotion speed in meters per second.");

        /// <summary>Crouch stealth movement velocity in meters per second (base 2.0 m/s).</summary>
        public static readonly PhysicalStat CrouchSpeed =
            new(CharacterStatType.CrouchSpeed, "Crouch Speed", "m/s", "Crouch movement speed in meters per second.");

        /// <summary>Swimming locomotion velocity in meters per second (base 2.0 m/s).</summary>
        public static readonly PhysicalStat SwimSpeed =
            new(CharacterStatType.SwimSpeed, "Swim Speed", "m/s", "Swimming locomotion speed in meters per second.");

        /// <summary>Total equipped body armor points before piecewise physical mitigation.</summary>
        public static readonly PhysicalStat BodyArmor =
            new(CharacterStatType.BodyArmor, "Body Armor", "pts", "Total body armor points.");

        // --- 3x3 Cartesian Resource Recovery Matrix (On Kill, On Damage Taken, On Cause Stagger) ---
        /// <summary>Flat HP restored to the attacker on a killing blow (base 0 HP).</summary>
        public static readonly PhysicalStat HealthOnKill =
            new(CharacterStatType.HealthOnKill, "Health On Kill", "HP", "Flat health restored on a killing blow.");

        /// <summary>Flat stamina restored to the attacker on a killing blow (base 0 pts).</summary>
        public static readonly PhysicalStat StaminaOnKill =
            new(CharacterStatType.StaminaOnKill, "Stamina On Kill", "pts", "Flat stamina restored on a killing blow.");

        /// <summary>Flat Eitr restored to the attacker on a killing blow (base 0 pts).</summary>
        public static readonly PhysicalStat EitrOnKill =
            new(CharacterStatType.EitrOnKill, "Eitr On Kill", "pts", "Flat Eitr restored on a killing blow.");

        /// <summary>Flat HP restored to the defender upon taking damage (base 0 HP).</summary>
        public static readonly PhysicalStat HealthOnDamageTaken =
            new(CharacterStatType.HealthOnDamageTaken, "Health On Damage Taken", "HP", "Flat health restored upon taking damage.");

        /// <summary>Flat stamina restored to the defender upon taking damage (base 0 pts).</summary>
        public static readonly PhysicalStat StaminaOnDamageTaken =
            new(CharacterStatType.StaminaOnDamageTaken, "Stamina On Damage Taken", "pts", "Flat stamina restored upon taking damage.");

        /// <summary>Flat Eitr restored to the defender upon taking damage (base 0 pts).</summary>
        public static readonly PhysicalStat EitrOnDamageTaken =
            new(CharacterStatType.EitrOnDamageTaken, "Eitr On Damage Taken", "pts", "Flat Eitr restored upon taking damage.");

        /// <summary>Flat HP restored to the attacker when causing target poise break / stagger (base 0 HP).</summary>
        public static readonly PhysicalStat HealthOnCauseStagger =
            new(CharacterStatType.HealthOnCauseStagger, "Health On Cause Stagger", "HP", "Flat health restored when breaking enemy poise.");

        /// <summary>Flat stamina restored to the attacker when causing target poise break / stagger (base 0 pts).</summary>
        public static readonly PhysicalStat StaminaOnCauseStagger =
            new(CharacterStatType.StaminaOnCauseStagger, "Stamina On Cause Stagger", "pts", "Flat stamina restored when breaking enemy poise.");

        /// <summary>Flat Eitr restored to the attacker when causing target poise break / stagger (base 0 pts).</summary>
        public static readonly PhysicalStat EitrOnCauseStagger =
            new(CharacterStatType.EitrOnCauseStagger, "Eitr On Cause Stagger", "pts", "Flat Eitr restored when breaking enemy poise.");

        // Compatibility Aliases
        public static readonly PhysicalStat HealthRestoreOnKill = HealthOnKill;
        public static readonly PhysicalStat StaminaRestoreOnKill = StaminaOnKill;
        public static readonly PhysicalStat EitrRestoreOnKill = EitrOnKill;
        public static readonly PhysicalStat StaminaRestoreOnStagger = StaminaOnCauseStagger;

        /// <summary>Duration of the timed parry reaction window in seconds (base 0.25 sec).</summary>
        public static readonly PhysicalStat ParryWindow =
            new(CharacterStatType.ParryWindow, "Parry Window", "sec", "Timed parry window duration in seconds.");

        /// <summary>Base critical strike chance rating (0.0 .. 1.0). Over-crit (>1.0) cascades into multi-crit tiers.</summary>
        public static readonly PhysicalStat CriticalStrikeChance =
            new(CharacterStatType.CriticalStrikeChance, "Critical Strike Chance", "rating", "Critical strike chance rating.");

        // =========================================================================
        // 2. RESISTANCE STATS (Baseline 1.0x; Support Tier 3 Compounding Multipliers ONLY)
        // =========================================================================

        public static readonly ResistanceStat ResistancePhysical =
            new(CharacterStatType.ResistancePhysical, "Physical Damage Taken", "Umbrella resistance multiplying incoming Blunt, Slash, and Pierce.");

        public static readonly ResistanceStat ResistanceElemental =
            new(CharacterStatType.ResistanceElemental, "Elemental Damage Taken", "Umbrella resistance multiplying incoming Fire, Frost, and Lightning.");

        public static readonly ResistanceStat ResistanceSlashing =
            new(CharacterStatType.ResistanceSlashing, "Slashing Damage Taken", "Multiplier on incoming slashing damage.");

        public static readonly ResistanceStat ResistanceBlunt =
            new(CharacterStatType.ResistanceBlunt, "Blunt Damage Taken", "Multiplier on incoming blunt damage.");

        public static readonly ResistanceStat ResistancePiercing =
            new(CharacterStatType.ResistancePiercing, "Piercing Damage Taken", "Multiplier on incoming piercing damage.");

        public static readonly ResistanceStat ResistanceFire =
            new(CharacterStatType.ResistanceFire, "Fire Damage Taken", "Multiplier on incoming fire damage.");

        public static readonly ResistanceStat ResistanceFrost =
            new(CharacterStatType.ResistanceFrost, "Frost Damage Taken", "Multiplier on incoming frost damage.");

        public static readonly ResistanceStat ResistanceLightning =
            new(CharacterStatType.ResistanceLightning, "Lightning Damage Taken", "Multiplier on incoming lightning damage.");

        public static readonly ResistanceStat ResistancePoison =
            new(CharacterStatType.ResistancePoison, "Poison Damage Taken", "Multiplier on incoming poison damage.");

        public static readonly ResistanceStat ResistanceSpirit =
            new(CharacterStatType.ResistanceSpirit, "Spirit Damage Taken", "Multiplier on incoming spirit damage.");

        public static readonly ResistanceStat ResistanceChop =
            new(CharacterStatType.ResistanceChop, "Chop Damage Taken", "Multiplier on incoming chop damage.");

        public static readonly ResistanceStat ResistancePickaxe =
            new(CharacterStatType.ResistancePickaxe, "Pickaxe Damage Taken", "Multiplier on incoming pickaxe damage.");

        /// <summary>Multiplier on fall damage taken (Tier 3 only; 0.5x halves fall damage).</summary>
        public static readonly ResistanceStat ResistanceFall =
            new(CharacterStatType.ResistanceFall, "Fall Damage Taken", "Multiplier on incoming fall damage.");

        /// <summary>Multiplier on incoming Bleed damage taken and bleed application eligibility (base 1.0x). Set to 0 to grant immunity.</summary>
        public static readonly ResistanceStat ResistanceBleed =
            new(CharacterStatType.ResistanceBleed, "Bleed Resistance", "Multiplier on incoming Bleed damage taken and bleed application eligibility.");

        public static readonly ResistanceStat BleedResistance = ResistanceBleed;

        // =========================================================================
        // 3. MULTIPLIER STATS (Baseline 1.0x; Support Tier 2 Additive % and Tier 3 Multipliers ONLY)
        // =========================================================================

        // --- Food Effectiveness ---
        public static readonly MultiplierStat FoodHealthEffectiveness =
            new(CharacterStatType.FoodHealthEffectiveness, "Food Health Effectiveness", "Scales flat HP contributed by food.");

        public static readonly MultiplierStat FoodHealthRegenEffectiveness =
            new(CharacterStatType.FoodHealthRegenEffectiveness, "Food Health Regen Effectiveness", "Scales flat HP/s contributed by food.");

        public static readonly MultiplierStat FoodStaminaEffectiveness =
            new(CharacterStatType.FoodStaminaEffectiveness, "Food Stamina Effectiveness", "Scales flat stamina contributed by food.");

        public static readonly MultiplierStat FoodEitrEffectiveness =
            new(CharacterStatType.FoodEitrEffectiveness, "Food Eitr Effectiveness", "Scales flat Eitr contributed by food.");

        // --- Locomotion & Mount Multipliers ---
        public static readonly MultiplierStat JogSpeed =
            new(CharacterStatType.JogSpeed, "Jog Speed", "Multiplier on jog velocity (default 1.0x).");

        public static readonly MultiplierStat RunSpeed =
            new(CharacterStatType.RunSpeed, "Sprint Speed", "Multiplier on sprint velocity (default 1.0x).");

        public static readonly MultiplierStat RidingSpeed =
            new(CharacterStatType.RidingSpeed, "Riding Speed", "Multiplier on mount run speed (default 1.0x).");

        public static readonly MultiplierStat RidingControl =
            new(CharacterStatType.RidingControl, "Riding Control", "Turning obedience and steering acceleration factor (0.0 .. 1.0).");

        // --- Defense & Guard ---
        public static readonly MultiplierStat BlockPowerMultiplier =
            new(CharacterStatType.BlockPowerMultiplier, "Block Power Multiplier", "Multiplier on shield/weapon block armor.");

        public static readonly MultiplierStat ParryMultiplier =
            new(CharacterStatType.ParryMultiplier, "Parry Multiplier", "Multiplier on timed parry deflection bonus.");

        public static readonly MultiplierStat StaggerResistance =
            new(CharacterStatType.StaggerResistance, "Stagger Resistance", "Poise multiplier on stagger threshold.");

        // --- Global Offensive Damage Types ---
        public static readonly MultiplierStat DamageSlashing =
            new(CharacterStatType.DamageSlashing, "Slashing Damage", "Multiplier on slashing damage.");

        public static readonly MultiplierStat DamageBlunt =
            new(CharacterStatType.DamageBlunt, "Blunt Damage", "Multiplier on blunt damage.");

        public static readonly MultiplierStat DamagePiercing =
            new(CharacterStatType.DamagePiercing, "Piercing Damage", "Multiplier on piercing damage.");

        public static readonly MultiplierStat DamageChop =
            new(CharacterStatType.DamageChop, "Chop Damage", "Multiplier on tree chop damage.");

        public static readonly MultiplierStat DamagePickaxe =
            new(CharacterStatType.DamagePickaxe, "Pickaxe Damage", "Multiplier on mineral pickaxe damage.");

        public static readonly MultiplierStat DamageFire =
            new(CharacterStatType.DamageFire, "Fire Damage", "Multiplier on fire elemental damage.");

        public static readonly MultiplierStat DamageFrost =
            new(CharacterStatType.DamageFrost, "Frost Damage", "Multiplier on frost elemental damage.");

        public static readonly MultiplierStat DamageLightning =
            new(CharacterStatType.DamageLightning, "Lightning Damage", "Multiplier on lightning elemental damage.");

        public static readonly MultiplierStat DamagePoison =
            new(CharacterStatType.DamagePoison, "Poison Damage", "Multiplier on poison elemental damage.");

        public static readonly MultiplierStat DamageSpirit =
            new(CharacterStatType.DamageSpirit, "Spirit Damage", "Multiplier on spirit magic damage.");

        // --- Combat Delivery, Grip & Archetypes ---
        public static readonly MultiplierStat DamagePhysical =
            new(CharacterStatType.DamagePhysical, "Physical Damage Bonus", "Multiplier across all physical damage types.");

        public static readonly MultiplierStat DamageElemental =
            new(CharacterStatType.DamageElemental, "Elemental Damage Bonus", "Multiplier across all elemental damage types.");

        public static readonly MultiplierStat DamageMelee =
            new(CharacterStatType.DamageMelee, "Melee Damage Bonus", "Multiplier on melee weapon attacks.");

        public static readonly MultiplierStat DamageRanged =
            new(CharacterStatType.DamageRanged, "Ranged Damage Bonus", "Multiplier on ranged weapon attacks.");

        public static readonly MultiplierStat DamageMagic =
            new(CharacterStatType.DamageMagic, "Magic Damage Bonus", "Multiplier on magic spell attacks.");

        public static readonly MultiplierStat DamageOneHanded =
            new(CharacterStatType.DamageOneHanded, "One-Handed Damage Bonus", "Multiplier on 1H melee weapon attacks.");

        public static readonly MultiplierStat DamageTwoHanded =
            new(CharacterStatType.DamageTwoHanded, "Two-Handed Damage Bonus", "Multiplier on 2H melee weapon attacks.");

        // --- Attack Styles & Techniques ---
        public static readonly MultiplierStat AttackSpeed =
            new(CharacterStatType.AttackSpeed, "Attack Speed", "Multiplier on weapon attack animation playback speed.");

        public static readonly MultiplierStat DamageSneak =
            new(CharacterStatType.DamageSneak, "Sneak Damage Bonus", "Multiplier on unaware stealth attacks.");

        public static readonly MultiplierStat CriticalStrikeMultiplier =
            new(CharacterStatType.CriticalStrikeMultiplier, "Critical Strike Multiplier", "Critical hit damage multiplier per tier (default 1.5x).");

        public static readonly MultiplierStat CleaveEfficiency =
            new(CharacterStatType.CleaveEfficiency, "Cleave Efficiency", "Multi-target cleave damage retention efficiency.");

        public static readonly MultiplierStat DamageComboFinisher =
            new(CharacterStatType.DamageComboFinisher, "Combo Finisher Damage", "Multiplier on 3rd-hit combo finisher attacks.");

        public static readonly MultiplierStat DamageSecondaryAttack =
            new(CharacterStatType.DamageSecondaryAttack, "Secondary Attack Damage", "Multiplier on weapon secondary attacks.");

        public static readonly MultiplierStat StaggerDamage =
            new(CharacterStatType.StaggerDamage, "Stagger Power", "Multiplier on poise damage dealt (HitData.m_staggerMultiplier).");

        public static readonly MultiplierStat SlashBleedPercent =
            new(CharacterStatType.SlashBleedPercent, "Slash Bleed Bonus", "Additive bonus to slash bleed fraction (+0.05 = +30% total bleed).");

        public static readonly MultiplierStat BleedDamagePercent = SlashBleedPercent; // Alias

        public static readonly MultiplierStat BleedDamageMultiplier = SlashBleedPercent; // Alias

        public static readonly MultiplierStat ParryPoiseMultiplier =
            new(CharacterStatType.ParryPoiseMultiplier, "Parry Poise Multiplier", "Multiplier on poise damage dealt by successful parries.");

        // --- Situational & Target Context Modifiers ---
        public static readonly MultiplierStat DamageAgainstBosses =
            new(CharacterStatType.DamageAgainstBosses, "Boss Damage Bonus", "Multiplier on damage dealt to bosses.");

        public static readonly MultiplierStat DamageAgainstStaggered =
            new(CharacterStatType.DamageAgainstStaggered, "Stagger Punish Bonus", "Multiplier on damage dealt to staggering targets.");

        public static readonly MultiplierStat DamageAgainstAirborne =
            new(CharacterStatType.DamageAgainstAirborne, "Airborne Damage Bonus", "Multiplier on damage dealt to flying targets.");

        public static readonly MultiplierStat DamageExecute =
            new(CharacterStatType.DamageExecute, "Execute Damage Bonus", "Multiplier on damage dealt to targets at or below 35% health.");

        public static readonly MultiplierStat DamageFirstStrike =
            new(CharacterStatType.DamageFirstStrike, "First Strike Bonus", "Multiplier on damage dealt to targets at or above 90% health.");

        public static readonly MultiplierStat DamageBerserk =
            new(CharacterStatType.DamageBerserk, "Berserk Damage Bonus", "Multiplier on damage dealt while attacker is at or below 35% health.");

        public static readonly MultiplierStat DamageFlawless =
            new(CharacterStatType.DamageFlawless, "Flawless Damage Bonus", "Multiplier on damage dealt while attacker is at or above 90% health.");

        // --- Weapon Skill Damage Archetypes (baseline 1.0x) ---
        public static readonly MultiplierStat DamageSwords =
            new(CharacterStatType.DamageSwords, "Swords Damage", "Multiplier on sword attack damage.");

        public static readonly MultiplierStat DamageKnives =
            new(CharacterStatType.DamageKnives, "Knives Damage", "Multiplier on knife/dagger attack damage.");

        public static readonly MultiplierStat DamageClubs =
            new(CharacterStatType.DamageClubs, "Clubs Damage", "Multiplier on club/mace attack damage.");

        public static readonly MultiplierStat DamagePolearms =
            new(CharacterStatType.DamagePolearms, "Polearms Damage", "Multiplier on atgeir/polearm attack damage.");

        public static readonly MultiplierStat DamageSpears =
            new(CharacterStatType.DamageSpears, "Spears Damage", "Multiplier on spear attack damage.");

        public static readonly MultiplierStat DamageAxes =
            new(CharacterStatType.DamageAxes, "Axes Damage", "Multiplier on battleaxe/axe attack damage.");

        public static readonly MultiplierStat DamageBows =
            new(CharacterStatType.DamageBows, "Bows Damage", "Multiplier on bow projectile damage.");

        public static readonly MultiplierStat DamageCrossbows =
            new(CharacterStatType.DamageCrossbows, "Crossbows Damage", "Multiplier on crossbow projectile damage.");

        public static readonly MultiplierStat DamageUnarmed =
            new(CharacterStatType.DamageUnarmed, "Unarmed Damage", "Multiplier on fist/kick attack damage (including fist weapons like Flesh Rippers).");

        public static readonly MultiplierStat DamageElementalMagic =
            new(CharacterStatType.DamageElementalMagic, "Elemental Magic Damage", "Multiplier on elemental magic staff damage.");

        public static readonly MultiplierStat DamageBloodMagic =
            new(CharacterStatType.DamageBloodMagic, "Blood Magic Damage", "Multiplier on blood magic staff damage.");

        // --- Weapon Skill Attack Speed Archetypes (baseline 1.0x) ---
        public static readonly MultiplierStat AttackSpeedSwords =
            new(CharacterStatType.AttackSpeedSwords, "Swords Attack Speed", "Multiplier on sword attack animation playback speed.");

        public static readonly MultiplierStat AttackSpeedKnives =
            new(CharacterStatType.AttackSpeedKnives, "Knives Attack Speed", "Multiplier on knife/dagger attack animation playback speed.");

        public static readonly MultiplierStat AttackSpeedClubs =
            new(CharacterStatType.AttackSpeedClubs, "Clubs Attack Speed", "Multiplier on club/mace attack animation playback speed.");

        public static readonly MultiplierStat AttackSpeedPolearms =
            new(CharacterStatType.AttackSpeedPolearms, "Polearms Attack Speed", "Multiplier on atgeir/polearm attack animation playback speed.");

        public static readonly MultiplierStat AttackSpeedSpears =
            new(CharacterStatType.AttackSpeedSpears, "Spears Attack Speed", "Multiplier on spear attack animation playback speed.");

        public static readonly MultiplierStat AttackSpeedAxes =
            new(CharacterStatType.AttackSpeedAxes, "Axes Attack Speed", "Multiplier on battleaxe/axe attack animation playback speed.");

        public static readonly MultiplierStat AttackSpeedBows =
            new(CharacterStatType.AttackSpeedBows, "Bows Attack Speed", "Multiplier on bow attack animation playback speed.");

        public static readonly MultiplierStat AttackSpeedCrossbows =
            new(CharacterStatType.AttackSpeedCrossbows, "Crossbows Attack Speed", "Multiplier on crossbow attack animation playback speed.");

        public static readonly MultiplierStat AttackSpeedUnarmed =
            new(CharacterStatType.AttackSpeedUnarmed, "Unarmed Attack Speed", "Multiplier on fist/kick attack animation playback speed.");

        public static readonly MultiplierStat AttackSpeedElementalMagic =
            new(CharacterStatType.AttackSpeedElementalMagic, "Elemental Magic Attack Speed", "Multiplier on elemental magic staff attack animation playback speed.");

        public static readonly MultiplierStat AttackSpeedBloodMagic =
            new(CharacterStatType.AttackSpeedBloodMagic, "Blood Magic Attack Speed", "Multiplier on blood magic staff attack animation playback speed.");

        // --- Ranged, Precision & Stealth ---
        public static readonly MultiplierStat BowDrawSpeed =
            new(CharacterStatType.BowDrawSpeed, "Bow Draw Speed", "Multiplier on bow draw speed (draw duration = base / speed).");

        public static readonly MultiplierStat CrossbowReloadSpeed =
            new(CharacterStatType.CrossbowReloadSpeed, "Crossbow Reload Speed", "Multiplier on crossbow reload speed (reload time = base / speed).");

        public static readonly MultiplierStat ProjectilePrecision =
            new(CharacterStatType.ProjectilePrecision, "Projectile Precision", "Precision rating (spread angle = base / precision).");

        public static readonly MultiplierStat Concealment =
            new(CharacterStatType.Concealment, "Stealth Concealment", "Concealment rating (AI visibility = base / concealment).");

        // --- Gathering, Crafting & Loot Yields ---
        public static readonly MultiplierStat CraftingYield =
            new(CharacterStatType.CraftingYield, "Crafting Yield", "Multi-drop roll budget on crafted items (base 1.0x).");

        public static readonly MultiplierStat CookingYield =
            new(CharacterStatType.CookingYield, "Cooking Yield", "Multi-drop roll budget on cooked food (base 1.0x).");

        public static readonly MultiplierStat ForagingYield =
            new(CharacterStatType.ForagingYield, "Foraging Yield", "Multi-drop roll budget on harvested pickables/crops (base 1.0x).");

        public static readonly MultiplierStat MiningYield =
            new(CharacterStatType.MiningYield, "Mining Yield", "Multi-drop roll budget on mined mineral/rock chunks (base 1.0x).");

        public static readonly MultiplierStat WoodcuttingYield =
            new(CharacterStatType.WoodcuttingYield, "Woodcutting Yield", "Multi-drop roll budget on felled wood/logs (base 1.0x).");

        public static readonly MultiplierStat MonsterDropYield =
            new(CharacterStatType.MonsterDropYield, "Monster Drop Yield", "Multi-drop roll budget on slain creature loot (base 1.0x).");

        public static readonly MultiplierStat HarvestRadius =
            new(CharacterStatType.HarvestRadius, "Harvest Radius", "Multiplier on scythe and agricultural piece interaction radius.");

        /// <summary>Multiplier on food lifetime (1.5x = food burns 33% slower, lasting 50% longer).</summary>
        public static readonly MultiplierStat FoodDurationMultiplier =
            new(CharacterStatType.FoodDurationMultiplier, "Food Duration", "Multiplier on how long consumed food lasts.");

        /// <summary>Multiplier on knockback (HitData.m_pushForce) dealt to targets.</summary>
        public static readonly MultiplierStat KnockbackDealt =
            new(CharacterStatType.KnockbackDealt, "Knockback Dealt", "Multiplier on knockback force dealt to targets.");

        /// <summary>Multiplier on melee attack reach (Attack.m_attackRange).</summary>
        public static readonly MultiplierStat MeleeReach =
            new(CharacterStatType.MeleeReach, "Melee Reach", "Multiplier on melee attack range.");

        /// <summary>Multiplier on ship wind sailing force factor while at the helm (base 1.0x).</summary>
        public static readonly MultiplierStat ShipSailSpeed =
            new(CharacterStatType.ShipSailSpeed, "Ship Sail Speed", "Multiplier on ship wind sailing force.");

        /// <summary>Multiplier on ship rudder paddle and reverse impulse while at the helm (base 1.0x).</summary>
        public static readonly MultiplierStat ShipRudderSpeed =
            new(CharacterStatType.ShipRudderSpeed, "Ship Rudder Speed", "Multiplier on ship paddle and reverse speed.");

        public static readonly MultiplierStat ShipSpeed = ShipSailSpeed; // Compatibility alias

        /// <summary>Damage multiplier dealt while EnvMan.IsNight() is true (base 1.0x).</summary>
        public static readonly MultiplierStat DamageAtNight =
            new(CharacterStatType.DamageAtNight, "Night Damage Bonus", "Multiplier on damage dealt during night hours.");

        /// <summary>Damage multiplier dealt while the attacker is airborne (jumping/falling, base 1.0x).</summary>
        public static readonly MultiplierStat DamageWhileAirborne =
            new(CharacterStatType.DamageWhileAirborne, "Airborne Damage Bonus", "Multiplier on damage dealt while off the ground.");

        /// <summary>Damage multiplier dealt against targets suffering negative status effects or staggered (base 1.0x).</summary>
        public static readonly MultiplierStat DamageAgainstAfflicted =
            new(CharacterStatType.DamageAgainstAfflicted, "Afflicted Target Damage Bonus", "Multiplier on damage dealt to afflicted targets.");

        // =========================================================================
        // 4. ACTION EFFICIENCY STATS (Asymptotic Denominator Scaling; Tier 2 & 3 ONLY)
        // =========================================================================

        public static readonly EfficiencyStat SprintEfficiency =
            new(CharacterStatType.SprintEfficiency, "Sprint Stamina Efficiency", "Efficiency rating on running stamina drain.");

        public static readonly EfficiencyStat JumpEfficiency =
            new(CharacterStatType.JumpEfficiency, "Jump Stamina Efficiency", "Efficiency rating on jumping stamina cost.");

        public static readonly EfficiencyStat DodgeEfficiency =
            new(CharacterStatType.DodgeEfficiency, "Dodge Stamina Efficiency", "Efficiency rating on dodge roll stamina cost.");

        public static readonly EfficiencyStat SneakEfficiency =
            new(CharacterStatType.SneakEfficiency, "Sneak Stamina Efficiency", "Efficiency rating on sneak stamina drain.");

        public static readonly EfficiencyStat SwimEfficiency =
            new(CharacterStatType.SwimEfficiency, "Swim Stamina Efficiency", "Efficiency rating on swimming stamina drain.");

        public static readonly EfficiencyStat AttackStaminaEfficiency =
            new(CharacterStatType.AttackStaminaEfficiency, "Attack Stamina Efficiency", "Efficiency rating on weapon attack stamina cost.");

        public static readonly EfficiencyStat AttackEitrEfficiency =
            new(CharacterStatType.AttackEitrEfficiency, "Attack Eitr Efficiency", "Efficiency rating on magic attack Eitr cost.");

        public static readonly EfficiencyStat AttackHealthEfficiency =
            new(CharacterStatType.AttackHealthEfficiency, "Attack Health Efficiency", "Efficiency rating on blood magic health cost.");

        public static readonly EfficiencyStat BowDrawStaminaEfficiency =
            new(CharacterStatType.BowDrawStaminaEfficiency, "Bow Hold Stamina Efficiency", "Efficiency rating on bow hold stamina drain.");

        public static readonly EfficiencyStat BowDrawEitrEfficiency =
            new(CharacterStatType.BowDrawEitrEfficiency, "Staff Hold Eitr Efficiency", "Efficiency rating on staff hold Eitr drain.");

        public static readonly EfficiencyStat FishingStaminaEfficiency =
            new(CharacterStatType.FishingStaminaEfficiency, "Fishing Reel Stamina Efficiency", "Efficiency rating on fishing reel-in stamina drain.");

        public static readonly EfficiencyStat BlockStaminaEfficiency =
            new(CharacterStatType.BlockStaminaEfficiency, "Block Stamina Efficiency", "Efficiency rating on blocking attack stamina cost.");

        public static readonly EfficiencyStat RidingStaminaEfficiency =
            new(CharacterStatType.RidingStaminaEfficiency, "Riding Stamina Efficiency", "Efficiency rating on mount sprint stamina drain.");

        // --- Delivery, Grip & Attack Step Efficiencies ---
        public static readonly EfficiencyStat MeleeStaminaEfficiency =
            new(CharacterStatType.MeleeStaminaEfficiency, "Melee Stamina Efficiency", "Efficiency rating on melee attack stamina costs.");

        public static readonly EfficiencyStat RangedStaminaEfficiency =
            new(CharacterStatType.RangedStaminaEfficiency, "Ranged Stamina Efficiency", "Efficiency rating on ranged attack stamina costs.");

        public static readonly EfficiencyStat OneHandedStaminaEfficiency =
            new(CharacterStatType.OneHandedStaminaEfficiency, "One-Handed Stamina Efficiency", "Efficiency rating on 1H melee attack stamina costs.");

        public static readonly EfficiencyStat TwoHandedStaminaEfficiency =
            new(CharacterStatType.TwoHandedStaminaEfficiency, "Two-Handed Stamina Efficiency", "Efficiency rating on 2H melee attack stamina costs.");

        public static readonly EfficiencyStat SecondaryAttackStaminaEfficiency =
            new(CharacterStatType.SecondaryAttackStaminaEfficiency, "Secondary Attack Stamina Efficiency", "Efficiency rating on secondary attack stamina costs.");

        // --- Per-Weapon Action Efficiencies (Asymptotic Denominator Scaling, baseline 0.0) ---
        public static readonly EfficiencyStat SwordsEfficiency =
            new(CharacterStatType.SwordsEfficiency, "Swords Stamina Efficiency", "Efficiency rating on sword attack stamina cost.");

        public static readonly EfficiencyStat KnivesEfficiency =
            new(CharacterStatType.KnivesEfficiency, "Knives Stamina Efficiency", "Efficiency rating on knife/dagger attack stamina cost.");

        public static readonly EfficiencyStat ClubsEfficiency =
            new(CharacterStatType.ClubsEfficiency, "Clubs Stamina Efficiency", "Efficiency rating on club/mace attack stamina cost.");

        public static readonly EfficiencyStat PolearmsEfficiency =
            new(CharacterStatType.PolearmsEfficiency, "Polearms Stamina Efficiency", "Efficiency rating on atgeir/polearm attack stamina cost.");

        public static readonly EfficiencyStat SpearsEfficiency =
            new(CharacterStatType.SpearsEfficiency, "Spears Stamina Efficiency", "Efficiency rating on spear attack stamina cost.");

        public static readonly EfficiencyStat AxesEfficiency =
            new(CharacterStatType.AxesEfficiency, "Axes Stamina Efficiency", "Efficiency rating on axe attack stamina cost.");

        public static readonly EfficiencyStat BowsEfficiency =
            new(CharacterStatType.BowsEfficiency, "Bows Stamina Efficiency", "Efficiency rating on bow attack stamina cost.");

        public static readonly EfficiencyStat CrossbowReloadStaminaEfficiency =
            new(CharacterStatType.CrossbowReloadStaminaEfficiency, "Crossbow Reload Stamina Efficiency", "Efficiency rating on crossbow reload stamina drain.");

        public static readonly EfficiencyStat CrossbowsEfficiency = CrossbowReloadStaminaEfficiency; // Compatibility alias

        public static readonly EfficiencyStat UnarmedEfficiency =
            new(CharacterStatType.UnarmedEfficiency, "Unarmed Stamina Efficiency", "Efficiency rating on unarmed attack stamina cost (including fist weapons like Flesh Rippers).");

        public static readonly EfficiencyStat ElementalMagicEitrEfficiency =
            new(CharacterStatType.ElementalMagicEitrEfficiency, "Elemental Magic Eitr Efficiency", "Efficiency rating on elemental magic attack Eitr cost.");

        public static readonly EfficiencyStat ElementalMagicEfficiency = ElementalMagicEitrEfficiency; // Compatibility alias

        public static readonly EfficiencyStat BloodMagicHealthEfficiency =
            new(CharacterStatType.BloodMagicHealthEfficiency, "Blood Magic Health Efficiency", "Efficiency rating on blood magic attack Health cost.");

        public static readonly EfficiencyStat BloodMagicEitrEfficiency =
            new(CharacterStatType.BloodMagicEitrEfficiency, "Blood Magic Eitr Efficiency", "Efficiency rating on blood magic attack Eitr cost.");

        public static readonly EfficiencyStat BloodMagicEfficiency = BloodMagicHealthEfficiency; // Compatibility alias

        /// <summary>Efficiency rating on Hammer, Hoe, and Cultivator stamina costs: cost = base / (1 + eff).</summary>
        public static readonly EfficiencyStat ToolStaminaEfficiency =
            new(CharacterStatType.ToolStaminaEfficiency, "Tool Stamina Efficiency", "Efficiency rating on Hammer, Hoe, and Cultivator stamina costs.");

        public static readonly EfficiencyStat HomeToolsEfficiency = ToolStaminaEfficiency; // Compatibility alias

        /// <summary>Efficiency rating reducing equipment durability wear: wear = base / (1 + eff).</summary>
        public static readonly EfficiencyStat DurabilityEfficiency =
            new(CharacterStatType.DurabilityEfficiency, "Durability Efficiency", "Efficiency rating reducing equipment wear on tools, weapons, and armor.");

        public static readonly EfficiencyStat DurabilityLossMultiplier = DurabilityEfficiency; // Compatibility alias

        /// <summary>Efficiency rating reducing cargo mass inertia when pulling carts: cargo mass = base / (1 + eff).</summary>
        public static readonly EfficiencyStat CartWeightEfficiency =
            new(CharacterStatType.CartWeightEfficiency, "Cart Weight Efficiency", "Efficiency rating reducing cart cargo inertia.");

        public static readonly EfficiencyStat CartWeightMultiplier = CartWeightEfficiency; // Compatibility alias

        // =========================================================================
        // 5. ARMOR EFFICIENCY STATS (Asymptotic Denominator / Piecewise Scaling, base 0.0)
        // =========================================================================

        /// <summary>Umbrella physical armor efficiency scaling Blunt, Slash, and Pierce damage reduction.</summary>
        public static readonly EfficiencyStat ArmorEfficiency =
            new(CharacterStatType.ArmorEfficiency, "Armor Efficiency", "Umbrella physical armor efficiency scaling Blunt, Slash, and Pierce mitigation.");

        /// <summary>Extra physical armor efficiency specific to Blunt damage.</summary>
        public static readonly EfficiencyStat BluntArmorEfficiency =
            new(CharacterStatType.BluntArmorEfficiency, "Blunt Armor Efficiency", "Physical armor efficiency against Blunt damage.");

        /// <summary>Extra physical armor efficiency specific to Slash damage.</summary>
        public static readonly EfficiencyStat SlashArmorEfficiency =
            new(CharacterStatType.SlashArmorEfficiency, "Slash Armor Efficiency", "Physical armor efficiency against Slash damage.");

        /// <summary>Extra physical armor efficiency specific to Pierce damage.</summary>
        public static readonly EfficiencyStat PierceArmorEfficiency =
            new(CharacterStatType.PierceArmorEfficiency, "Pierce Armor Efficiency", "Physical armor efficiency against Pierce damage.");
    }
}
