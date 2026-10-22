using System;
using System.Collections.Generic;
using System.Text;
using VikingLib.Combat.Roll;
using VikingLib.Configuration;

namespace VikingLib.Combat.Telemetry
{
    /// <summary>
    /// Encapsulates the complete diagnostic telemetry and transformation history of a single combat hit.
    /// Supports multiple customizable formatting styles (Tree, Compact, Audit).
    /// </summary>
    public class DamageTrace
    {
        private static long _hitCounter;

        public ulong HitId { get; }
        public DateTime Timestamp { get; }
        public string AttackerName { get; }
        public string TargetName { get; }
        public HitData.DamageTypes InitialDamage { get; }
        public HitData.DamageTypes FinalDamage { get; set; }

        // Weapon Origin Metadata
        public string? WeaponName { get; set; }
        public string? AttackStepKey { get; set; }
        public string? AttackDisplayName { get; set; }
        public HitData.DamageTypes? WeaponBaseDamage { get; set; }
        public Skills.SkillType SkillType { get; set; }
        public float SkillLevel { get; set; }
        public float SkillRollFactor { get; set; } = 1.0f;
        public float RollLowerBound { get; set; }
        public float RollUpperBound { get; set; }
        public List<RollStepRecord> RollSteps { get; } = new();

        // Structured step records
        public List<TierRecord> AttackerTierRecords { get; } = new();
        public List<MitigationRecord> MitigationRecords { get; } = new();
        public List<TraceStep> GenericSteps { get; } = new();

        public DamageTrace(string attackerName, string targetName, HitData.DamageTypes initialDamage)
        {
            HitId = (ulong)System.Threading.Interlocked.Increment(ref _hitCounter);
            Timestamp = DateTime.UtcNow;
            AttackerName = string.IsNullOrEmpty(attackerName) ? "Unknown Attacker" : attackerName;
            TargetName = string.IsNullOrEmpty(targetName) ? "Unknown Target" : targetName;
            InitialDamage = initialDamage.Clone();
            FinalDamage = initialDamage.Clone();
        }

        public void RecordStep(DamagePhase phase, string source, string operation, float delta, string details = "")
        {
            GenericSteps.Add(new TraceStep(phase, source, operation, delta, details));
        }

        public void RecordTierModifiers(TierRecord record)
        {
            AttackerTierRecords.Add(record);
        }

        public void RecordMitigation(MitigationRecord record)
        {
            MitigationRecords.Add(record);
        }

        /// <summary>
        /// Formats this trace according to the specified style or the active user configuration.
        /// </summary>
        public string ToFormattedReport(DamageLogFormat? formatOverride = null)
        {
            DamageLogFormat format = formatOverride ?? ModConfig.LogFormat.Value;
            return format switch
            {
                DamageLogFormat.Tree => FormatTree(),
                DamageLogFormat.Compact => FormatCompact(),
                DamageLogFormat.Audit => FormatAudit(),
                _ => FormatTree()
            };
        }

        #region Tree Formatting (User-Tweaked Style)

