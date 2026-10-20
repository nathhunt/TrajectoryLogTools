// RebuildDetailsViewModel.cs - the Rebuild details window: one rebuilt field, summary first (status,
// what to check, the numbers of the QA report as cards), then the difference map, leaf by leaf and
// the full report. Previous / Next step through the fields of the same rebuild.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrajectoryLogTools.Analysis;
using TrajectoryLogTools.App.Services;

namespace TrajectoryLogTools.App.ViewModels
{
    public partial class RebuildDetailsViewModel : ObservableObject
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly List<RebuildResultRow> _rows;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(Row), nameof(Position), nameof(Report), nameof(Output), nameof(PatientId), nameof(HasProblems), nameof(Title))]
        [NotifyCanExecuteChangedFor(nameof(PreviousCommand), nameof(NextCommand))]
        private int _index;
        [ObservableProperty] private int _selectedValue = 1;
        [ObservableProperty] private int _maxValue = 113;

        public RebuildResultRow Row { get { return _rows[Index]; } }
        public SubbeamResult Result { get { return Row.Result; } }
        public string Title { get { return Row.Name + " · Rebuild details"; } }
        public string Position { get { return "Field " + (Index + 1) + " of " + _rows.Count; } }
        public string PatientId { get { return Row.Log?.PatientId; } }
        public string Output { get { return Result?.OutputPath ?? ""; } }
        public string Report { get { return Result?.Report ?? Row.Error ?? ""; } }
        public bool HasProblems { get { return Row.Problems.Count > 0; } }
        public bool ShowNavigation { get { return _rows.Count > 1; } }
        public string SelectedValueName { get { return "Pair " + (SelectedValue / 2 + 1) + (SelectedValue % 2 == 0 ? " X1" : " X2"); } }

        public ObservableCollection<Fact> Cards { get; } = new ObservableCollection<Fact>();

        public event EventHandler ChartsChanged;

        public RebuildDetailsViewModel(List<RebuildResultRow> rows, int index)
        {
            _rows = rows;
            _index = Math.Max(0, Math.Min(index, rows.Count - 1));
            Fill();
        }

        partial void OnIndexChanged(int value) { Fill(); }
        partial void OnSelectedValueChanged(int value) { OnPropertyChanged(nameof(SelectedValueName)); Raise(); }

        void Raise() { var h = ChartsChanged; if (h != null) h(this, EventArgs.Empty); }

        bool CanPrevious() { return Index > 0; }
        bool CanNext() { return Index < _rows.Count - 1; }

        [RelayCommand(CanExecute = nameof(CanPrevious))]
        void Previous() { Index--; }

        [RelayCommand(CanExecute = nameof(CanNext))]
        void Next() { Index++; }

        [RelayCommand]
        void OpenFolder()
        {
            string p = Output.Length > 0 ? Path.GetDirectoryName(Output) : Row.Log?.Folder;
            if (!string.IsNullOrEmpty(p)) Dialogs.OpenFolder(p);
        }

        void Fill()
        {
            Cards.Clear();
            SubbeamResult s = Result;
            if (s != null && s.Reconstructed != null)
            {
                MaxValue = s.Reconstructed.GetLength(1) - 1;
                if (SelectedValue > MaxValue) SelectedValue = MaxValue;
            }
            if (s != null && s.Ok)
            {
                Cards.Add(new Fact { Label = "Delivered vs planned", Value = Row.Delivered, Detail = Row.DeliveredDetail + " · actual minus the plan's (expected) positions" });
                Cards.Add(new Fact { Label = "Eclipse leaf-gap rules", Value = Row.GapRules, Kind = s.RemainingGapProblems == 0 ? "ok" : "caution",
                                     Detail = string.Format(Inv, "min gap {0:0.0##} mm · {1} closed, {2} opened · largest leaf move {3:F3} mm",
                                                            s.MinGapMm, s.GapsClosed, s.GapsOpened, Math.Max(s.MaxGapShiftMm, s.MaxPinShiftMm)) });
                Cards.Add(new Fact { Label = "Gantry", Value = Row.Gantry,
                                     Detail = string.Format(Inv, "from the {0} · start {1:F1}° IEC · actual vs planned up to {2:F2}°", Source(s.GantrySource), s.GantryStartIec, s.GantryActualVsExpectedMaxDeg) });
                Cards.Add(new Fact { Label = "Control points", Value = s.ControlPoints.ToString(Inv),
                                     Detail = "log CP " + s.FirstLogControlPoint + "–" + (s.FirstLogControlPoint + s.ControlPoints - 1) +
                                              (s.EmptyIntervals.Count == 0 ? " · every interval had data" : " · " + s.EmptyIntervals.Count + " interval(s) without data"),
                                     Kind = s.EmptyIntervals.Count == 0 ? "neutral" : "caution" });
                Cards.Add(new Fact { Label = "MU", Value = s.Mu.ToString("F2", Inv) + " planned", Detail = s.DeliveredMu.ToString("F2", Inv) + " delivered" });
                Cards.Add(new Fact { Label = "Snapshots used", Value = s.SnapshotsUsed.ToString("N0", Inv),
                                     Detail = s.HoldSnapshotsExcluded + " beam-hold excluded · " + s.SetupSnapshotsSkipped + " set-up skipped" });
                Cards.Add(new Fact { Label = "Built from", Value = (s.Positions == "expected" ? "Expected" : "Actual") + " positions",
                                     Detail = (s.Axis == "cp" ? "control-point axis" : "delivered-MU axis") + " · " + s.Leaves + " leaves · collimator " + s.CollimatorDeg.ToString("F1", Inv) + "°" });
                if (!string.IsNullOrEmpty(s.TemplatePath))
                {
                    bool fits = !double.IsNaN(s.TemplateGantryMaxDeg) && s.TemplateGantryMaxDeg <= 0.5 && s.TemplateMetersetMax <= 0.002;
                    Cards.Add(new Fact { Label = "Eclipse export", Value = fits ? "Matches the log" : "Doesn't match", Kind = fits ? "ok" : "critical",
                                         Detail = string.Format(Inv, "method check {0:F3} mm · gantry {1:F3}° · meterset weight {2:F5}", s.MethodCheckMaxMm, s.TemplateGantryMaxDeg, s.TemplateMetersetMax) });
                }
            }
            Raise();
        }

        static string Source(string g) { return g == "template" ? "Eclipse export" : g == "actual" ? "actual gantry" : "plan"; }
    }
}
