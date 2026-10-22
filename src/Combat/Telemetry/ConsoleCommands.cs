using Jotunn.Entities;
using VikingLib.Configuration;

namespace VikingLib.Combat.Telemetry
{
    /// <summary>
    /// In-game console command to inspect the most recent combat hit breakdown.
    /// </summary>
    public class TraceConsoleCommand : ConsoleCommand
    {
        public override string Name => "vl_trace";
        public override string Help => "Prints the hierarchical damage breakdown of the most recent combat hit.";

        public override void Run(string[] args)
        {
            if (DamagePipeline.LastTrace == null)
            {
                global::Console.instance?.Print("[VikingLib] No combat hits have been recorded yet.");
                return;
            }

            global::Console.instance?.Print(DamagePipeline.LastTrace.ToFormattedReport());
        }
    }

    /// <summary>
    /// In-game console command to toggle or configure combat telemetry logging.
    /// Usage: vl_log [off | player | all]
    /// </summary>
    public class LogConsoleCommand : ConsoleCommand
    {
        public override string Name => "vl_log";
        public override string Help => "Configures VikingLib combat logging mode. Usage: vl_log [off | player | all]";

        public override void Run(string[] args)
        {
            if (args.Length > 0)
            {
                string arg = args[0].Trim().ToLowerInvariant();
                switch (arg)
                {
                    case "off":
                        ModConfig.LoggingMode.Value = DamageLoggingMode.Off;
                        break;
                    case "player":
                    case "playeronly":
                        ModConfig.LoggingMode.Value = DamageLoggingMode.PlayerOnly;
                        break;
                    case "all":
                    case "allcombat":
                        ModConfig.LoggingMode.Value = DamageLoggingMode.AllCombat;
                        break;
                    default:
                        global::Console.instance?.Print($"[VikingLib] Unknown mode '{arg}'. Valid options: off, player, all");
                        return;
                }
            }

            global::Console.instance?.Print($"[VikingLib] Current logging mode: {ModConfig.LoggingMode.Value}");
        }
    }

    /// <summary>
    /// In-game console command to view or configure the active armor mitigation model.
    /// Usage: vl_armor [linear | piecewise | k &lt;value&gt;]
    /// </summary>
    public class ArmorConsoleCommand : ConsoleCommand
    {
        public override string Name => "vl_armor";
        public override string Help => "Views or sets the active armor mitigation model. Usage: vl_armor [linear | piecewise | k <value>]";

        public override void Run(string[] args)
        {
            if (args.Length > 0)
            {
                string arg = args[0].Trim().ToLowerInvariant();
                switch (arg)
                {
                    case "linear":
                    case "linearehp":
                    case "ehp":
                        ModConfig.ArmorModel.Value = ArmorMitigationModel.LinearEHP;
                        global::Console.instance?.Print($"[VikingLib] Armor mitigation model set to: LinearEHP (K = {ModConfig.ArmorScaleConstant.Value:F1})");
                        return;

                    case "piecewise":
                    case "vanilla":
                    case "vanillapiecewise":
                        ModConfig.ArmorModel.Value = ArmorMitigationModel.VanillaPiecewise;
                        global::Console.instance?.Print("[VikingLib] Armor mitigation model set to: VanillaPiecewise");
                        return;

                    case "k":
                        if (args.Length > 1 && float.TryParse(args[1], out float newK) && newK > 0f)
                        {
                            ModConfig.ArmorScaleConstant.Value = newK;
                            global::Console.instance?.Print($"[VikingLib] LinearEHP ArmorScaleConstant (K) set to: {newK:F1}");
                        }
                        else
                        {
                            global::Console.instance?.Print($"[VikingLib] Current K constant: {ModConfig.ArmorScaleConstant.Value:F1}. Usage: vl_armor k <number>");
                        }
                        return;

                    default:
                        global::Console.instance?.Print($"[VikingLib] Unknown argument '{arg}'. Valid options: linear, piecewise, k <value>");
                        return;
                }
            }

            global::Console.instance?.Print($"[VikingLib] Current Armor Model: {ModConfig.ArmorModel.Value} (K = {ModConfig.ArmorScaleConstant.Value:F1})");
        }
    }

    /// <summary>
    /// In-game console command to export all weapon attack configurations to VanillaWeaponAttacks.json.
    /// </summary>
    public class DumpAttacksConsoleCommand : ConsoleCommand
    {
        public override string Name => "vl_dump_attacks";
        public override string Help => "Dumps all weapon attack profiles from ObjectDB to VanillaWeaponAttacks.json.";