        private string FormatTree()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[VikingLib Hit #{HitId}] {AttackerName} -> {TargetName}");

            // Weapon Origin line
            if (!string.IsNullOrEmpty(WeaponName) && WeaponBaseDamage != null)
            {
                string boundsInfo = (RollLowerBound > 0f && RollUpperBound > 0f)
                    ? $" (Roll: {SkillRollFactor:F2}x in [{RollLowerBound:F2} .. {RollUpperBound:F2}])"
                    : $" (Roll: {SkillRollFactor:F2}x)";
                string skillInfo = (SkillType != Skills.SkillType.None && RollSteps.Count == 0)
                    ? $" | {SkillType} Skill {SkillLevel:F0}{boundsInfo}"
                    : boundsInfo;
                string attackSuffix = !string.IsNullOrEmpty(AttackDisplayName) ? $" [{AttackDisplayName}]" : "";
                sb.AppendLine($"├── Weapon: {WeaponName}{attackSuffix} | Base: {FormatDamageTypes(WeaponBaseDamage.Value)}{skillInfo}");

                if (RollSteps.Count > 0)
                {
                    sb.AppendLine("│   └── Roll Bounds Pipeline:");
                    for (int s = 0; s < RollSteps.Count; s++)
                    {
                        var step = RollSteps[s];
                        string sBranch = (s == RollSteps.Count - 1) ? "└──" : "├──";
                        sb.AppendLine($"│       {sBranch} {step.Format()}");
                    }
                }
            }
            else
            {
                string boundsInfo = (RollLowerBound > 0f && RollUpperBound > 0f)
                    ? $" (Roll: {SkillRollFactor:F2}x in [{RollLowerBound:F2} .. {RollUpperBound:F2}])"
                    : "";
                sb.AppendLine($"├── Attacker Base: {FormatDamageTypes(InitialDamage)}{boundsInfo}");

                if (RollSteps.Count > 0)
                {
                    sb.AppendLine("│   └── Roll Bounds Pipeline:");
                    for (int s = 0; s < RollSteps.Count; s++)
                    {
                        var step = RollSteps[s];
                        string sBranch = (s == RollSteps.Count - 1) ? "└──" : "├──";
                        sb.AppendLine($"│       {sBranch} {step.Format()}");
                    }
                }
            }

            bool hasAttackerMods = AttackerTierRecords.Count > 0;
            bool hasMitigation = MitigationRecords.Count > 0;
            var targetDefSteps = GenericSteps.FindAll(s => s.Phase == DamagePhase.TargetDefenses);
            var postHitSteps = GenericSteps.FindAll(s => s.Phase == DamagePhase.PostHitProcs);

            if (!hasAttackerMods && !hasMitigation && targetDefSteps.Count == 0 && postHitSteps.Count == 0)
            {
                sb.AppendLine("├── No modifications applied (Pure Hit).");
            }
            else
            {
                // Phase: AttackerModifiers
                if (hasAttackerMods)
                {
                    sb.AppendLine("├── Phase: AttackerModifiers");
                    for (int i = 0; i < AttackerTierRecords.Count; i++)
                    {
                        var rec = AttackerTierRecords[i];
                        float delta = rec.ResultingValue - rec.BaseValue;
                        string sign = delta >= 0 ? "+" : "";
                        sb.AppendLine($"│   └── {rec.Type}: {rec.BaseValue:F1} -> {rec.ResultingValue:F1} ({sign}{delta:F1})");

                        var activeTiers = new List<Action<bool>>();

                        if (rec.FlatSources.Count > 0)
                        {
                            activeTiers.Add(isLast =>
                            {
                                string tBranch = isLast ? "└──" : "├──";
                                string sign = rec.FlatSum >= 0 ? "+" : "";
                                sb.AppendLine($"│       {tBranch} Tier 1 (Flat: {sign}{rec.FlatSum:F1}):");
                                for (int s = 0; s < rec.FlatSources.Count; s++)
                                {
                                    string sBranch = (s == rec.FlatSources.Count - 1) ? "└──" : "├──";
                                    string ind = isLast ? "    " : "│   ";
                                    sb.AppendLine($"│       {ind}{sBranch} {rec.FlatSources[s]}");
                                }
                            });
                        }

                        if (rec.IncreasedSources.Count > 0)
                        {
                            activeTiers.Add(isLast =>
                            {
                                string tBranch = isLast ? "└──" : "├──";
                                string sign = rec.IncreasedSum >= 0 ? "+" : "";
                                sb.AppendLine($"│       {tBranch} Tier 2 (Increased: {sign}{rec.IncreasedSum * 100:F0}%):");
                                for (int s = 0; s < rec.IncreasedSources.Count; s++)
                                {
                                    string sBranch = (s == rec.IncreasedSources.Count - 1) ? "└──" : "├──";
                                    string ind = isLast ? "    " : "│   ";
                                    sb.AppendLine($"│       {ind}{sBranch} {rec.IncreasedSources[s]}");
                                }
                            });
                        }

                        if (rec.MultiplierSources.Count > 0)
                        {
                            activeTiers.Add(isLast =>
                            {
                                string tBranch = isLast ? "└──" : "├──";
                                sb.AppendLine($"│       {tBranch} Tier 3 (Multipliers: x{rec.MultiplierProduct:F2}):");
                                for (int s = 0; s < rec.MultiplierSources.Count; s++)
                                {
                                    string sBranch = (s == rec.MultiplierSources.Count - 1) ? "└──" : "├──";
                                    string ind = isLast ? "    " : "│   ";
                                    sb.AppendLine($"│       {ind}{sBranch} {rec.MultiplierSources[s]}");
                                }
                            });
                        }

                        for (int t = 0; t < activeTiers.Count; t++)
                        {
                            activeTiers[t](t == activeTiers.Count - 1);
                        }
                    }
                }

                // Phase: TargetDefenses
                if (targetDefSteps.Count > 0)
                {
                    sb.AppendLine("├── Phase: TargetDefenses");
                    for (int i = 0; i < targetDefSteps.Count; i++)
                    {
                        var step = targetDefSteps[i];
                        bool isLast = (i == targetDefSteps.Count - 1);
                        string branch = isLast ? "└──" : "├──";
                        sb.AppendLine($"│   {branch} {step.Source}: {step.Operation}");
                    }
                }

                // Phase: Mitigation
                if (hasMitigation)
                {
                    sb.AppendLine("├── Phase: Mitigation");
                    for (int i = 0; i < MitigationRecords.Count; i++)
                    {
                        var mit = MitigationRecords[i];
                        bool isLastType = (i == MitigationRecords.Count - 1);
                        string tBranch = isLastType ? "└──" : "├──";
                        string ind = isLastType ? "    " : "│   ";

                        float delta = mit.MitigatedDamage - mit.IncomingDamage;
                        string sign = delta >= 0 ? "+" : "";
                        sb.AppendLine($"│   {tBranch} {mit.Type}: {mit.IncomingDamage:F1} -> {mit.MitigatedDamage:F1} ({sign}{delta:F1})");

                        bool showArmor = mit.HasArmorMitigation || mit.ArmorReduction > 0.01f || mit.ArmorValue > 0.01f;
                        if (showArmor)
                        {
                            sb.AppendLine($"│   {ind}├── {FormatArmorTree(mit)}");
                        }
                        sb.AppendLine($"│   {ind}└── Resistance: x{mit.ResistanceMultiplier:F2} ({mit.ResistanceLabel})");
                    }
                }

                // Phase: PostHitProcs
                if (postHitSteps.Count > 0)
                {
                    sb.AppendLine("├── Phase: PostHitProcs");
                    for (int i = 0; i < postHitSteps.Count; i++)
                    {
                        var step = postHitSteps[i];
                        bool isLast = (i == postHitSteps.Count - 1);
                        string branch = isLast ? "└──" : "├──";
                        sb.AppendLine($"│   {branch} {step.Source}: {step.Operation}");
                    }
                }
            }

            // Final Damage Dealt line
            sb.AppendLine($"└── Final Damage Dealt: {FormatDamageTypes(FinalDamage)}");
            return sb.ToString();
        }

