using System;
using System.Collections.Generic;
using UnityEngine;
using VikingLib.Combat.Defense;
using VikingLib.Combat.Telemetry;
using VikingLib.Configuration;

namespace VikingLib.Combat
{
    /// <summary>
    /// Rich mutable context passed through each phase of the VikingLib Damage Pipeline.
    /// Manages 3-tier mathematical modifiers, granular damage types, crit rolls, flags, and telemetry.
    /// </summary>
    public class DamageContext
    {
        public HitData Hit { get; }
        public Character? Attacker { get; }
        public Character? Target { get; }
        public WearNTear? TargetPiece { get; }
        public IDestructible? DestructibleTarget { get; }

        public DamageFlags Flags { get; set; }
        public DamagePhase CurrentPhase { get; internal set; } = DamagePhase.PreCheck;
        public bool IsCancelled { get; private set; }
        public bool IsPreview { get; set; }

        public float CritChance { get; set; }
        public float CritMultiplier { get; set; } = 1.5f;

        public float ArmorPenetrationFlat { get; set; }
        public float ArmorPenetrationPercent { get; set; }
        public float LifestealPercent { get; set; }

        /// <summary>
        /// True when the calculated damage at the end of Phase 5 (Execution) would kill the target.
        /// Set before <see cref="DamagePipeline.OnLethalHit"/> fires; reset if a handler defies death.
        /// </summary>
        public bool IsLethal { get; internal set; }

        /// <summary>
        /// True in Phase 6 (PostHitProcs) when this hit actually killed the target Character.
        /// </summary>
        public bool IsKillingBlow { get; internal set; }

        /// <summary>
        /// True if the target Character was already staggering when this hit landed.
        /// </summary>
        public bool TargetWasStaggering { get; internal set; }

        /// <summary>
        /// True in Phase 6 (PostHitProcs) when this hit pushed the target over its stagger threshold.
        /// </summary>
        public bool CausedStagger { get; internal set; }

        public EntityDefenseData Defense { get; set; }
        public DamageTrace? Trace { get; }

        // Working damages broken down by type
        private readonly Dictionary<DamageType, float> _damages = new();

        // 3-tier modifiers accumulated during current phase
        private readonly List<DamageModifierRecord> _phaseModifiers = new();

        public DamageContext(HitData hit, Character? attacker, Character? target, DamageTrace? trace = null)
        {
            Hit = hit ?? throw new ArgumentNullException(nameof(hit));
            Attacker = attacker;
            Target = target;
            DestructibleTarget = target;
            Trace = trace;

            Defense = target != null ? EntityDefenseRegistry.GetDefense(target) : new EntityDefenseData();
            InitializeDamagesFromHit();
            InitializeDefaultFlags();
        }

        public DamageContext(HitData hit, Character? attacker, WearNTear targetPiece, DamageTrace? trace = null)
        {
            Hit = hit ?? throw new ArgumentNullException(nameof(hit));
            Attacker = attacker;
            TargetPiece = targetPiece;
            DestructibleTarget = targetPiece;
            Trace = trace;

            Defense = targetPiece != null ? EntityDefenseRegistry.GetDefense(targetPiece) : new EntityDefenseData();
            InitializeDamagesFromHit();
            InitializeDefaultFlags();
        }

        public DamageContext(HitData hit, Character? attacker, IDestructible? destructibleTarget, EntityDefenseData defense, DamageTrace? trace = null)
        {
            Hit = hit ?? throw new ArgumentNullException(nameof(hit));
            Attacker = attacker;
            DestructibleTarget = destructibleTarget;
            if (destructibleTarget is Character character) Target = character;
            else if (destructibleTarget is WearNTear piece) TargetPiece = piece;
            Trace = trace;

            Defense = defense ?? new EntityDefenseData();
            InitializeDamagesFromHit();
            InitializeDefaultFlags();
        }

        private void InitializeDamagesFromHit()
        {
            _damages[DamageType.Blunt] = Hit.m_damage.m_blunt;
            _damages[DamageType.Slash] = Hit.m_damage.m_slash;
            _damages[DamageType.Pierce] = Hit.m_damage.m_pierce;
            _damages[DamageType.Chop] = Hit.m_damage.m_chop;
            _damages[DamageType.Pickaxe] = Hit.m_damage.m_pickaxe;
            _damages[DamageType.Fire] = Hit.m_damage.m_fire;
            _damages[DamageType.Frost] = Hit.m_damage.m_frost;
            _damages[DamageType.Lightning] = Hit.m_damage.m_lightning;
            _damages[DamageType.Poison] = Hit.m_damage.m_poison;
            _damages[DamageType.Spirit] = Hit.m_damage.m_spirit;
            _damages[DamageType.TrueDamage] = 0f;
        }

