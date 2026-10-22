using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using VikingLib.Combat;
using VikingLib.Stats;

namespace VikingLib.StatusEffects
{
    /// <summary>
    /// Base status effect class for VikingLib.
    /// Manages declarative 3-tier modifiers, multi-slice architectures with independent timers and continuous values,
    /// dynamic duration/intensity stacking, charge consumption, and shortest-duration-first damage absorption shields.
    /// </summary>
    public class VikingStatusEffect : StatusEffect
    {
        [SerializeField]
        private string _definitionName = string.Empty;

        private VikingStatusEffectDefinition? _definition;
        private float _dotTimer;
        private List<StatusEffectSlice>? _slices;

        private List<StatusEffectSlice> EnsureSlices()
        {
            if (_slices == null) _slices = new List<StatusEffectSlice>();
            return _slices;
        }

        /// <summary>
        /// Clears all active slices from this status effect instance.
        /// </summary>
        public void ClearSlices()
        {
            EnsureSlices().Clear();
            RecalculateSliceAggregation();
            ReapplyModifiers();
        }

        /// <summary>
        /// The blueprint definition governing this status effect.
        /// </summary>
        public VikingStatusEffectDefinition Definition
        {
            get
            {
                if (_definition == null && !string.IsNullOrEmpty(_definitionName))
                {
                    _definition = StatusEffectRegistry.GetDefinition(_definitionName);
                }
                return _definition ?? throw new InvalidOperationException($"VikingStatusEffect '{name}' has no definition associated.");
            }
            internal set
            {
                _definition = value;
                _definitionName = value.Name;
            }
        }

        /// <summary>
        /// Active slices belonging to this status effect instance.
        /// </summary>
        public IReadOnlyList<StatusEffectSlice> Slices => EnsureSlices();

        /// <summary>
        /// Number of currently active slices.
        /// </summary>
        public int ActiveSliceCount => EnsureSlices().Count;

        /// <summary>
        /// Total aggregated slice value according to the definition's <see cref="SliceAggregationMode"/>.
        /// </summary>
        public float TotalSliceValue { get; private set; }

        /// <summary>
        /// Current stack count for single-instance stacking effects (clamped to MaxStacks).
        /// </summary>
        public int CurrentStacks { get; private set; } = 1;

        /// <summary>
        /// Current remaining charges for charge-based effects.
        /// </summary>
        public int CurrentCharges { get; private set; }

        /// <summary>
        /// Current remaining damage absorption shield capacity.
        /// </summary>
        public float CurrentShieldCapacity { get; private set; }

        public void Initialize(VikingStatusEffectDefinition definition)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            try { name = definition.Name; } catch { /* In offline testing, native UnityEngine.Object.name may throw */ }
            m_name = string.IsNullOrEmpty(definition.DisplayName) ? definition.Name : definition.DisplayName;
            m_tooltip = definition.Tooltip;
            if (definition.Icon == null)
            {
                definition.Icon = StatusEffectIconFactory.GetIconForDefinition(definition.Name);
            }
            m_icon = definition.Icon;
            m_category = definition.Category;
            m_hidden = definition.Hidden;
            m_ttl = definition.BaseDuration;
            m_startEffects = definition.StartEffects ?? new EffectList();
            m_stopEffects = definition.StopEffects ?? new EffectList();
            CurrentStacks = 1;
            CurrentCharges = definition.InitialCharges;
            CurrentShieldCapacity = definition.InitialShieldCapacity;
            EnsureSlices().Clear();
            TotalSliceValue = 0f;
        }

