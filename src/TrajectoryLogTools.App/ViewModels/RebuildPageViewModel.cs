// RebuildPageViewModel.cs - Rebuild Eclipse MLC for one or more logs (the ticked ones): their
// fields are found automatically and listed by delivery (plan and day), you choose where to save,
// and each field gets an Eclipse MLC file built from its log, with a status: Ready to import,
// Check first (with the reasons) or Failed.
//
// A delivery may come as one log per field or as one log holding several fields (subbeams); both
// are listed the same way, a field per row, in the order delivered. Each log is rebuilt on its own.
//
// Eclipse exports (templates) are an Advanced option, off by default: with one, the file keeps
// the export's header, meterset weights, jaws and angles and only the leaves are replaced.

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
using TrajectoryLogTools.Core;

namespace TrajectoryLogTools.App.ViewModels
{
    /// <summary>A rebuilt field, as shown on its result card.</summary>
    public sealed class RebuildResultRow
    {
        public SubbeamResult Result { get; set; }
        public LogItem Log { get; set; }
        public string Name { get; set; }
        public string Source { get; set; }
        public string StatusText { get; set; }
        /// <summary>"ok", "caution" or "critical".</summary>
        public string StatusKind { get; set; }
        public string FileName { get; set; }
        public string Delivered { get; set; }
        public string DeliveredDetail { get; set; }
        public string GapRules { get; set; }
        public string GapDetail { get; set; }
        public string Gantry { get; set; }
        public string GantryDetail { get; set; }
        public List<string> Problems { get; set; } = new List<string>();
        public string Error { get; set; }
        public bool Ok { get { return Result != null && Result.Ok; } }
    }

    public partial class RebuildPageViewModel : ObservableObject
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        readonly MainViewModel _main;
        /// <summary>The logs asked for, in the order given; those that can't be rebuilt are in Skipped.</summary>
        public List<LogItem> Items { get; }

        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _error = "";
        [ObservableProperty] private string _mlcText = "";
        [ObservableProperty] private bool _saveNextToLog = true;
        [ObservableProperty] private string _otherFolder = "";
        // advanced
        [ObservableProperty] private bool _useActual = true;
        [ObservableProperty] private bool _axisMu = true;
        [ObservableProperty] private bool _gantryFromPlan = true;
        [ObservableProperty] private string _minGap = "";
        [ObservableProperty] private bool _includeHold;
        [ObservableProperty] private bool _useExports;
        [ObservableProperty] private RebuildResultRow _selected;
        [ObservableProperty] private int _selectedValue = 1;
        [ObservableProperty] private int _maxValue = 113;

        public ObservableCollection<MlcFieldGroup> Groups { get; } = new ObservableCollection<MlcFieldGroup>();
        public ObservableCollection<SkippedLogRow> Skipped { get; } = new ObservableCollection<SkippedLogRow>();
        public ObservableCollection<RebuildResultRow> Results { get; } = new ObservableCollection<RebuildResultRow>();

        IEnumerable<MlcSubbeamRow> Fields { get { return Groups.SelectMany(g => g.Fields); } }
        List<LogItem> Usable { get { return Fields.Select(f => f.Log).Distinct().ToList(); } }

        public string Title { get { return Items.Count == 1 ? Items[0].DisplayName : Items.Count + " logs"; } }
        public string Detail
        {
            get
            {
                if (Items.Count == 1) return Items[0].DateText + " · " + Items[0].Path;
                var plans = Items.Select(i => i.DisplayName).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                return plans.Count <= 3 ? string.Join(", ", plans) : plans.Count + " plans";
            }
        }
        public string PathsText { get { return string.Join(Environment.NewLine, Items.Select(i => i.Path)); } }
        public string FieldsHint
        {
            get
            {
                return Items.Count == 1
                    ? "Found in the log. Each ticked field gets its own Eclipse MLC file, built from the leaf positions the machine delivered."
                    : "Found in the " + Items.Count + " logs and listed by delivery (plan and day), in the order delivered. " +
                      "Each ticked field gets its own Eclipse MLC file, built from the leaf positions the machine delivered.";
            }
        }
        public string NextToLogText
        {
            get
            {
                var folders = Items.Select(i => i.Folder).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (folders.Count == 1) return (Items.Count == 1 ? "Next to the log (" : "Next to the logs (") + Items[0].FolderShort + ")";
                return "Next to each log (" + folders.Count + " folders)";
            }
        }
        public bool HasSkipped { get { return Skipped.Count > 0; } }
        public string AdvancedSummary
        {
            get
            {
                double gapDefault = Usable.Select(l => l.Record?.Mlc?.MinDynamicGapMm).FirstOrDefault(g => g.HasValue) ?? 0.5;
                var parts = new List<string>
                {
                    UseActual ? "actual positions" : "expected positions",
                    AxisMu ? "delivered-MU axis" : "control-point axis",
                    "gap " + (string.IsNullOrWhiteSpace(MinGap) ? gapDefault.ToString("0.0##", Inv) : MinGap.Trim()) + " mm",
                };
                if (!GantryFromPlan) parts.Add("actual gantry");
                if (IncludeHold) parts.Add("with beam-hold snapshots");
                if (UseExports) parts.Add("using Eclipse exports");
                return string.Join(", ", parts);
            }
        }
        public string RebuildText
        {
            get
            {
                int n = Fields.Count(f => f.Include);
                return n == 1 ? "Rebuild 1 field" : "Rebuild " + n + " fields";
            }
        }
        public string SelectedValueName { get { return "Pair " + (SelectedValue / 2 + 1) + (SelectedValue % 2 == 0 ? " X1" : " X2"); } }
        public string SelectedReport { get { return Selected?.Result?.Report ?? Selected?.Error ?? ""; } }

