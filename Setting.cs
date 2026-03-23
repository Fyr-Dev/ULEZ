using Colossal.IO.AssetDatabase;
using Game.Modding;
using Game.Settings;
using Game.UI;
using Game.UI.Widgets;

namespace ULEZ
{
    [FileLocation(nameof(ULEZ))]
    [SettingsUIGroupOrder(kChargeGroup, kGameplayGroup, kAboutGroup)]
    [SettingsUIShowGroupName(kChargeGroup, kGameplayGroup, kAboutGroup)]
    public class Setting : ModSetting
    {
        public const string kSection = "Main";
        public const string kChargeGroup = "Charges";
        public const string kGameplayGroup = "Gameplay";
        public const string kAboutGroup = "About";

        public Setting(IMod mod) : base(mod) { }

        [SettingsUISection(kSection, kGameplayGroup)]
        public bool Enabled { get; set; } = true;

        [SettingsUISection(kSection, kChargeGroup)]
        [SettingsUISlider(min = 5, max = 500, step = 5, unit = Unit.kMoney)]
        public int DailyCharge { get; set; } = 50;

        [SettingsUISection(kSection, kChargeGroup)]
        [SettingsUISlider(min = 1f, max = 10f, step = 0.5f, unit = Unit.kFloatSingleFraction)]
        public float PathCostMultiplier { get; set; } = 3.0f;

        [SettingsUISection(kSection, kGameplayGroup)]
        [SettingsUISlider(min = 50, max = 2000, step = 50, unit = Unit.kInteger)]
        public int MaxVehiclesPerScan { get; set; } = 400;

        [SettingsUISection(kSection, kAboutGroup)]
        public bool DebugLogging { get; set; } = false;

        public float ChargeMultiplier => PathCostMultiplier;

        [SettingsUISection(kSection, kAboutGroup)]
        [SettingsUIButton]
        public bool ResetSettings
        {
            set
            {
                SetDefaults();
                ApplyAndSave();
            }
        }

        public override void SetDefaults()
        {
            Enabled = true;
            DailyCharge = 50;
            PathCostMultiplier = 3.0f;
            MaxVehiclesPerScan = 400;
            DebugLogging = false;
        }
    }
}