        #endregion

        #region Compact Formatting

        private string FormatCompact()
        {
            var sb = new StringBuilder();
            string attackSuffix = !string.IsNullOrEmpty(AttackDisplayName) ? $" [{AttackDisplayName}]" : "";
            sb.AppendLine($"[VikingLib Hit #{HitId}] {AttackerName} ({WeaponName ?? "Unarmed"}{attackSuffix}) -> {TargetName}");

            if (RollSteps.Count > 0)
            {
                var stepSummaries = new List<string>();
                foreach (var step in RollSteps)
                {
                    stepSummaries.Add(step.Format());
                }
                string boundsInfo = (RollLowerBound > 0f && RollUpperBound > 0f) ? $" in [{RollLowerBound:F2} .. {RollUpperBound:F2}]" : "";
                sb.AppendLine($"├── Roll Pipeline: {SkillRollFactor:F2}x{boundsInfo} [{string.Join(" -> ", stepSummaries)}]");
            }
            else if (WeaponBaseDamage != null)
            {
                string boundsInfo = (RollLowerBound > 0f && RollUpperBound > 0f)
                    ? $" in [{RollLowerBound:F2} .. {RollUpperBound:F2}]"
                    : "";
                sb.AppendLine($"├── Origin: {FormatDamageTypes(WeaponBaseDamage.Value)} * {SkillRollFactor:F2} Roll{boundsInfo} ({SkillType} {SkillLevel:F0})");
            }

            var compactTargetDefSteps = GenericSteps.FindAll(s => s.Phase == DamagePhase.TargetDefenses);
            var compactPostHitSteps = GenericSteps.FindAll(s => s.Phase == DamagePhase.PostHitProcs);

            if (AttackerTierRecords.Count == 0 && MitigationRecords.Count == 0 && compactTargetDefSteps.Count == 0 && compactPostHitSteps.Count == 0)
            {
                sb.AppendLine("├── No modifications applied.");
            }
            else
            {
                foreach (var rec in AttackerTierRecords)
                {
                    sb.AppendLine($"├── Phase 1: AttackerModifiers ({rec.Type}: {rec.BaseValue:F1} -> {rec.ResultingValue:F1})");
                    if (rec.FlatSources.Count > 0)
                        sb.AppendLine($"│   ├── [Tier 1: Flat {rec.FlatSum:+0.0;-0.0}] {string.Join(", ", rec.FlatSources)}");
                    if (rec.IncreasedSources.Count > 0)
                        sb.AppendLine($"│   ├── [Tier 2: Inc +{rec.IncreasedSum * 100:F0}%] {string.Join(", ", rec.IncreasedSources)}");
                    if (rec.MultiplierSources.Count > 0)
                        sb.AppendLine($"│   └── [Tier 3: Mult x{rec.MultiplierProduct:F2}] {string.Join(", ", rec.MultiplierSources)}");
                }

                foreach (var step in compactTargetDefSteps)
                {
                    sb.AppendLine($"├── Phase 2: TargetDefenses [{step.Source}] {step.Operation}");
                }

                foreach (var mit in MitigationRecords)
                {
                    sb.AppendLine($"├── Phase 3: Mitigation ({mit.Type}: {mit.IncomingDamage:F1} -> {mit.MitigatedDamage:F1})");
                    bool showArmor = mit.HasArmorMitigation || mit.ArmorReduction > 0.01f || mit.ArmorValue > 0.01f;
                    if (showArmor)
                        sb.AppendLine($"│   ├── {FormatArmorCompact(mit)}");
                    sb.AppendLine($"│   └── [Resistance: {mit.ResistanceLabel}] x{mit.ResistanceMultiplier:F2}");
                }

                foreach (var step in compactPostHitSteps)
                {
                    sb.AppendLine($"├── Phase 5: PostHitProcs [{step.Source}] {step.Operation}");
                }
            }

            sb.AppendLine($"└── Final: {FormatDamageTypes(FinalDamage)}");
            return sb.ToString();
        }

