// CompareViewModel.cs - Compare two logs:
//   Delivery: two deliveries of the same field, matched by control point, beam on only
//             (the compare_trajectory_logs.py analysis): differences by axis, per leaf, per beam,
//             beam holds, and the comparison figure.
//   File:     header, subbeams and every snapshot value (the converter's compare command), e.g.
//             to check a v4.0 conversion against its v5.1 original.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrajectoryLogTools.Analysis;
using TrajectoryLogTools.App.Services;
using TrajectoryLogTools.Charts;
using TrajectoryLogTools.Core;

namespace TrajectoryLogTools.App.ViewModels
{
    public partial class CompareViewModel : ObservableObject
    {
        [ObservableProperty] private string _pathA = "";
        [ObservableProperty] private string _pathB = "";
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsFileMode))]
        private bool _isDeliveryMode = true;
        [ObservableProperty] private bool _dropStrayFirstSnapshot = true;
        [ObservableProperty] private bool _ignoreVersion;
        [ObservableProperty] private string _tolerance = "0";
        [ObservableProperty] private bool _ignoreCrc;
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _reportText = "";
        [ObservableProperty] private string _fileCompareText = "";
        [ObservableProperty] private string _status = "";
        [ObservableProperty] private int _resultTab;

        public bool IsFileMode { get { return !IsDeliveryMode; } }

        public ObservableCollection<AxisRow> Summary { get; } = new ObservableCollection<AxisRow>();
        public ObservableCollection<BeamRow> Beams { get; } = new ObservableCollection<BeamRow>();
        public ObservableCollection<HoldRow> Holds { get; } = new ObservableCollection<HoldRow>();

        public DeliveryComparisonResult Result { get; private set; }
        public bool HasResult { get { return Result != null; } }

        public event EventHandler ChartsChanged;

        [RelayCommand]
        void BrowseA()
        {
            string p = Dialogs.OpenFile("Log A", Dialogs.LogFilter, PathA);
            if (p != null) PathA = p;
        }

        [RelayCommand]
        void BrowseB()
        {
            string p = Dialogs.OpenFile("Log B", Dialogs.LogFilter, string.IsNullOrEmpty(PathB) ? PathA : PathB);
            if (p != null) PathB = p;
        }

        [RelayCommand]
        void Swap() { string a = PathA; PathA = PathB; PathB = a; }

        static string Clean(string s) { return (s ?? "").Trim().Trim('"'); }

        [RelayCommand]
        async Task Run()
        {
            string a = Clean(PathA), b = Clean(PathB);
            if (!File.Exists(a) || !File.Exists(b)) { Dialogs.Warn("Choose two log files that exist."); return; }
            IsBusy = true;
            Status = "Comparing...";
            try
            {
                if (IsDeliveryMode) await RunDelivery(a, b);
                else await RunFile(a, b);
            }
            catch (Exception ex)
            {
                Status = "Failed: " + ex.Message;
                Dialogs.Error(ex.Message);
            }
            finally { IsBusy = false; }
        }

        async Task RunDelivery(string a, string b)
        {
            bool drop = DropStrayFirstSnapshot, ignoreCrc = IgnoreCrc;
            DeliveryComparisonResult r = await Task.Run(() =>
                DeliveryComparison.Compare(DeliveryRecord.Load(a, ignoreCrc), DeliveryRecord.Load(b, ignoreCrc), dropStrayFirstSnapshot: drop));
            Result = r;
            Summary.Clear(); Beams.Clear(); Holds.Clear();
            foreach (var s in r.Summary) Summary.Add(AxisRow.From(s));
            foreach (var x in new[] { Tuple.Create("A", r.A), Tuple.Create("B", r.B) })
            {
                foreach (var bm in x.Item2.Beams) Beams.Add(BeamRow.From(bm, x.Item1));
                foreach (var h in x.Item2.Holds) Holds.Add(HoldRow.From(h, x.Item1));
            }
            ReportText = DeliveryComparison.Report(r);
            AxisStat mlc = r.Summary.LastOrDefault(s => s.Axis.StartsWith("MLC") && s.Values == "Actual" && s.Sampling == "continuous");
            Status = mlc == null ? "Done." : string.Format(CultureInfo.InvariantCulture,
                "Done. Actual leaf positions A − B: RMS {0:F3} mm, max {1:F3} mm (continuous).", mlc.Rms, mlc.Max);
            ResultTab = 0;
            OnPropertyChanged(nameof(HasResult));
            var h2 = ChartsChanged; if (h2 != null) h2(this, EventArgs.Empty);
        }

        async Task RunFile(string a, string b)
        {
            double tol;
            if (!double.TryParse(Tolerance, NumberStyles.Float, CultureInfo.InvariantCulture, out tol) || tol < 0)
            {
                Dialogs.Warn("Tolerance must be a number >= 0.");
                return;
            }
            var opt = new Options { Command = Command.Compare, IgnoreVersion = IgnoreVersion, Tolerance = tol, IgnoreCrc = IgnoreCrc, MaxDiffs = 50 };
            bool same = false;
            List<string> lines = await Task.Run(() => Commands.RunCompare(a, b, opt, out same));
            FileCompareText = string.Join(Environment.NewLine, lines);
            Status = same ? "SAME" : "DIFFERENT";
            ResultTab = 4;
        }

        [RelayCommand]
        void ExportResults()
        {
            if (Result == null) { Dialogs.Warn("Run a delivery comparison first."); return; }
            string folder = Dialogs.PickFolder("Save the comparison results to", PathA);
            if (folder == null) return;
            try
            {
                List<string> files = DeliveryComparison.WriteResults(Result, folder);
                string png = Path.Combine(folder, "expected_vs_actual_comparison.png");
                DeliveryCharts.SaveFigure(Result, png);
                files.Add(png);
                Dialogs.Info("Saved:\n" + string.Join("\n", files.Select(Path.GetFileName)) + "\n\nto " + folder);
            }
            catch (Exception ex) { Dialogs.Error(ex.Message); }
        }
    }
}
