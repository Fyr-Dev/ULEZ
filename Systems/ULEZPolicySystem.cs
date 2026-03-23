using System.Collections.Generic;
using Colossal.Serialization.Entities;
using Game;
using Game.Areas;
using Game.Serialization;
using Game.Simulation;
using Unity.Entities;

namespace ULEZ.Systems
{
    /// <summary>
    /// Tracks which districts have ULEZ enabled.
    /// </summary>
    public partial class ULEZPolicySystem : GameSystemBase, IDefaultSerializable
    {
        public struct DistrictReportSnapshot
        {
            public Entity District;
            public int ActivationDay;
            public int BaselineDays;
            public int LifetimeTraffic;
            public int LifetimeCharges;
            public int LifetimeRevenue;
            public int HistoricalDays;
            public int CurrentDayTraffic;
            public int CurrentDayCharges;
            public int CurrentDayRevenue;
            public int LastDayTraffic;
            public int LastDayCharges;
            public int LastDayRevenue;
            public float BaselineAverageTraffic;
        }

        public struct SystemReportSnapshot
        {
            public int ActiveDistricts;
            public int TrackedDistricts;
            public int CurrentDayTraffic;
            public int CurrentDayCharges;
            public int CurrentDayRevenue;
            public int LastDayTraffic;
            public int LifetimeTraffic;
            public int LifetimeCharges;
            public int LifetimeRevenue;
            public float ActiveBaselineAverageTraffic;
        }

        private struct DistrictAnalytics
        {
            public int ActivationDay;
            public int BaselineDays;
            public int BaselineTraffic;
            public int HistoricalDays;
            public int HistoricalTraffic;
            public int HistoricalCharges;
            public int HistoricalRevenue;
            public int CurrentDayTraffic;
            public int CurrentDayCharges;
            public int CurrentDayRevenue;
            public int LastDayTraffic;
            public int LastDayCharges;
            public int LastDayRevenue;
        }

        private readonly HashSet<Entity> _ulezDistricts = new HashSet<Entity>();
        private readonly Dictionary<Entity, DistrictAnalytics> _districtAnalytics = new Dictionary<Entity, DistrictAnalytics>();
        private SerializerSystem _serializerSystem;
        private SimulationSystem _simulationSystem;
        private int _version = 1;

        public int Version => _version;

        protected override void OnCreate()
        {
            base.OnCreate();
            _serializerSystem = World.GetOrCreateSystemManaged<SerializerSystem>();
            _simulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();

            Mod.Debug("ULEZPolicySystem created.");
        }

        protected override void OnUpdate()
        {
        }

        /// <summary>
        /// Returns true if ULEZ is enabled for the district.
        /// </summary>
        public bool IsULEZActive(Entity district)
        {
            if (district == Entity.Null || !_ulezDistricts.Contains(district))
                return false;

            if (!EntityManager.Exists(district) || !EntityManager.HasComponent<District>(district))
            {
                _ulezDistricts.Remove(district);
                return false;
            }

            return true;
        }

        public void SetULEZActive(Entity district, bool active)
        {
            if (district == Entity.Null)
                return;

            var analytics = EnsureAnalytics(district);

            bool changed;
            if (active)
                changed = _ulezDistricts.Add(district);
            else
                changed = _ulezDistricts.Remove(district);

            if (!changed)
                return;

            if (active && analytics.ActivationDay < 0)
            {
                analytics.ActivationDay = GetCurrentDay();
                _districtAnalytics[district] = analytics;
            }

            IncrementVersion();
            _serializerSystem?.SetDirty();
        }

        public void ToggleULEZ(Entity district)
        {
            SetULEZActive(district, !IsULEZActive(district));
        }

        /// <summary>
        /// Counts how many districts currently have ULEZ active.
        /// </summary>
        public int CountActiveULEZDistricts()
        {
            if (_ulezDistricts.Count == 0)
                return 0;

            var staleDistricts = new List<Entity>();
            foreach (var district in _ulezDistricts)
            {
                if (!EntityManager.Exists(district) || !EntityManager.HasComponent<District>(district))
                    staleDistricts.Add(district);
            }

            for (int index = 0; index < staleDistricts.Count; index++)
                _ulezDistricts.Remove(staleDistricts[index]);

            return _ulezDistricts.Count;
        }

