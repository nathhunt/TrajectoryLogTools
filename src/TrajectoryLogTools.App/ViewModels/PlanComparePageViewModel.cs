// PlanComparePageViewModel.cs - a log compared with its DICOM RT Plan (the Compare page with a
// plan as B), answer first: is it this plan, did the machine get the plan as approved, and how
// accurately was it delivered. The plan is read for this page only: it isn't added to Open logs
// or Recent logs.

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

namespace TrajectoryLogTools.App.ViewModels
{
    /// <summary>One field of the log in the field picker.</summary>
    public sealed class PlanFieldChoice
    {
        public PlanFieldComparison Field { get; set; }
        public string Label { get; set; }
    }

    public partial class PlanComparePageViewModel : ObservableObject
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly MainViewModel _main;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PathA), nameof(DateA))]
        private LogItem _a;
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(PlanLine))]
        private string _planPath;
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _error = "";
        [ObservableProperty] private string _verdict = "";
        /// <summary>"ok" or "caution".</summary>
        [ObservableProperty] private string _verdictKind = "ok";
        [ObservableProperty] private string _identityWarning = "";
        [ObservableProperty] private string _report = "";
        [ObservableProperty] private string _notInLog = "";
        [ObservableProperty] private PlanFieldChoice _selectedField;

        public string PathA { get { return A == null ? "" : A.Path; } }
        public string DateA { get { return A == null ? "" : A.DisplayName + " · " + A.DateText; } }

        public RtPlan Plan { get; private set; }
        public PlanComparisonResult Result { get; private set; }
        public bool HasResult { get { return Result != null && Result.AnyCompared; } }

        /// <summary>The plan, as one line: patient, plan, approval, beams.</summary>
        public string PlanLine
        {
            get
            {
                if (Plan == null) return PlanPath == null ? "" : Path.GetFileName(PlanPath);
                int n = Plan.TreatmentBeams.Count();
                return string.Join(" · ", new[] { Plan.PatientId, Plan.DisplayName, Plan.ApprovalStatus.Length > 0 ? Plan.ApprovalStatus.ToLowerInvariant() : null,
                                                  n + (n == 1 ? " treatment beam" : " treatment beams") }.Where(s => !string.IsNullOrEmpty(s)));
            }
        }

        public ObservableCollection<Fact> Cards { get; } = new ObservableCollection<Fact>();
        public ObservableCollection<PlanFieldRow> Fields { get; } = new ObservableCollection<PlanFieldRow>();
        public ObservableCollection<PlanFieldChoice> FieldChoices { get; } = new ObservableCollection<PlanFieldChoice>();
        public bool ShowFieldPicker { get { return FieldChoices.Count > 1; } }

        public string FieldsHeader { get { return "Per field · angles " + Display.AnglesName; } }

        public event EventHandler ChartsChanged;
        void RaiseCharts() { var h = ChartsChanged; if (h != null) h(this, EventArgs.Empty); }
        partial void OnSelectedFieldChanged(PlanFieldChoice value) { RaiseCharts(); }

        public PlanComparePageViewModel(LogItem a, string planPath, MainViewModel main)
        {
            _main = main;
            _a = a;
            _planPath = planPath;
            Run();
        }

        /// <summary>The angle scale changed: rebuild the table and redraw.</summary>
        public void RefreshAngles()
        {
            if (Result == null) return;
            Show(Result);
            RaiseCharts();
        }

        /// <summary>A file dropped on A (a log) or B (a plan, or a log: then it's a log-to-log comparison).</summary>
        public void Replace(bool isA, string path)
        {
            if (RtPlan.IsDicom(path))
            {
                if (isA) { _main.ShowNotice("A is the log: drop the plan on B.", "caution"); return; }
                PlanPath = path;
                Run();
                return;
            }
            var items = _main.OpenPaths(new[] { path });
            if (items.Count != 1) { _main.CurrentPage = this; return; }
            if (isA) { _main.CurrentPage = this; A = items[0]; Run(); }
            else _main.ShowCompare(A, items[0]);
        }

        [RelayCommand]
        void BrowseA()
        {
            string p = Dialogs.OpenFile("Log", Dialogs.LogFilter, A?.Folder);
            if (p != null) Replace(true, p);
        }

        [RelayCommand]
        void BrowseB()
        {
            string p = Dialogs.OpenFile("Plan (or a log to compare with)", Dialogs.LogOrPlanFilter, PlanPath != null ? Path.GetDirectoryName(PlanPath) : A?.Folder);
            if (p != null) Replace(false, p);
        }

        [RelayCommand]
        void Back() { if (A != null && _main.Logs.Contains(A)) _main.ShowLog(A); }

        async void Run()
        {
            IsBusy = true;
            Error = ""; Verdict = ""; IdentityWarning = ""; Report = ""; NotInLog = "";
            Result = null;
            Cards.Clear(); Fields.Clear(); FieldChoices.Clear();
            SelectedField = null;
            OnPropertyChanged(nameof(HasResult));
            try
            {
                string planPath = PlanPath;
                Plan = await Task.Run(() => RtPlan.Load(planPath));
                OnPropertyChanged(nameof(PlanLine));
                await A.LoadAsync();
                if (A.LoadError != null) throw new InvalidDataException("the log can't be read: " + A.LoadError);
                DeliveryRecord rec = A.Record;
                RtPlan plan = Plan;
                string label = A.DisplayName + " " + A.DateText;
                Result = await Task.Run(() => PlanComparison.Compare(rec, plan, label));
                Show(Result);
                if (!Result.AnyCompared)
                    Error = "No field of this log could be compared with this plan: " +
                            string.Join("; ", Result.Fields.Select(f => f.Label + ": " + f.Error)) + ".";
            }
            catch (Exception ex)
            {
                Error = "This log can't be compared with " + (PlanPath == null ? "the plan" : Path.GetFileName(PlanPath)) + ": " + ex.Message;
            }
            finally
            {
                IsBusy = false;
                OnPropertyChanged(nameof(HasResult));
                OnPropertyChanged(nameof(ShowFieldPicker));
                RaiseCharts();
            }
        }

        void Show(PlanComparisonResult r)
        {
            Cards.Clear(); Fields.Clear();
            PlanFieldChoice keep = SelectedField;
            FieldChoices.Clear();

            // ---- identity ----
            var warn = new List<string>();
            if (r.SamePatient == false) warn.Add("The log and the plan are different patients (" + r.LogPatientId + " and " + r.Plan.PatientId + ").");
            if (r.SamePlanUid == false) warn.Add("The log was delivered from another plan, or another version of this plan: its plan UID is " + r.LogPlanUid + ", the plan's is " + r.Plan.SopInstanceUid + ".");
            IdentityWarning = string.Join(" ", warn);

            // ---- verdict ----
            var compared = r.Compared.ToList();
            if (compared.Count > 0)
            {
                string identity = r.SamePlanUid == true ? "This log is a delivery of this plan (same plan UID)."
                                : r.SamePlanUid == false ? "The plan UIDs differ."
                                : "The log doesn't record a plan UID, so only the plan values can say whether it is this plan.";
                string partial = compared.Any(f => f.Partial) ? " The log covers part of a beam (see Per field)." : "";
                if (r.TransferOk)
                {
                    VerdictKind = r.SamePlanUid == false || r.SamePatient == false ? "caution" : "ok";
                    Verdict = identity + " The machine's planned values match the plan, so the differences below are the delivery's." + partial;
                }
                else
                {
                    VerdictKind = "caution";
                    var bad = compared.Where(f => !f.TransferOk).ToList();
                    Verdict = identity + string.Format(Inv, " The machine's planned values differ from the plan in {0} (leaves by up to {1:F2} mm, gantry by up to {2:F2}°): " +
                        "it may have been delivered from another version of the plan. The delivery differences below include these.",
                        string.Join(", ", bad.Select(f => f.Label)), bad.Max(f => f.TransferLeafMaxMm), bad.Max(f => f.TransferGantryMaxDeg)) + partial;
                }
            }

            // ---- cards ----
            var settings = AppSettings.Current;
            if (compared.Count > 0)
            {
                DiffStat leaves = r.Leaves, gantry = r.Gantry;
                Cards.Add(Card("MLC, actual − plan", leaves.Max.ToString("F2", Inv) + " mm max",
                               string.Format(Inv, "RMS {0:F3} mm · 95% under {1:F3} mm", leaves.Rms, leaves.P95), leaves.Max, settings.MlcToleranceMm, "mm"));
                Cards.Add(Card("Gantry, actual − plan", gantry.Max.ToString("F2", Inv) + "° max", "RMS " + gantry.Rms.ToString("F3", Inv) + "°", gantry.Max, settings.GantryToleranceDeg, "°"));
                double planned = compared.Sum(f => f.MuPlanCovered), delivered = compared.Sum(f => f.MuDelivered);
                Cards.Add(Card("MU, delivered − plan", (delivered - planned).ToString("+0.000;−0.000;0.000", Inv) + " MU",
                               string.Format(Inv, "{0:F2} of {1:F2} MU", delivered, planned), Math.Abs(delivered - planned), settings.MuTolerance, " MU"));
                Cards.Add(new Fact
                {
                    Label = "Plan check (log planned vs plan)",
                    Value = r.TransferOk ? "Matches" : "Differs",
                    Detail = string.Format(Inv, "Leaves {0:F3} mm, gantry {1:F3}° max", compared.Max(f => f.TransferLeafMaxMm), compared.Max(f => f.TransferGantryMaxDeg)),
                    Kind = r.TransferOk ? "ok" : "critical",
                });
                int beams = r.Plan.TreatmentBeams.Count();
                Cards.Add(new Fact
                {
                    Label = "Fields",
                    Value = compared.Count + " of " + beams + (beams == 1 ? " beam" : " beams"),
                    Detail = r.BeamsNotInLog.Count == 0 ? "Every treatment beam is in this log" : "Not in this log: " + string.Join(", ", r.BeamsNotInLog.Select(b => b.Label)),
                    Kind = r.Fields.Any(f => !f.Compared) || compared.Any(f => f.Partial) ? "caution" : "neutral",
                });
            }
            NotInLog = r.BeamsNotInLog.Count == 0 ? "" :
                "Plan beams not in this log: " + string.Join(", ", r.BeamsNotInLog.Select(b => b.Label)) + " (usual when each field is delivered in its own log).";

            foreach (PlanFieldComparison f in r.Fields) Fields.Add(PlanFieldRow.From(f));
            foreach (PlanFieldComparison f in compared)
                FieldChoices.Add(new PlanFieldChoice { Field = f, Label = f.Label + (f.Beam.Label != f.Label ? " ↔ plan beam " + f.Beam.Label : "") });
            SelectedField = FieldChoices.FirstOrDefault(c => keep != null && c.Field.Subbeam == keep.Field.Subbeam) ?? FieldChoices.FirstOrDefault();
            OnPropertyChanged(nameof(FieldsHeader));
            OnPropertyChanged(nameof(ShowFieldPicker));
            Report = PlanComparison.Report(r);
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

        [RelayCommand]
        void SaveReport()
        {
            if (Result == null || !Result.AnyCompared) return;
            string folder = Dialogs.PickFolder("Save the plan comparison to", A.Folder);
            if (folder == null) return;
            try
            {
                List<string> files = PlanComparison.WriteResults(Result, folder);
                files.AddRange(PlanCharts.SaveFigures(Result, folder, scale: Display.Angles));
                _main.ShowNotice("Saved " + files.Count + " files (report, tables and figures) to " + folder, "info");
            }
            catch (Exception ex) { _main.ShowNotice("Could not save: " + ex.Message, "critical"); }
        }
    }

    /// <summary>One field of a log-vs-plan comparison, for the Per field table.</summary>
    public sealed class PlanFieldRow
    {
        public string Field { get; set; }
        public string PlanBeam { get; set; }
        public string ControlPoints { get; set; }
        public string MuPlan { get; set; }
        public string MuLogPlanned { get; set; }
        public string MuDelivered { get; set; }
        public string Gantry { get; set; }
        public string Collimator { get; set; }
        public string PlanCheck { get; set; }
        public string MlcMax { get; set; }
        public string MlcRms { get; set; }
        public string Mlc95 { get; set; }
        public string MlcCpMax { get; set; }
        public string GantryMax { get; set; }
        public string Note { get; set; }

        static string A(double iec) { return Fmt.N(Analysis.Angles.Convert(iec, AngleScale.Iec, Display.Angles), 1); }

        public static PlanFieldRow From(PlanFieldComparison f)
        {
            var row = new PlanFieldRow { Field = f.Label, PlanBeam = f.Beam == null ? "" : f.Beam.Label };
            if (!f.Compared) { row.Note = "Not compared: " + f.Error; return row; }
            int last = f.FirstPlanCp + f.ControlPoints - 1;
            row.ControlPoints = f.FirstPlanCp + "–" + last + " of " + f.PlanControlPoints + (f.Partial ? " (partial)" : "");
            row.MuPlan = Fmt.N(f.Partial ? f.MuPlanCovered : f.MuPlan, 3);
            row.MuLogPlanned = Fmt.N(f.MuExpected, 3);
            row.MuDelivered = Fmt.N(f.MuDelivered, 3);
            row.Gantry = A(f.PlanGantry[0]) + " → " + A(f.PlanGantry[f.ControlPoints - 1]);
            row.Collimator = A(f.PlanCollimator) + " (actual " + A(f.ActualCollimator) + ")";
            row.PlanCheck = f.TransferOk ? "Matches" : string.Format(CultureInfo.InvariantCulture, "Differs: {0:F3} mm, {1:F3}°", f.TransferLeafMaxMm, f.TransferGantryMaxDeg);
            row.MlcMax = Fmt.N(f.Leaves.Max, 3);
            row.MlcRms = Fmt.N(f.Leaves.Rms, 3);
            row.Mlc95 = Fmt.N(f.Leaves.P95, 3);
            row.MlcCpMax = Fmt.N(f.CpLeaves.Max, 3);
            row.GantryMax = Fmt.N(f.Gantry.Max, 3);
            row.Note = "Matched by " + f.MatchedBy + "; largest leaf difference: " + f.Leaves.MaxAt;
            return row;
        }
    }
}
