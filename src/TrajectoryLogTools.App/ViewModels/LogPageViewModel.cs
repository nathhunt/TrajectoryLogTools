// LogPageViewModel.cs - the page of one open log: key facts, then Overview (delivery timeline,
// fields, header), Axes, MLC and Raw tabs.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrajectoryLogTools.Analysis;
using TrajectoryLogTools.App.Services;
using TrajectoryLogTools.Charts;
using TrajectoryLogTools.Core;

namespace TrajectoryLogTools.App.ViewModels
{
    /// <summary>One of the cards across the top of the log page.</summary>
    public sealed class Fact
    {
        public string Label { get; set; }
        public string Value { get; set; }
        public string Detail { get; set; }
        /// <summary>"neutral", "ok", "caution" or "critical".</summary>
        public string Kind { get; set; } = "neutral";
    }

    /// <summary>A stretch of the delivery timeline.</summary>
    public sealed class TimelineSegment
    {
        public double Fraction { get; set; }
        /// <summary>"on", "on2" (alternate fields), "interruption" or "hold".</summary>
        public string Kind { get; set; }
        public string Label { get; set; }
        public string ToolTip { get; set; }
    }

    public sealed class FieldRow
    {
        public string Name { get; set; }
        public string Gantry { get; set; }
        public string Collimator { get; set; }
        public string ControlPoints { get; set; }
        public string MuPlanned { get; set; }
        public string MuDelivered { get; set; }
        public string BeamOn { get; set; }
        public string MlcRms { get; set; }
        public string MlcMax { get; set; }
    }

    public partial class LogPageViewModel : ObservableObject
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        const int MaxDumpChars = 4000000;

        readonly MainViewModel _main;
        public LogItem Item { get; }

        [ObservableProperty] private bool _isLoading = true;
        [ObservableProperty] private string _error = "";
        [ObservableProperty] private string _crcWarning = "";
        /// <summary>0 Overview, 1 Axes, 2 MLC, 3 Raw.</summary>
        [ObservableProperty] private int _selectedTab;
        [ObservableProperty] private bool _vsControlPoint;
        [ObservableProperty] private KeyValuePair<string, string> _selectedAxis;
        [ObservableProperty] private string _rawDump = "";
        [ObservableProperty] private bool _rawLoading;
        [ObservableProperty] private string _headerDetails = "";
        [ObservableProperty] private string _worstLeaves = "";

        public string Title { get { return Item.DisplayName; } }
        public string TitleSuffix { get { return string.IsNullOrEmpty(Item.FieldsText) ? "" : " · " + Item.FieldsText; } }
        public string Detail
        {
            get
            {
                string kind = Item.IsCsv ? "CSV export of a log" : "Halcyon log " + Item.Version;
                return Item.DateText + " · " + kind + " · " + Item.Path;
            }
        }

        public ObservableCollection<Fact> Facts { get; } = new ObservableCollection<Fact>();
        public ObservableCollection<TimelineSegment> Timeline { get; } = new ObservableCollection<TimelineSegment>();
        public ObservableCollection<string> TimelineTicks { get; } = new ObservableCollection<string>();
        public ObservableCollection<FieldRow> Fields { get; } = new ObservableCollection<FieldRow>();
        public ObservableCollection<HoldRow> Holds { get; } = new ObservableCollection<HoldRow>();
        public ObservableCollection<KeyValuePair<string, string>> Axes { get; } = new ObservableCollection<KeyValuePair<string, string>>();

        public DeliveryRecord Record { get { return Item.Record; } }

        /// <summary>Raised when the charts need redrawing (the view owns the plot controls).</summary>
        public event EventHandler ChartsChanged;

        public LogPageViewModel(LogItem item, MainViewModel main)
        {
            Item = item;
            _main = main;
            Load();
        }