        #endregion

        #region Audit Card Formatting

        private string FormatAudit()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"=== [VikingLib Combat Audit #{HitId}] ===========================================");
            string boundsInfo = (RollLowerBound > 0f && RollUpperBound > 0f)
                ? $" (Roll: {SkillRollFactor:F2}x in [{RollLowerBound:F2} .. {RollUpperBound:F2}])"
                : $" (Roll: {SkillRollFactor:F2}x)";
            string attackSuffix = !string.IsNullOrEmpty(AttackDisplayName) ? $" [{AttackDisplayName}]" : "";
            sb.AppendLine($"Attacker : {AttackerName} ({WeaponName ?? "Unarmed"}{attackSuffix}) | {SkillType} Skill {SkillLevel:F0}{boundsInfo}");
            sb.AppendLine($"Target   : {TargetName}");
            sb.AppendLine("------------------------------------------------------------------------------");

            if (RollSteps.Count > 0)
            {
                sb.AppendLine("DAMAGE ROLL STEPS:");
                if (WeaponBaseDamage != null)
                {
                    sb.AppendLine($"  [Weapon Base]      {FormatDamageTypes(WeaponBaseDamage.Value)}");
                }
                for (int i = 0; i < RollSteps.Count; i++)
                {
                    var step = RollSteps[i];
                    sb.AppendLine($"  {step.Format(21)}");
                }
                sb.AppendLine($"  [Roll Result]      {SkillRollFactor:F2}x factor (uniform roll in [{RollLowerBound:F2} .. {RollUpperBound:F2}])");
                sb.AppendLine("------------------------------------------------------------------------------");
            }

