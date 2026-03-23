using System;
using cohtml.Net;
using Game;
using Game.SceneFlow;
using Game.UI;
using Game.UI.InGame;
using Unity.Entities;

namespace ULEZ.Systems
{
    /// <summary>
    /// Adds a lightweight custom panel for toggling ULEZ on the selected district.
    /// </summary>
    public partial class ULEZDistrictUISystem : GameSystemBase
    {
        private const string PanelId = "ulez-district-panel";

        private View _view;
        private BoundEventHandle _toggleDistrictHandle;
        private SelectedInfoUISystem _selectedInfoSystem;
        private ULEZPolicySystem _ulezPolicySystem;
        private NameSystem _nameSystem;
        private Entity _lastDistrict;
        private bool _lastVisible;
        private bool _lastActive;
        private string _lastDistrictName = string.Empty;
        private string _lastStatsMarkup = string.Empty;
        private bool _initialized;

        protected override void OnCreate()
        {
            base.OnCreate();

            _selectedInfoSystem = World.GetOrCreateSystemManaged<SelectedInfoUISystem>();
            _ulezPolicySystem = World.GetOrCreateSystemManaged<ULEZPolicySystem>();
            _nameSystem = World.GetOrCreateSystemManaged<NameSystem>();
        }

        protected override void OnUpdate()
        {
            if (!_initialized)
            {
                InitializeUi();
            }

            if (_view == null)
                return;

            Entity selectedDistrict = Entity.Null;
            bool visible = false;
            if (_selectedInfoSystem != null)
            {
                var selected = _selectedInfoSystem.selectedEntity;
                if (selected != Entity.Null && EntityManager.HasComponent<Game.Areas.District>(selected))
                {
                    selectedDistrict = selected;
                    visible = true;
                }
            }

            if (visible)
                _ulezPolicySystem.EnsureDistrictTracked(selectedDistrict);

            bool active = visible && _ulezPolicySystem.IsULEZActive(selectedDistrict);
            string districtName = visible ? GetDistrictName(selectedDistrict) : string.Empty;
            string statsMarkup = visible ? BuildStatsMarkup(selectedDistrict, active) : string.Empty;

            if (selectedDistrict != _lastDistrict || visible != _lastVisible || active != _lastActive || districtName != _lastDistrictName || statsMarkup != _lastStatsMarkup)
            {
                UpdatePanel(districtName, visible, active, statsMarkup);
                _lastDistrict = selectedDistrict;
                _lastVisible = visible;
                _lastActive = active;
                _lastDistrictName = districtName;
                _lastStatsMarkup = statsMarkup;
            }
        }

        protected override void OnDestroy()
        {
            if (_view != null)
            {
                if (!Equals(_toggleDistrictHandle, default(BoundEventHandle)))
                    _view.UnregisterFromEvent(_toggleDistrictHandle);
                _view.ExecuteScript($"(function() {{ var panel = document.getElementById('{PanelId}'); if (panel) panel.remove(); }})();");
            }

            base.OnDestroy();
        }

        private void InitializeUi()
        {
            _view = GameManager.instance?.userInterface?.view?.View;
            if (_view == null)
                return;

            _toggleDistrictHandle = _view.RegisterForEvent("ULEZToggleDistrict", new Action(OnToggleDistrict));
            _view.ExecuteScript(BuildBootstrapScript());
            _initialized = true;
        }

        private void OnToggleDistrict()
        {
            var selected = _selectedInfoSystem?.selectedEntity ?? Entity.Null;
            if (selected == Entity.Null || !EntityManager.HasComponent<Game.Areas.District>(selected))
                return;

            _ulezPolicySystem.ToggleULEZ(selected);

            string districtName = GetDistrictName(selected);
            bool active = _ulezPolicySystem.IsULEZActive(selected);
            string statsMarkup = BuildStatsMarkup(selected, active);
            UpdatePanel(districtName, true, active, statsMarkup);
            _lastDistrict = selected;
            _lastVisible = true;
            _lastActive = active;
            _lastDistrictName = districtName;
            _lastStatsMarkup = statsMarkup;
        }

        private string GetDistrictName(Entity district)
        {
            string rendered = _nameSystem?.GetRenderedLabelName(district);
            if (!string.IsNullOrWhiteSpace(rendered))
                return rendered;

            return $"District #{district.Index}";
        }

