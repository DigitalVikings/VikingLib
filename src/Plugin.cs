using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using Jotunn.Managers;
using VikingLib.Combat.Telemetry;
using VikingLib.Configuration;
using VikingLib.StatusEffects;

namespace VikingLib
{
    [BepInPlugin(ModGuid, ModName, ModVersion)]
    [BepInDependency(Jotunn.Main.ModGuid, BepInDependency.DependencyFlags.HardDependency)]
    public class VikingLibPlugin : BaseUnityPlugin
    {
        public const string ModGuid = "com.digitalvikings.vikinglib";
        public const string ModName = "VikingLib";
        public const string ModVersion = "0.2.2";

        public static VikingLibPlugin Instance { get; private set; } = null!;
        public static ManualLogSource Log { get; internal set; } = null!;
        private Harmony _harmony = null!;

        private void Awake()
        {
            Instance = this;
            Log = Logger;
            Log.LogInfo($"{ModName} v{ModVersion} initializing...");

            // Initialize configuration
            ModConfig.Init(Config);

            // Apply Harmony patches
            _harmony = new Harmony(ModGuid);
            _harmony.PatchAll();

            // Initialize canonical status effects
            CanonicalStatusEffects.Init();

            // Register in-game terminal debug commands
            CommandManager.Instance.AddConsoleCommand(new TraceConsoleCommand());
            CommandManager.Instance.AddConsoleCommand(new LogConsoleCommand());
            CommandManager.Instance.AddConsoleCommand(new ArmorConsoleCommand());
            CommandManager.Instance.AddConsoleCommand(new DumpAttacksConsoleCommand());
            CommandManager.Instance.AddConsoleCommand(new BuffTestConsoleCommand());
            CommandManager.Instance.AddConsoleCommand(new StatusEffectConsoleCommand());

            // Listen for server synchronization events
            HookJotunnSync();

            Log.LogInfo($"{ModName} v{ModVersion} loaded successfully. Combat pipeline and telemetry active.");
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private void HookJotunnSync()
        {
            SynchronizationManager.OnConfigurationSynchronized += (sender, args) =>
            {
                Log.LogInfo("Server configuration synchronized successfully.");
            };
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
