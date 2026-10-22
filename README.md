# VikingLib

**VikingLib** is a modular combat mechanics, character statistic, and damage calculation framework for Valheim. It decouples Valheim's combat and stat systems from monolithic vanilla methods, providing a deterministic foundation for custom combat overhauls, RPG progression, weapon packs, and perk mods.

![Combat Dossier](https://raw.githubusercontent.com/DigitalVikings/VikingLib/main/combat_dossier.png)
![Staggered Target Combat](https://raw.githubusercontent.com/DigitalVikings/VikingLib/main/staggered_greyling.png)

> ⚠️ **Work in Progress (Pre-Release v0.1.0)**: This framework is in active development and has not been tested extensively across all game configurations. Features, internal APIs, and mechanics may change, and unintended edge cases may occur. Backup your character and world saves before testing.

---

## How It Works

VikingLib intercepts and standardizes all combat math, stat mutations, and stamina drains through deterministic pipelines:

- **6-Phase Prioritized Damage Pipeline**: Every melee hit, projectile, and environmental impact executes sequentially across 6 distinct phases (`PreCheck`, `AttackerModifiers`, `TargetDefenses`, `Mitigation`, `Execution`, `PostHitProcs`), with full support for multi-target cleave decay, armor penetration, and continuous resistances.
- **Dual-Space 3-Tier Mathematical Engine**: Evaluates character stats across 90+ tokens using strict mathematical tiers: Tier 1 (Flat base values), Tier 2 (Additive percentages), and Tier 3 (Compounding multipliers).
- **Asymptotic Action Efficiency**: Stamina, eitr, and health costs scale smoothly in the denominator, ensuring efficiency buffs never reduce action drains to zero or exploit negative numbers (+100% efficiency halves action costs; +200% divides costs by three).
- **Combat Dossier & Hugin's Compendium**: Inspect full weapon combo chains, cleave ratings, and live stamina costs by holding `[Shift]` over weapon tooltips, and view active character statistics cleanly categorized inside Hugin's Compendium (`TextsDialog`).

<details>
<summary><b>Click to expand Combat Stat Taxonomy (90+ Declarative Tokens)</b></summary>

| Category | Representative Tokens | Description |
| :--- | :--- | :--- |
| **Vitals & Units** | `MaxHealth`, `MaxStamina`, `MaxEitr`, `HealthRegen`, `BodyArmor`, `JumpHeight` | Physical vitals supporting Flat additions, Additive percentages, and Multipliers. |
| **Delivery & Stance** | `DamagePhysical`, `DamageElemental`, `DamageMelee`, `DamageRanged`, `DamageMagic`, `DamageOneHanded`, `DamageTwoHanded` | Umbrella damage multipliers across combat stances and delivery methods. |
| **Action Efficiencies** | `MeleeStaminaEfficiency`, `OneHandedStaminaEfficiency`, `TwoHandedStaminaEfficiency`, `RangedStaminaEfficiency`, `SecondaryAttackStaminaEfficiency` | Asymptotic stamina efficiency ratings scaling attack costs per grip and step. |
| **Techniques** | `AttackSpeed`, `CriticalStrikeChance`, `CriticalStrikeMultiplier`, `DamageSecondaryAttack`, `DamageComboFinisher`, `DamageSneak`, `CleaveEfficiency`, `StaggerDamage`, `BowDrawSpeed`, `CrossbowReloadSpeed` | Modifiers altering attack tempo, critical hits, stealth ambushes, and ranged handling. |
| **Situational Contexts** | `DamageAgainstBosses`, `DamageAgainstStaggered`, `DamageAgainstAirborne`, `DamageExecute`, `DamageFirstStrike`, `DamageBerserk`, `DamageFlawless` | Dynamic combat context modifiers evaluated in Phase 1 based on live creature states. |
| **Weapon Families** | `DamageSwords`, `DamageKnives`, `DamageClubs`, `DamagePolearms`, `DamageSpears`, `DamageAxes`, `DamageBows`, `DamageCrossbows`, `DamageUnarmed` | Per-weapon-family mastery multipliers. |
| **Continuous Resistances**| `ResistancePhysical`, `ResistanceElemental`, `ResistanceSlashing` ... `ResistanceSpirit` (10 types) | Multiplicative damage-taken factors ($\text{Taken} = \text{Base} \times \prod \text{Multipliers}$). Tier 3 only. |
| **Yields & Agriculture** | `MiningYield`, `WoodcuttingYield`, `MonsterDropYield`, `CraftingYield`, `CookingYield`, `ForagingYield`, `HarvestRadius` | Multi-drop roll budgets on resources and agricultural tool interaction radiuses. |

</details>

<details>
<summary><b>Click to expand Developer Console Commands & Diagnostics</b></summary>

VikingLib includes developer commands accessible in the Valheim console (`F5`):

| Command | Usage | Description |
| :--- | :--- | :--- |
| **`vl_buff`** | `vl_buff [on \| off \| clear]` | Toggles or clears a suite of synthetic test buffs across all taxonomy categories on the local player. |
| **`vl_trace`** | `vl_trace` | Prints a formatted diagnostic breakdown of the most recent combat hit (damage rolls, modifiers, armor, and resistances). |
| **`vl_log`** | `vl_log [off \| player \| all]` | Configures live combat telemetry logging to the BepInEx console. |
| **`vl_dump_attacks`** | `vl_dump_attacks` | Exports all weapon attack configurations from `ObjectDB` to `VanillaWeaponAttacks.json`. |

</details>

<details>
<summary><b>Click to expand Downstream Mod Integration Guide</b></summary>

Reference `VikingLib.dll` in your mod project and declare the BepInDependency:

```csharp
[BepInPlugin("com.author.mymod", "MyCombatMod", "1.0.0")]
[BepInDependency("com.digitalvikings.vikinglib", BepInDependency.DependencyFlags.HardDependency)]
public class MyModPlugin : BaseUnityPlugin
{
    private void Awake()
    {
        // Add declarative character stat modifier (+20% sword damage)
        CharacterStats.DamageSwords.AddIncreased(Player.m_localPlayer, 0.20f, "Swordmaster");

        // Add type-safe resistance modifier (Tier 3 Multiplier ONLY, -20% fire damage taken)
        CharacterStats.ResistanceFire.AddMultiplier(Player.m_localPlayer, 0.80f, "Fire Ward");

        // Add asymptotic efficiency modifier (+25% melee stamina efficiency)
        CharacterStats.MeleeStaminaEfficiency.AddIncreased(Player.m_localPlayer, 0.25f, "Iron Stamina");
    }
}
```

</details>
