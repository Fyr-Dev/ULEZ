using Game;
using Game.Simulation;
using Game.UI;
using Game.UI.InGame;
using ULEZ.Bridge;
using Unity.Entities;

namespace ULEZ.Systems
{
    /// <summary>
    /// Tracks daily ULEZ revenue and posts a summary chirp at the end of each 
    /// in-game day using CustomChirps. Reports total charges collected, number 
    /// of vehicles charged, and the active ULEZ zone count.
    /// </summary>
    public partial class ULEZDailyRevenueSystem : GameSystemBase
    {
        private SimulationSystem _simulationSystem;
        private ULEZPolicySystem _policySystem;
        private NameSystem _nameSystem;
        private int _currentDayRevenue;
        private int _currentDayCharges;
        private int _lastReportDay = -1;
        private bool _loggedBridgeAvailability;

        /// <summary>
        /// Add revenue from a vehicle charge. Called by ULEZVehicleChargeSystem.
        /// </summary>
        public void RecordCharge(Entity district, int amount)
        {
            _currentDayRevenue += amount;
            _currentDayCharges++;
            _policySystem.RecordCharge(district, amount);
        }

        protected override void OnCreate()
        {
            base.OnCreate();
            _simulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            _policySystem = World.GetOrCreateSystemManaged<ULEZPolicySystem>();
            _nameSystem = World.GetOrCreateSystemManaged<NameSystem>();
            Mod.Debug("ULEZDailyRevenueSystem created.");
        }

        protected override void OnUpdate()
        {
            if (Mod.Settings == null || !Mod.Settings.Enabled)
                return;

            if (!_loggedBridgeAvailability)
            {
                Mod.Debug($"ULEZ: CustomChirps available={CustomChirpsBridge.IsAvailable}");
                _loggedBridgeAvailability = true;
            }

            int currentDay = GetCurrentDay();

            if (_lastReportDay < 0)
            {
                _lastReportDay = currentDay;
                return;
            }

            if (currentDay != _lastReportDay)
            {
                _policySystem.CompleteDay();
                PostDailyChirp();
                _currentDayRevenue = 0;
                _currentDayCharges = 0;
                _lastReportDay = currentDay;
            }
        }

        private int GetCurrentDay()
        {
            uint frame = _simulationSystem.frameIndex;
            return (int)(frame / 262144u);
        }

        private void PostDailyChirp()
        {
            int zoneCount = _policySystem.CountActiveULEZDistricts();
            if (_currentDayCharges == 0 && zoneCount == 0)
                return;

            string zoneDesc = zoneCount == 1 ? "1 active ULEZ district" : $"{zoneCount} active ULEZ districts";
            string headline = _currentDayCharges > 0
                ? $"ULEZ update: £{_currentDayRevenue:N0} collected today from {_currentDayCharges:N0} charged cars across {zoneDesc}."
                : $"ULEZ update: no charges were collected today across {zoneDesc}.";

            string districtTrend = BuildDistrictTrendText();
            string lifetimeSummary = BuildLifetimeSummaryText();
            string message = string.Join(" ", new[] { headline, districtTrend, lifetimeSummary }).Trim();

            Mod.Debug(message);

            if (CustomChirpsBridge.IsAvailable)
            {
                bool posted = CustomChirpsBridge.PostChirp(
                    text: message,
                    department: DepartmentAccountBridge.EnvironmentalProtectionAgency,
                    entity: Entity.Null,
                    customSenderName: "ULEZ Authority"
                );

                if (!posted)
                    Mod.Log.Warn("ULEZ: Failed to post daily CustomChirps revenue update.");
            }
            else
            {
                Mod.Debug("ULEZ: CustomChirps not available, skipping daily chirp.");
            }
        }

        private string BuildDistrictTrendText()
        {
            ULEZPolicySystem.SystemReportSnapshot systemReport = _policySystem.GetSystemReport();
            if (!_policySystem.TryGetTopDistrictReport(out ULEZPolicySystem.DistrictReportSnapshot report))
                return systemReport.CurrentDayTraffic > 0
                    ? $"Tracked districts saw {systemReport.CurrentDayTraffic:N0} cars today."
                    : "Tracked districts stayed quiet today.";

            string districtName = GetDistrictName(report.District);
            string priorComparison;
            if (report.BaselineDays <= 0)
            {
                priorComparison = "We are still collecting before-ULEZ traffic for that district.";
            }
            else if (report.BaselineAverageTraffic <= 0.01f)
            {
                priorComparison = report.LastDayTraffic > 0
                    ? "Traffic there is above its old pre-ULEZ level."
                    : "Traffic there is still close to its old near-zero level.";
            }
            else
            {
                float deltaPercent = ((report.LastDayTraffic - report.BaselineAverageTraffic) / report.BaselineAverageTraffic) * 100f;
                int roundedPercent = (int)System.Math.Round(System.Math.Abs(deltaPercent), System.MidpointRounding.AwayFromZero);

                if (deltaPercent <= -10f)
                    priorComparison = $"Traffic there was {roundedPercent}% lower than before ULEZ.";
                else if (deltaPercent >= 10f)
                    priorComparison = $"Traffic there was {roundedPercent}% higher than before ULEZ.";
                else
                    priorComparison = "Traffic there stayed close to the pre-ULEZ average.";
            }

            int trafficShare = systemReport.LastDayTraffic > 0
                ? (int)System.Math.Round(report.LastDayTraffic * 100f / systemReport.LastDayTraffic, System.MidpointRounding.AwayFromZero)
                : 0;

            return $"{districtName} was the busiest ULEZ district today with {report.LastDayTraffic:N0} cars seen, £{report.LastDayRevenue:N0} collected, and {report.LastDayCharges:N0} charged cars. {priorComparison} That was {trafficShare:N0}% of all tracked district traffic today.";
        }

        private string BuildLifetimeSummaryText()
        {
            int lifetimeTraffic = _policySystem.GetLifetimeTrafficTotal();
            int lifetimeRevenue = _policySystem.GetLifetimeRevenueTotal();
            int lifetimeCharges = _policySystem.GetLifetimeChargeTotal();

            if (_policySystem.TryGetTopDistrictReport(out ULEZPolicySystem.DistrictReportSnapshot report))
            {
                string districtName = GetDistrictName(report.District);
                return $"Since tracking began, {districtName} has seen {report.LifetimeTraffic:N0} cars and collected £{report.LifetimeRevenue:N0} from {report.LifetimeCharges:N0} charged cars. Across the whole system, ULEZ has seen {lifetimeTraffic:N0} cars and collected £{lifetimeRevenue:N0} from {lifetimeCharges:N0} charged cars.";
            }

            return $"System total since rollout: {lifetimeTraffic:N0} cars tracked, £{lifetimeRevenue:N0} collected, and {lifetimeCharges:N0} charged cars.";
        }

        private string GetDistrictName(Entity district)
        {
            string rendered = _nameSystem?.GetRenderedLabelName(district);
            if (!string.IsNullOrWhiteSpace(rendered))
                return rendered;

            return $"District #{district.Index}";
        }
    }
}
