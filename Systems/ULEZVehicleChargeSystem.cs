using Game;
using Game.Areas;
using Game.Common;
using Game.Economy;
using Game.Net;
using Game.Simulation;
using Game.Tools;
using Game.Vehicles;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Entities;

namespace ULEZ.Systems
{
    /// <summary>
    /// Monitors personal vehicles travelling through ULEZ districts and applies
    /// charges to their owner household. Works by checking the vehicle's current
    /// lane, finding the road edge it belongs to, and looking up the edge's district.
    /// </summary>
    public partial class ULEZVehicleChargeSystem : GameSystemBase
    {
        private const int MaxOwnerDepth = 8;
        private const uint ChargeIntervalFrames = 2048;

        private SimulationSystem _simulationSystem;
        private ULEZDailyRevenueSystem _revenueSystem;
        private ULEZPolicySystem _policySystem;
        private EntityQuery _movingVehicleQuery;
        private uint _lastChargeFrame;
        private int _lastChargeDay = -1;
        private int _cachedPolicyVersion = -1;
        private readonly HashSet<ulong> _chargedVehiclesToday = new HashSet<ulong>();
        private readonly Dictionary<ulong, Entity> _laneEdgeCache = new Dictionary<ulong, Entity>();
        private readonly Dictionary<ulong, Entity> _edgeDistrictCache = new Dictionary<ulong, Entity>();
        private readonly Dictionary<ulong, bool> _edgeChargeabilityCache = new Dictionary<ulong, bool>();
        private int _scanCursor;
        private int _totalChargesThisCycle;
        private int _totalRevenueThisCycle;
        private int _failedPayerResolutionsThisCycle;

        protected override void OnCreate()
        {
            base.OnCreate();

            _simulationSystem = World.GetOrCreateSystemManaged<SimulationSystem>();
            _revenueSystem = World.GetOrCreateSystemManaged<ULEZDailyRevenueSystem>();
            _policySystem = World.GetOrCreateSystemManaged<ULEZPolicySystem>();

            // Query for personal vehicles that are currently driving (have CarCurrentLane)
            _movingVehicleQuery = GetEntityQuery(
                ComponentType.ReadOnly<PersonalCar>(),
                ComponentType.ReadOnly<CarCurrentLane>(),
                ComponentType.ReadOnly<Owner>(),
                ComponentType.Exclude<Deleted>(),
                ComponentType.Exclude<Temp>(),
                ComponentType.Exclude<ParkedCar>()
            );

            Mod.Debug("ULEZVehicleChargeSystem created.");
        }

        protected override void OnDestroy()
        {
            base.OnDestroy();
        }

        protected override void OnUpdate()
        {
            if (Mod.Settings == null || !Mod.Settings.Enabled)
                return;

            if (!_policySystem.IsAnyDistrictActive() && !_policySystem.HasTrackedDistricts())
                return;

            EnsurePolicyCachesCurrent();

            uint currentFrame = _simulationSystem.frameIndex;
            int currentDay = GetCurrentDay(currentFrame);
            if (currentDay != _lastChargeDay)
            {
                _chargedVehiclesToday.Clear();
                _scanCursor = 0;
                _lastChargeDay = currentDay;
            }

            if (currentFrame - _lastChargeFrame < ChargeIntervalFrames)
                return;

            _lastChargeFrame = currentFrame;
            _totalChargesThisCycle = 0;
            _totalRevenueThisCycle = 0;
            _failedPayerResolutionsThisCycle = 0;

            ProcessPersonalVehicles();

            if (_totalChargesThisCycle > 0)
            {
                Mod.Debug($"ULEZ: Charged {_totalChargesThisCycle} vehicles, total revenue: {_totalRevenueThisCycle}");
            }
            else if (_failedPayerResolutionsThisCycle > 0)
            {
                Mod.DebugWarn($"ULEZ: Found vehicles in active districts but could not resolve a chargeable payer for {_failedPayerResolutionsThisCycle} vehicle(s) this cycle.");
            }
        }

        private void ProcessPersonalVehicles()
        {
            if (_movingVehicleQuery.IsEmptyIgnoreFilter)
                return;

            var entities = _movingVehicleQuery.ToEntityArray(Allocator.Temp);
            var chargeAmount = GetEffectiveChargeAmount();
            int maxVehiclesPerScan = GetMaxVehiclesPerScan();

            if (entities.Length == 0)
            {
                entities.Dispose();
                return;
            }

            int startIndex = entities.Length > 0 ? _scanCursor % entities.Length : 0;
            int processedCount = 0;

            for (int offset = 0; offset < entities.Length && processedCount < maxVehiclesPerScan; offset++)
            {
                int i = (startIndex + offset) % entities.Length;
                var entity = entities[i];
                processedCount++;

                ulong vehicleKey = GetVehicleKey(entity);
                if (_chargedVehiclesToday.Contains(vehicleKey))
                    continue;

                var currentLane = EntityManager.GetComponentData<CarCurrentLane>(entity);
                var laneEntity = currentLane.m_Lane;

                if (laneEntity == Entity.Null)
                    continue;

                if (!TryGetDistrictForLane(laneEntity, out Entity districtEntity, out bool isActiveDistrict))
                    continue;

                _policySystem.RecordTrafficObservation(districtEntity);

                if (!isActiveDistrict)
                    continue;

                // Resolve the actual payer entity; this is not always the direct vehicle owner.
                var vehicleOwner = EntityManager.GetComponentData<Owner>(entity);
                if (!TryResolveChargePayer(vehicleOwner.m_Owner, out Entity payer))
                {
                    _failedPayerResolutionsThisCycle++;
                    continue;
                }

                if (!ApplyCharge(payer, chargeAmount))
                    continue;

                _chargedVehiclesToday.Add(vehicleKey);
                _totalChargesThisCycle++;
                _totalRevenueThisCycle += chargeAmount;

                _revenueSystem?.RecordCharge(districtEntity, chargeAmount);
            }

            _scanCursor = (startIndex + processedCount) % entities.Length;

            entities.Dispose();
        }