        private void UpdatePanel(string districtName, bool visible, bool active, string statsMarkup)
        {
            string safeName = ToJavaScriptString(districtName);
            string stateText = active ? "ULEZ active" : "ULEZ inactive";
            string buttonText = active ? "Disable ULEZ" : "Enable ULEZ";

            _view.ExecuteScript(
                "window.ULEZPanel && window.ULEZPanel.setState(" +
                (visible ? "true" : "false") + "," +
                ToJavaScriptString(stateText) + "," +
                ToJavaScriptString(buttonText) + "," +
                safeName + "," +
                ToJavaScriptString(statsMarkup) + ");");
        }

        private string BuildStatsMarkup(Entity district, bool active)
        {
            if (!_ulezPolicySystem.TryGetDistrictReport(district, out ULEZPolicySystem.DistrictReportSnapshot report))
            {
                return active
                    ? "<div class='ulez-note'>Tracking starts after the first full in-game day.</div>"
                    : "<div class='ulez-note'>Enable ULEZ here to start district traffic and revenue tracking.</div>";
            }

            _ulezPolicySystem.EnsureDistrictTracked(district);

            string trendClass = BuildTrendClass(report);
            string trendText = BuildTrendText(report);
            int trackedDays = System.Math.Max(report.HistoricalDays, 0);
            int baselineDays = System.Math.Max(report.BaselineDays, 0);
            ULEZPolicySystem.SystemReportSnapshot systemReport = _ulezPolicySystem.GetSystemReport();

            return "<div class='ulez-stats-grid'>" +
                BuildStatCard("Today", $"{report.CurrentDayTraffic:N0} pass(es)", $"£{report.CurrentDayRevenue:N0} from {report.CurrentDayCharges:N0} charge(s)") +
                BuildStatCard("Last full day", $"{report.LastDayTraffic:N0} pass(es)", $"£{report.LastDayRevenue:N0} from {report.LastDayCharges:N0} charge(s)") +
                BuildStatCard("Lifetime", $"{report.LifetimeTraffic:N0} pass(es)", $"£{report.LifetimeRevenue:N0} from {report.LifetimeCharges:N0} charge(s)") +
                BuildStatCard("Baseline", baselineDays.ToString("N0") + " day(s)", baselineDays > 0 ? $"Avg {report.BaselineAverageTraffic:N0} pass(es)/day" : "Builds before ULEZ is enabled") +
                "</div>" +
                "<div class='ulez-note " + trendClass + "'>" + EscapeHtml(trendText) + "</div>" +
                BuildSystemSummaryMarkup(systemReport, trackedDays, report.ActivationDay);
        }

        private static string BuildSystemSummaryMarkup(ULEZPolicySystem.SystemReportSnapshot report, int trackedDays, int activationDay)
        {
            string statusDetail = activationDay >= 0
                ? $"ULEZ live since day {activationDay:N0}"
                : trackedDays > 0 ? "Watching traffic before ULEZ activation" : "Watching district traffic";

            string baselineDetail = report.ActiveBaselineAverageTraffic > 0.01f
                ? $"Active-district baseline avg: {report.ActiveBaselineAverageTraffic:N0} pass(es)/day"
                : "Baseline average builds as more districts are watched pre-ULEZ";

            return "<div class='ulez-system-block'>" +
                "<div class='ulez-system-title'>Citywide ULEZ totals</div>" +
                "<div class='ulez-stats-grid'>" +
                    BuildStatCard("Network today", $"{report.CurrentDayTraffic:N0} pass(es)", $"£{report.CurrentDayRevenue:N0} from {report.CurrentDayCharges:N0} charge(s)") +
                    BuildStatCard("Network last day", $"{report.LastDayTraffic:N0} pass(es)", $"{report.ActiveDistricts:N0} active district(s)") +
                    BuildStatCard("Network lifetime", $"{report.LifetimeTraffic:N0} pass(es)", $"£{report.LifetimeRevenue:N0} from {report.LifetimeCharges:N0} charge(s)") +
                    BuildStatCard("Coverage", $"{report.TrackedDistricts:N0} tracked district(s)", baselineDetail) +
                "</div>" +
                "<div class='ulez-note ulez-note-neutral'>" + EscapeHtml(statusDetail) + "</div>" +
                "</div>";
        }

