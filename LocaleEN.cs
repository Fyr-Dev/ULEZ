using System.Collections.Generic;
using Colossal;

namespace ULEZ
{
    /// <summary>
    /// English locale strings for the ULEZ mod settings UI.
    /// </summary>
    public class LocaleEN : IDictionarySource
    {
        private readonly Setting _setting;

        public LocaleEN(Setting setting)
        {
            _setting = setting;
        }

        public IEnumerable<KeyValuePair<string, string>> ReadEntries(
            IList<IDictionaryEntryError> errors,
            Dictionary<string, int> indexCounts)
        {
            return new Dictionary<string, string>
            {
                // Mod name
                { _setting.GetSettingsLocaleID(), "ULEZ - Ultra Low Emission Zone" },

                // Section
                { _setting.GetOptionTabLocaleID(Setting.kSection), "ULEZ Settings" },

                // Groups
                { _setting.GetOptionGroupLocaleID(Setting.kChargeGroup), "Charges & Costs" },
                { _setting.GetOptionGroupLocaleID(Setting.kGameplayGroup), "Gameplay" },
                { _setting.GetOptionGroupLocaleID(Setting.kAboutGroup), "About" },

                // Charge settings
                {
                    _setting.GetOptionLabelLocaleID(nameof(Setting.DailyCharge)),
                    "ULEZ Daily Charge"
                },
                {
                    _setting.GetOptionDescLocaleID(nameof(Setting.DailyCharge)),
                    "The amount charged per personal vehicle trip through a ULEZ zone. Higher charges discourage driving more effectively."
                },

                {
                    _setting.GetOptionLabelLocaleID(nameof(Setting.PathCostMultiplier)),
                    "Charge Multiplier"
                },
                {
                    _setting.GetOptionDescLocaleID(nameof(Setting.PathCostMultiplier)),
                    "Multiplier applied to the base ULEZ charge. 1.0 = base charge only, 3.0 = triple the base charge."
                },

                // Gameplay settings
                {
                    _setting.GetOptionLabelLocaleID(nameof(Setting.Enabled)),
                    "Enable ULEZ System"
                },
                {
                    _setting.GetOptionDescLocaleID(nameof(Setting.Enabled)),
                    "Master toggle for the ULEZ system. When disabled, no charges are applied and pathfinding is unaffected."
                },

                {
                    _setting.GetOptionLabelLocaleID(nameof(Setting.MaxVehiclesPerScan)),
                    "Vehicle Scan Budget"
                },
                {
                    _setting.GetOptionDescLocaleID(nameof(Setting.MaxVehiclesPerScan)),
                    "Maximum number of moving vehicles the mod inspects in a single scan. Lower values reduce stutter in large cities but make charges update more gradually."
                },
                {
                    _setting.GetOptionLabelLocaleID(nameof(Setting.DebugLogging)),
                    "Debug Logging"
                },
                {
                    _setting.GetOptionDescLocaleID(nameof(Setting.DebugLogging)),
                    "Enable verbose diagnostic logging for troubleshooting. Leave this off for normal play."
                },

                // About
                {
                    _setting.GetOptionLabelLocaleID(nameof(Setting.ResetSettings)),
                    "Reset to Defaults"
                },
                {
                    _setting.GetOptionDescLocaleID(nameof(Setting.ResetSettings)),
                    "Reset all ULEZ settings back to their default values."
                },
            };
        }

        public void Unload() { }
    }
}