        public override void Setup(Character character)
        {
            if (m_startEffects == null) m_startEffects = new EffectList();
            if (m_stopEffects == null) m_stopEffects = new EffectList();

            try
            {
                base.Setup(character);
            }
            catch (Exception ex)
            {
                VikingLibPlugin.Log?.LogWarning($"[VikingLib] Suppressed exception during base.Setup() on status effect '{name}': {ex.Message}");
            }

            // Re-resolve definition if cloned by SEMan
            if (_definition == null && !string.IsNullOrEmpty(_definitionName))
            {
                _definition = StatusEffectRegistry.GetDefinition(_definitionName);
            }

            if (m_icon == null && _definition?.Icon != null)
            {
                m_icon = _definition.Icon;
            }
            if (m_icon == null && _definition != null)
            {
                m_icon = StatusEffectIconFactory.GetIconForDefinition(_definition.Name);
            }

            // Bloodless creature immunity check for Bleed
            if (Definition.Name == CanonicalStatusEffects.BleedId && !Combat.Defense.EntityDefenseRegistry.CanBleed(character))
            {
                m_time = m_ttl > 0f ? m_ttl + 1f : 1f;
                return;
            }

            _dotTimer = 0f;
            EnsureSlices().Clear();

            if (Definition.IsMultiSlice)
            {
                float initialVal = Definition.InitialShieldCapacity > 0f ? Definition.InitialShieldCapacity : Definition.DefaultSliceValue;
                float dur = Definition.DefaultSliceDuration > 0f ? Definition.DefaultSliceDuration : Definition.BaseDuration;
                if (initialVal > 0f || dur > 0f)
                {
                    AddSlice(initialVal, dur);
                }
            }
            else
            {
                CurrentStacks = 1;
                CurrentCharges = Definition.InitialCharges;
                CurrentShieldCapacity = Definition.InitialShieldCapacity;
            }

            if (m_character != null)
            {
                RegisterModifiers();
            }
        }

        public override void Stop()
        {
            if (m_startEffects == null) m_startEffects = new EffectList();
            if (m_stopEffects == null) m_stopEffects = new EffectList();

            try
            {
                base.Stop();
            }
            catch (Exception ex)
            {
                VikingLibPlugin.Log?.LogWarning($"[VikingLib] Suppressed exception during base.Stop() on status effect '{name}': {ex.Message}");
            }

            if (m_character != null)
            {
                CharacterStatManager.RemoveModifiersByOwner(m_character, this);
            }
        }

        public override void ResetTime()
        {
            var def = Definition;

            if (def.IsMultiSlice)
            {
                float val = def.InitialShieldCapacity > 0f
                    ? (def.ShieldCapacityPerStack > 0f ? def.ShieldCapacityPerStack : def.InitialShieldCapacity)
                    : def.DefaultSliceValue;
                float dur = def.DefaultSliceDuration > 0f ? def.DefaultSliceDuration : def.BaseDuration;
                AddSlice(val, dur);
                return;
            }

            switch (def.StackPolicy)
            {
                case StackPolicy.RefreshDurationOnly:
                    m_time = 0f;
                    break;

                case StackPolicy.IntensityOnly:
                    if (CurrentStacks < def.MaxStacks)
                    {
                        CurrentStacks++;
                        if (def.ShieldCapacityPerStack > 0f)
                        {
                            CurrentShieldCapacity += def.ShieldCapacityPerStack;
                        }
                        ReapplyModifiers();
                    }
                    break;

                case StackPolicy.IntensityAndRefresh:
                    m_time = 0f;
                    if (CurrentStacks < def.MaxStacks)
                    {
                        CurrentStacks++;
                        if (def.ShieldCapacityPerStack > 0f)
                        {
                            CurrentShieldCapacity += def.ShieldCapacityPerStack;
                        }
                        ReapplyModifiers();
                    }
                    break;

                case StackPolicy.IgnoreIfActive:
                    break;
            }

            if (def.RefreshChargesOnReapply && def.InitialCharges > 0)
            {
                CurrentCharges = def.InitialCharges;
            }
        }

