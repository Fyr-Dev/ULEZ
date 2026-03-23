using System;
using System.Globalization;
using System.Text;
using cohtml.Net;
using Game;
using Game.SceneFlow;
using Game.UI;
using Game.UI.InGame;
using Unity.Entities;

namespace ULEZ.Systems
{
    public partial class ULEZDistrictUISystem : GameSystemBase
    {
        private const string PanelId = "ulez-district-panel";
        private const int GraphWidth = 320;
        private const int GraphHeight = 138;
        private const int GraphPadding = 14;

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
            string stateText = active ? "ULEZ is on in this district" : "ULEZ is off in this district";
            string buttonText = active ? "Turn ULEZ off" : "Turn ULEZ on";

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
                    : "<div class='ulez-note'>Turn ULEZ on here to start tracking money and car traffic in this district.</div>";
            }

            _ulezPolicySystem.EnsureDistrictTracked(district);

            ULEZPolicySystem.SystemReportSnapshot systemReport = _ulezPolicySystem.GetSystemReport();
            BuildTrafficChangeCard(report, out string trafficChangeValue, out string trafficChangeDetail);
            int elapsedHoursToday = GetElapsedHours(report.CurrentHourIndex);
            int averageCarsPerHourToday = report.CurrentDayTraffic > 0
                ? (int)Math.Round(report.CurrentDayTraffic / (double)elapsedHoursToday, MidpointRounding.AwayFromZero)
                : 0;
            int currentHourTraffic = GetHourlyValue(report.CurrentDayTrafficByHour, report.CurrentHourIndex);
            string trendClass = BuildTrendClass(report);
            string trendText = BuildTrendText(report);