        private static string BuildStatCard(string label, string value, string detail)
        {
            return "<div class='ulez-stat-card'>" +
                "<div class='ulez-stat-label'>" + EscapeHtml(label) + "</div>" +
                "<div class='ulez-stat-value'>" + EscapeHtml(value) + "</div>" +
                "<div class='ulez-stat-detail'>" + EscapeHtml(detail) + "</div>" +
                "</div>";
        }

        private static string BuildTrendText(ULEZPolicySystem.DistrictReportSnapshot report)
        {
            if (report.BaselineDays <= 0)
                return "Tracking district traffic now. Leave ULEZ off for at least one full day to establish a pre-ULEZ baseline.";

            if (report.HistoricalDays <= 0)
                return "Baseline is ready. Complete one full ULEZ day here before comparing the effect.";

            if (report.BaselineAverageTraffic <= 0.01f)
            {
                return report.LastDayTraffic > 0
                    ? "Private-car traffic is above this district's pre-ULEZ baseline."
                    : "Private-car traffic remains close to zero versus this district's pre-ULEZ baseline.";
            }

            float deltaPercent = ((report.LastDayTraffic - report.BaselineAverageTraffic) / report.BaselineAverageTraffic) * 100f;
            int roundedPercent = (int)Math.Round(Math.Abs(deltaPercent), MidpointRounding.AwayFromZero);

            if (deltaPercent <= -10f)
                return $"Last full day was down {roundedPercent}% versus this district's pre-ULEZ baseline.";

            if (deltaPercent >= 10f)
                return $"Last full day was up {roundedPercent}% versus this district's pre-ULEZ baseline.";

            return "Last full day was broadly steady versus this district's pre-ULEZ baseline.";
        }

        private static string BuildTrendClass(ULEZPolicySystem.DistrictReportSnapshot report)
        {
            if (report.BaselineDays <= 0 || report.HistoricalDays <= 0)
                return "ulez-note-neutral";

            if (report.BaselineAverageTraffic <= 0.01f)
                return report.LastDayTraffic > 0 ? "ulez-note-up" : "ulez-note-down";

            float deltaPercent = ((report.LastDayTraffic - report.BaselineAverageTraffic) / report.BaselineAverageTraffic) * 100f;
            if (deltaPercent <= -10f)
                return "ulez-note-down";

            if (deltaPercent >= 10f)
                return "ulez-note-up";

            return "ulez-note-neutral";
        }

        private static string EscapeHtml(string text)
        {
            if (string.IsNullOrEmpty(text))
                return string.Empty;

            return text
                .Replace("&", "&amp;")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;")
                .Replace("\"", "&quot;")
                .Replace("'", "&#39;");
        }

        private static string ToJavaScriptString(string text)
        {
            if (text == null)
                return "''";

            return "'" + text
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\r", string.Empty)
                .Replace("\n", "\\n") + "'";
        }