        /// <summary>The angle scale the tables were built in; when it has changed since, they are rebuilt.</summary>
        AngleScale? _anglesShown;
        public string FieldsTitle { get { return "Fields · angles " + Display.AnglesName; } }
        public string HoldsTitle { get { return "Beam holds · angles " + Display.AnglesName; } }

        public void Activate()
        {
            if (_anglesShown.HasValue && _anglesShown != Display.Angles) RefreshAngles();
            else RaiseCharts();
        }

        /// <summary>The angle scale changed: rebuild the angle tables and timeline, redraw the charts.</summary>
        public void RefreshAngles()
        {
            if (Item.Record != null && Item.Summary != null && _anglesShown.HasValue)
            {
                try { BuildAngleTables(); }
                catch (Exception ex) { Error = "Could not analyse this log: " + ex.Message; }
            }
            OnPropertyChanged(nameof(FieldsTitle));
            OnPropertyChanged(nameof(HoldsTitle));
            RaiseCharts();
        }

        void RaiseCharts() { var h = ChartsChanged; if (h != null) h(this, EventArgs.Empty); }

        partial void OnVsControlPointChanged(bool value) { RaiseCharts(); }
        partial void OnSelectedAxisChanged(KeyValuePair<string, string> value) { RaiseCharts(); }
        partial void OnSelectedTabChanged(int value)
        {
            if (value == 3 && RawDump.Length == 0 && !Item.IsCsv) ShowRawDump();
            RaiseCharts();
        }

        async void Load()
        {
            await Item.LoadAsync();
            IsLoading = false;
            if (Item.LoadError != null) { Error = "This log can't be read: " + Item.LoadError; return; }
            if (Item.CrcFailed)
                CrcWarning = "The file's CRC does not verify, so it may be damaged. It is shown anyway; check the Raw tab before relying on it.";
            try { Build(); }
            catch (Exception ex) { Error = "Could not analyse this log: " + ex.Message; }
            RaiseCharts();
        }

