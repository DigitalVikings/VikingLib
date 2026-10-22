# VikingLib v1.0 Integration Guide for Downstream Mods

This guide summarizes the newly added features and extension points in **VikingLib** for downstream mod developers and agents.

---

## 1. Network Poise Synchronization (`StaggerManager`)
Monster and boss poise is now owner-authoritative and automatically synchronized via `ZDO` (`VL_PoiseRatio`).
- **No manual sync needed**: `StaggerManager.AddPoiseDamage` automatically syncs ratio to the target's `ZDO`.
- **UI Queries**: Call `StaggerManager.GetPoisePercentage(character)` to inspect poise percentage on either local or remote entities.

---

## 2. Damage Conversion & Extra Elemental Gain (`DamageContext`)
Instead of manual arithmetic or ad-hoc damage replacement in Phase 1 handlers:
- **Deductive Conversion** (deducts from source, adds to target):
  ```csharp
  ctx.ConvertDamage(DamageType.Slash, DamageType.Fire, ratio: 0.50f, source: "Flame Infusion");
  ```
- **Auxiliary Elemental Gain** (leaves base intact, adds extra):
  ```csharp
  // Gain +15% of physical damage as Fire (e.g. Plains Tribe 4pc):
  ctx.AddExtraDamageFromPhysical(DamageType.Fire, ratio: 0.15f, source: "Plains Tribe");

  // Gain +25% of Blunt as Lightning:
  ctx.AddExtraDamageFrom(DamageType.Blunt, DamageType.Lightning, ratio: 0.25f, source: "Thunderstrike");
  ```

---

## 3. Retaliation & Damage Reflection (`DamagePipeline.ReflectDamage`)
Instead of manually instantiating raw `HitData`:
```csharp
// In Phase 2 OnHitTaken or defensive proc:
DamagePipeline.ReflectDamage(
    defender: player,
    attacker: ctx.Attacker,
    originalCtx: ctx,
    ratio: 0.50f, // 50% reflect
    overrideType: DamageType.Blunt, // Or null for proportional distribution
    source: "Carapace Counter"
);
```
- **Loop Protection**: Automatically tags the reflected hit with `DamageFlags.IsReflected`. Hits carrying this flag cannot be reflected again.
- **Standard Mitigation**: The reflected hit undergoes standard armor and resistance mitigation on the attacker.

---

## 4. Reusable Proc Cooldowns (`ProcCooldownManager`)
Instead of static timestamps or custom timer dictionaries:
```csharp
// Session / in-memory cooldown (cleans up automatically on entity despawn):
if (ProcCooldownManager.TryTrigger(player, "CarapaceWard", cooldownSeconds: 45f))
{
    // Proc activated
}

// Persistent cooldown across character relogs (saved to Player.m_customData):
if (ProcCooldownManager.TryTrigger(player, "LastStand", cooldownSeconds: 300f, persist: true))
{
    // 5-minute Last Stand triggered; surviving relogs and world hops
}
```

---

## 5. Unified On-Hit Status Application (`DamagePipeline.ApplyOnHitStatus`)
Instead of manual `SEMan.AddStatusEffect` calls:
```csharp
// Applies either VikingLib multi-slice effects or native Valheim effects:
DamagePipeline.ApplyOnHitStatus(target, "Frost", value: 0f, duration: 5f, attacker: player);
DamagePipeline.ApplyOnHitStatus(target, CanonicalStatusEffects.BleedId, value: 15f, duration: 10f, attacker: player);
```
- Automatically checks biological immunity (`EntityDefenseRegistry.CanBleed(target)`).
- Automatically scales duration/magnitude by target elemental resistances.

---

## 6. Public UI Extension APIs

### A. Weapon Combat Dossier Trait Badges
Register custom trait lines to display under `[Traits & Modifiers]` in the Shift-expanded weapon tooltip:
```csharp
TooltipEngine.RegisterLineProvider(ctx =>
{
    if (ctx.Item?.m_shared?.m_skillType == Skills.SkillType.Swords)
    {
        return new[] { "<color=#00FFFF>• Blade Dance: +20% Attack Speed</color>" };
    }
    return null;
}, priority: 100);
```

### B. Hugin's Compendium Custom Categories
Register structured categories under the Character Stats compendium topic (sparse-pruned when empty):
```csharp
CharacterStatsGlossaryPanel.RegisterCustomCategory("Rested Rites", player =>
{
    var entries = AttunementManager.GetActiveEntries(player);
    if (entries.Count == 0) return null; // Automatically omitted from display

    return new[]
    {
        ("Attuned Power", $"{PowerCalculator.CalculatePowerMultiplier(player) * 100f:0}%"),
        ("Active Rites", $"{entries.Count} Active")
    };
}, priority: 50);
```