        public override void Run(string[] args)
        {
            string result = VikingLib.Combat.Attacks.WeaponAttackExtractor.ExportAllWeapons();
            global::Console.instance?.Print($"[VikingLib] {result}");
        }
    }

    /// <summary>
    /// In-game console command to apply or clear synthetic test buffs across combat taxonomy tags.
    /// Usage: vl_buff [on | off | clear]
    /// </summary>
    public class BuffTestConsoleCommand : ConsoleCommand
    {
        public const string BuffOwner = "VikingLibTestBuff";

        public override string Name => "vl_buff";
        public override string Help => "Applies or clears test buffs across offensive, defensive, and efficiency tags. Usage: vl_buff [on | off | clear]";

        public override void Run(string[] args)
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                global::Console.instance?.Print("[VikingLib] No local player active in world.");
                return;
            }

            string sub = args.Length > 0 ? args[0].Trim().ToLowerInvariant() : string.Empty;

            if (sub == "laststand")
            {
                ToggleLastStand();
                return;
            }

            if (sub == "off" || sub == "clear")
            {
                int removed = VikingLib.Stats.CharacterStatManager.RemoveModifiersByOwner(player, BuffOwner);
                global::Console.instance?.Print($"[VikingLib] Cleared {removed} test buffs from local player.");
                return;
            }

            // If no args and already has test buffs, toggle them off
            int existing = VikingLib.Stats.CharacterStatManager.RemoveModifiersByOwner(player, BuffOwner);
            if (existing > 0 && sub != "on")
            {
                global::Console.instance?.Print($"[VikingLib] Toggled test buffs OFF (cleared {existing} modifiers).");
                return;
            }

            // Apply test modifiers
            // 1. Delivery & Stance
            VikingLib.Stats.CharacterStats.DamageMelee.AddIncreased(player, 0.25f, "Test Delivery (Melee +25%)", owner: BuffOwner);
            VikingLib.Stats.CharacterStats.DamageOneHanded.AddIncreased(player, 0.15f, "Test Stance (1H +15%)", owner: BuffOwner);
            VikingLib.Stats.CharacterStats.DamageMagic.AddIncreased(player, 0.20f, "Test Stance (Magic +20%)", owner: BuffOwner);

            // 2. Attack Styles & Techniques
            VikingLib.Stats.CharacterStats.CriticalStrikeChance.AddFlat(player, 0.15f, "Test Style (Crit Chance +15%)", owner: BuffOwner);
            VikingLib.Stats.CharacterStats.CriticalStrikeMultiplier.AddMultiplier(player, 1.33f, "Test Style (Crit Mult x1.33)", owner: BuffOwner);
            VikingLib.Stats.CharacterStats.DamageSecondaryAttack.AddIncreased(player, 0.30f, "Test Style (Secondary +30%)", owner: BuffOwner);
            VikingLib.Stats.CharacterStats.DamageComboFinisher.AddIncreased(player, 0.25f, "Test Style (Finisher +25%)", owner: BuffOwner);
            VikingLib.Stats.CharacterStats.StaggerDamage.AddIncreased(player, 0.50f, "Test Style (Stagger Power +50%)", owner: BuffOwner);
            VikingLib.Stats.CharacterStats.BowDrawSpeed.AddIncreased(player, 0.20f, "Test Style (Bow Draw Speed +20%)", owner: BuffOwner);

            // 3. Situational & Target Contexts
            VikingLib.Stats.CharacterStats.DamageAgainstBosses.AddIncreased(player, 0.35f, "Test Hunter (Boss +35%)", owner: BuffOwner);
            VikingLib.Stats.CharacterStats.DamageAgainstStaggered.AddIncreased(player, 0.25f, "Test Executioner (Staggered +25%)", owner: BuffOwner);
            VikingLib.Stats.CharacterStats.DamageExecute.AddIncreased(player, 0.40f, "Test Cull (Execute +40%)", owner: BuffOwner);

            // 4. Weapon Families
            VikingLib.Stats.CharacterStats.DamageSwords.AddIncreased(player, 0.20f, "Test Mastery (Swords +20%)", owner: BuffOwner);
            VikingLib.Stats.CharacterStats.DamageBows.AddIncreased(player, 0.15f, "Test Mastery (Bows +15%)", owner: BuffOwner);
            VikingLib.Stats.CharacterStats.DamageUnarmed.AddIncreased(player, 0.25f, "Test Mastery (Unarmed +25%)", owner: BuffOwner);

