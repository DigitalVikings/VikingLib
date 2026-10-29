# Changelog

All notable changes to **VikingLib** will be documented in this file.

## [0.2.3] - 2026-10-29

### Changed
- **Modularized Phase Handlers**: Decoupled default combat phase implementations into dedicated handler structures for improved maintainability.
- **Skill Lifecycle Architecture**: Reorganized native skill interception patches into dedicated domain modules.

## [0.2.2] - 2026-10-27

### Changed
- **Calibrated Stagger Durations**: Refined stagger recovery windows across creature tiers (Minor foes ~2.0s, Standard mobs ~2.75s, Heavy elites ~4.0s, Bosses full 5.0s, and Player 2.0s baseline).
- **Combat Dossier Formatting**: Streamlined weapon tooltip layout, attack profile headers, and keybinding prompt clarity.

## [0.2.1] - 2026-10-25
### Fixed
- Fixed critical game startup error triggered by cooking station drop yield telemetry registration.

## [0.2.0] - 2026-10-25

### Added
- **Universal Poise & Boss Stagger Engine**: Continuous poise accumulation, uniform stagger duration, and overhead stagger meters, enabling full stagger stuns against all world bosses and giants.
- **Economic Yield Engine**: Configurable gathering, mining, woodcutting, cooking, and crafting bonus yield mechanics with detailed logging receipts.
- **Physical Triad Calibration**: Scaled Slash Bleed to 35% of post-mitigation damage, Melee Pierce Precision to +25%, and decoupled enemy physical attack stagger buildup.

### Changed
- Default cleave mode changed from `Continuous` to `Geometric`.

## [0.1.0] - 2026-10-25

### Added
- **6-Phase Combat Pipeline**: Modular damage processing framework (`PreCheck`, `AttackerModifiers`, `TargetDefenses`, `Mitigation`, `Execution`, `PostHitProcs`) supporting multi-target cleave, armor penetration, and continuous resistances.
- **Universal Poise & Boss Stagger Engine**: Continuous poise accumulation, uniform stagger duration, and overhead stagger meters, enabling full stagger stuns against all world bosses and giants.
- **Dual-Space 3-Tier Stat Architecture**: Standardized mathematical engine evaluating character stats across 90+ tokens using flat bases, additive percentages, and compounding multipliers with asymptotic stamina efficiencies.
- **Multi-Slice Status Effect System**: Dynamic status effect architecture supporting independent slices, duration-first absorption shields, and automated stat scaling.
- **Economic Yield Engine**: Configurable gathering, mining, woodcutting, cooking, and crafting bonus yield mechanics with detailed logging receipts.
- **Network Serialization Protocol**: Robust binary protocol maintaining byte symmetry and save-file safety for custom combat data over RPCs.