            var auditTargetDefSteps = GenericSteps.FindAll(s => s.Phase == DamagePhase.TargetDefenses);
            var auditPostHitSteps = GenericSteps.FindAll(s => s.Phase == DamagePhase.PostHitProcs);

            if (AttackerTierRecords.Count == 0 && MitigationRecords.Count == 0 && auditTargetDefSteps.Count == 0 && auditPostHitSteps.Count == 0)
            {
                sb.AppendLine($"DAMAGE: {FormatDamageTypes(FinalDamage)} (Unmodified Hit)");
            }
            else
            {
                foreach (var rec in AttackerTierRecords)
                {
                    sb.AppendLine($"DAMAGE TYPE: {rec.Type}");
                    sb.AppendLine($"  [Initial Roll]    Base: {rec.BaseValue:F1}");
                    if (rec.FlatSources.Count > 0)
                        sb.AppendLine($"  [Tier 1: Flat]    +{rec.FlatSum:F1} ({string.Join(", ", rec.FlatSources)})");
                    if (rec.IncreasedSources.Count > 0)
                        sb.AppendLine($"  [Tier 2: Inc %]   +{rec.IncreasedSum * 100:F0}% ({string.Join(", ", rec.IncreasedSources)})");
                    if (rec.MultiplierSources.Count > 0)
                        sb.AppendLine($"  [Tier 3: Mult]    x{rec.MultiplierProduct:F2} ({string.Join(", ", rec.MultiplierSources)})");
                    sb.AppendLine($"  [Outgoing Sub]    = {rec.ResultingValue:F1}");
                }

                if (auditTargetDefSteps.Count > 0)
                {
                    sb.AppendLine("TARGET DEFENSES:");
                    foreach (var step in auditTargetDefSteps)
                    {
                        sb.AppendLine($"  [{step.Source}] {step.Operation}");
                    }
                    sb.AppendLine("------------------------------------------------------------------------------");
                }

                foreach (var mit in MitigationRecords)
                {
                    bool showArmor = mit.HasArmorMitigation || mit.ArmorReduction > 0.01f || mit.ArmorValue > 0.01f;
                    if (showArmor)
                        sb.AppendLine(FormatArmorAudit(mit));
                    sb.AppendLine($"  [Resistance]      x{mit.ResistanceMultiplier:F2} ({mit.ResistanceLabel})");
                    sb.AppendLine($"  [Final for Type]  = {mit.MitigatedDamage:F1} {mit.Type}");
                }

                if (auditPostHitSteps.Count > 0)
                {
                    sb.AppendLine("POST-HIT PROCS:");
                    foreach (var step in auditPostHitSteps)
                    {
                        sb.AppendLine($"  [{step.Source}] {step.Operation}");
                    }
                    sb.AppendLine("------------------------------------------------------------------------------");
                }
            }