        private void InitializeDefaultFlags()
        {
            Flags = DamageFlags.CanTriggerProcs;

            if (Hit.m_skill is Skills.SkillType.Swords or Skills.SkillType.Knives or Skills.SkillType.Clubs or
                Skills.SkillType.Polearms or Skills.SkillType.Spears or Skills.SkillType.Axes or Skills.SkillType.Unarmed)
            {
                Flags |= DamageFlags.Melee;
            }
            else if (Hit.m_skill is Skills.SkillType.Bows or Skills.SkillType.Crossbows)
            {
                Flags |= DamageFlags.Ranged;
            }
            else if (Hit.m_skill is Skills.SkillType.ElementalMagic or Skills.SkillType.BloodMagic)
            {
                Flags |= DamageFlags.Magic;
            }

            if (!Hit.m_blockable) Flags |= DamageFlags.CannotBeBlocked;
            if (!Hit.m_dodgeable) Flags |= DamageFlags.CannotBeDodged;

            if (Attacks.WeaponAttackRegistry.TryGetHitAttack(Hit, out var atkInfo) && atkInfo != null)
            {
                if ((atkInfo.Flags & DamageFlags.IsReflected) != 0)
                {
                    Flags |= DamageFlags.IsReflected;
                }
            }
        }

        /// <summary>
        /// Cancels this hit entirely, zeroing out all damage and preventing further calculation.
        /// </summary>
        public void Cancel(string reason = "Cancelled")
        {
            IsCancelled = true;
            foreach (var key in new List<DamageType>(_damages.Keys))
            {
                _damages[key] = 0f;
            }
            Trace?.RecordStep(CurrentPhase, "Pipeline", $"Hit Cancelled ({reason})", 0f);
        }

        /// <summary>
        /// Clamps the hit's total damage so the target Character survives with 1 HP ("Last Stand" / death defiance).
        /// Intended to be called from an <see cref="DamagePipeline.OnLethalHit"/> subscriber.
        /// Scales every damage type proportionally, sets <see cref="DamageFlags.DeathDefied"/> and clears <see cref="IsLethal"/>.
        /// </summary>
        /// <returns>True if damage was clamped; false if the hit was not lethal or there is no Character target.</returns>
        public bool DefyDeath(string source = "Death Defiance")
        {
            if (Target == null || IsCancelled || !IsLethal) return false;

            float total = GetTotalDamage();
            float allowed = Mathf.Max(0f, Target.GetHealth() - 1f);
            if (total <= allowed || total <= 0f) return false;

            float factor = allowed / total;
            foreach (var key in new List<DamageType>(_damages.Keys))
            {
                _damages[key] *= factor;
            }

            Flags |= DamageFlags.DeathDefied;
            IsLethal = false;
            Trace?.RecordStep(CurrentPhase, source, $"Lethal hit clamped: {total:F1} -> {allowed:F1} damage (target survives at 1 HP)", factor);
            return true;
        }

        public float GetDamage(DamageType type) => _damages.TryGetValue(type, out float val) ? val : 0f;

        public void SetDamage(DamageType type, float value)
        {
            _damages[type] = Mathf.Max(0f, value);
        }

        public float GetTotalDamage()
        {
            float total = 0f;
            foreach (var kvp in _damages) total += kvp.Value;
            return total;
        }

        public float GetTotalPhysicalDamage() =>
            GetDamage(DamageType.Blunt) + GetDamage(DamageType.Slash) + GetDamage(DamageType.Pierce);

        public float GetTotalElementalDamage() =>
            GetDamage(DamageType.Fire) + GetDamage(DamageType.Frost) + GetDamage(DamageType.Lightning);

        /// <summary>
        /// Absorbs a finite amount of damage across all active damage types proportionally.
        /// Records the absorption in combat telemetry.
        /// </summary>
        /// <param name="maxAmount">Maximum amount of damage to absorb.</param>
        /// <param name="source">Label of the shield/barrier absorption source.</param>
        /// <returns>Actual amount of damage absorbed.</returns>
        public float AbsorbDamage(float maxAmount, string source)
        {
            if (maxAmount <= 0.001f || IsCancelled) return 0f;
            float currentTotal = GetTotalDamage();
            if (currentTotal <= 0.001f) return 0f;

            float toAbsorb = Mathf.Min(maxAmount, currentTotal);
            float remainingRatio = (currentTotal - toAbsorb) / currentTotal;

            foreach (var key in new List<DamageType>(_damages.Keys))
            {
                _damages[key] *= remainingRatio;
            }

            Trace?.RecordStep(CurrentPhase, source, $"Shield absorbed {toAbsorb:F1} damage ({currentTotal:F1} -> {GetTotalDamage():F1})", toAbsorb);
            return toAbsorb;
        }