        void Build()
        {
            DeliveryRecord d = Item.Record;
            LogSummary s = Item.Summary;
            List<Subbeam> subbeams = d.Log != null ? d.Log.Subbeams : (Item.Header != null ? Item.Header.Subbeams : new List<Subbeam>());
            Func<int, string> fieldName = b => b >= 1 && b <= subbeams.Count && !string.IsNullOrWhiteSpace(subbeams[b - 1].Name)
                ? subbeams[b - 1].Name : "Field " + b;

            // ---- facts ----
            if (s != null)
            {
                Facts.Add(new Fact
                {
                    Label = "MU delivered",
                    Value = s.MuDelivered.ToString("F2", Inv),
                    Detail = string.Format(Inv, "of {0:F2} planned ({1:+0.00;-0.00;0.00})", s.MuPlanned, s.MuDelivered - s.MuPlanned)
                });
                Facts.Add(new Fact
                {
                    Label = "Duration",
                    Value = s.TotalTimeS.ToString("F1", Inv) + " s",
                    Detail = s.Beams.Count + (s.Beams.Count == 1 ? " field · " : " fields · ") + d.RowCount + " snapshots"
                });
                var interruptions = s.Holds.Where(h => h.Type == "interruption").ToList();
                if (interruptions.Count > 0)
                {
                    BeamHold first = interruptions[0];
                    Facts.Add(new Fact
                    {
                        Label = "Beam holds",
                        Value = interruptions.Count + (interruptions.Count == 1 ? " interruption" : " interruptions"),
                        Detail = string.Format(Inv, "{0:F2} s at CP {1:F1}, {2}{3}", first.DurationS, first.ControlPoint, fieldName(first.Beam),
                                               interruptions.Count > 1 ? " (first)" : ""),
                        Kind = "caution"
                    });
                }
                else
                    Facts.Add(new Fact { Label = "Beam holds", Value = "No interruptions", Detail = s.Holds.Count + " planned hold(s)" });
            }
            double mlcMax = 0, sum2 = 0; long n = 0;
            for (int l = 1; l <= d.LeafCount; l++)
            {
                double[] a = d.Column(DeliveryRecord.LeafColumn(l, "Actual")), e = d.Column(DeliveryRecord.LeafColumn(l, "Expected"));
                for (int i = 0; i < d.RowCount; i++)
                    if (d.IsBeamOn(i)) { double v = Math.Abs(a[i] - e[i]) * 10; mlcMax = Math.Max(mlcMax, v); sum2 += v * v; n++; }
            }
            if (n > 0)
                Facts.Add(new Fact { Label = "MLC actual − expected", Value = mlcMax.ToString("F2", Inv) + " mm max",
                                     Detail = "RMS " + Math.Sqrt(sum2 / n).ToString("F3", Inv) + " mm, beam on" });
            MlcGeometry g0 = d.Mlc;
            string mlcText = g0 != null ? g0.Name + " MLC" : (d.LeafCount > 0 ? d.LeafCount + "-leaf MLC (not supported for rebuild)" : "no MLC");
            if (d.Log != null)
                Facts.Add(Item.CrcFailed
                    ? new Fact { Label = "File", Value = "CRC failed", Detail = mlcText, Kind = "critical" }
                    : new Fact { Label = "File", Value = "CRC OK", Detail = mlcText + " · axis scale " + d.AxisScale, Kind = "ok" });
            else
                Facts.Add(new Fact { Label = "File", Value = "CSV export", Detail = mlcText });

            _anglesShown = Display.Angles;
            if (s != null) BuildAngleTables();

            // ---- header details ----
            var sb = new StringBuilder();
            if (d.Log != null)
            {
                TrajectoryLog log = d.Log;
                sb.AppendLine("Sampling interval: " + log.SamplingIntervalMs + " ms    Snapshots: " + log.NumSnapshots + (log.Truncated != 0 ? " (TRUNCATED)" : ""));
                sb.AppendLine("Axes: " + string.Join(", ", log.AxisIds.Select(TrajectoryLog.AxisName)));
                sb.AppendLine("Axis scale: " + log.AxisScale + " - " + TrajectoryLog.AxisScaleName(log.AxisScale));
                if (log.MachineSerial.Length > 0) sb.AppendLine("Machine: specifier " + log.MachineSpecifier + ", serial " + log.MachineSerial);
                sb.AppendLine("MLC model field: " + log.MlcModel + "    CRC: " + (log.CrcWarning ?? log.CrcVariant.Name));
                for (int i = 0; i < log.Subbeams.Count; i++)
                {
                    Subbeam b = log.Subbeams[i];
                    sb.AppendLine(string.Format(Inv, "Subbeam {0}: {1}, starts at CP {2}, {3:F2} MU, radiation time {4:F2} s", i + 1, b.Name, b.ControlPoint, b.MU, b.RadTime));
                }
                foreach (string m in log.MetaDataText.Replace("\r\n", "\n").Split('\n').Where(x => x.Trim().Length > 0))
                    sb.AppendLine("Metadata: " + m.Replace("\t", " "));
            }
            else sb.AppendLine("A CSV export holds the snapshot data only; the header details are in the .bin log.");
            HeaderDetails = sb.ToString().TrimEnd();

            // ---- charts ----
            foreach (var a in LogCharts.PlottableAxes(d)) Axes.Add(a);
            SelectedAxis = Axes.FirstOrDefault(a => a.Key == "GantryRtn_deg");
            if (SelectedAxis.Key == null && Axes.Count > 0) SelectedAxis = Axes[0];
            var worst = LogCharts.WorstLeaves(d, 3);
            if (worst.Count > 0)
                WorstLeaves = "Largest RMS: " + string.Join(", ", worst.Select(w => "leaf " + w.Key + " (" + w.Value.ToString("F3", Inv) + " mm)")) + ".";
        }