        /// <summary>
        /// Returns true if the mod is enabled and at least one district has ULEZ active.
        /// </summary>
        public bool IsAnyDistrictActive()
        {
            if (Mod.Settings == null || !Mod.Settings.Enabled)
                return false;
            return _ulezDistricts.Count > 0;
        }

        public bool HasTrackedDistricts()
        {
            if (Mod.Settings == null || !Mod.Settings.Enabled)
                return false;

            return _districtAnalytics.Count > 0;
        }

        public void EnsureDistrictTracked(Entity district)
        {
            if (district == Entity.Null || !IsDistrictEntityValid(district))
                return;

            if (_districtAnalytics.ContainsKey(district))
                return;

            EnsureAnalytics(district);
            _serializerSystem?.SetDirty();
        }

        public bool IsDistrictTracked(Entity district)
        {
            return district != Entity.Null && _districtAnalytics.ContainsKey(district);
        }

        public void RecordTrafficObservation(Entity district)
        {
            if (district == Entity.Null)
                return;

            var analytics = EnsureAnalytics(district);
            analytics.CurrentDayTraffic++;
            _districtAnalytics[district] = analytics;
        }

        public void RecordCharge(Entity district, int amount)
        {
            if (district == Entity.Null || amount <= 0)
                return;

            var analytics = EnsureAnalytics(district);
            analytics.CurrentDayCharges++;
            analytics.CurrentDayRevenue += amount;
            _districtAnalytics[district] = analytics;
        }

        public void CompleteDay()
        {
            var trackedDistricts = new List<Entity>(_districtAnalytics.Keys);
            bool changed = false;

            for (int index = 0; index < trackedDistricts.Count; index++)
            {
                Entity district = trackedDistricts[index];
                if (!IsDistrictEntityValid(district))
                {
                    _districtAnalytics.Remove(district);
                    _ulezDistricts.Remove(district);
                    changed = true;
                    continue;
                }

                var analytics = _districtAnalytics[district];
                bool isActive = _ulezDistricts.Contains(district);
                bool hasActivity = analytics.CurrentDayTraffic > 0 || analytics.CurrentDayCharges > 0 || analytics.CurrentDayRevenue > 0;
                if (!hasActivity)
                    continue;

                analytics.LastDayTraffic = analytics.CurrentDayTraffic;
                analytics.LastDayCharges = analytics.CurrentDayCharges;
                analytics.LastDayRevenue = analytics.CurrentDayRevenue;

                if (analytics.ActivationDay < 0)
                {
                    analytics.BaselineTraffic += analytics.CurrentDayTraffic;
                    analytics.BaselineDays++;
                }
                else if (isActive)
                {
                    analytics.HistoricalTraffic += analytics.CurrentDayTraffic;
                    analytics.HistoricalCharges += analytics.CurrentDayCharges;
                    analytics.HistoricalRevenue += analytics.CurrentDayRevenue;
                    analytics.HistoricalDays++;
                }

                analytics.CurrentDayTraffic = 0;
                analytics.CurrentDayCharges = 0;
                analytics.CurrentDayRevenue = 0;
                _districtAnalytics[district] = analytics;
                changed = true;
            }

            if (changed)
                _serializerSystem?.SetDirty();
        }

        public bool TryGetTopDistrictReport(out DistrictReportSnapshot report)
        {
            report = default;
            bool found = false;

            foreach (var pair in _districtAnalytics)
            {
                Entity district = pair.Key;
                DistrictAnalytics analytics = pair.Value;
                if (!IsDistrictEntityValid(district) || analytics.ActivationDay < 0 || analytics.LastDayTraffic <= 0)
                    continue;

                if (!found || analytics.LastDayTraffic > report.LastDayTraffic)
                {
                    report = CreateSnapshot(district, analytics);
                    found = true;
                }
            }

            return found;
        }

        public bool TryGetDistrictReport(Entity district, out DistrictReportSnapshot report)
        {
            report = default;
            if (!_districtAnalytics.TryGetValue(district, out DistrictAnalytics analytics) || !IsDistrictEntityValid(district))
                return false;

            report = CreateSnapshot(district, analytics);
            return true;
        }