        /// <summary>
        /// Adds a flat damage bonus (Tier 1).
        /// </summary>
        public void AddFlat(float amount, string source, DamageType? type = null)
        {
            if (amount == 0f) return;
            _phaseModifiers.Add(new DamageModifierRecord(ModifierTier.Flat, amount, source, type));
        }

        /// <summary>
        /// Adds an additive percentage modifier (Tier 2, e.g. +0.15f for +15%).
        /// </summary>
        public void AddIncreasedPercent(float percent, string source, DamageType? type = null)
        {
            if (percent == 0f) return;
            _phaseModifiers.Add(new DamageModifierRecord(ModifierTier.IncreasedPercent, percent, source, type));
        }

        /// <summary>
        /// Deductively converts a fraction of damage from one damage type to another.
        /// Deducts from the source type and adds to the destination type, recording the operation in telemetry.
        /// </summary>
        /// <param name="fromType">Source damage type to convert from.</param>
        /// <param name="toType">Destination damage type to convert to.</param>
        /// <param name="ratio">Fraction [0.0 .. 1.0] to convert (e.g. 0.50f for 50%).</param>
        /// <param name="source">Attribution label for telemetry audits.</param>
        public void ConvertDamage(DamageType fromType, DamageType toType, float ratio, string source)
        {
            if (ratio <= 0f || IsCancelled) return;
            ratio = Mathf.Clamp01(ratio);
            float sourceDamage = GetDamage(fromType);
            if (sourceDamage <= 0.001f) return;

            float converted = sourceDamage * ratio;
            SetDamage(fromType, sourceDamage - converted);
            SetDamage(toType, GetDamage(toType) + converted);
            Trace?.RecordStep(CurrentPhase, source, $"Converted {ratio * 100:F0}% ({converted:F1}) {fromType} -> {toType}", converted);
        }

        /// <summary>
        /// Leaves the source damage type untouched while granting an additional percentage of its damage as a different damage type.
        /// Commonly used for "gain X% of Physical as Fire" mechanics.
        /// </summary>
        /// <param name="sourceBase">Source damage type to base the bonus on.</param>
        /// <param name="targetType">Destination damage type to add.</param>
        /// <param name="ratio">Percentage factor to grant (e.g. 0.15f for +15%).</param>
        /// <param name="source">Attribution label for telemetry audits.</param>
        public void AddExtraDamageFrom(DamageType sourceBase, DamageType targetType, float ratio, string source)
        {
            if (ratio <= 0f || IsCancelled) return;
            float baseAmount = GetDamage(sourceBase);
            if (baseAmount <= 0.001f) return;

            float extra = baseAmount * ratio;
            AddFlat(extra, source, targetType);
            Trace?.RecordStep(CurrentPhase, source, $"Added {ratio * 100:F0}% ({extra:F1}) of {sourceBase} as {targetType}", extra);
        }

        /// <summary>
        /// Leaves physical damage untouched while granting an additional percentage of total physical damage (Blunt + Slash + Pierce)
        /// as a secondary damage type (e.g. Fire or Poison).
        /// </summary>
        /// <param name="targetType">Destination damage type to add.</param>
        /// <param name="ratio">Percentage factor to grant (e.g. 0.15f for +15%).</param>
        /// <param name="source">Attribution label for telemetry audits.</param>
        public void AddExtraDamageFromPhysical(DamageType targetType, float ratio, string source)
        {
            if (ratio <= 0f || IsCancelled) return;
            float physTotal = GetTotalPhysicalDamage();
            if (physTotal <= 0.001f) return;

            float extra = physTotal * ratio;
            AddFlat(extra, source, targetType);
            Trace?.RecordStep(CurrentPhase, source, $"Added {ratio * 100:F0}% ({extra:F1}) of total Physical as {targetType}", extra);
        }