        /// <summary>The fields and beam holds tables and the timeline: they show gantry and collimator in the chosen scale.</summary>
        void BuildAngleTables()
        {
            DeliveryRecord d = Item.Record;
            LogSummary s = Item.Summary;
            List<Subbeam> subbeams = d.Log != null ? d.Log.Subbeams : (Item.Header != null ? Item.Header.Subbeams : new List<Subbeam>());
            Func<int, string> fieldName = b => b >= 1 && b <= subbeams.Count && !string.IsNullOrWhiteSpace(subbeams[b - 1].Name)
                ? subbeams[b - 1].Name : "Field " + b;
            Func<double, double> angle = g => Display.Angle(g, d.AxisScale);
            Fields.Clear(); Holds.Clear(); Timeline.Clear(); TimelineTicks.Clear();
            _anglesShown = Display.Angles;
            {
                double[] sub = d.TryColumn("Subbeam"), ge = d.TryColumn("GantryRtn_deg_Expected"), ce = d.TryColumn("CollRtn_deg_Expected");
                foreach (BeamStat b in s.Beams)
                {
                    var on = Enumerable.Range(0, d.RowCount).Where(i => (sub == null || (int)sub[i] == b.Beam) && d.IsBeamOn(i)).ToList();
                    string gantry = "", coll = "";
                    if (on.Count > 0 && ge != null)
                    {
                        double g1 = angle(ge[on[0]]), g2 = angle(ge[on[on.Count - 1]]);
                        gantry = Math.Abs(((g1 - g2) % 360 + 540) % 360 - 180) < 0.5 ? g1.ToString("F1", Inv) + "°" : g1.ToString("F1", Inv) + "° → " + g2.ToString("F1", Inv) + "°";
                    }
                    if (on.Count > 0 && ce != null) coll = angle(ce[on[0]]).ToString("F1", Inv) + "°";
                    Fields.Add(new FieldRow
                    {
                        Name = fieldName(b.Beam), Gantry = gantry, Collimator = coll, ControlPoints = b.ControlPoints,
                        MuPlanned = b.MuPlanned.ToString("F2", Inv), MuDelivered = b.MuDelivered.ToString("F2", Inv),
                        BeamOn = b.BeamOnS.ToString("F2", Inv) + " s",
                        MlcRms = double.IsNaN(b.MlcRmsErrMm) ? "" : b.MlcRmsErrMm.ToString("F3", Inv) + " mm",
                        MlcMax = double.IsNaN(b.MlcMaxErrMm) ? "" : b.MlcMaxErrMm.ToString("F2", Inv) + " mm",
                    });
                }
                foreach (BeamHold h in s.Holds) Holds.Add(HoldRow.From(h, fieldName(h.Beam), d.AxisScale));
                BuildTimeline(d, s, fieldName);
            }
        }