            sb.AppendLine("------------------------------------------------------------------------------");
            sb.AppendLine($"FINAL DAMAGE DEALT: {FormatDamageTypes(FinalDamage)}");
            sb.AppendLine("==============================================================================");
            return sb.ToString();
        }

        #endregion

        #region Armor & Damage Formatting Helpers

        private static string FormatPenetration(float baseArmor, float effArmor, float penPct, float penFlat)
        {
            if (effArmor <= 0.001f && baseArmor > 0.001f)
            {
                return " -> 0.0 eff (100% bypassed by pen)";
            }
            if (penPct > 0.001f && penFlat > 0.001f)
            {
                return $" -> {effArmor:F1} eff (-{penPct * 100:F0}% pen, -{penFlat:F1} flat)";
            }
            if (penPct > 0.001f)
            {
                return $" -> {effArmor:F1} eff (-{penPct * 100:F0}% pen)";
            }
            if (penFlat > 0.001f)
            {
                return $" -> {effArmor:F1} eff (-{penFlat:F1} flat pen)";
            }
            return string.Empty;
        }

        private static string FormatArmorTree(MitigationRecord mit)
        {
            string penText = FormatPenetration(mit.ArmorValue, mit.EffectiveArmor, mit.ArmorPenetrationPercent, mit.ArmorPenetrationFlat);

            if (mit.ArmorModel == ArmorMitigationModel.LinearEHP)
            {
                string bonusText = "";
                if (mit.ArmorEfficiency > 0.001f)
                    bonusText = $", incl. +{mit.ArmorEfficiency * 100:F0}% bonus";
                else if (mit.ArmorEfficiency < -0.001f)
                    bonusText = $", incl. {mit.ArmorEfficiency * 100:F0}% penalty";

                float drPercent = mit.LinearEhpTotalEfficiency > 0.001f
                    ? (mit.LinearEhpTotalEfficiency / (1f + mit.LinearEhpTotalEfficiency)) * 100f
                    : 0f;

                string effText = mit.EffectiveArmor <= 0.001f
                    ? "Eff: 0% -> 0% DR"
                    : $"Eff: +{mit.LinearEhpTotalEfficiency * 100:F0}% -> {drPercent:F0}% DR{bonusText}";

                return $"Armor [Linear EHP (K={mit.ArmorScaleConstant:F0})]: AC {mit.ArmorValue:F1}{penText} ({effText}) -> -{mit.ArmorReduction:F1} mitigation";
            }
            else
            {
                string branch = mit.IsPiecewiseQuadratic ? "Quadratic: D < 2*AC" : "Subtraction: D >= 2*AC";
                string scaledText = "";
                if (mit.ArmorEfficiency > 0.001f)
                    scaledText = $" -> Scaled AC: {mit.ScaledPiecewiseArmor:F1} (+{mit.ArmorEfficiency * 100:F0}% eff)";
                else if (mit.ArmorEfficiency < -0.001f)
                    scaledText = $" -> Scaled AC: {mit.ScaledPiecewiseArmor:F1} ({mit.ArmorEfficiency * 100:F0}% eff)";

                return $"Armor [Piecewise ({branch})]: AC {mit.ArmorValue:F1}{penText}{scaledText} -> -{mit.ArmorReduction:F1} mitigation";
            }
        }

        private static string FormatArmorCompact(MitigationRecord mit)
        {
            string penText = FormatPenetration(mit.ArmorValue, mit.EffectiveArmor, mit.ArmorPenetrationPercent, mit.ArmorPenetrationFlat);

            if (mit.ArmorModel == ArmorMitigationModel.LinearEHP)
            {
                float drPercent = mit.LinearEhpTotalEfficiency > 0.001f
                    ? (mit.LinearEhpTotalEfficiency / (1f + mit.LinearEhpTotalEfficiency)) * 100f
                    : 0f;

                string effText = mit.EffectiveArmor <= 0.001f
                    ? "Eff 0% (0% DR)"
                    : $"Eff +{mit.LinearEhpTotalEfficiency * 100:F0}% ({drPercent:F0}% DR)";
                return $"[Armor: Linear EHP (K={mit.ArmorScaleConstant:F0}), AC {mit.ArmorValue:F1}{penText}, {effText}] -{mit.ArmorReduction:F1} mitigated";
            }
            else
            {
                string branch = mit.IsPiecewiseQuadratic ? "Quadratic" : "Subtraction";
                string scaledText = Math.Abs(mit.ArmorEfficiency) > 0.001f
                    ? $", Scaled {mit.ScaledPiecewiseArmor:F1}"
                    : "";
                return $"[Armor: Piecewise ({branch}), AC {mit.ArmorValue:F1}{penText}{scaledText}] -{mit.ArmorReduction:F1} mitigated";
            }
        }

        private static string FormatArmorAudit(MitigationRecord mit)
        {
            string penText = FormatPenetration(mit.ArmorValue, mit.EffectiveArmor, mit.ArmorPenetrationPercent, mit.ArmorPenetrationFlat);

            if (mit.ArmorModel == ArmorMitigationModel.LinearEHP)
            {
                string bonusText = "";
                if (mit.ArmorEfficiency > 0.001f)
                    bonusText = $", incl. +{mit.ArmorEfficiency * 100:F0}% bonus";
                else if (mit.ArmorEfficiency < -0.001f)
                    bonusText = $", incl. {mit.ArmorEfficiency * 100:F0}% penalty";

                float drPercent = mit.LinearEhpTotalEfficiency > 0.001f
                    ? (mit.LinearEhpTotalEfficiency / (1f + mit.LinearEhpTotalEfficiency)) * 100f
                    : 0f;

                return $"  [Armor: Linear EHP] -{mit.ArmorReduction:F1} from Armor ({mit.ArmorValue:F1} AC{penText}, K={mit.ArmorScaleConstant:F0}, Total Eff: +{mit.LinearEhpTotalEfficiency * 100:F0}% -> {drPercent:F0}% DR{bonusText})";
            }
            else
            {
                string branch = mit.IsPiecewiseQuadratic ? "Quadratic (D < 2*AC)" : "Subtraction (D >= 2*AC)";
                string scaledText = Math.Abs(mit.ArmorEfficiency) > 0.001f
                    ? $", Scaled AC: {mit.ScaledPiecewiseArmor:F1} ({mit.ArmorEfficiency * 100:F0}% eff)"
                    : "";
                return $"  [Armor: Piecewise]  -{mit.ArmorReduction:F1} from Armor ({mit.ArmorValue:F1} AC{penText}{scaledText}, Branch: {branch})";
            }
        }

        #endregion

        private static string FormatDamageTypes(HitData.DamageTypes dt)
        {
            var parts = new List<string>();
            if (dt.m_blunt > 0.01f) parts.Add($"Blunt: {dt.m_blunt:F1}");
            if (dt.m_slash > 0.01f) parts.Add($"Slash: {dt.m_slash:F1}");
            if (dt.m_pierce > 0.01f) parts.Add($"Pierce: {dt.m_pierce:F1}");
            if (dt.m_fire > 0.01f) parts.Add($"Fire: {dt.m_fire:F1}");
            if (dt.m_frost > 0.01f) parts.Add($"Frost: {dt.m_frost:F1}");
            if (dt.m_lightning > 0.01f) parts.Add($"Lightning: {dt.m_lightning:F1}");
            if (dt.m_poison > 0.01f) parts.Add($"Poison: {dt.m_poison:F1}");
            if (dt.m_spirit > 0.01f) parts.Add($"Spirit: {dt.m_spirit:F1}");
            if (dt.m_chop > 0.01f) parts.Add($"Chop: {dt.m_chop:F1}");
            if (dt.m_pickaxe > 0.01f) parts.Add($"Pickaxe: {dt.m_pickaxe:F1}");

            if (parts.Count == 0) return "[0.0]";
            return "[" + string.Join(", ", parts) + "]";
        }
    }
}