            return "<div class='ulez-section-title'>District summary</div>" +
                "<div class='ulez-stats-grid'>" +
                    BuildStatCard("Average cars per hour today", averageCarsPerHourToday.ToString("N0"), $"{currentHourTraffic:N0} cars seen this hour") +
                    BuildStatCard("Money collected today", $"£{report.CurrentDayRevenue:N0}", $"{report.CurrentDayCharges:N0} cars charged today") +
                    BuildStatCard("Money collected overall", $"£{report.LifetimeRevenue:N0}", $"{report.LifetimeCharges:N0} charged cars since tracking started") +
                    BuildStatCard("Traffic vs before ULEZ", trafficChangeValue, trafficChangeDetail) +
                "</div>" +
                BuildTrafficGraphMarkup(report, systemReport) +
                "<div class='ulez-note " + trendClass + "'>" + EscapeHtml(trendText) + "</div>" +
                BuildSystemSummaryMarkup(report, systemReport, active);
        }

        private static string BuildSystemSummaryMarkup(ULEZPolicySystem.DistrictReportSnapshot districtReport, ULEZPolicySystem.SystemReportSnapshot report, bool active)
        {
            int districtCurrentHour = GetHourlyValue(districtReport.CurrentDayTrafficByHour, report.CurrentHourIndex);
            int restOfCityCurrentHour = Math.Max(GetHourlyValue(report.CurrentDayTrafficByHour, report.CurrentHourIndex) - districtCurrentHour, 0);
            int trafficShare = report.CurrentDayTraffic > 0
                ? (int)Math.Round(districtReport.CurrentDayTraffic * 100d / report.CurrentDayTraffic, MidpointRounding.AwayFromZero)
                : 0;
            string statusDetail = active
                ? $"ULEZ has been switched on here since day {districtReport.ActivationDay:N0}."
                : districtReport.BaselineDays > 0
                    ? "This district is still building a before-ULEZ traffic picture."
                    : "Turn ULEZ on when you are ready to start charging cars here.";
            string baselineDetail = report.ActiveBaselineAverageTraffic > 0.01f
                ? $"Active ULEZ districts used to average {report.ActiveBaselineAverageTraffic:N0} cars per day before ULEZ."
                : "Before-ULEZ comparisons appear after tracked districts spend time with ULEZ switched off.";

            return "<div class='ulez-system-block'>" +
                "<div class='ulez-section-title'>Wider city picture</div>" +
                "<div class='ulez-stats-grid'>" +
                    BuildStatCard("Tracked city today", $"{report.CurrentDayTraffic:N0} cars", $"£{report.CurrentDayRevenue:N0} from {report.CurrentDayCharges:N0} charged cars") +
                    BuildStatCard("Rest of tracked city this hour", restOfCityCurrentHour.ToString("N0"), "Outside this district, based on all tracked districts") +
                    BuildStatCard("This district's share today", trafficShare > 0 ? $"{trafficShare:N0}%" : "0%", report.CurrentDayTraffic > 0 ? "Of all tracked car traffic seen today" : "No tracked district traffic yet today") +
                    BuildStatCard("Active ULEZ districts", report.ActiveDistricts.ToString("N0"), baselineDetail) +
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

        private static string BuildTrafficGraphMarkup(ULEZPolicySystem.DistrictReportSnapshot report, ULEZPolicySystem.SystemReportSnapshot systemReport)
        {
            int[] districtToday = report.CurrentDayTrafficByHour ?? new int[24];
            float[] districtBeforeUlez = report.BaselineAverageTrafficByHour ?? new float[24];
            int[] restOfCityToday = BuildRestOfCitySeries(systemReport.CurrentDayTrafficByHour, districtToday);
            double maxValue = GetLargestSeriesValue(districtToday, restOfCityToday, districtBeforeUlez);
            if (maxValue < 1d)
                maxValue = 1d;

            string graphSubtitle = report.BaselineDays > 0
                ? "Orange shows this district today. Green shows this district before ULEZ. Blue shows the rest of the tracked city today."
                : "Orange shows this district today. Blue shows the rest of the tracked city today. Leave ULEZ off for one full day to unlock the green before-ULEZ line.";

            StringBuilder svg = new StringBuilder();
            svg.Append("<svg viewBox='0 0 348 166' class='ulez-graph-svg' preserveAspectRatio='none'>");
            svg.Append("<line x1='14' y1='26' x2='334' y2='26' class='ulez-grid-line' />");
            svg.Append("<line x1='14' y1='64' x2='334' y2='64' class='ulez-grid-line' />");
            svg.Append("<line x1='14' y1='102' x2='334' y2='102' class='ulez-grid-line' />");
            svg.Append("<line x1='14' y1='140' x2='334' y2='140' class='ulez-grid-line' />");
            svg.Append("<polyline points='").Append(BuildPolylinePoints(districtToday, maxValue)).Append("' class='ulez-line ulez-line-district' />");
            if (report.BaselineDays > 0)
                svg.Append("<polyline points='").Append(BuildPolylinePoints(districtBeforeUlez, maxValue)).Append("' class='ulez-line ulez-line-baseline' />");
            svg.Append("<polyline points='").Append(BuildPolylinePoints(restOfCityToday, maxValue)).Append("' class='ulez-line ulez-line-city' />");
            svg.Append("</svg>");

            return "<div class='ulez-graph-card'>" +
                "<div class='ulez-graph-head'>" +
                    "<div class='ulez-graph-title'>Traffic through the day</div>" +
                    "<div class='ulez-graph-subtitle'>" + EscapeHtml(graphSubtitle) + "</div>" +
                "</div>" +
                "<div class='ulez-graph-frame'>" + svg + "</div>" +
                "<div class='ulez-graph-axis'><span>00:00</span><span>06:00</span><span>12:00</span><span>18:00</span><span>24:00</span></div>" +
                "<div class='ulez-legend'>" +
                    BuildLegendItem("ulez-legend-district", "This district today") +
                    (report.BaselineDays > 0 ? BuildLegendItem("ulez-legend-baseline", "This district before ULEZ") : string.Empty) +
                    BuildLegendItem("ulez-legend-city", "Rest of tracked city today") +
                "</div>" +
                "<div class='ulez-graph-caption'>" + EscapeHtml(BuildGraphCaption(report, restOfCityToday)) + "</div>" +
                "</div>";
        }

        private static string BuildLegendItem(string cssClass, string label)
        {
            return "<div class='ulez-legend-item'><span class='ulez-legend-swatch " + cssClass + "'></span><span>" + EscapeHtml(label) + "</span></div>";
        }

        private static string BuildGraphCaption(ULEZPolicySystem.DistrictReportSnapshot report, int[] restOfCityToday)
        {
            int[] districtToday = report.CurrentDayTrafficByHour ?? new int[24];
            int busiestHour = GetPeakHour(districtToday);
            int busiestHourTraffic = GetHourlyValue(districtToday, busiestHour);
            int restOfCityTraffic = GetHourlyValue(restOfCityToday, busiestHour);

            if (busiestHourTraffic <= 0)
            {
                return report.BaselineDays > 0
                    ? "No tracked car traffic has been seen in this district yet today."
                    : "No tracked car traffic has been seen in this district yet today, and the before-ULEZ line will appear after one full baseline day.";
            }

            string baselineSentence;
            if (report.BaselineDays <= 0)
            {
                baselineSentence = "We are still collecting before-ULEZ traffic for this district.";
            }
            else
            {
                float baselineAtPeak = GetHourlyValue(report.BaselineAverageTrafficByHour, busiestHour);
                if (baselineAtPeak <= 0.01f)
                {
                    baselineSentence = "That hour was busier than this district's old pre-ULEZ pattern.";
                }
                else
                {
                    double deltaPercent = ((busiestHourTraffic - baselineAtPeak) / baselineAtPeak) * 100d;
                    int roundedPercent = (int)Math.Round(Math.Abs(deltaPercent), MidpointRounding.AwayFromZero);
                    if (deltaPercent <= -10d)
                        baselineSentence = $"That is {roundedPercent}% lower than this district's usual traffic for that hour before ULEZ.";
                    else if (deltaPercent >= 10d)
                        baselineSentence = $"That is {roundedPercent}% higher than this district's usual traffic for that hour before ULEZ.";
                    else
                        baselineSentence = "That hour was close to this district's normal pre-ULEZ traffic.";
                }
            }

            return $"The busiest time today was {FormatHourWindow(busiestHour)} with {busiestHourTraffic:N0} cars seen in this district. {baselineSentence} The rest of the tracked city saw {restOfCityTraffic:N0} cars in the same hour.";
        }

        private static void BuildTrafficChangeCard(ULEZPolicySystem.DistrictReportSnapshot report, out string value, out string detail)
        {
            if (report.BaselineDays <= 0)
            {
                value = "Learning baseline";
                detail = "Leave ULEZ off for one full day to unlock a before-and-after comparison.";
                return;
            }

            if (report.HistoricalDays <= 0)
            {
                value = "Waiting for a full ULEZ day";
                detail = "Finish one full day with ULEZ on to compare against the old traffic pattern.";
                return;
            }

            if (report.BaselineAverageTraffic <= 0.01f)
            {
                if (report.LastDayTraffic > 0)
                {
                    value = "More cars than before";
                    detail = "The last full day had traffic where the old baseline was close to zero.";
                }
                else
                {
                    value = "Still close to zero";
                    detail = "The last full day stayed close to the district's old near-zero traffic level.";
                }

                return;
            }

            double deltaPercent = ((report.LastDayTraffic - report.BaselineAverageTraffic) / report.BaselineAverageTraffic) * 100d;
            int roundedPercent = (int)Math.Round(Math.Abs(deltaPercent), MidpointRounding.AwayFromZero);

            if (deltaPercent <= -10d)
            {
                value = $"{roundedPercent}% fewer cars";
                detail = "Compared with this district's average day before ULEZ.";
                return;
            }

            if (deltaPercent >= 10d)
            {
                value = $"{roundedPercent}% more cars";
                detail = "Compared with this district's average day before ULEZ.";
                return;
            }

            value = "Little change";
            detail = "The last full day was close to this district's average day before ULEZ.";
        }

        private static string BuildTrendText(ULEZPolicySystem.DistrictReportSnapshot report)
        {
            if (report.BaselineDays <= 0)
                return "This district is still learning what traffic looks like before ULEZ. Leave ULEZ off for one full day if you want a cleaner before-and-after comparison.";

            if (report.HistoricalDays <= 0)
                return "Before-ULEZ traffic is ready. Finish one full day with ULEZ switched on here to see whether car traffic changed.";

            if (report.BaselineAverageTraffic <= 0.01f)
            {
                return report.LastDayTraffic > 0
                    ? "The last full day had more car traffic than this district normally saw before ULEZ."
                    : "The last full day stayed close to this district's near-zero pre-ULEZ traffic.";
            }

            double deltaPercent = ((report.LastDayTraffic - report.BaselineAverageTraffic) / report.BaselineAverageTraffic) * 100d;
            int roundedPercent = (int)Math.Round(Math.Abs(deltaPercent), MidpointRounding.AwayFromZero);

            if (deltaPercent <= -10d)
                return $"The last full day had {roundedPercent}% fewer cars than this district's average day before ULEZ.";

            if (deltaPercent >= 10d)
                return $"The last full day had {roundedPercent}% more cars than this district's average day before ULEZ.";

            return "The last full day was close to this district's usual traffic before ULEZ.";
        }

        private static string BuildTrendClass(ULEZPolicySystem.DistrictReportSnapshot report)
        {
            if (report.BaselineDays <= 0 || report.HistoricalDays <= 0)
                return "ulez-note-neutral";

            if (report.BaselineAverageTraffic <= 0.01f)
                return report.LastDayTraffic > 0 ? "ulez-note-up" : "ulez-note-down";

            double deltaPercent = ((report.LastDayTraffic - report.BaselineAverageTraffic) / report.BaselineAverageTraffic) * 100d;
            if (deltaPercent <= -10d)
                return "ulez-note-down";

            if (deltaPercent >= 10d)
                return "ulez-note-up";

            return "ulez-note-neutral";
        }

        private static string BuildPolylinePoints(int[] values, double maxValue)
        {
            return BuildPolylinePointsInternal(index => GetHourlyValue(values, index), maxValue);
        }

        private static string BuildPolylinePoints(float[] values, double maxValue)
        {
            return BuildPolylinePointsInternal(index => GetHourlyValue(values, index), maxValue);
        }

        private static string BuildPolylinePointsInternal(Func<int, double> valueSelector, double maxValue)
        {
            StringBuilder builder = new StringBuilder();
            int pointCount = 24;
            double chartWidth = GraphWidth - (GraphPadding * 2d);
            double chartHeight = GraphHeight - (GraphPadding * 2d);

            for (int index = 0; index < pointCount; index++)
            {
                double x = GraphPadding + ((chartWidth / (pointCount - 1d)) * index);
                double y = GraphPadding + chartHeight - ((valueSelector(index) / maxValue) * chartHeight);
                if (index > 0)
                    builder.Append(' ');

                builder.Append(x.ToString("0.##", CultureInfo.InvariantCulture));
                builder.Append(',');
                builder.Append(y.ToString("0.##", CultureInfo.InvariantCulture));
            }

            return builder.ToString();
        }

        private static int[] BuildRestOfCitySeries(int[] citySeries, int[] districtSeries)
        {
            int[] result = new int[24];
            for (int index = 0; index < result.Length; index++)
                result[index] = Math.Max(GetHourlyValue(citySeries, index) - GetHourlyValue(districtSeries, index), 0);

            return result;
        }

        private static double GetLargestSeriesValue(int[] districtSeries, int[] citySeries, float[] baselineSeries)
        {
            double maxValue = 0d;
            for (int index = 0; index < 24; index++)
            {
                maxValue = Math.Max(maxValue, GetHourlyValue(districtSeries, index));
                maxValue = Math.Max(maxValue, GetHourlyValue(citySeries, index));
                maxValue = Math.Max(maxValue, GetHourlyValue(baselineSeries, index));
            }

            return maxValue;
        }

        private static int GetElapsedHours(int currentHourIndex)
        {
            if (currentHourIndex < 0)
                return 1;

            if (currentHourIndex >= 23)
                return 24;

            return currentHourIndex + 1;
        }

        private static int GetPeakHour(int[] values)
        {
            int peakHour = 0;
            int peakValue = GetHourlyValue(values, 0);
            for (int index = 1; index < 24; index++)
            {
                int currentValue = GetHourlyValue(values, index);
                if (currentValue > peakValue)
                {
                    peakValue = currentValue;
                    peakHour = index;
                }
            }

            return peakHour;
        }

        private static string FormatHourWindow(int hour)
        {
            int startHour = Math.Max(Math.Min(hour, 23), 0);
            int endHour = Math.Min(startHour + 1, 24);
            return startHour.ToString("00") + ":00-" + endHour.ToString("00") + ":00";
        }

        private static int GetHourlyValue(int[] values, int index)
        {
            if (values == null || index < 0 || index >= values.Length)
                return 0;

            return values[index];
        }

        private static float GetHourlyValue(float[] values, int index)
        {
            if (values == null || index < 0 || index >= values.Length)
                return 0f;

            return values[index];
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
    style.innerHTML = '#ulez-district-panel{position:fixed;right:24px;top:120px;width:428px;z-index:999999;pointer-events:none;font-family:var(--fontFamily,\'Segoe UI\');}' +
        '#ulez-district-panel.hidden{display:none;}' +
        '#ulez-district-panel .ulez-card{pointer-events:auto;background:rgba(19,29,39,0.96);color:#eef6fb;border:1px solid rgba(154,193,173,0.24);border-radius:16px;box-shadow:0 22px 54px rgba(0,0,0,0.34);overflow:hidden;}' +
        '#ulez-district-panel .ulez-head{padding:16px 18px 12px;background:linear-gradient(135deg,rgba(35,95,74,0.95),rgba(27,58,74,0.95));}' +
        '#ulez-district-panel .ulez-kicker{font-size:11px;letter-spacing:0.12em;text-transform:uppercase;opacity:0.84;}' +
        '#ulez-district-panel .ulez-title{margin-top:4px;font-size:21px;font-weight:700;}' +
        '#ulez-district-panel .ulez-body{padding:16px 18px 18px;display:grid;gap:12px;}' +
        '#ulez-district-panel .ulez-district{font-size:17px;font-weight:700;}' +
        '#ulez-district-panel .ulez-state{color:#b7d7c0;font-size:13px;}' +
        '#ulez-district-panel .ulez-description{color:rgba(238,246,251,0.78);font-size:13px;line-height:1.45;}' +
        '#ulez-district-panel .ulez-section-title{font-size:12px;letter-spacing:0.1em;text-transform:uppercase;color:rgba(238,246,251,0.7);}' +
        '#ulez-district-panel .ulez-stats-grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:10px;}' +
        '#ulez-district-panel .ulez-stat-card{background:rgba(255,255,255,0.06);border:1px solid rgba(255,255,255,0.08);border-radius:12px;padding:11px 12px;min-height:90px;}' +
        '#ulez-district-panel .ulez-stat-label{font-size:11px;letter-spacing:0.07em;text-transform:uppercase;color:rgba(238,246,251,0.68);line-height:1.35;}' +
        '#ulez-district-panel .ulez-stat-value{margin-top:7px;font-size:17px;font-weight:700;line-height:1.2;}' +
        '#ulez-district-panel .ulez-stat-detail{margin-top:6px;font-size:12px;color:rgba(238,246,251,0.76);line-height:1.35;}' +
        '#ulez-district-panel .ulez-note{padding:11px 12px;border-radius:12px;background:rgba(154,209,170,0.08);border:1px solid rgba(154,209,170,0.16);color:rgba(238,246,251,0.9);font-size:12px;line-height:1.48;}' +
        '#ulez-district-panel .ulez-note-up{background:rgba(194,110,93,0.16);border-color:rgba(235,146,123,0.34);color:#ffe0d7;}' +
        '#ulez-district-panel .ulez-note-down{background:rgba(103,166,122,0.16);border-color:rgba(154,209,170,0.34);color:#dbf7e2;}' +
        '#ulez-district-panel .ulez-note-neutral{background:rgba(154,209,170,0.08);border-color:rgba(154,209,170,0.16);color:rgba(238,246,251,0.9);}' +
        '#ulez-district-panel .ulez-system-block{display:grid;gap:10px;margin-top:2px;}' +
        '#ulez-district-panel .ulez-graph-card{display:grid;gap:8px;padding:12px;border-radius:14px;background:rgba(255,255,255,0.04);border:1px solid rgba(255,255,255,0.08);}' +
        '#ulez-district-panel .ulez-graph-head{display:grid;gap:4px;}' +
        '#ulez-district-panel .ulez-graph-title{font-size:14px;font-weight:700;}' +
        '#ulez-district-panel .ulez-graph-subtitle{font-size:12px;line-height:1.4;color:rgba(238,246,251,0.76);}' +
        '#ulez-district-panel .ulez-graph-frame{height:166px;border-radius:12px;background:rgba(11,17,24,0.65);padding:0 4px;overflow:hidden;}' +
        '#ulez-district-panel .ulez-graph-svg{width:100%;height:100%;display:block;}' +
        '#ulez-district-panel .ulez-grid-line{stroke:rgba(255,255,255,0.12);stroke-width:1;stroke-dasharray:3 5;}' +
        '#ulez-district-panel .ulez-line{fill:none;stroke-width:3;stroke-linecap:round;stroke-linejoin:round;}' +
        '#ulez-district-panel .ulez-line-district{stroke:#f5b66a;}' +
        '#ulez-district-panel .ulez-line-baseline{stroke:#78d39b;stroke-dasharray:6 6;}' +
        '#ulez-district-panel .ulez-line-city{stroke:#73baf7;}' +
        '#ulez-district-panel .ulez-graph-axis{display:flex;justify-content:space-between;font-size:11px;color:rgba(238,246,251,0.58);padding:0 2px;}' +
        '#ulez-district-panel .ulez-legend{display:flex;flex-wrap:wrap;gap:8px 14px;font-size:12px;color:rgba(238,246,251,0.82);}' +
        '#ulez-district-panel .ulez-legend-item{display:flex;align-items:center;gap:6px;}' +
        '#ulez-district-panel .ulez-legend-swatch{display:inline-block;width:20px;height:3px;border-radius:999px;}' +
        '#ulez-district-panel .ulez-legend-district{background:#f5b66a;}' +
        '#ulez-district-panel .ulez-legend-baseline{background:linear-gradient(90deg,#78d39b 0,#78d39b 50%,transparent 50%,transparent 100%);background-size:8px 100%;}' +
        '#ulez-district-panel .ulez-legend-city{background:#73baf7;}' +
        '#ulez-district-panel .ulez-graph-caption{font-size:12px;line-height:1.45;color:rgba(238,246,251,0.84);}' +
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
            '<div class=\'ulez-state\' id=\'ulez-district-state\'>ULEZ is off in this district</div>' +
            '<div class=\'ulez-description\'>Simple traffic and money tracking for the selected district. Use the chart to compare today\'s traffic with this district\'s normal pattern before ULEZ and with the rest of the tracked city.</div>' +
            '<div id=\'ulez-district-stats\'><div class=\'ulez-note\'>Turn ULEZ on here to start tracking money and car traffic in this district.</div></div>' +
            '<button id=\'ulez-district-toggle\' class=\'ulez-button off\'>Turn ULEZ on</button>' +
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
            button.textContent = buttonText || 'Turn ULEZ on';
            button.classList.toggle('off', buttonText === 'Turn ULEZ on');
        }
    };
})();";
        }
    }
}