        private void EnsurePolicyCachesCurrent()
        {
            int policyVersion = _policySystem.Version;
            if (policyVersion == _cachedPolicyVersion)
                return;

            _edgeChargeabilityCache.Clear();
            _cachedPolicyVersion = policyVersion;
        }

        private bool TryGetDistrictForLane(Entity laneEntity, out Entity district, out bool isActiveDistrict)
        {
            district = Entity.Null;
            isActiveDistrict = false;
            ulong laneKey = GetVehicleKey(laneEntity);
            if (!_laneEdgeCache.TryGetValue(laneKey, out Entity edgeEntity) || !IsEntityReferenceValid(edgeEntity))
            {
                if (!EntityManager.HasComponent<Owner>(laneEntity))
                    return false;

                edgeEntity = EntityManager.GetComponentData<Owner>(laneEntity).m_Owner;
                if (edgeEntity == Entity.Null)
                    return false;

                _laneEdgeCache[laneKey] = edgeEntity;
            }

            ulong edgeKey = GetVehicleKey(edgeEntity);
            if (!_edgeDistrictCache.TryGetValue(edgeKey, out district) || (district != Entity.Null && !IsEntityReferenceValid(district)))
            {
                district = ResolveDistrictForEdge(edgeEntity);
                _edgeDistrictCache[edgeKey] = district;
            }

            if (district == Entity.Null)
                return false;

            if (_edgeChargeabilityCache.TryGetValue(edgeKey, out bool isActive))
            {
                isActiveDistrict = isActive && _policySystem.IsULEZActive(district);
                return true;
            }

            isActive = _policySystem.IsULEZActive(district);
            _edgeChargeabilityCache[edgeKey] = isActive;
            isActiveDistrict = isActive;
            return true;
        }

        private Entity ResolveDistrictForEdge(Entity edgeEntity)
        {
            if (!IsEntityReferenceValid(edgeEntity))
                return Entity.Null;

            if (EntityManager.HasComponent<CurrentDistrict>(edgeEntity))
                return EntityManager.GetComponentData<CurrentDistrict>(edgeEntity).m_District;

            if (!EntityManager.HasComponent<Owner>(edgeEntity))
                return Entity.Null;

            Entity parentEntity = EntityManager.GetComponentData<Owner>(edgeEntity).m_Owner;
            if (!IsEntityReferenceValid(parentEntity) || !EntityManager.HasComponent<CurrentDistrict>(parentEntity))
                return Entity.Null;

            return EntityManager.GetComponentData<CurrentDistrict>(parentEntity).m_District;
        }

        private bool IsEntityReferenceValid(Entity entity)
        {
            return entity != Entity.Null && EntityManager.Exists(entity);
        }

        private static int GetCurrentDay(uint frame)
        {
            return (int)(frame / 262144u);
        }

        private static ulong GetVehicleKey(Entity entity)
        {
            return ((ulong)(uint)entity.Index << 32) | (uint)entity.Version;
        }

        private int GetEffectiveChargeAmount()
        {
            int baseCharge = Mod.Settings.DailyCharge;
            float multiplier = Mod.Settings.ChargeMultiplier;
            if (multiplier <= 1f)
                return baseCharge;

            return (int)System.Math.Round(baseCharge * multiplier, System.MidpointRounding.AwayFromZero);
        }

        private int GetMaxVehiclesPerScan()
        {
            int configured = Mod.Settings.MaxVehiclesPerScan;
            if (configured < 1)
                return 1;

            return configured;
        }

        private bool TryResolveChargePayer(Entity owner, out Entity payer)
        {
            payer = Entity.Null;
            if (owner == Entity.Null)
                return false;

            Entity current = owner;
            for (int depth = 0; depth < MaxOwnerDepth && current != Entity.Null; depth++)
            {
                if (EntityManager.HasBuffer<Resources>(current))
                {
                    payer = current;
                    return true;
                }

                if (!EntityManager.HasComponent<Owner>(current))
                    break;

                current = EntityManager.GetComponentData<Owner>(current).m_Owner;
            }

            return false;
        }

        private bool ApplyCharge(Entity payer, int amount)
        {
            if (!EntityManager.HasBuffer<Resources>(payer))
                return false;

            var buffer = EntityManager.GetBuffer<Resources>(payer);
            for (int i = 0; i < buffer.Length; i++)
            {
                if (buffer[i].m_Resource != Resource.Money)
                    continue;

                var resource = buffer[i];
                resource.m_Amount -= amount;
                buffer[i] = resource;
                return true;
            }

            return false;
        }
    }
}
