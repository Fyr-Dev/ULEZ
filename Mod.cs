using Colossal.IO.AssetDatabase;
using Colossal.Logging;
using Game;
using Game.Modding;
using Game.SceneFlow;

namespace ULEZ
{
    /// <summary>
    /// Main mod entry point for the ULEZ (Ultra Low Emission Zone) mod.
    /// Applies district-based private-car charging for selected areas.
    /// </summary>
    public class Mod : IMod
    {
        public static ILog Log = LogManager.GetLogger(nameof(ULEZ));
        public static Setting Settings { get; private set; }
        public static bool DebugLoggingEnabled => Settings?.DebugLogging ?? false;

        public static void Debug(string message)
        {
            if (DebugLoggingEnabled)
                Log.Info(message);
        }

        public static void DebugWarn(string message)
        {
            if (DebugLoggingEnabled)
                Log.Warn(message);
        }

        public void OnLoad(UpdateSystem updateSystem)
        {
            Log.Info("Loading ULEZ mod...");

            // Initialize settings
            Settings = new Setting(this);
            Settings.RegisterInOptionsUI();

            // Load saved settings
            GameManager.instance.localizationManager.AddSource("en-US", new LocaleEN(Settings));
            AssetDatabase.global.LoadSettings(nameof(ULEZ), Settings, new Setting(this));

            // Register game systems
            updateSystem.UpdateAt<Systems.ULEZPolicySystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<Systems.ULEZDistrictUISystem>(SystemUpdatePhase.UIUpdate);
            updateSystem.UpdateAt<Systems.ULEZVehicleChargeSystem>(SystemUpdatePhase.GameSimulation);
            updateSystem.UpdateAt<Systems.ULEZDailyRevenueSystem>(SystemUpdatePhase.GameSimulation);

            Log.Info("ULEZ mod loaded successfully.");
        }

        public void OnDispose()
        {
            Log.Info("Disposing ULEZ mod...");

            // Deregister settings
            if (Settings != null)
            {
                Settings.UnregisterInOptionsUI();
                Settings = null;
            }

            Log.Info("ULEZ mod disposed.");
        }
    }
}