        /// <summary>
        /// Adds an independent compounding multiplier (Tier 3, e.g. 1.5f for 1.5x).
        /// </summary>
        public void AddMultiplier(float factor, string source, DamageType? type = null)
        {
            if (factor == 1f) return;
            _phaseModifiers.Add(new DamageModifierRecord(ModifierTier.Multiplier, factor, source, type));
        }

        /// <summary>
        /// Evaluates all accumulated 3-tier modifiers for the current phase in one mathematically sound pass:
        /// Damage = (Base + Sum(Flat)) * (1 + Sum(Increased%)) * Product(Multipliers).
        /// </summary>
        public void ResolvePhaseModifiers()
        {
            if (_phaseModifiers.Count == 0 || IsCancelled) return;

            foreach (var type in new List<DamageType>(_damages.Keys))
            {
                float baseVal = _damages[type];
                if (baseVal <= 0f && !HasFlatAdditionForType(type)) continue;

                float flatSum = 0f;
                float increasedSum = 0f;
                float multiplierProduct = 1f;

                var flatDetails = new List<string>();
                var incDetails = new List<string>();
                var multDetails = new List<string>();

                foreach (var mod in _phaseModifiers)
                {
                    if (mod.Type.HasValue && mod.Type.Value != type) continue;

                    switch (mod.Tier)
                    {
                        case ModifierTier.Flat:
                            flatSum += mod.Value;
                            flatDetails.Add($"+{mod.Value:F1} ('{mod.Source}')");
                            break;
                        case ModifierTier.IncreasedPercent:
                            increasedSum += mod.Value;
                            incDetails.Add($"+{mod.Value * 100:F0}% ('{mod.Source}')");
                            break;
                        case ModifierTier.Multiplier:
                            multiplierProduct *= mod.Value;
                            multDetails.Add($"x{mod.Value:F2} ('{mod.Source}')");
                            break;
                    }
                }

                float calculated = (baseVal + flatSum) * Mathf.Max(0f, 1f + increasedSum) * multiplierProduct;
                calculated = Mathf.Max(0f, calculated);
                _damages[type] = calculated;

                if (Trace != null && (flatDetails.Count > 0 || incDetails.Count > 0 || multDetails.Count > 0))
                {
                    var rec = new TierRecord
                    {
                        Type = type,
                        BaseValue = baseVal,
                        ResultingValue = calculated,
                        FlatSum = flatSum,
                        IncreasedSum = increasedSum,
                        MultiplierProduct = multiplierProduct
                    };
                    rec.FlatSources.AddRange(flatDetails);
                    rec.IncreasedSources.AddRange(incDetails);
                    rec.MultiplierSources.AddRange(multDetails);

                    Trace.RecordTierModifiers(rec);
                }
            }

            _phaseModifiers.Clear();
        }

        private readonly Dictionary<DamageType, MitigationRecord> _mitigationTracker = new();

        private MitigationRecord GetOrCreateMitigationRecord(DamageType type, float incoming)
        {
            if (!_mitigationTracker.TryGetValue(type, out var rec))
            {
                rec = new MitigationRecord
                {
                    Type = type,
                    IncomingDamage = incoming,
                    MitigatedDamage = incoming
                };
                _mitigationTracker[type] = rec;
            }
            return rec;
        }

