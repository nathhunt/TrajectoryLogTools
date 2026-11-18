// PlanComparePageViewModel.cs - logs compared with their DICOM RT Plan (the Compare page with a
// plan as B), answer first: is it this plan, did the machine get the plan as approved, and how
// accurately was it delivered. One log, or several together (e.g. one per field, or a delivery
// resumed in another log): then the page also says, beam by beam, which logs delivered it and
// whether it was delivered completely, once. The plan is read for this page only: it isn't added
// to Open logs or Recent logs.

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

        /// <summary>The logs compared with the plan (at least one).</summary>
        public ObservableCollection<LogItem> Logs { get; } = new ObservableCollection<LogItem>();
        /// <summary>The first log (the one Back returns to).</summary>
        public LogItem A { get { return Logs.FirstOrDefault(); } }
        public bool SeveralLogs { get { return Logs.Count > 1; } }
        public string LogsTitle { get { return Logs.Count == 1 ? "Log" : Logs.Count + " logs"; } }
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
        /// <summary>Every treatment beam of the plan and how the logs delivered it (shown for several logs).</summary>
        public ObservableCollection<PlanBeamRow> Beams { get; } = new ObservableCollection<PlanBeamRow>();
        public bool ShowBeams { get { return Result != null && Result.MultipleLogs; } }
        public bool ShowFieldPicker { get { return FieldChoices.Count > 1; } }

        public string FieldsHeader { get { return "Per field · angles " + Display.AnglesName; } }

        public event EventHandler ChartsChanged;
        void RaiseCharts() { var h = ChartsChanged; if (h != null) h(this, EventArgs.Empty); }
        partial void OnSelectedFieldChanged(PlanFieldChoice value) { RaiseCharts(); }

        public PlanComparePageViewModel(IEnumerable<LogItem> logs, string planPath, MainViewModel main)
        {
            _main = main;
            foreach (LogItem l in logs.Where(l => l != null).Distinct().OrderBy(l => l.Date)) Logs.Add(l);
            _planPath = planPath;
            Run();
        }

        void LogsChanged()
        {
            OnPropertyChanged(nameof(A)); OnPropertyChanged(nameof(PathA));
            OnPropertyChanged(nameof(SeveralLogs)); OnPropertyChanged(nameof(LogsTitle));
        }

        /// <summary>Adds logs (already open, or opened now) and compares again.</summary>
        public void AddLogs(IEnumerable<LogItem> items)
        {
            bool any = false;
            foreach (LogItem l in items)
                if (!Logs.Contains(l)) { Logs.Add(l); any = true; }
            if (!any) return;
            var sorted = Logs.OrderBy(l => l.Date).ToList();
            Logs.Clear();
            foreach (LogItem l in sorted) Logs.Add(l);
            LogsChanged();
            Run();
        }

        [RelayCommand]
        void RemoveLog(LogItem log)
        {
            if (log == null || Logs.Count <= 1) return;   // at least one log
            Logs.Remove(log);
            LogsChanged();
            Run();
        }

        /// <summary>The angle scale changed: rebuild the table and redraw.</summary>
        public void RefreshAngles()
        {
            if (Result == null) return;
            Show(Result);
            RaiseCharts();
        }

        /// <summary>
        /// Files dropped on A (logs: added to the comparison) or on B (a plan: it replaces the plan; one
        /// log: then it's a log-to-log comparison with the first log).
        /// </summary>
        public void Drop(bool onA, string[] paths)
        {
            string plan = paths.FirstOrDefault(RtPlan.IsDicom);
            if (plan != null && (!onA || paths.All(RtPlan.IsDicom)))
            {
                PlanPath = plan;
                Run();
                return;
            }
            var items = _main.OpenPaths(paths.Where(p => !RtPlan.IsDicom(p)));
            _main.CurrentPage = this;
            if (items.Count == 0) return;
            if (onA) AddLogs(items);
            else if (items.Count == 1) _main.ShowCompare(A, items[0]);
            else AddLogs(items);
        }

        [RelayCommand]
        void AddLogsBrowse()
        {
            string[] files = Dialogs.OpenFiles("Logs to compare with the plan", Dialogs.LogFilter, A?.Folder);
            if (files.Length > 0) Drop(true, files);
        }

        [RelayCommand]
        void BrowseB()
        {
            string p = Dialogs.OpenFile("Plan (or a log to compare with)", Dialogs.LogOrPlanFilter, PlanPath != null ? Path.GetDirectoryName(PlanPath) : A?.Folder);
            if (p != null) Drop(false, new[] { p });
        }

        [RelayCommand]
        void Back() { if (A != null && _main.Logs.Contains(A)) _main.ShowLog(A); }

        async void Run()
        {
            IsBusy = true;
            Error = ""; Verdict = ""; IdentityWarning = ""; Report = ""; NotInLog = "";
            Result = null;
            Cards.Clear(); Fields.Clear(); FieldChoices.Clear(); Beams.Clear();
            SelectedField = null;
            OnPropertyChanged(nameof(HasResult));
            OnPropertyChanged(nameof(ShowBeams));
            var logs = Logs.ToList();
            try
            {
                string planPath = PlanPath;
                Plan = await Task.Run(() => RtPlan.Load(planPath));
                OnPropertyChanged(nameof(PlanLine));
                foreach (LogItem l in logs) await l.LoadAsync();
                LogItem bad = logs.FirstOrDefault(l => l.LoadError != null);
                if (bad != null) throw new InvalidDataException(Path.GetFileName(bad.Path) + " can't be read: " + bad.LoadError);
                var records = logs.Select(l => l.Record).ToList();
                var labels = logs.Select(l => l.DisplayName + " " + l.DateText).ToList();
                RtPlan plan = Plan;
                Result = await Task.Run(() => records.Count == 1 ? PlanComparison.Compare(records[0], plan, labels[0])
                                                                 : PlanComparison.Compare(records, labels, plan));
                Show(Result);
                Core.AppLog.Info("Compared " + (logs.Count == 1 ? "log " + A.Path : logs.Count + " logs") + " with plan " + PlanPath + Environment.NewLine +
                            (logs.Count > 1 ? string.Concat(logs.Select(l => "log " + l.Path + Environment.NewLine)) : "") +
                            (IdentityWarning.Length > 0 ? IdentityWarning + Environment.NewLine : "") + Verdict +
                            (Cards.Count > 0 ? Environment.NewLine + string.Join(" · ", Cards.Select(c => c.Label + ": " + c.Value)) : "") +
                            (Result.MultipleLogs ? string.Concat(Result.Beams.Select(b => Environment.NewLine + "beam " + b.Beam.Label + ": " + b.Status)) : "") +
                            string.Concat(Result.Fields.Where(f => !f.Compared).Select(f => Environment.NewLine + f.Label + " not compared: " + f.Error)));
                if (!Result.AnyCompared)
                    Error = "No field of " + (logs.Count == 1 ? "this log" : "these logs") + " could be compared with this plan: " +
                            string.Join("; ", Result.Fields.Select(f => f.Label + ": " + f.Error)) + ".";
            }
            catch (Exception ex)
            {
                Error = (logs.Count == 1 ? "This log" : "These logs") + " can't be compared with " + (PlanPath == null ? "the plan" : Path.GetFileName(PlanPath)) + ": " + ex.Message;
                Core.AppLog.Error("Plan comparison failed: " + ex.Message, ex, "Logs", logs.Select(l => l.Path).ToList(), "Plan", PlanPath);
            }
            finally
            {
                IsBusy = false;
                OnPropertyChanged(nameof(HasResult));
                OnPropertyChanged(nameof(ShowFieldPicker));
                OnPropertyChanged(nameof(ShowBeams));
                RaiseCharts();
            }
        }

        void Show(PlanComparisonResult r)
        {
            Cards.Clear(); Fields.Clear(); Beams.Clear();
            PlanFieldChoice keep = SelectedField;
            FieldChoices.Clear();
            bool several = r.MultipleLogs;

            // ---- identity (log by log) ----
            var warn = new List<string>();
            foreach (PlanLogInfo l in r.Logs)
            {
                string which = several ? l.Label + ": " : "";
                if (l.SamePatient == false) warn.Add(which + "the log and the plan are different patients (" + l.PatientId + " and " + r.Plan.PatientId + ").");
                if (l.SamePlanUid == false) warn.Add(which + "the log was delivered from another plan, or another version of this plan: its plan UID is " + l.PlanUid + ", the plan's is " + r.Plan.SopInstanceUid + ".");
                if (l.Error != null) warn.Add(which + "not compared: " + l.Error);
            }
            IdentityWarning = string.Join(" ", warn.Select(w => char.ToUpper(w[0]) + w.Substring(1)));

            // ---- verdict ----
            var compared = r.Compared.ToList();
            if (compared.Count > 0)
            {
                string identity = r.SamePlanUid == true ? (several ? "These logs are deliveries of this plan (same plan UID)." : "This log is a delivery of this plan (same plan UID).")
                                : r.SamePlanUid == false ? "The plan UIDs differ."
                                : several ? "Not every log records a plan UID, so only the plan values can say whether they are this plan."
                                : "The log doesn't record a plan UID, so only the plan values can say whether it is this plan.";
                string partial = several
                    ? (r.Beams.All(b => b.Complete && !b.Repeated) ? " Every beam of the plan was delivered completely, once." : " Not every beam was delivered completely, once (see Beams).")
                    : compared.Any(f => f.Partial) ? " The log covers part of a beam (see Per field)." : "";
                if (r.TransferOk)
                {
                    VerdictKind = r.SamePlanUid == false || r.SamePatient == false || (several && !r.Beams.All(b => b.Complete && !b.Repeated)) ? "caution" : "ok";
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
                // one log: the MU of what it covers; several: the plan's total against all delivered
                double planned = several ? r.MuPlanTotal : compared.Sum(f => f.MuPlanCovered), delivered = several ? r.MuDeliveredTotal : compared.Sum(f => f.MuDelivered);
                Cards.Add(Card("MU, delivered − plan", (delivered - planned).ToString("+0.000;−0.000;0.000", Inv) + " MU",
                               string.Format(Inv, "{0:F2} of {1:F2} MU{2}", delivered, planned, several ? " (plan total)" : ""), Math.Abs(delivered - planned), settings.MuTolerance, " MU"));
                Cards.Add(new Fact
                {
                    Label = "Plan check (log planned vs plan)",
                    Value = r.TransferOk ? "Matches" : "Differs",
                    Detail = string.Format(Inv, "Leaves {0:F3} mm, gantry {1:F3}° max", compared.Max(f => f.TransferLeafMaxMm), compared.Max(f => f.TransferGantryMaxDeg)),
                    Kind = r.TransferOk ? "ok" : "critical",
                });
                int beams = r.Plan.TreatmentBeams.Count();
                if (several)
                {
                    int complete = r.Beams.Count(b => b.Complete && !b.Repeated);
                    var notes = r.Beams.Where(b => !(b.Complete && !b.Repeated)).Select(b => b.Beam.Label + ": " + b.Status.ToLowerInvariant()).ToList();
                    Cards.Add(new Fact
                    {
                        Label = "Beams",
                        Value = complete + " of " + beams + " complete",
                        Detail = notes.Count == 0 ? "Each delivered completely, once, in " + r.Logs.Count + " logs" : string.Join("; ", notes),
                        Kind = notes.Count == 0 && !r.Fields.Any(f => !f.Compared) ? "ok" : "caution",
                    });
                }
                else Cards.Add(new Fact
                {
                    Label = "Fields",
                    Value = compared.Count + " of " + beams + (beams == 1 ? " beam" : " beams"),
                    Detail = r.BeamsNotInLog.Count == 0 ? "Every treatment beam is in this log" : "Not in this log: " + string.Join(", ", r.BeamsNotInLog.Select(b => b.Label)),
                    Kind = r.Fields.Any(f => !f.Compared) || compared.Any(f => f.Partial) ? "caution" : "neutral",
                });
            }
            NotInLog = r.BeamsNotInLog.Count == 0 ? "" : several
                ? "Plan beams in none of these logs: " + string.Join(", ", r.BeamsNotInLog.Select(b => b.Label)) + "."
                : "Plan beams not in this log: " + string.Join(", ", r.BeamsNotInLog.Select(b => b.Label)) + " (usual when each field is delivered in its own log).";

            foreach (PlanFieldComparison f in r.Fields) Fields.Add(PlanFieldRow.From(f));
            if (several) foreach (PlanBeamSummary b in r.Beams) Beams.Add(PlanBeamRow.From(b));
            foreach (PlanFieldComparison f in compared)
                FieldChoices.Add(new PlanFieldChoice
                {
                    Field = f,
                    Label = f.Label + (f.Beam.Label != f.Label ? " ↔ plan beam " + f.Beam.Label : "") + (several ? " · " + f.LogLabel : ""),
                });
            SelectedField = FieldChoices.FirstOrDefault(c => keep != null && c.Field.Subbeam == keep.Field.Subbeam && c.Field.LogPath == keep.Field.LogPath) ?? FieldChoices.FirstOrDefault();
            OnPropertyChanged(nameof(ShowBeams));
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
                Core.AppLog.Info("Saved the plan comparison of " + string.Join(", ", Logs.Select(l => l.Path)) + " with " + PlanPath, files);
            }
            catch (Exception ex)
            {
                _main.ShowNotice("Could not save: " + ex.Message, "critical");
                Core.AppLog.Error("Could not save the plan comparison", ex, "Folder", folder, "Log", A?.Path, "Plan", PlanPath);
            }
        }
    }

    /// <summary>One treatment beam of the plan and how the logs delivered it, for the Beams table.</summary>
    public sealed class PlanBeamRow
    {
        public string Beam { get; set; }
        public string Status { get; set; }
        /// <summary>"ok" or "caution".</summary>
        public string StatusKind { get; set; }
        public string ControlPoints { get; set; }
        public string MuPlan { get; set; }
        public string MuDelivered { get; set; }
        public string DeliveredIn { get; set; }

        public static PlanBeamRow From(PlanBeamSummary b)
        {
            return new PlanBeamRow
            {
                Beam = b.Beam.Label,
                Status = b.Status,
                StatusKind = b.Complete && !b.Repeated ? "ok" : "caution",
                ControlPoints = b.CoveredControlPoints + " of " + b.ControlPoints,
                MuPlan = Fmt.N(b.MuPlan, 3),
                MuDelivered = b.Delivered ? Fmt.N(b.MuDelivered, 3) : "",
                DeliveredIn = string.Join(", ", b.Fields.Select(f => f.LogLabel)),
            };
        }
    }

    /// <summary>One field of a log-vs-plan comparison, for the Per field table.</summary>
    public sealed class PlanFieldRow
    {
        public string Log { get; set; }
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
            var row = new PlanFieldRow { Log = f.LogLabel, Field = f.Label, PlanBeam = f.Beam == null ? "" : f.Beam.Label };
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