        /// <summary>
        /// Adds an independent slice to this status effect instance with its own duration, value, and tick timer.
        /// </summary>
        public StatusEffectSlice AddSlice(float value, float duration = -1f, Character? source = null, object? customData = null)
        {
            var def = Definition;
            var slices = EnsureSlices();
            if (duration < 0f)
            {
                duration = def.DefaultSliceDuration > 0f ? def.DefaultSliceDuration : def.BaseDuration;
            }

            if (def.MaxSlices > 0 && slices.Count >= def.MaxSlices)
            {
                slices.RemoveAt(0);
            }

            float tickInterval = def.SliceTickInterval;
            var slice = new StatusEffectSlice(value, duration, tickInterval, source, customData);
            slices.Add(slice);

            RecalculateSliceAggregation();
            ReapplyModifiers();
            return slice;
        }

        private void RecalculateSliceAggregation()
        {
            if (!Definition.IsMultiSlice)
            {
                TotalSliceValue = 0f;
                return;
            }

            var slices = EnsureSlices();
            switch (Definition.SliceAggregation)
            {
                case SliceAggregationMode.Sum:
                    float sum = 0f;
                    for (int i = 0; i < slices.Count; i++) sum += slices[i].Value;
                    TotalSliceValue = sum;
                    break;

                case SliceAggregationMode.Max:
                    float max = 0f;
                    for (int i = 0; i < slices.Count; i++)
                    {
                        if (slices[i].Value > max) max = slices[i].Value;
                    }
                    TotalSliceValue = max;
                    break;

                case SliceAggregationMode.Count:
                    TotalSliceValue = slices.Count;
                    break;

                default:
                    TotalSliceValue = 0f;
                    break;
            }

            if (Definition.InitialShieldCapacity > 0f)
            {
                CurrentShieldCapacity = TotalSliceValue;
            }
        }

        public override void UpdateStatusEffect(float dt)
        {
            base.UpdateStatusEffect(dt);

            if (m_character == null || m_character.IsDead()) return;

            var def = Definition;

            if (def.IsMultiSlice)
            {
                var slices = EnsureSlices();
                bool anyChanged = false;
                for (int i = slices.Count - 1; i >= 0; i--)
                {
                    if (i >= slices.Count) continue;
                    var slice = slices[i];

                    if (slice.Tick(dt, out bool shouldTickDamage, out float tickDamage))
                    {
                        anyChanged = true;
                        if (shouldTickDamage && tickDamage > 0f)
                        {
                            ApplySlicePeriodicTick(slice, tickDamage);
                        }
                    }

                    if (slice.IsExpired || (slice.TickInterval > 0f && slice.Value <= 0.001f))
                    {
                        slices.RemoveAt(i);
                        anyChanged = true;
                    }
                }

                if (anyChanged)
                {
                    RecalculateSliceAggregation();
                    ReapplyModifiers();
                }

                if (slices.Count == 0)
                {
                    m_time = m_ttl > 0f ? m_ttl + 1f : 1f;
                }
            }
            else
            {
                if (def.PeriodicDamageType.HasValue && def.PeriodicDamageAmount > 0f)
                {
                    _dotTimer += dt;
                    if (_dotTimer >= def.PeriodicDamageInterval)
                    {
                        _dotTimer -= def.PeriodicDamageInterval;
                        ApplyPeriodicTick();
                    }
                }
            }
        }

        private void ApplySlicePeriodicTick(StatusEffectSlice slice, float tickDamage)
        {
            if (m_character == null || m_character.IsDead()) return;

            var hit = new HitData
            {
                m_point = m_character.GetCenterPoint(),
                m_dir = Vector3.up,
                m_hitType = HitData.HitType.Undefined,
                m_attacker = slice.Source != null ? slice.Source.GetZDOID() : ZDOID.None
            };

            switch (Definition.PeriodicDamageType ?? DamageType.Slash)
            {
                case DamageType.Blunt: hit.m_damage.m_blunt = tickDamage; break;
                case DamageType.Slash: hit.m_damage.m_slash = tickDamage; break;
                case DamageType.Pierce: hit.m_damage.m_pierce = tickDamage; break;
                case DamageType.Fire: hit.m_damage.m_fire = tickDamage; break;
                case DamageType.Frost: hit.m_damage.m_frost = tickDamage; break;
                case DamageType.Lightning: hit.m_damage.m_lightning = tickDamage; break;
                case DamageType.Poison: hit.m_damage.m_poison = tickDamage; break;
                case DamageType.Spirit: hit.m_damage.m_spirit = tickDamage; break;
                default: hit.m_damage.m_slash = tickDamage; break;
            }

            if (Definition.PeriodicIgnoresArmor)
            {
                m_character.ApplyDamage(hit, showDamageText: true, triggerEffects: true, HitData.DamageModifier.Ignore);
            }
            else
            {
                m_character.Damage(hit);
            }
        }

