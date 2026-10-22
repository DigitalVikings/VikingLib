using System.Text;
using Jotunn.Entities;
using VikingLib.Stats;

namespace VikingLib.StatusEffects
{
    /// <summary>
    /// In-game console command to inspect, apply, or clear VikingLib status effects on the local player.
    /// Usage: vl_se [apply &lt;name&gt; [stacks] [duration] | list | clear [name] | info &lt;name&gt;]
    /// </summary>
    public class StatusEffectConsoleCommand : ConsoleCommand
    {
        public override string Name => "vl_se";
        public override string Help => "VikingLib status effect tools. Usage: vl_se [apply <name> [stacks] [duration] | list | clear [name] | info <name>]";

        public override void Run(string[] args)
        {
            if (args.Length == 0)
            {
                global::Console.instance?.Print(Help);
                return;
            }

            string sub = args[0].Trim().ToLowerInvariant();
            switch (sub)
            {
                case "list":
                    ListStatusEffects();
                    break;

                case "apply":
                    if (args.Length < 2)
                    {
                        global::Console.instance?.Print("[VikingLib] Usage: vl_se apply <name> [stacks] [duration]");
                        return;
                    }
                    string applyName = args[1];
                    int stacks = 1;
                    if (args.Length > 2) int.TryParse(args[2], out stacks);
                    float duration = -1f;
                    if (args.Length > 3) float.TryParse(args[3], out duration);
                    ApplyToPlayer(applyName, stacks, duration);
                    break;

                case "clear":
                    string? clearName = args.Length > 1 ? args[1] : null;
                    ClearPlayerEffects(clearName);
                    break;

                case "info":
                    if (args.Length < 2)
                    {
                        global::Console.instance?.Print("[VikingLib] Usage: vl_se info <name>");
                        return;
                    }
                    PrintInfo(args[1]);
                    break;

                default:
                    global::Console.instance?.Print($"[VikingLib] Unknown subcommand '{sub}'. {Help}");
                    break;
            }
        }

        private static void ListStatusEffects()
        {
            var defs = StatusEffectRegistry.GetAllDefinitions();
            var sb = new StringBuilder();
            sb.AppendLine($"<color=orange>[ Registered Status Effects ({defs.Count}) ]</color>");
            foreach (var def in defs)
            {
                string charges = def.InitialCharges > 0 ? $", Charges: {def.InitialCharges}" : "";
                string shield = def.InitialShieldCapacity > 0 ? $", Barrier: {def.InitialShieldCapacity:F0}" : "";
                sb.AppendLine($"  • <b>{def.Name}</b> (\"{def.DisplayName}\") [Duration: {def.BaseDuration:F0}s, MaxStacks: {def.MaxStacks}{charges}{shield}]");
            }
            global::Console.instance?.Print(sb.ToString().TrimEnd());
        }

        private static void ApplyToPlayer(string name, int stacks, float duration)
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                global::Console.instance?.Print("[VikingLib] No local player active in world.");
                return;
            }

            var se = StatusEffectRegistry.Apply(player, name, stacks, duration);
            if (se != null)
            {
                global::Console.instance?.Print($"[VikingLib] Applied '{name}' (x{stacks}) to {player.GetPlayerName()}!");
            }
            else
            {
                global::Console.instance?.Print($"[VikingLib] Failed to apply '{name}'. Is it registered in StatusEffectRegistry?");
            }
        }

        private static void ClearPlayerEffects(string? name)
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                global::Console.instance?.Print("[VikingLib] No local player active in world.");
                return;
            }

            var seman = player.GetSEMan();
            if (seman == null) return;

            if (string.IsNullOrEmpty(name))
            {
                var active = seman.GetStatusEffects();
                int cleared = 0;
                for (int i = active.Count - 1; i >= 0; i--)
                {
                    if (active[i] is VikingStatusEffect vse)
                    {
                        seman.RemoveStatusEffect(vse);
                        cleared++;
                    }
                }
                global::Console.instance?.Print($"[VikingLib] Cleared {cleared} VikingLib status effects from player.");
            }
            else
            {
                bool removed = StatusEffectRegistry.Remove(player, name);
                global::Console.instance?.Print(removed
                    ? $"[VikingLib] Removed '{name}' from player."
                    : $"[VikingLib] Effect '{name}' was not active on player.");
            }
        }

        private static void PrintInfo(string name)
        {
            var def = StatusEffectRegistry.GetDefinition(name);
            if (def == null)
            {
                global::Console.instance?.Print($"[VikingLib] Status effect '{name}' not found.");
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"<color=orange>[ Status Effect: {def.Name} ]</color>");
            sb.AppendLine($"  Display Name: {def.DisplayName}");
            sb.AppendLine($"  Tooltip: {def.Tooltip}");
            sb.AppendLine($"  Base Duration: {def.BaseDuration:F1}s");
            sb.AppendLine($"  Max Stacks: {def.MaxStacks} ({def.StackPolicy})");
            if (def.InitialCharges > 0)
            {
                sb.AppendLine($"  Initial Charges: {def.InitialCharges} (Trigger: {def.ChargeConsumptionTrigger})");
            }
            if (def.InitialShieldCapacity > 0f)
            {
                sb.AppendLine($"  Barrier Capacity: {def.InitialShieldCapacity:F0} (+{def.ShieldCapacityPerStack:F0}/stack)");
            }
            if (def.PeriodicDamageType.HasValue && def.PeriodicDamageAmount > 0f)
            {
                sb.AppendLine($"  Periodic Damage: {def.PeriodicDamageAmount:F1} {def.PeriodicDamageType.Value} every {def.PeriodicDamageInterval:F1}s (Ignores Armor: {def.PeriodicIgnoresArmor})");
            }
            if (def.Modifiers.Count > 0)
            {
                sb.AppendLine("  Modifiers per stack:");
                foreach (var mod in def.Modifiers)
                {
                    string stat = mod.CustomStatKey ?? mod.StatType?.ToString() ?? "Unknown";
                    sb.AppendLine($"    • {stat}: {mod.Tier} {mod.ValuePerStack}");
                }
            }

            global::Console.instance?.Print(sb.ToString().TrimEnd());
        }
    }
}