        private bool HasFlatAdditionForType(DamageType type)
        {
            foreach (var mod in _phaseModifiers)
            {
                if (mod.Tier == ModifierTier.Flat && (!mod.Type.HasValue || mod.Type.Value == type) && mod.Value > 0f)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Applies armor mitigation to a specific physical damage type using effective armor and bonus efficiency.
        /// Supports dual calculation models: LinearEHP (asymptotic efficiency) and VanillaPiecewise (native threshold).
        /// </summary>
        public void MitigateWithArmor(DamageType type, float armor, float bonusEfficiency = 0f)
        {
            if (IsCancelled || armor <= 0f) return;
            if (Flags.HasFlag(DamageFlags.IgnoresArmor) || Flags.HasFlag(DamageFlags.TrueDamage)) return;

            float current = GetDamage(type);
            if (current <= 0.01f) return;

            // Apply armor penetration
            float effectiveArmor = armor * Mathf.Clamp01(1f - ArmorPenetrationPercent) - ArmorPenetrationFlat;
            effectiveArmor = Mathf.Max(0f, effectiveArmor);

            var activeModel = ModConfig.ArmorModel.Value;
            float k = Mathf.Max(1.0f, ModConfig.ArmorScaleConstant.Value);
            float totalEfficiency = 0f;
            float scaledArmor = 0f;
            bool isQuadratic = false;
            float mitigated;

            if (effectiveArmor <= 0.001f)
            {
                mitigated = current;
            }
            else if (activeModel == ArmorMitigationModel.LinearEHP)
            {
                float baseEfficiency = effectiveArmor / k;
                totalEfficiency = baseEfficiency + bonusEfficiency;
                float denom = Mathf.Max(0.01f, 1.0f + totalEfficiency);
                mitigated = current / denom;
            }
            else
            {
                // VanillaPiecewise mode: scale effective armor by bonus efficiency
                scaledArmor = effectiveArmor * Mathf.Max(0f, 1.0f + bonusEfficiency);
                if (scaledArmor <= 0.001f)
                {
                    mitigated = current;
                }
                else if (current < 2f * scaledArmor)
                {
                    isQuadratic = true;
                    mitigated = (current * current) / (4f * scaledArmor);
                }
                else
                {
                    isQuadratic = false;
                    mitigated = current - scaledArmor;
                }
            }
            mitigated = Mathf.Max(0f, mitigated);

            SetDamage(type, mitigated);

            if (Trace != null)
            {
                var rec = GetOrCreateMitigationRecord(type, current);
                rec.HasArmorMitigation = true;
                rec.ArmorModel = activeModel;
                rec.ArmorValue = armor;
                rec.EffectiveArmor = effectiveArmor;
                rec.ArmorEfficiency = bonusEfficiency;
                rec.ArmorPenetrationPercent = ArmorPenetrationPercent;
                rec.ArmorPenetrationFlat = ArmorPenetrationFlat;
                rec.ArmorScaleConstant = k;
                rec.LinearEhpTotalEfficiency = totalEfficiency;
                rec.ScaledPiecewiseArmor = scaledArmor;
                rec.IsPiecewiseQuadratic = isQuadratic;
                rec.ArmorReduction += (current - mitigated);
                rec.MitigatedDamage = mitigated;
            }
        }

        /// <summary>
        /// Applies a continuous float resistance multiplier to a specific damage type.
        /// </summary>
        public void ApplyResistance(DamageType type, float multiplier)
        {
            if (IsCancelled || type == DamageType.TrueDamage || Flags.HasFlag(DamageFlags.TrueDamage)) return;

            float current = GetDamage(type);
            if (current <= 0.01f) return;

            float final = current * Mathf.Max(0f, multiplier);
            SetDamage(type, final);

            if (Trace != null)
            {
                var rec = GetOrCreateMitigationRecord(type, current);
                rec.ResistanceMultiplier = multiplier;
                rec.ResistanceLabel = EntityDefenseRegistry.ToVanillaModifier(multiplier).ToString();
                rec.MitigatedDamage = final;
            }
        }

        /// <summary>
        /// Flushes any recorded mitigations to the trace.
        /// </summary>
        public void FlushMitigationTrace()
        {
            if (Trace == null) return;
            foreach (var kvp in _mitigationTracker)
            {
                if (kvp.Value.HasMitigation)
                {
                    Trace.RecordMitigation(kvp.Value);
                }
            }
            _mitigationTracker.Clear();
        }

        /// <summary>
        /// Writes calculated damage totals back into the native HitData structure.
        /// </summary>
        public void WriteBackToHitData()
        {
            Hit.m_damage.m_blunt = GetDamage(DamageType.Blunt);
            Hit.m_damage.m_slash = GetDamage(DamageType.Slash);
            Hit.m_damage.m_pierce = GetDamage(DamageType.Pierce);
            Hit.m_damage.m_chop = GetDamage(DamageType.Chop);
            Hit.m_damage.m_pickaxe = GetDamage(DamageType.Pickaxe);
            Hit.m_damage.m_fire = GetDamage(DamageType.Fire);
            Hit.m_damage.m_frost = GetDamage(DamageType.Frost);
            Hit.m_damage.m_lightning = GetDamage(DamageType.Lightning);
            Hit.m_damage.m_poison = GetDamage(DamageType.Poison);
            Hit.m_damage.m_spirit = GetDamage(DamageType.Spirit);

            // TrueDamage is converted to pure nonPlayer / split damage so vanilla health deduction can apply it
            float trueDmg = GetDamage(DamageType.TrueDamage);
            if (trueDmg > 0.01f)
            {
                // Add to slash/blunt or distribute into total
                Hit.m_damage.m_blunt += trueDmg;
            }

            if (Trace != null)
            {
                Trace.FinalDamage = Hit.m_damage.Clone();
            }
        }
    }
}