        void BuildTimeline(DeliveryRecord d, LogSummary s, Func<int, string> fieldName)
        {
            double[] t = d.Column("ElapsedTime_s");
            double[] sub = d.TryColumn("Subbeam");
            int n = d.RowCount;
            if (n < 2) return;
            double dt = Enumerable.Range(1, n - 1).Select(i => t[i] - t[i - 1]).Where(x => x > 0).DefaultIfEmpty(0.02).Min();
            double t0 = t[0], total = t[n - 1] + dt - t0;
            if (total <= 0) return;

            var raw = new List<Tuple<int, int>>();   // start, end (inclusive) of runs with the same state
            int start = 0;
            Func<int, string> state = i => d.IsBeamOn(i) ? "on" + (sub == null ? 1 : (int)sub[i]) : "hold";
            for (int i = 1; i <= n; i++)
                if (i == n || state(i) != state(start)) { raw.Add(Tuple.Create(start, i - 1)); start = i; }

            var fieldIndex = new Dictionary<int, int>();
            var labelled = new HashSet<int>();
            var longest = new Dictionary<int, double>();
            foreach (var r in raw)
                if (d.IsBeamOn(r.Item1))
                {
                    int b = sub == null ? 1 : (int)sub[r.Item1];
                    double dur = (r.Item2 + 1 < n ? t[r.Item2 + 1] : t[r.Item2] + dt) - t[r.Item1];
                    if (!longest.ContainsKey(b) || dur > longest[b]) longest[b] = dur;
                }
            foreach (var r in raw)
            {
                double begin = t[r.Item1], end = r.Item2 + 1 < n ? t[r.Item2 + 1] : t[r.Item2] + dt;
                double dur = Math.Max(0, end - begin);
                var seg = new TimelineSegment { Fraction = dur / total };
                if (d.IsBeamOn(r.Item1))
                {
                    int b = sub == null ? 1 : (int)sub[r.Item1];
                    if (!fieldIndex.ContainsKey(b)) fieldIndex[b] = fieldIndex.Count;
                    seg.Kind = fieldIndex[b] % 2 == 0 ? "on" : "on2";
                    if (!labelled.Contains(b) && Math.Abs(dur - longest[b]) < 1e-9) { seg.Label = fieldName(b) + " · beam on"; labelled.Add(b); }
                    seg.ToolTip = string.Format(Inv, "{0}: beam on {1:F2}–{2:F2} s", fieldName(b), begin - t0, end - t0);
                }
                else
                {
                    BeamHold h = s.Holds.FirstOrDefault(x => Math.Abs(x.StartS - (begin - t0)) < 1e-6) ??
                                 s.Holds.OrderBy(x => Math.Abs(x.StartS - (begin - t0))).FirstOrDefault();
                    string type = h == null ? "hold" : h.Type;
                    seg.Kind = type == "interruption" ? "interruption" : "hold";
                    string what = type == "interruption" ? "Interruption (nothing moved)"
                                : type == "move" ? "Planned hold: gantry / collimator move" + (h != null ? " (gantry " + Display.AngleText(h.Gantry, d.AxisScale) + ", coll " + Display.AngleText(h.Coll, d.AxisScale) + ")" : "")
                                : type == "start" ? "Hold at the start" : type == "end" ? "Hold at the end" : "Hold";
                    seg.ToolTip = string.Format(Inv, "{0}, {1:F2}–{2:F2} s", what, begin - t0, end - t0);
                }
                Timeline.Add(seg);
            }
            for (int k = 0; k <= 4; k++) TimelineTicks.Add((total * k / 4).ToString(total < 20 ? "F1" : "F0", Inv) + " s");
        }

        async void ShowRawDump()
        {
            RawLoading = true;
            try
            {
                string path = Item.Path;
                RawDump = await Task.Run(() =>
                {
                    var warnings = new List<string>();
                    string text = Core.RawDump.ToText(File.ReadAllBytes(path), path, warnings);
                    string head = warnings.Count > 0 ? "WARNINGS:\r\n  " + string.Join("\r\n  ", warnings) + "\r\n\r\n" : "";
                    if (text.Length > MaxDumpChars)
                        text = text.Substring(0, MaxDumpChars) + "\r\n\r\n... (shown up to " + MaxDumpChars / 1000000 +
                               " million characters; Export > Raw dump writes the whole file)";
                    return head + text;
                });
            }
            catch (Exception ex) { RawDump = "Could not dump the file: " + ex.Message; }
            finally { RawLoading = false; }
        }

        [RelayCommand]
        void CompareWith()
        {
            string p = Dialogs.OpenFile("Compare " + Item.DisplayName + " with", Dialogs.LogFilter, Item.Folder);
            if (p == null) return;
            var other = _main.OpenPaths(new[] { p });
            if (other.Count == 1 && other[0] != Item) _main.ShowCompare(Item, other[0]);
        }

        [RelayCommand]
        void Rebuild() { _main.ShowRebuild(_main.WithChecked(Item)); }

        [RelayCommand]
        void Export() { _main.ExportLogs(new List<LogItem> { Item }); }

        [RelayCommand]
        void CloseLog() { _main.CloseLog(Item); }
    }
}