        public int GetLifetimeRevenueTotal()
        {
            int total = 0;
            foreach (var analytics in _districtAnalytics.Values)
                total += analytics.HistoricalRevenue + analytics.CurrentDayRevenue;
            return total;
        }

        public int GetLifetimeTrafficTotal()
        {
            int total = 0;
            foreach (var analytics in _districtAnalytics.Values)
                total += analytics.BaselineTraffic + analytics.HistoricalTraffic + analytics.CurrentDayTraffic;
            return total;
        }

        public int GetLifetimeChargeTotal()
        {
            int total = 0;
            foreach (var analytics in _districtAnalytics.Values)
                total += analytics.HistoricalCharges + analytics.CurrentDayCharges;
            return total;
        }

        public SystemReportSnapshot GetSystemReport()
        {
            SystemReportSnapshot report = default;
            float baselineTotal = 0f;
            int baselineDistricts = 0;

            report.ActiveDistricts = CountActiveULEZDistricts();

            foreach (var pair in _districtAnalytics)
            {
                if (!IsDistrictEntityValid(pair.Key))
                    continue;

                DistrictAnalytics analytics = pair.Value;
                report.TrackedDistricts++;
                report.CurrentDayTraffic += analytics.CurrentDayTraffic;
                report.CurrentDayCharges += analytics.CurrentDayCharges;
                report.CurrentDayRevenue += analytics.CurrentDayRevenue;
                report.LastDayTraffic += analytics.LastDayTraffic;
                report.LifetimeTraffic += analytics.BaselineTraffic + analytics.HistoricalTraffic + analytics.CurrentDayTraffic;
                report.LifetimeCharges += analytics.HistoricalCharges + analytics.CurrentDayCharges;
                report.LifetimeRevenue += analytics.HistoricalRevenue + analytics.CurrentDayRevenue;

                if (analytics.BaselineDays > 0 && analytics.ActivationDay >= 0)
                {
                    baselineTotal += (float)analytics.BaselineTraffic / analytics.BaselineDays;
                    baselineDistricts++;
                }
            }

            if (baselineDistricts > 0)
                report.ActiveBaselineAverageTraffic = baselineTotal / baselineDistricts;

            return report;
        }

        public void Serialize<TWriter>(TWriter writer) where TWriter : IWriter
        {
            var block = writer.Begin();
            writer.Write(_ulezDistricts.Count);
            foreach (var district in _ulezDistricts)
            {
                writer.Write(district);
            }

            writer.Write(_districtAnalytics.Count);
            foreach (var pair in _districtAnalytics)
            {
                writer.Write(pair.Key);
                writer.Write(pair.Value.ActivationDay);
                writer.Write(pair.Value.HistoricalDays);
                writer.Write(pair.Value.HistoricalCharges);
                writer.Write(pair.Value.HistoricalRevenue);
                writer.Write(pair.Value.CurrentDayCharges);
                writer.Write(pair.Value.CurrentDayRevenue);
                writer.Write(pair.Value.LastDayCharges);
                writer.Write(pair.Value.LastDayRevenue);
            }

            writer.Write(_districtAnalytics.Count);
            foreach (var pair in _districtAnalytics)
            {
                writer.Write(pair.Key);
                writer.Write(pair.Value.BaselineDays);
                writer.Write(pair.Value.BaselineTraffic);
                writer.Write(pair.Value.HistoricalTraffic);
                writer.Write(pair.Value.CurrentDayTraffic);
                writer.Write(pair.Value.LastDayTraffic);
            }
            writer.End(block);
        }