        private void ApplyPeriodicTick()
        {
            if (m_character == null || m_character.IsDead()) return;

            var def = Definition;
            float totalTickDamage = def.PeriodicDamageAmount * (def.ModifiersScaleWithStacks ? CurrentStacks : 1f);

            var hit = new HitData
            {
                m_point = m_character.GetCenterPoint(),
                m_dir = Vector3.up,
                m_hitType = HitData.HitType.Undefined,
                m_attacker = ZDOID.None
            };

            switch (def.PeriodicDamageType!.Value)
            {
                case DamageType.Blunt: hit.m_damage.m_blunt = totalTickDamage; break;
                case DamageType.Slash: hit.m_damage.m_slash = totalTickDamage; break;
                case DamageType.Pierce: hit.m_damage.m_pierce = totalTickDamage; break;
                case DamageType.Fire: hit.m_damage.m_fire = totalTickDamage; break;
                case DamageType.Frost: hit.m_damage.m_frost = totalTickDamage; break;
                case DamageType.Lightning: hit.m_damage.m_lightning = totalTickDamage; break;
                case DamageType.Poison: hit.m_damage.m_poison = totalTickDamage; break;
                case DamageType.Spirit: hit.m_damage.m_spirit = totalTickDamage; break;
                default: hit.m_damage.m_blunt = totalTickDamage; break;
            }

            if (def.PeriodicIgnoresArmor)
            {
                m_character.ApplyDamage(hit, showDamageText: true, triggerEffects: true, HitData.DamageModifier.Ignore);
            }
            else
            {
                m_character.Damage(hit);
            }
        }

        public override bool IsDone()
        {
            if (Definition.IsMultiSlice)
            {
                return EnsureSlices().Count == 0;
            }

            if (m_ttl > 0f && m_time >= m_ttl)
            {
                return true;
            }

            if (Definition.InitialCharges > 0 && CurrentCharges <= 0)
            {
                return true;
            }

            if (Definition.InitialShieldCapacity > 0f && CurrentShieldCapacity <= 0.001f && m_ttl <= 0f)
            {
                return true;
            }

            return false;
        }

        public override string GetIconText()
        {
            if (Definition.CustomIconTextFormatter != null)
            {
                return Definition.CustomIconTextFormatter(this);
            }

            if (Definition.IsMultiSlice)
            {
                switch (Definition.SliceIconFormat)
                {
                    case SliceIconFormat.PositivePercent:
                        return $"+{Mathf.RoundToInt(TotalSliceValue * 100)}%";

                    case SliceIconFormat.NegativePercent:
                        return $"-{Mathf.RoundToInt(TotalSliceValue * 100)}%";

                    case SliceIconFormat.IntegerCeiling:
                        return Mathf.CeilToInt(TotalSliceValue).ToString();

                    case SliceIconFormat.SingleDecimal:
                        return TotalSliceValue.ToString("F1");

                    default:
                        var slices = EnsureSlices();
                        return slices.Count > 1 ? $"x{slices.Count}" : base.GetIconText();
                }
            }

            if (CurrentStacks > 1)
            {
                return $"x{CurrentStacks}";
            }

            if (Definition.InitialCharges > 0)
            {
                return CurrentCharges.ToString();
            }

            if (Definition.InitialShieldCapacity > 0f)
            {
                return Mathf.CeilToInt(CurrentShieldCapacity).ToString();
            }

            return base.GetIconText();
        }

