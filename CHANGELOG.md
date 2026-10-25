# Changelog

All notable changes to **VikingLib** will be documented in this file.

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