        public void Deserialize<TReader>(TReader reader) where TReader : IReader
        {
            _ulezDistricts.Clear();
            _districtAnalytics.Clear();

            var block = reader.Begin();
            reader.Read(out int count);
            for (int index = 0; index < count; index++)
            {
                reader.Read(out Entity district);
                if (district != Entity.Null)
                    _ulezDistricts.Add(district);
            }

            try
            {
                reader.Read(out int analyticsCount);
                for (int index = 0; index < analyticsCount; index++)
                {
                    reader.Read(out Entity district);
                    if (district == Entity.Null)
                    {
                        SkipAnalyticsRecord(reader);
                        continue;
                    }

                    DistrictAnalytics analytics = default;
                    reader.Read(out analytics.ActivationDay);
                    reader.Read(out analytics.HistoricalDays);
                    reader.Read(out analytics.HistoricalCharges);
                    reader.Read(out analytics.HistoricalRevenue);
                    reader.Read(out analytics.CurrentDayCharges);
                    reader.Read(out analytics.CurrentDayRevenue);
                    reader.Read(out analytics.LastDayCharges);
                    reader.Read(out analytics.LastDayRevenue);
                    _districtAnalytics[district] = analytics;
                }

                reader.Read(out int trafficAnalyticsCount);
                for (int index = 0; index < trafficAnalyticsCount; index++)
                {
                    reader.Read(out Entity district);
                    if (district == Entity.Null)
                    {
                        SkipTrafficAnalyticsRecord(reader);
                        continue;
                    }

                    DistrictAnalytics analytics = EnsureAnalytics(district);
                    reader.Read(out analytics.BaselineDays);
                    reader.Read(out analytics.BaselineTraffic);
                    reader.Read(out analytics.HistoricalTraffic);
                    reader.Read(out analytics.CurrentDayTraffic);
                    reader.Read(out analytics.LastDayTraffic);
                    _districtAnalytics[district] = analytics;
                }
            }
            catch
            {
                // Older saves only persisted active district entities.
            }
            reader.End(block);

            IncrementVersion();
        }

        public void SetDefaults(Context context)
        {
            _ulezDistricts.Clear();
            _districtAnalytics.Clear();
            IncrementVersion();
        }

        private DistrictAnalytics EnsureAnalytics(Entity district)
        {
            if (_districtAnalytics.TryGetValue(district, out DistrictAnalytics analytics))
                return analytics;

            analytics = new DistrictAnalytics
            {
                ActivationDay = -1
            };
            _districtAnalytics[district] = analytics;
            return analytics;
        }

        private DistrictReportSnapshot CreateSnapshot(Entity district, DistrictAnalytics analytics)
        {
            float baselineAverageTraffic = 0f;
            if (analytics.BaselineDays > 0)
                baselineAverageTraffic = (float)analytics.BaselineTraffic / analytics.BaselineDays;

            return new DistrictReportSnapshot
            {
                District = district,
                ActivationDay = analytics.ActivationDay,
                BaselineDays = analytics.BaselineDays,
                LifetimeTraffic = analytics.BaselineTraffic + analytics.HistoricalTraffic + analytics.CurrentDayTraffic,
                LifetimeCharges = analytics.HistoricalCharges + analytics.CurrentDayCharges,
                LifetimeRevenue = analytics.HistoricalRevenue + analytics.CurrentDayRevenue,
                HistoricalDays = analytics.HistoricalDays,
                CurrentDayTraffic = analytics.CurrentDayTraffic,
                CurrentDayCharges = analytics.CurrentDayCharges,
                CurrentDayRevenue = analytics.CurrentDayRevenue,
                LastDayTraffic = analytics.LastDayTraffic,
                LastDayCharges = analytics.LastDayCharges,
                LastDayRevenue = analytics.LastDayRevenue,
                BaselineAverageTraffic = baselineAverageTraffic
            };
        }

        private int GetCurrentDay()
        {
            return (int)(_simulationSystem.frameIndex / 262144u);
        }

        private bool IsDistrictEntityValid(Entity district)
        {
            return district != Entity.Null && EntityManager.Exists(district) && EntityManager.HasComponent<District>(district);
        }

        private static void SkipAnalyticsRecord<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int _);
            reader.Read(out int _);
            reader.Read(out int _);
            reader.Read(out int _);
            reader.Read(out int _);
            reader.Read(out int _);
            reader.Read(out int _);
            reader.Read(out int _);
        }

        private static void SkipTrafficAnalyticsRecord<TReader>(TReader reader) where TReader : IReader
        {
            reader.Read(out int _);
            reader.Read(out int _);
            reader.Read(out int _);
            reader.Read(out int _);
            reader.Read(out int _);
        }

        private void IncrementVersion()
        {
            unchecked
            {
                _version++;
                if (_version == int.MinValue)
                    _version = 1;
            }
        }
    }
}
