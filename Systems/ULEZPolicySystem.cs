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
        private const int HoursPerDay = 24;

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
            public int CurrentHourIndex;
            public int[] CurrentDayTrafficByHour;
            public int[] LastDayTrafficByHour;
            public float[] BaselineAverageTrafficByHour;
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
            public int CurrentHourIndex;
            public int[] CurrentDayTrafficByHour;
            public int[] LastDayTrafficByHour;
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
            public int[] BaselineHourlyTraffic;
            public int[] CurrentDayTrafficByHour;
            public int[] LastDayTrafficByHour;
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
            int hourIndex = GetCurrentHourIndex();
            analytics.CurrentDayTraffic++;
            analytics.CurrentDayTrafficByHour[hourIndex]++;
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
                EnsureAnalyticsArrays(ref analytics);
                bool isActive = _ulezDistricts.Contains(district);
                bool hasActivity = analytics.CurrentDayTraffic > 0 || analytics.CurrentDayCharges > 0 || analytics.CurrentDayRevenue > 0;
                if (!hasActivity)
                    continue;

                analytics.LastDayTraffic = analytics.CurrentDayTraffic;
                analytics.LastDayCharges = analytics.CurrentDayCharges;
                analytics.LastDayRevenue = analytics.CurrentDayRevenue;
                CopyHourlyData(analytics.CurrentDayTrafficByHour, analytics.LastDayTrafficByHour);

                if (analytics.ActivationDay < 0)
                {
                    analytics.BaselineTraffic += analytics.CurrentDayTraffic;
                    analytics.BaselineDays++;
                    AddHourlyData(analytics.BaselineHourlyTraffic, analytics.CurrentDayTrafficByHour);
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
                ClearHourlyData(analytics.CurrentDayTrafficByHour);
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
            SystemReportSnapshot report = new SystemReportSnapshot
            {
                CurrentHourIndex = GetCurrentHourIndex(),
                CurrentDayTrafficByHour = CreateHourlyBuckets(),
                LastDayTrafficByHour = CreateHourlyBuckets()
            };
            float baselineTotal = 0f;
            int baselineDistricts = 0;

            report.ActiveDistricts = CountActiveULEZDistricts();

            foreach (var pair in _districtAnalytics)
            {
                if (!IsDistrictEntityValid(pair.Key))
                    continue;

                DistrictAnalytics analytics = pair.Value;
                EnsureAnalyticsArrays(ref analytics);
                report.TrackedDistricts++;
                report.CurrentDayTraffic += analytics.CurrentDayTraffic;
                report.CurrentDayCharges += analytics.CurrentDayCharges;
                report.CurrentDayRevenue += analytics.CurrentDayRevenue;
                report.LastDayTraffic += analytics.LastDayTraffic;
                report.LifetimeTraffic += analytics.BaselineTraffic + analytics.HistoricalTraffic + analytics.CurrentDayTraffic;
                report.LifetimeCharges += analytics.HistoricalCharges + analytics.CurrentDayCharges;
                report.LifetimeRevenue += analytics.HistoricalRevenue + analytics.CurrentDayRevenue;
                AddHourlyData(report.CurrentDayTrafficByHour, analytics.CurrentDayTrafficByHour);
                AddHourlyData(report.LastDayTrafficByHour, analytics.LastDayTrafficByHour);

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

            writer.Write(_districtAnalytics.Count);
            foreach (var pair in _districtAnalytics)
            {
                writer.Write(pair.Key);
                WriteHourlyArray(writer, pair.Value.BaselineHourlyTraffic);
                WriteHourlyArray(writer, pair.Value.CurrentDayTrafficByHour);
                WriteHourlyArray(writer, pair.Value.LastDayTrafficByHour);
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

                reader.Read(out int hourlyAnalyticsCount);
                for (int index = 0; index < hourlyAnalyticsCount; index++)
                {
                    reader.Read(out Entity district);
                    if (district == Entity.Null)
                    {
                        SkipHourlyAnalyticsRecord(reader);
                        continue;
                    }

                    DistrictAnalytics analytics = EnsureAnalytics(district);
                    ReadHourlyArray(reader, analytics.BaselineHourlyTraffic);
                    ReadHourlyArray(reader, analytics.CurrentDayTrafficByHour);
                    ReadHourlyArray(reader, analytics.LastDayTrafficByHour);
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
            {
                EnsureAnalyticsArrays(ref analytics);
                _districtAnalytics[district] = analytics;
                return analytics;
            }

            analytics = new DistrictAnalytics
            {
                ActivationDay = -1,
                BaselineHourlyTraffic = CreateHourlyBuckets(),
                CurrentDayTrafficByHour = CreateHourlyBuckets(),
                LastDayTrafficByHour = CreateHourlyBuckets()
            };
            _districtAnalytics[district] = analytics;
            return analytics;
        }

        private DistrictReportSnapshot CreateSnapshot(Entity district, DistrictAnalytics analytics)
        {
            EnsureAnalyticsArrays(ref analytics);
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
                BaselineAverageTraffic = baselineAverageTraffic,
                CurrentHourIndex = GetCurrentHourIndex(),
                CurrentDayTrafficByHour = CloneHourlyData(analytics.CurrentDayTrafficByHour),
                LastDayTrafficByHour = CloneHourlyData(analytics.LastDayTrafficByHour),
                BaselineAverageTrafficByHour = CreateBaselineAverageHourlyData(analytics.BaselineHourlyTraffic, analytics.BaselineDays)
            };
        }

        private int GetCurrentDay()
        {
            return (int)(_simulationSystem.frameIndex / 262144u);
        }

        private int GetCurrentHourIndex()
        {
            uint frameOfDay = _simulationSystem.frameIndex % 262144u;
            return (int)(frameOfDay * HoursPerDay / 262144u);
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

        private static void SkipHourlyAnalyticsRecord<TReader>(TReader reader) where TReader : IReader
        {
            SkipHourlyArray(reader);
            SkipHourlyArray(reader);
            SkipHourlyArray(reader);
        }

        private static void EnsureAnalyticsArrays(ref DistrictAnalytics analytics)
        {
            analytics.BaselineHourlyTraffic ??= CreateHourlyBuckets();
            analytics.CurrentDayTrafficByHour ??= CreateHourlyBuckets();
            analytics.LastDayTrafficByHour ??= CreateHourlyBuckets();
        }

        private static int[] CreateHourlyBuckets()
        {
            return new int[HoursPerDay];
        }

        private static int[] CloneHourlyData(int[] source)
        {
            var copy = CreateHourlyBuckets();
            if (source == null)
                return copy;

            int length = source.Length < HoursPerDay ? source.Length : HoursPerDay;
            for (int index = 0; index < length; index++)
                copy[index] = source[index];

            return copy;
        }

        private static float[] CreateBaselineAverageHourlyData(int[] totals, int baselineDays)
        {
            var averages = new float[HoursPerDay];
            if (totals == null || baselineDays <= 0)
                return averages;

            int length = totals.Length < HoursPerDay ? totals.Length : HoursPerDay;
            for (int index = 0; index < length; index++)
                averages[index] = (float)totals[index] / baselineDays;

            return averages;
        }

        private static void AddHourlyData(int[] destination, int[] source)
        {
            if (destination == null || source == null)
                return;

            int length = destination.Length < source.Length ? destination.Length : source.Length;
            if (length > HoursPerDay)
                length = HoursPerDay;

            for (int index = 0; index < length; index++)
                destination[index] += source[index];
        }

        private static void CopyHourlyData(int[] source, int[] destination)
        {
            if (source == null || destination == null)
                return;

            int length = source.Length < destination.Length ? source.Length : destination.Length;
            if (length > HoursPerDay)
                length = HoursPerDay;

            for (int index = 0; index < length; index++)
                destination[index] = source[index];
        }

        private static void ClearHourlyData(int[] values)
        {
            if (values == null)
                return;

            int length = values.Length < HoursPerDay ? values.Length : HoursPerDay;
            for (int index = 0; index < length; index++)
                values[index] = 0;
        }

        private static void WriteHourlyArray<TWriter>(TWriter writer, int[] values) where TWriter : IWriter
        {
            if (values == null)
                values = CreateHourlyBuckets();

            for (int index = 0; index < HoursPerDay; index++)
            {
                int value = index < values.Length ? values[index] : 0;
                writer.Write(value);
            }
        }

        private static void ReadHourlyArray<TReader>(TReader reader, int[] target) where TReader : IReader
        {
            for (int index = 0; index < HoursPerDay; index++)
            {
                reader.Read(out int value);
                if (target != null && index < target.Length)
                    target[index] = value;
            }
        }

        private static void SkipHourlyArray<TReader>(TReader reader) where TReader : IReader
        {
            for (int index = 0; index < HoursPerDay; index++)
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