        public event EventHandler ChartsChanged;

        public RebuildPageViewModel(IEnumerable<LogItem> items, MainViewModel main)
        {
            Items = items.Distinct().ToList();
            _main = main;
            PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(UseActual) || e.PropertyName == nameof(AxisMu) || e.PropertyName == nameof(MinGap) ||
                    e.PropertyName == nameof(GantryFromPlan) || e.PropertyName == nameof(IncludeHold) || e.PropertyName == nameof(UseExports))
                    OnPropertyChanged(nameof(AdvancedSummary));
            };
            Load();
        }

        void RaiseCharts() { var h = ChartsChanged; if (h != null) h(this, EventArgs.Empty); }

        partial void OnSelectedChanged(RebuildResultRow value)
        {
            OnPropertyChanged(nameof(SelectedReport));
            if (value?.Result?.Reconstructed != null) MaxValue = value.Result.Reconstructed.GetLength(1) - 1;
            RaiseCharts();
        }

        partial void OnSelectedValueChanged(int value) { OnPropertyChanged(nameof(SelectedValueName)); RaiseCharts(); }

        // =========================================================================================
        // Finding the fields
        // =========================================================================================

        /// <summary>A field found in a log, before it is placed in its delivery.</summary>
        sealed class Found
        {
            public LogItem Log;
            public SubbeamInfo Info;
            public string Name;
            public int Index;      // 1-based position in its log
            public int InLog;      // fields in its log
        }

        async void Load()
        {
            IsBusy = true;
            try
            {
                await Task.WhenAll(Items.Select(i => i.LoadAsync()));
                var found = new List<Found>();
                var mlcs = new List<string>();
                foreach (LogItem item in Items)
                {
                    string reason = null;
                    if (item.LoadError != null) reason = "Can't be read: " + item.LoadError;
                    else if (item.Record.Mlc == null) reason = MlcGeometry.UnsupportedMessage(item.Record.LeafCount);
                    List<SubbeamInfo> infos = null;
                    if (reason == null)
                    {
                        DeliveryRecord d = item.Record;
                        try { infos = await Task.Run(() => TemplateMatcher.Describe(d)); }
                        catch (Exception ex) { reason = ex.Message; }
                        if (infos != null && infos.Count == 0) reason = "No field with the beam on was found in it.";
                    }
                    if (reason != null)
                    {
                        Skipped.Add(new SkippedLogRow { Name = item.DisplayName + " · " + item.DateText + " · " + Path.GetFileName(item.Path), Reason = reason });
                        continue;
                    }
                    mlcs.Add(item.Record.Mlc.Description);
                    List<Subbeam> subbeams = item.Record.Log != null ? item.Record.Log.Subbeams : new List<Subbeam>();
                    for (int k = 0; k < infos.Count; k++)
                    {
                        SubbeamInfo i = infos[k];
                        string name = i.Subbeam >= 1 && i.Subbeam <= subbeams.Count && !string.IsNullOrWhiteSpace(subbeams[i.Subbeam - 1].Name)
                            ? subbeams[i.Subbeam - 1].Name.Trim() : null;
                        found.Add(new Found { Log = item, Info = i, Name = name, Index = k + 1, InLog = infos.Count });
                    }
                }
                var distinctMlc = mlcs.Distinct().ToList();
                MlcText = distinctMlc.Count == 1 ? distinctMlc[0] : string.Join(" / ", distinctMlc);
                BuildGroups(found);
                if (Items.Count == 1 && Skipped.Count == 1) { Error = Skipped[0].Reason; Skipped.Clear(); }
            }
            catch (Exception ex) { Error = ex.Message; }
            finally
            {
                IsBusy = false;
                OnPropertyChanged(nameof(HasSkipped));
                OnPropertyChanged(nameof(RebuildText));
                OnPropertyChanged(nameof(AdvancedSummary));
            }
        }

        /// <summary>
        /// One group per delivery: same patient (when the header says), same plan, same day. Within
        /// it, fields in the order delivered (log time, then position in the log). A field name met
        /// twice in a delivery is flagged: usually an interrupted field delivered in two logs.
        /// </summary>
        void BuildGroups(List<Found> found)
        {
            var deliveries = found
                .GroupBy(f => (Patient(f.Log) + "|" + f.Log.DisplayName + "|" + f.Log.Date.Date.ToString("yyyyMMdd", Inv)).ToUpperInvariant())
                .Select(g => g.OrderBy(f => f.Log.Date).ThenBy(f => f.Log.Path, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.Index).ToList())
                .OrderBy(g => g[0].Log.Date)
                .ToList();
            foreach (List<Found> fields in deliveries)
            {
                LogItem first = fields[0].Log;
                int logs = fields.Select(f => f.Log).Distinct().Count();
                var group = new MlcFieldGroup
                {
                    Title = first.DisplayName,
                    Detail = first.Date.ToString("dd MMM yyyy", Inv) + " · " + Count(fields.Count, "field") + " in " + Count(logs, "log"),
                };
                int unnamed = 0;
                foreach (Found f in fields)
                {
                    string name = f.Name ?? (f.InLog == 1 && logs == 1 ? "Field" : "Field " + (++unnamed));
                    string time = f.Log.Date.ToString("HH:mm", Inv);
                    string file = Path.GetFileName(f.Log.Path);
                    var row = new MlcSubbeamRow
                    {
                        Log = f.Log,
                        Subbeam = f.Info.Subbeam,
                        Name = name,
                        ControlPoints = f.Info.ControlPoints,
                        GantryIec = f.Info.GantryIec,
                        Info = string.Format(Inv, "{0} control points · gantry {1:F1}° · {2:F1} MU", f.Info.ControlPoints, f.Info.GantryIec, f.Info.Mu),
                        Source = (f.InLog == 1 ? "Own log" : "Field " + f.Index + " of " + f.InLog + " in one log") + " · " + time + " · " + file,
                    };
                    if (f.Log.Badge == "Interrupted") row.Note = "This log has an interruption.";
                    row.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(MlcSubbeamRow.Include)) OnPropertyChanged(nameof(RebuildText)); };
                    group.Add(row);
                }
                foreach (var same in group.Fields.Where(r => r.Name.StartsWith("Field", StringComparison.Ordinal) == false)
                                                 .GroupBy(r => r.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                {
                    var times = same.Select(r => r.Log.Date.ToString("HH:mm", Inv)).ToList();
                    foreach (MlcSubbeamRow r in same)
                        r.Note = (r.Note.Length > 0 ? r.Note + " " : "") + "Delivered " + same.Count() + " times (" + string.Join(", ", times) +
                                 "), e.g. resumed after an interruption; each part gets its own file.";
                }
                Groups.Add(group);
            }
        }

        static string Patient(LogItem l) { return l.Header?.Meta("Patient ID") ?? l.Header?.Meta("PatientID") ?? ""; }

        static string Count(int n, string what) { return n + " " + what + (n == 1 ? "" : "s"); }

        [RelayCommand]
        void Back()
        {
            LogItem back = _main.CurrentLog != null && Items.Contains(_main.CurrentLog) ? _main.CurrentLog : Items.FirstOrDefault();
            if (back != null) _main.ShowLog(back);
        }

        [RelayCommand]
        void SelectAll(string on)
        {
            bool value = on != "false";
            foreach (MlcFieldGroup g in Groups) g.Include = value;
        }

        [RelayCommand]
        void BrowseFolder()
        {
            string p = Dialogs.PickFolder("Save the Eclipse MLC files to", string.IsNullOrEmpty(OtherFolder) ? Items.FirstOrDefault()?.Folder : OtherFolder);
            if (p != null) { OtherFolder = p; SaveNextToLog = false; }
        }

        string FolderFor(LogItem log) { return SaveNextToLog ? log.Folder : OtherFolder.Trim().Trim('"'); }

        [RelayCommand]
        void OpenFolder()
        {
            LogItem log = Selected?.Log ?? Fields.FirstOrDefault()?.Log ?? Items.FirstOrDefault();
            if (log != null) Dialogs.OpenFolder(FolderFor(log));
        }

        // =========================================================================================
        // Eclipse exports (Advanced)
        // =========================================================================================

        [RelayCommand]
        void AddExports()
        {
            string[] files = Dialogs.OpenFiles("Eclipse MLC exports of these beams", Dialogs.EclipseFilter, Items.FirstOrDefault()?.Folder);
            if (files.Length > 0) AddExportFiles(files);
        }

        /// <summary>Matches exports to the fields without one, across all the logs, by control points and gantry angle.</summary>
        public void AddExportFiles(IEnumerable<string> files)
        {
            var templates = files.Select(TemplateMatcher.Read).ToList();
            var free = Fields.Where(f => string.IsNullOrEmpty(f.Template)).ToList();
            // Match works on subbeam numbers; number the free fields 0..n-1 so fields of different logs don't clash.
            var keyed = free.Select((f, i) => new SubbeamInfo { Subbeam = i, ControlPoints = f.ControlPoints, GantryIec = f.GantryIec }).ToList();
            var match = TemplateMatcher.Match(keyed, templates);
            foreach (var m in match) SetTemplate(free[m.Key], m.Value);
            var unmatched = templates.Where(t => !match.Values.Contains(t)).ToList();
            if (unmatched.Count > 0)
                _main.ShowNotice("Not used (no field with the same number of control points): " +
                                 string.Join("; ", unmatched.Select(t => Path.GetFileName(t.Path) +
                                     (t.Error != null ? " - " + t.Error : " - " + t.ControlPoints + " control points"))), "caution");
            UseExports = true;
        }

        [RelayCommand]
        void ChooseExport(MlcSubbeamRow row)
        {
            if (row == null) return;
            string p = Dialogs.OpenFile("Eclipse MLC export for " + row.Name, Dialogs.EclipseFilter, row.Template ?? row.Log.Folder);
            if (p != null) SetTemplate(row, TemplateMatcher.Read(p));
        }

        [RelayCommand]
        void ClearExport(MlcSubbeamRow row)
        {
            if (row == null) return;
            row.Template = null;
            row.TemplateInfo = "";
        }

        static void SetTemplate(MlcSubbeamRow row, TemplateInfo t)
        {
            row.Template = t.Path;
            if (t.Error != null) row.TemplateInfo = "Can't read: " + t.Error;
            else
            {
                row.TemplateInfo = string.Format(Inv, "{0} control points · gantry {1:F1}°", t.ControlPoints, t.GantryIec);
                if (t.ControlPoints != row.ControlPoints) row.TemplateInfo += " - does not fit this field (" + row.ControlPoints + ")";
            }
        }

        // =========================================================================================
        // Rebuild
        // =========================================================================================

        /// <summary>Checks the choices; returns an error message, or null when OK to start.</summary>
        string Validate(List<MlcSubbeamRow> selected, out double gap)
        {
            gap = 0;
            if (selected.Count == 0) return "Tick at least one field.";
            if (UseExports && selected.Any(f => string.IsNullOrEmpty(f.Template)))
                return "With 'Use Eclipse exports' on, every ticked field needs an export. Add the missing ones, or turn the option off.";
            if (MinGap.Trim().Length > 0 && (!double.TryParse(MinGap, NumberStyles.Float, Inv, out gap) || gap < 0))
                return "Minimum leaf gap must be a number of mm, or empty for the MLC's own.";
            if (!SaveNextToLog)
            {
                if (OtherFolder.Trim().Trim('"').Length == 0) return "Choose a folder to save to.";
                // The files are named after the log, so two logs with the same file name would overwrite each other.
                var clash = selected.Select(f => f.Log).Distinct().GroupBy(l => Path.GetFileName(l.Path), StringComparer.OrdinalIgnoreCase)
                                    .FirstOrDefault(g => g.Count() > 1);
                if (clash != null)
                    return "Two of the logs are both called " + clash.Key + ", so their files would overwrite each other in one folder. Save next to each log instead.";
            }
            return null;
        }

        [RelayCommand]
        async Task Rebuild()
        {
            var selected = Fields.Where(f => f.Include).ToList();
            double gap;
            string error = Validate(selected, out gap);
            if (error != null) { _main.ShowNotice(error, "caution"); return; }

            IsBusy = true;
            Results.Clear();
            Selected = null;
            var all = new List<SubbeamResult>();
            var folders = new List<string>();
            try
            {
                // One run per log, its ticked fields in list order (the templates follow the same order).
                foreach (var perLog in selected.GroupBy(f => f.Log))
                {
                    LogItem log = perLog.Key;
                    List<MlcSubbeamRow> rows = perLog.ToList();
                    string folder = FolderFor(log);
                    var o = new MlcReconstructionOptions
                    {
                        Positions = UseActual ? "actual" : "expected",
                        Axis = AxisMu ? "mu" : "cp",
                        GantryMode = GantryFromPlan ? "plan" : "actual",
                        IncludeHold = IncludeHold,
                        MinGap = MinGap.Trim().Length > 0 ? gap : (double?)null,
                        Subbeams = string.Join(",", rows.Select(f => f.Subbeam)),
                        OutputFolder = folder,
                    };
                    if (UseExports) o.Templates.AddRange(rows.Select(f => f.Template));
                    DeliveryRecord record = log.Record;
                    string path = log.Path;
                    try
                    {
                        Directory.CreateDirectory(folder);
                        MlcReconstructionResult r = await Task.Run(() => MlcReconstructor.Run(record, path, o));
                        if (!folders.Contains(folder, StringComparer.OrdinalIgnoreCase)) folders.Add(folder);
                        foreach (SubbeamResult s in r.Subbeams)
                        {
                            MlcSubbeamRow f = rows.FirstOrDefault(x => x.Subbeam == s.Subbeam);
                            Results.Add(Row(s, log, f?.Name ?? "Field " + s.Subbeam, f?.Source ?? Path.GetFileName(path)));
                            all.Add(s);
                        }
                    }
                    catch (Exception ex)
                    {
                        // The whole log failed (e.g. a template that doesn't fit); its fields are shown as failed and the others carry on.
                        foreach (MlcSubbeamRow f in rows)
                        {
                            var s = new SubbeamResult { Subbeam = f.Subbeam, Ok = false, Error = ex.Message, Status = RebuildStatus.Failed };
                            Results.Add(Row(s, log, f.Name, f.Source));
                            all.Add(s);
                        }
                    }
                }
                Selected = Results.FirstOrDefault();
                int ready = all.Count(s => s.Status == RebuildStatus.Ready);
                string where = folders.Count == 1 ? folders[0] : folders.Count + " folders (next to each log)";
                _main.ShowNotice(ready == all.Count
                    ? (ready == 1 ? "The field is" : "All " + ready + " fields are") + " ready to import. Files saved to " + where
                    : ready + " of " + all.Count + " fields ready; see the results.", ready == all.Count ? "info" : "caution");
            }
            catch (Exception ex) { _main.ShowNotice("Rebuild failed: " + ex.Message, "critical"); }
            finally { IsBusy = false; }
        }

        static RebuildResultRow Row(SubbeamResult s, LogItem log, string name, string source)
        {
            var row = new RebuildResultRow { Result = s, Log = log, Name = name, Source = source, Error = s.Error };
            switch (s.Status)
            {
                case RebuildStatus.Ready: row.StatusText = "Ready to import"; row.StatusKind = "ok"; break;
                case RebuildStatus.CheckFirst: row.StatusText = "Check first"; row.StatusKind = "caution"; break;
                default: row.StatusText = "Failed"; row.StatusKind = "critical"; break;
            }
            if (!s.Ok) { row.Problems.Add(s.Error); return row; }
            row.FileName = Path.GetFileName(s.OutputPath);
            row.Delivered = "RMS " + s.DeliveredRmsMm.ToString("F3", Inv) + " mm";
            row.DeliveredDetail = "max " + s.DeliveredMaxMm.ToString("F2", Inv) + " mm at " + s.DeliveredMaxAt;
            row.GapRules = s.RemainingGapProblems == 0 ? "All pass" : s.RemainingGapProblems + " problem(s)";
            row.GapDetail = s.GapsClosed + " closed exactly, " + s.GapsOpened + " opened to the minimum";
            row.Gantry = s.GantryNote == "from the template" ? "From the export" : s.GantryNote.StartsWith("not evenly", StringComparison.Ordinal) ? "Rounded to 0.01°" : "Rebuilt exactly";
            row.GantryDetail = s.GantryNote;
            row.Problems.AddRange(s.Problems);
            return row;
        }

        [RelayCommand]
        void SelectResult(RebuildResultRow row) { if (row != null) Selected = row; }
    }
}
