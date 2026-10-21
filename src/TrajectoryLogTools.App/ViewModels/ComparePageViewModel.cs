// ComparePageViewModel.cs - two deliveries compared, answer first: what was compared, the
// differences that matter as cards (against the tolerances in Settings, if set), the charts,
// then the details. File-level comparison (every stored value) is one click away for
// validating conversions.

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
    public partial class ComparePageViewModel : ObservableObject
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly MainViewModel _main;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PathA), nameof(DateA), nameof(PatientWarning))]
        private LogItem _a;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PathB), nameof(DateB), nameof(PatientWarning))]
        private LogItem _b;
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _error = "";
        [ObservableProperty] private string _verdict = "";
        /// <summary>"ok" (same plan) or "caution" (the plans differ).</summary>
        [ObservableProperty] private string _verdictKind = "ok";
        [ObservableProperty] private string _report = "";
        [ObservableProperty] private bool _fileIgnoreVersion = true;

        public string PathA { get { return A == null ? "" : A.Path; } }
        public string PathB { get { return B == null ? "" : B.Path; } }
        public string DateA { get { return A == null ? "" : A.DisplayName + " · " + A.DateText; } }
        public string DateB { get { return B == null ? "" : B.DisplayName + " · " + B.DateText; } }
        /// <summary>Set when the two logs name different patients.</summary>
        public string PatientWarning
        {
            get
            {
                if (A?.PatientId == null || B?.PatientId == null || string.Equals(A.PatientId, B.PatientId, StringComparison.OrdinalIgnoreCase)) return "";
                return "A and B are different patients (" + A.PatientId + " and " + B.PatientId + ").";
            }
        }

        public ObservableCollection<Fact> Cards { get; } = new ObservableCollection<Fact>();
        public ObservableCollection<BeamRow> Beams { get; } = new ObservableCollection<BeamRow>();
        public ObservableCollection<HoldRow> Holds { get; } = new ObservableCollection<HoldRow>();
        public ObservableCollection<AxisRow> Axes { get; } = new ObservableCollection<AxisRow>();

        public DeliveryComparisonResult Result { get; private set; }
        // Section headings, with the angle scale their tables are in. Built here in full: the
        // Fluent Expander template ignores HeaderStringFormat, so a format in the XAML would be lost.
        public string BeamsHeader { get { return "Per field, A and B · angles " + Display.AnglesName; } }
        public string HoldsHeader { get { return "Beam holds, A and B · angles " + Display.AnglesName; } }
        public string AxesHeader { get { return "By axis (all statistics) · angles " + Display.AnglesName; } }
        /// <summary>The report text is the comparison's own output, in the log's scale.</summary>
        public string ReportHeader { get { return "Full report" + (Result == null ? "" : " · angles as recorded, " + Angles.Name(Angles.ScaleOf(Result.AxisScale))); } }

        /// <summary>The angle scale changed: rebuild the tables and redraw.</summary>
        public void RefreshAngles()
        {
            if (Result == null) return;
            Cards.Clear(); Beams.Clear(); Holds.Clear(); Axes.Clear();
            Show(Result);
            RaiseCharts();
        }
        public bool HasResult { get { return Result != null; } }
        public bool CanCompareFiles { get { return A != null && B != null && !A.IsCsv && !B.IsCsv; } }

        public event EventHandler ChartsChanged;

        public ComparePageViewModel(LogItem a, LogItem b, MainViewModel main)
        {
            _main = main;
            _a = a;
            _b = b;
            Run();
        }

        void RaiseCharts() { var h = ChartsChanged; if (h != null) h(this, EventArgs.Empty); }

        [RelayCommand]
        void Swap()
        {
            LogItem t = A; A = B; B = t;
            Run();
        }

        /// <summary>Replaces A or B by a file dropped on it.</summary>
        public void Replace(bool isA, string path)
        {
            var items = _main.OpenPaths(new[] { path });
            _main.CurrentPage = this;
            if (items.Count != 1) return;
            if (isA) A = items[0]; else B = items[0];
            Run();
        }

        [RelayCommand] void BrowseA() { Browse(true); }
        [RelayCommand] void BrowseB() { Browse(false); }

        void Browse(bool isA)
        {
            string p = Dialogs.OpenFile(isA ? "Log A" : "Log B", Dialogs.LogFilter, (isA ? A : B)?.Folder);
            if (p != null) Replace(isA, p);
        }

        [RelayCommand]
        void BackToA() { if (A != null) _main.ShowLog(A); }

        /// <summary>Back to the log shown before (A or B), else A.</summary>
        [RelayCommand]
        void Back()
        {
            LogItem back = _main.CurrentLog != null && (_main.CurrentLog == A || _main.CurrentLog == B) ? _main.CurrentLog : A;
            if (back != null && _main.Logs.Contains(back)) _main.ShowLog(back);
        }

        async void Run()
        {
            IsBusy = true;
            Error = "";
            Result = null;
            Cards.Clear(); Beams.Clear(); Holds.Clear(); Axes.Clear();
            OnPropertyChanged(nameof(CanCompareFiles));
            try
            {
                await A.LoadAsync();
                await B.LoadAsync();
                if (A.LoadError != null || B.LoadError != null)
                    throw new InvalidDataException((A.LoadError != null ? "A: " + A.LoadError : "B: " + B.LoadError));
                DeliveryRecord ra = A.Record, rb = B.Record;
                Result = await Task.Run(() => DeliveryComparison.Compare(ra, rb, A.DisplayName + " " + A.DateText, B.DisplayName + " " + B.DateText));
                Show(Result);
            }
            catch (Exception ex)
            {
                Error = "These two logs can't be compared delivery by delivery: " + ex.Message +
                        (CanCompareFiles ? " The file comparison (File differences, below) still works." : "");
            }
            finally
            {
                IsBusy = false;
                OnPropertyChanged(nameof(HasResult));
                RaiseCharts();
            }
        }

        static AxisStat Stat(DeliveryComparisonResult r, string axis, string values)
        {
            return r.Summary.FirstOrDefault(s => s.Axis.StartsWith(axis, StringComparison.Ordinal) && s.Values == values && s.Sampling == "continuous");
        }

        void Show(DeliveryComparisonResult r)
        {
            AxisStat mlcE = Stat(r, "MLC", "Expected"), mlcA = Stat(r, "MLC", "Actual");
            AxisStat gE = Stat(r, "Gantry", "Expected"), gA = Stat(r, "Gantry", "Actual"), muA = Stat(r, "MU", "Actual");
            bool samePlan = (mlcE == null || mlcE.Max < 0.01) && (gE == null || gE.Max < 0.05);
            if (samePlan)
            {
                VerdictKind = "ok";
                Verdict = "Same plan, delivered twice. The planned (expected) positions are identical, so the differences below are the machine's.";
            }
            else
            {
                VerdictKind = "caution";
                Verdict = string.Format(Inv, "The plans differ: planned leaf positions differ by up to {0:F2} mm and planned gantry by up to {1:F2}°. " +
                    "The delivered differences below include the plan differences.", mlcE == null ? 0 : mlcE.Max, gE == null ? 0 : gE.Max);
            }

            var settings = AppSettings.Current;
            if (mlcA != null)
                Cards.Add(Card("MLC actual, A − B", mlcA.Max.ToString("F2", Inv) + " mm max",
                               string.Format(Inv, "RMS {0:F3} mm · 95% under {1:F3} mm", mlcA.Rms, mlcA.P95), mlcA.Max, settings.MlcToleranceMm, "mm"));
            if (gA != null)
                Cards.Add(Card("Gantry actual, A − B", gA.Max.ToString("F2", Inv) + "° max", "RMS " + gA.Rms.ToString("F3", Inv) + "°", gA.Max, settings.GantryToleranceDeg, "°"));
            if (muA != null)
                Cards.Add(Card("MU actual, A − B", muA.Max.ToString("F3", Inv) + " MU max",
                               string.Format(Inv, "A {0:F2}, B {1:F2} delivered", r.A.MuDelivered, r.B.MuDelivered), muA.Max, settings.MuTolerance, " MU"));
            int ia = r.A.Holds.Count(h => h.Type == "interruption"), ib = r.B.Holds.Count(h => h.Type == "interruption");
            Cards.Add(new Fact
            {
                Label = "Interruptions",
                Value = "A " + ia + " · B " + ib,
                Detail = ia + ib == 0 ? "No unplanned beam holds" : "Matching by control point removes their effect",
                Kind = ia + ib > 0 ? "caution" : "neutral"
            });

            foreach (var x in new[] { Tuple.Create("A", r.A), Tuple.Create("B", r.B) })
            {
                DeliveryRecord rec = x.Item1 == "A" ? A.Record : B.Record;
                foreach (var bm in x.Item2.Beams) Beams.Add(BeamRow.From(bm, x.Item1, r.AxisScale, rec));
                foreach (var h in x.Item2.Holds) Holds.Add(HoldRow.From(h, x.Item1, r.AxisScale));
            }
            foreach (var s in r.Summary) Axes.Add(AxisRow.From(s, r.AxisScale));
            foreach (string n in new[] { nameof(BeamsHeader), nameof(HoldsHeader), nameof(AxesHeader), nameof(ReportHeader) }) OnPropertyChanged(n);
            Report = DeliveryComparison.Report(r);
        }

        static Fact Card(string label, string value, string detail, double max, double? tolerance, string unit)
        {
            var f = new Fact { Label = label, Value = value, Detail = detail };
            if (tolerance.HasValue)
            {
                bool within = max <= tolerance.Value;
                f.Kind = within ? "ok" : "critical";
                f.Detail = (within ? "Within " : "Over ") + tolerance.Value.ToString("0.###", Inv) + unit + " · " + detail;
            }
            return f;
        }

        /// <summary>Opens the File comparison window for A and B (not modal, so it can sit beside the page).</summary>
        [RelayCommand]
        void CompareFiles()
        {
            if (!CanCompareFiles) return;
            var w = new Views.FileCompareWindow { DataContext = new FileCompareViewModel(A, B, FileIgnoreVersion) };
            w.Owner = System.Windows.Application.Current.MainWindow;
            w.Show();
        }

        [RelayCommand]
        void SaveReport()
        {
            if (Result == null) return;
            string folder = Dialogs.PickFolder("Save the comparison to", A.Folder);
            if (folder == null) return;
            try
            {
                List<string> files = DeliveryComparison.WriteResults(Result, folder);
                string png = Path.Combine(folder, "expected_vs_actual_comparison.png");
                DeliveryCharts.SaveFigure(Result, png, scale: Display.Angles);
                files.Add(png);
                _main.ShowNotice("Saved " + files.Count + " files (report, tables and figure) to " + folder, "info");
            }
            catch (Exception ex) { _main.ShowNotice("Could not save: " + ex.Message, "critical"); }
        }
    }
}