        private static string BuildBootstrapScript()
        {
            return @"(function () {
    if (window.ULEZPanel) {
        return;
    }

    var style = document.createElement('style');
    style.innerHTML = '#ulez-district-panel{position:fixed;right:24px;top:120px;width:320px;z-index:999999;pointer-events:none;font-family:var(--fontFamily,\'Segoe UI\');}' +
        '#ulez-district-panel.hidden{display:none;}' +
        '#ulez-district-panel .ulez-card{pointer-events:auto;background:rgba(24,42,55,0.94);color:#eef6fb;border:1px solid rgba(154,193,173,0.28);border-radius:14px;box-shadow:0 20px 48px rgba(0,0,0,0.32);overflow:hidden;}' +
        '#ulez-district-panel .ulez-head{padding:14px 16px 10px;background:linear-gradient(135deg,rgba(35,95,74,0.95),rgba(27,58,74,0.95));}' +
        '#ulez-district-panel .ulez-kicker{font-size:11px;letter-spacing:0.12em;text-transform:uppercase;opacity:0.8;}' +
        '#ulez-district-panel .ulez-title{margin-top:4px;font-size:20px;font-weight:700;}' +
        '#ulez-district-panel .ulez-body{padding:16px;display:grid;gap:12px;}' +
        '#ulez-district-panel .ulez-district{font-size:16px;font-weight:600;}' +
        '#ulez-district-panel .ulez-state{color:#b7d7c0;font-size:13px;}' +
        '#ulez-district-panel .ulez-description{color:rgba(238,246,251,0.8);font-size:13px;line-height:1.4;}' +
        '#ulez-district-panel .ulez-stats-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px;}' +
        '#ulez-district-panel .ulez-stat-card{background:rgba(255,255,255,0.06);border:1px solid rgba(255,255,255,0.08);border-radius:12px;padding:10px 11px;min-height:82px;}' +
        '#ulez-district-panel .ulez-stat-label{font-size:11px;letter-spacing:0.08em;text-transform:uppercase;color:rgba(238,246,251,0.68);}' +
        '#ulez-district-panel .ulez-stat-value{margin-top:6px;font-size:15px;font-weight:700;line-height:1.2;}' +
        '#ulez-district-panel .ulez-stat-detail{margin-top:5px;font-size:12px;color:rgba(238,246,251,0.74);line-height:1.3;}' +
        '#ulez-district-panel .ulez-note{padding:10px 12px;border-radius:12px;background:rgba(154,209,170,0.08);border:1px solid rgba(154,209,170,0.16);color:rgba(238,246,251,0.9);font-size:12px;line-height:1.45;}' +
        '#ulez-district-panel .ulez-note-up{background:rgba(194,110,93,0.16);border-color:rgba(235,146,123,0.34);color:#ffe0d7;}' +
        '#ulez-district-panel .ulez-note-down{background:rgba(103,166,122,0.16);border-color:rgba(154,209,170,0.34);color:#dbf7e2;}' +
        '#ulez-district-panel .ulez-note-neutral{background:rgba(154,209,170,0.08);border-color:rgba(154,209,170,0.16);color:rgba(238,246,251,0.9);}' +
        '#ulez-district-panel .ulez-system-block{display:grid;gap:10px;margin-top:2px;}' +
        '#ulez-district-panel .ulez-system-title{font-size:12px;letter-spacing:0.1em;text-transform:uppercase;color:rgba(238,246,251,0.68);}' +
        '#ulez-district-panel .ulez-button{appearance:none;border:0;border-radius:10px;padding:12px 14px;cursor:pointer;background:#9ad1aa;color:#112229;font-size:14px;font-weight:700;}' +
        '#ulez-district-panel .ulez-button.off{background:#d7e7ee;color:#17313b;}';
    document.head.appendChild(style);

    var root = document.createElement('div');
    root.id = 'ulez-district-panel';
    root.className = 'hidden';
    root.innerHTML = '<div class=\'ulez-card\'>' +
        '<div class=\'ulez-head\'>' +
            '<div class=\'ulez-kicker\'>District Controls</div>' +
            '<div class=\'ulez-title\'>Ultra Low Emission Zone</div>' +
        '</div>' +
        '<div class=\'ulez-body\'>' +
            '<div class=\'ulez-district\' id=\'ulez-district-name\'>No district selected</div>' +
            '<div class=\'ulez-state\' id=\'ulez-district-state\'>ULEZ inactive</div>' +
            '<div class=\'ulez-description\'>Select a district and toggle ULEZ for that district only. Vehicles entering enabled districts will be charged.</div>' +
            '<div id=\'ulez-district-stats\'><div class=\'ulez-note\'>Enable ULEZ here to start district traffic and revenue tracking.</div></div>' +
            '<button id=\'ulez-district-toggle\' class=\'ulez-button off\'>Enable ULEZ</button>' +
        '</div>' +
    '</div>';
    document.body.appendChild(root);

    document.getElementById('ulez-district-toggle').addEventListener('click', function () {
        if (typeof engine !== 'undefined') {
            engine.trigger('ULEZToggleDistrict');
        }
    });

    window.ULEZPanel = {
        setState: function (visible, stateText, buttonText, districtName, statsMarkup) {
            root.classList.toggle('hidden', !visible);
            document.getElementById('ulez-district-name').textContent = districtName || 'Selected district';
            document.getElementById('ulez-district-state').textContent = stateText || '';
            document.getElementById('ulez-district-stats').innerHTML = statsMarkup || '';
            var button = document.getElementById('ulez-district-toggle');
            button.textContent = buttonText || 'Enable ULEZ';
            button.classList.toggle('off', buttonText === 'Enable ULEZ');
        }
    };
})();";
        }
    }
}