            // 5. Damage Types & Archetypes
            VikingLib.Stats.CharacterStats.DamageFire.AddIncreased(player, 0.20f, "Test Elemental (Fire +20%)", owner: BuffOwner);
            VikingLib.Stats.CharacterStats.DamageSlashing.AddIncreased(player, 0.15f, "Test Physical (Slashing +15%)", owner: BuffOwner);

            // 6. Action Resource Efficiencies
            VikingLib.Stats.CharacterStats.MeleeStaminaEfficiency.AddIncreased(player, 0.20f, "Test Endurance (Melee Eff +20%)", owner: BuffOwner);
            VikingLib.Stats.CharacterStats.SecondaryAttackStaminaEfficiency.AddIncreased(player, 0.25f, "Test Technique (Secondary Eff +25%)", owner: BuffOwner);
            VikingLib.Stats.CharacterStats.CrossbowReloadStaminaEfficiency.AddIncreased(player, 0.30f, "Test Reload (Crossbow Reload Eff +30%)", owner: BuffOwner);
            VikingLib.Stats.CharacterStats.AttackEitrEfficiency.AddIncreased(player, 0.20f, "Test Mystic (Attack Eitr Eff +20%)", owner: BuffOwner);
            VikingLib.Stats.CharacterStats.SprintEfficiency.AddIncreased(player, 0.25f, "Test Athletics (Sprint Eff +25%)", owner: BuffOwner);

            // 7. Defensive Resistances & Armor Efficiency
            VikingLib.Stats.CharacterStats.ResistanceFire.AddMultiplier(player, 0.80f, "Test Ward (Fire Taken x0.80)", owner: BuffOwner);
            VikingLib.Stats.CharacterStats.ResistancePhysical.AddMultiplier(player, 0.85f, "Test Ironclad (Physical Taken x0.85)", owner: BuffOwner);
            VikingLib.Stats.CharacterStats.ResistancePoison.AddMultiplier(player, 0.50f, "Test Antidote (Poison Taken x0.50)", owner: BuffOwner);
            VikingLib.Stats.CharacterStats.ArmorEfficiency.AddIncreased(player, 0.20f, "Test Plating (Armor Eff +20%)", owner: BuffOwner);
            VikingLib.Stats.CharacterStats.SlashArmorEfficiency.AddIncreased(player, 0.15f, "Test Mail (Slash Armor Eff +15%)", owner: BuffOwner);

            // 8. On-Hit Resource Procs
            VikingLib.Stats.CharacterStats.HealthOnKill.AddFlat(player, 5f, "Test Feast (Health On Kill +5)", owner: BuffOwner);
            VikingLib.Stats.CharacterStats.StaminaOnKill.AddFlat(player, 10f, "Test Momentum (Stamina On Kill +10)", owner: BuffOwner);
            VikingLib.Stats.CharacterStats.EitrOnKill.AddFlat(player, 5f, "Test Siphon (Eitr On Kill +5)", owner: BuffOwner);
            VikingLib.Stats.CharacterStats.StaminaOnCauseStagger.AddFlat(player, 8f, "Test Breaker (Stamina On Cause Stagger +8)", owner: BuffOwner);
            VikingLib.Stats.CharacterStats.HealthOnDamageTaken.AddFlat(player, 4f, "Test Retribution (Health On Damage Taken +4)", owner: BuffOwner);

            global::Console.instance?.Print("[VikingLib] Applied test buffs across combat taxonomy tags! Open Hugin's Compendium or hover weapons to inspect.");
        }

        private static bool _lastStandActive;

        private static void TestLastStandHandler(DamageContext ctx)
        {
            if (ctx.Target == Player.m_localPlayer)
            {
                ctx.DefyDeath("Test Last Stand");
            }
        }

        private static void ToggleLastStand()
        {
            _lastStandActive = !_lastStandActive;
            if (_lastStandActive) DamagePipeline.OnLethalHit += TestLastStandHandler;
            else DamagePipeline.OnLethalHit -= TestLastStandHandler;
            global::Console.instance?.Print($"[VikingLib] Test Last Stand is now {(_lastStandActive ? "ON (lethal hits leave you at 1 HP)" : "OFF")}.");
        }
    }
}