        public override string GetTooltipString()
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(m_tooltip))
            {
                sb.AppendLine(m_tooltip);
            }

            if (Definition.IsMultiSlice)
            {
                var slices = EnsureSlices();
                sb.AppendLine($"<color=#FFCC00>Active Slices:</color> {slices.Count}" + (Definition.MaxSlices > 0 ? $"/{Definition.MaxSlices}" : ""));
                if (Definition.InitialShieldCapacity > 0f)
                {
                    sb.AppendLine($"<color=#FFCC00>Barrier:</color> {Mathf.CeilToInt(TotalSliceValue)} HP");
                }
                else if (Definition.PeriodicDamageType.HasValue)
                {
                    sb.AppendLine($"<color=#FFCC00>Remaining Damage:</color> {TotalSliceValue:F1}");
                }
                else if (TotalSliceValue > 0f)
                {
                    sb.AppendLine($"<color=#FFCC00>Intensity:</color> {TotalSliceValue * 100:F0}%");
                }
            }
            else
            {
                if (Definition.MaxStacks > 1)
                {
                    sb.AppendLine($"<color=#FFCC00>Stacks:</color> {CurrentStacks}/{Definition.MaxStacks}");
                }

                if (Definition.InitialCharges > 0)
                {
                    sb.AppendLine($"<color=#FFCC00>Charges:</color> {CurrentCharges}/{Definition.InitialCharges}");
                }

                if (Definition.InitialShieldCapacity > 0f)
                {
                    sb.AppendLine($"<color=#FFCC00>Barrier:</color> {Mathf.CeilToInt(CurrentShieldCapacity)}/{Mathf.CeilToInt(Definition.InitialShieldCapacity + (CurrentStacks - 1) * Definition.ShieldCapacityPerStack)}");
                }

                if (m_ttl > 0f)
                {
                    float remaining = Mathf.Max(0f, m_ttl - m_time);
                    sb.AppendLine($"<color=#FFCC00>Duration:</color> {remaining:F1}s");
                }
            }

            var mods = BuildModifiers();
            if (mods.Count > 0)
            {
                sb.AppendLine("\n<color=orange><b>[ Modifiers ]</b></color>");
                foreach (var mod in mods)
                {
                    sb.AppendLine($"• {mod}");
                }
            }

            return sb.ToString().TrimEnd();
        }

        /// <summary>
        /// Attempts to consume charges or slices matching the given trigger.
        /// If all charges/slices are consumed, removes this status effect from the character.
        /// </summary>
        public bool TryConsumeCharge(ChargeConsumeTrigger trigger, int count = 1)
        {
            if ((Definition.ChargeConsumptionTrigger & trigger) == 0)
            {
                return false;
            }

            if (Definition.IsMultiSlice)
            {
                var slices = EnsureSlices();
                if (slices.Count == 0) return false;

                if (Definition.ConsumeAllChargesOnTrigger)
                {
                    slices.Clear();
                    RecalculateSliceAggregation();
                    ReapplyModifiers();
                    m_time = m_ttl > 0f ? m_ttl + 1f : 1f;
                    if (m_character != null)
                    {
                        m_character.GetSEMan()?.RemoveStatusEffect(this);
                    }
                    return true;
                }
                else
                {
                    int toRemove = Mathf.Min(count, slices.Count);
                    slices.RemoveRange(0, toRemove);
                    RecalculateSliceAggregation();
                    ReapplyModifiers();
                    if (slices.Count == 0)
                    {
                        m_time = m_ttl > 0f ? m_ttl + 1f : 1f;
                        if (m_character != null)
                        {
                            m_character.GetSEMan()?.RemoveStatusEffect(this);
                        }
                    }
                    return true;
                }
            }

            if (CurrentCharges <= 0)
            {
                return false;
            }

            CurrentCharges = Mathf.Max(0, CurrentCharges - count);
            if (CurrentCharges <= 0)
            {
                m_time = m_ttl > 0f ? m_ttl + 1f : 1f;
                if (m_character != null)
                {
                    m_character.GetSEMan()?.RemoveStatusEffect(this);
                }
            }
            return true;
        }

        /// <summary>
        /// Absorbs incoming damage up to the remaining barrier capacity.
        /// In multi-slice mode, consumes the slice with the shortest remaining duration first.
        /// </summary>
        public float AbsorbDamage(float incomingDamage)
        {
            if (incomingDamage <= 0f) return 0f;

            if (Definition.IsMultiSlice)
            {
                if (TotalSliceValue <= 0.001f) return 0f;

                var slices = EnsureSlices();
                // Deplete slice with the shortest remaining duration first (earliest deadline first)
                var sorted = slices.OrderBy(s => s.HasDuration ? s.DurationRemaining : float.MaxValue).ToList();

                float remainingToAbsorb = incomingDamage;
                float totalAbsorbed = 0f;

                for (int i = 0; i < sorted.Count; i++)
                {
                    var slice = sorted[i];
                    float absorbed = slice.Deduct(remainingToAbsorb);
                    totalAbsorbed += absorbed;
                    remainingToAbsorb -= absorbed;

                    if (remainingToAbsorb <= 0.001f) break;
                }

                slices.RemoveAll(s => s.Value <= 0.001f);
                RecalculateSliceAggregation();
                ReapplyModifiers();

                if (slices.Count == 0)
                {
                    m_time = m_ttl > 0f ? m_ttl + 1f : 1f;
                    if (m_character != null)
                    {
                        m_character.GetSEMan()?.RemoveStatusEffect(this);
                    }
                }

                return totalAbsorbed;
            }

            if (CurrentShieldCapacity <= 0.001f) return 0f;

            float singleAbsorbed = Mathf.Min(CurrentShieldCapacity, incomingDamage);
            CurrentShieldCapacity -= singleAbsorbed;

            if (CurrentShieldCapacity <= 0.001f)
            {
                m_time = m_ttl > 0f ? m_ttl + 1f : 1f;
                if (m_character != null && m_ttl <= 0f)
                {
                    m_character.GetSEMan()?.RemoveStatusEffect(this);
                }
            }

            return singleAbsorbed;
        }

        private void RegisterModifiers()
        {
            if (m_character == null) return;
            var mods = BuildModifiers();
            foreach (var mod in mods)
            {
                CharacterStatManager.AddModifier(m_character, mod);
            }
        }

        private void ReapplyModifiers()
        {
            if (m_character == null) return;
            var mods = BuildModifiers();
            CharacterStatManager.ReplaceModifiersByOwner(m_character, this, mods);
        }

        private List<StatModifier> BuildModifiers()
        {
            var result = new List<StatModifier>();
            var def = Definition;
            string effectLabel = !string.IsNullOrEmpty(m_name) ? m_name : def.Name;

            for (int i = 0; i < def.Modifiers.Count; i++)
            {
                var bp = def.Modifiers[i];
                float value;

                if (def.IsMultiSlice)
                {
                    if (bp.SliceValueTransformer != null)
                    {
                        value = bp.SliceValueTransformer(TotalSliceValue);
                    }
                    else
                    {
                        value = bp.ValuePerStack * TotalSliceValue;
                    }
                }
                else
                {
                    value = def.ModifiersScaleWithStacks ? bp.ValuePerStack * CurrentStacks : bp.ValuePerStack;
                }

                string source = string.IsNullOrEmpty(bp.Source) ? effectLabel : bp.Source;

                if (!string.IsNullOrEmpty(bp.CustomStatKey))
                {
                    result.Add(new StatModifier(bp.CustomStatKey, bp.Tier, value, source, bp.Condition, this));
                }
                else if (bp.StatType.HasValue)
                {
                    result.Add(new StatModifier(bp.StatType.Value, bp.Tier, value, source, bp.Condition, this));
                }
            }

            return result;
        }
    }
}
