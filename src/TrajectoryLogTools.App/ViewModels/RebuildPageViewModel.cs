// RebuildPageViewModel.cs - Rebuild Eclipse MLC: the fields of one log are found automatically,
// you choose where to save, and each field gets an Eclipse MLC file built from the log, with a
// status: Ready to import, Check first (with the reasons) or Failed.
//
// Eclipse exports (templates) are an Advanced option, off by default: with one, the file keeps
// the export's header, meterset weights, jaws and angles and only the leaves are replaced.

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
using TrajectoryLogTools.Core;

namespace TrajectoryLogTools.App.ViewModels
{
    /// <summary>A rebuilt field, as shown on its result card.</summary>
    public sealed class RebuildResultRow
    {
        public SubbeamResult Result { get; set; }
        public string Name { get; set; }
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
        public LogItem Item { get; }

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

        public ObservableCollection<MlcSubbeamRow> Fields { get; } = new ObservableCollection<MlcSubbeamRow>();
        public ObservableCollection<RebuildResultRow> Results { get; } = new ObservableCollection<RebuildResultRow>();

        public string Title { get { return Item.DisplayName; } }
        public string Detail { get { return Item.DateText + " · " + Item.Path; } }
        public string NextToLogText { get { return "Next to the log (" + Item.FolderShort + ")"; } }
        public string AdvancedSummary
        {
            get
            {
                var parts = new List<string>
                {
                    UseActual ? "actual positions" : "expected positions",
                    AxisMu ? "delivered-MU axis" : "control-point axis",
                    "gap " + (string.IsNullOrWhiteSpace(MinGap) ? (Item.Record?.Mlc?.MinDynamicGapMm ?? 0.5).ToString("0.0##", Inv) : MinGap.Trim()) + " mm",
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

        List<SubbeamInfo> _infos = new List<SubbeamInfo>();

        public RebuildPageViewModel(LogItem item, MainViewModel main)
        {
            Item = item;
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

        async void Load()
        {
            IsBusy = true;
            try
            {
                await Item.LoadAsync();
                if (Item.LoadError != null) { Error = "This log can't be read: " + Item.LoadError; return; }
                DeliveryRecord d = Item.Record;
                MlcGeometry g = d.Mlc;
                if (g == null) { Error = MlcGeometry.UnsupportedMessage(d.LeafCount); return; }
                MlcText = g.Description;
                _infos = await Task.Run(() => TemplateMatcher.Describe(d));
                List<Subbeam> subbeams = d.Log != null ? d.Log.Subbeams : new List<Subbeam>();
                foreach (SubbeamInfo i in _infos)
                {
                    string name = i.Subbeam >= 1 && i.Subbeam <= subbeams.Count && !string.IsNullOrWhiteSpace(subbeams[i.Subbeam - 1].Name)
                        ? subbeams[i.Subbeam - 1].Name : (i.Subbeam == 0 ? "Field" : "Field " + i.Subbeam);
                    var row = new MlcSubbeamRow
                    {
                        Subbeam = i.Subbeam,
                        Name = name,
                        ControlPoints = i.ControlPoints,
                        Info = string.Format(Inv, "{0} control points · gantry {1:F1}° · {2:F1} MU", i.ControlPoints, i.GantryIec, i.Mu),
                    };
                    row.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(MlcSubbeamRow.Include)) OnPropertyChanged(nameof(RebuildText)); };
                    Fields.Add(row);
                }
                OnPropertyChanged(nameof(RebuildText));
                OnPropertyChanged(nameof(AdvancedSummary));
            }
            catch (Exception ex) { Error = ex.Message; }
            finally { IsBusy = false; }
        }

        [RelayCommand]
        void Back() { _main.ShowLog(Item); }

        [RelayCommand]
        void BrowseFolder()
        {
            string p = Dialogs.PickFolder("Save the Eclipse MLC files to", string.IsNullOrEmpty(OtherFolder) ? Item.Folder : OtherFolder);
            if (p != null) { OtherFolder = p; SaveNextToLog = false; }
        }

        string OutputFolder { get { return SaveNextToLog ? Item.Folder : OtherFolder.Trim().Trim('"'); } }

        [RelayCommand]
        void OpenFolder() { Dialogs.OpenFolder(OutputFolder); }

        // ---- Eclipse exports (Advanced) ----

        [RelayCommand]
        void AddExports()
        {
            string[] files = Dialogs.OpenFiles("Eclipse MLC exports of these beams", Dialogs.EclipseFilter, Item.Folder);
            if (files.Length > 0) AddExportFiles(files);
        }

        public void AddExportFiles(IEnumerable<string> files)
        {
            var templates = files.Select(TemplateMatcher.Read).ToList();
            var free = _infos.Where(i => Fields.Any(f => f.Subbeam == i.Subbeam && string.IsNullOrEmpty(f.Template))).ToList();
            var match = TemplateMatcher.Match(free, templates);
            foreach (var m in match) SetTemplate(Fields.First(f => f.Subbeam == m.Key), m.Value);
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
            string p = Dialogs.OpenFile("Eclipse MLC export for " + row.Name, Dialogs.EclipseFilter, row.Template ?? Item.Folder);
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

        // ---- Rebuild ----

        [RelayCommand]
        async Task Rebuild()
        {
            if (Item.Record == null || Fields.Count == 0) return;
            var selected = Fields.Where(f => f.Include).ToList();
            if (selected.Count == 0) { _main.ShowNotice("Tick at least one field.", "caution"); return; }
            if (UseExports && selected.Any(f => string.IsNullOrEmpty(f.Template)))
            {
                _main.ShowNotice("With 'Use Eclipse exports' on, every ticked field needs an export. Add the missing ones, or turn the option off.", "caution");
                return;
            }
            double gap = 0;
            if (MinGap.Trim().Length > 0 && (!double.TryParse(MinGap, NumberStyles.Float, Inv, out gap) || gap < 0))
            {
                _main.ShowNotice("Minimum leaf gap must be a number of mm, or empty for the MLC's own.", "caution");
                return;
            }
            string folder = OutputFolder;
            if (string.IsNullOrWhiteSpace(folder)) { _main.ShowNotice("Choose a folder to save to.", "caution"); return; }

            var o = new MlcReconstructionOptions
            {
                Positions = UseActual ? "actual" : "expected",
                Axis = AxisMu ? "mu" : "cp",
                GantryMode = GantryFromPlan ? "plan" : "actual",
                IncludeHold = IncludeHold,
                MinGap = MinGap.Trim().Length > 0 ? gap : (double?)null,
                Subbeams = string.Join(",", selected.Select(f => f.Subbeam)),
                OutputFolder = folder,
            };
            if (UseExports) o.Templates.AddRange(selected.Select(f => f.Template));

            IsBusy = true;
            Results.Clear();
            try
            {
                Directory.CreateDirectory(folder);
                DeliveryRecord record = Item.Record;
                string path = Item.Path;
                MlcReconstructionResult r = await Task.Run(() => MlcReconstructor.Run(record, path, o));
                foreach (SubbeamResult s in r.Subbeams) Results.Add(Row(s, Fields.FirstOrDefault(f => f.Subbeam == s.Subbeam)?.Name ?? "Field " + s.Subbeam));
                Selected = Results.FirstOrDefault();
                int ready = r.Subbeams.Count(s => s.Status == RebuildStatus.Ready);
                _main.ShowNotice(ready == r.Subbeams.Count
                    ? (ready == 1 ? "The field is" : "All " + ready + " fields are") + " ready to import. Files saved to " + folder
                    : ready + " of " + r.Subbeams.Count + " fields ready; see the results.", ready == r.Subbeams.Count ? "info" : "caution");
            }
            catch (Exception ex) { _main.ShowNotice("Rebuild failed: " + ex.Message, "critical"); }
            finally { IsBusy = false; }
        }

        static RebuildResultRow Row(SubbeamResult s, string name)
        {
            var row = new RebuildResultRow { Result = s, Name = name, Error = s.Error };
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
