// Soft bridge to CustomChirps API via reflection.
// No hard dependency — if CustomChirps is not installed, calls are safely skipped.
// Based on https://github.com/ruzbeh0/CustomChirps

using System;
using System.Reflection;
using Unity.Entities;

namespace ULEZ.Bridge
{
    /// <summary>
    /// Local mirror of CustomChirps.Systems.DepartmentAccount (names must match).
    /// </summary>
    public enum DepartmentAccountBridge
    {
        Electricity,
        FireRescue,
        Roads,
        Water,
        Communications,
        Police,
        PropertyAssessmentOffice,
        Post,
        BusinessNews,
        CensusBureau,
        ParkAndRec,
        EnvironmentalProtectionAgency,
        Healthcare,
        LivingStandardsAssociation,
        Garbage,
        TourismBoard,
        Transportation,
        Education
    }

    /// <summary>
    /// Reflection-based bridge to CustomChirps API. No hard reference required.
    /// </summary>
    public static class CustomChirpsBridge
    {
        private static bool _resolved;
        private static Type _apiType;           // CustomChirps.Systems.CustomChirpApiSystem
        private static Type _deptEnumType;      // CustomChirps.Systems.DepartmentAccount
        private static MethodInfo _postChirp;   // PostChirp(string, DepartmentAccount, Entity, string)

        /// <summary>True if at least the entity-based API is available.</summary>
        public static bool IsAvailable
        {
            get { EnsureResolve(); return _apiType != null && _deptEnumType != null && _postChirp != null; }
        }

        /// <summary>
        /// Post a chirp message to the in-game Chirper feed.
        /// </summary>
        public static bool PostChirp(string text, DepartmentAccountBridge department, Entity entity, string customSenderName = null)
        {
            EnsureResolve();
            if (!IsAvailable) return false;

            try
            {
                var realDept = MapDepartment(department);
                var args = new object[] { text ?? string.Empty, realDept, entity, customSenderName };
                _postChirp.Invoke(null, args);
                return true;
            }
            catch (Exception ex)
            {
                Mod.DebugWarn($"CustomChirps PostChirp failed: {ex.Message}");
                return false;
            }
        }

        // ---- helpers ----

        private static object MapDepartment(DepartmentAccountBridge department)
        {
            try
            {
                EnsureResolve();
                if (_deptEnumType == null) return "Transportation";
                return Enum.Parse(_deptEnumType, department.ToString(), ignoreCase: false);
            }
            catch
            {
                return Enum.Parse(_deptEnumType, "Transportation", ignoreCase: true);
            }
        }

        private static void EnsureResolve()
        {
            if (_resolved) return;
            _resolved = true;

            _apiType = Type.GetType("CustomChirps.Systems.CustomChirpApiSystem, CustomChirps")
                       ?? FindType("CustomChirps.Systems.CustomChirpApiSystem");
            _deptEnumType = Type.GetType("CustomChirps.Systems.DepartmentAccount, CustomChirps")
                            ?? FindType("CustomChirps.Systems.DepartmentAccount");

            if (_apiType != null)
            {
                _postChirp = _apiType.GetMethod("PostChirp", BindingFlags.Public | BindingFlags.Static);
            }
        }

        private static Type FindType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    var t = asm.GetType(fullName, throwOnError: false);
                    if (t != null) return t;
                }
                catch { /* ignore */ }
            }
            return null;
        }
    }
}
