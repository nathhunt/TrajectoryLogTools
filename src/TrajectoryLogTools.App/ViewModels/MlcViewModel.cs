// MlcViewModel.cs - Eclipse MLC: rebuild the Eclipse MLC file (MLCPlan serialization) of each
// field of a log, ready to import onto the beam in Eclipse (the LogToEclipseMlc method).
// Templates (Eclipse exports of the same beams) are matched to the subbeams automatically by
// control-point count and gantry angle; each can be changed by hand.

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

namespace TrajectoryLogTools.App.ViewModels
{
    public partial class MlcViewModel : ObservableObject
    {
        [ObservableProperty] private string _logPath = "";
        [ObservableProperty] private string _logInfo = "Choose a delivery log (.bin, or a CSV export of one).";
        [ObservableProperty] private string _outputFolder = "";
        [ObservableProperty] private bool _useActualPositions = true;
        [ObservableProperty] private bool _axisMu = true;
        [ObservableProperty] private bool _gantryFromPlan = true;
        [ObservableProperty] private string _scale = "auto";
        [ObservableProperty] private bool _includeHold;
        [ObservableProperty] private string _minGap = "";
        [ObservableProperty] private string _mlcModel = "";
        [ObservableProperty] private bool _ignoreCrc;
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _runLog = "";
        [ObservableProperty] private SubbeamResult _selectedResult;
        [ObservableProperty] private int _selectedValue = 1;   // Eclipse value index of the leaf chart
        [ObservableProperty] private int _maxValue = 113;

        public ObservableCollection<MlcSubbeamRow> Subbeams { get; } = new ObservableCollection<MlcSubbeamRow>();
        public ObservableCollection<SubbeamResult> Results { get; } = new ObservableCollection<SubbeamResult>();
        public string[] Scales { get; } = { "auto", "varian", "iec" };

        public string SelectedValueName
        {
            get { return "Pair " + (SelectedValue / 2 + 1) + (SelectedValue % 2 == 0 ? " X1" : " X2"); }
        }

        public string SelectedReport { get { return SelectedResult == null ? "" : SelectedResult.Report ?? SelectedResult.Summary; } }

        DeliveryRecord _record;
        List<SubbeamInfo> _infos = new List<SubbeamInfo>();
        bool _outputChosen;   // the user picked the output folder; otherwise it follows the log

        public event EventHandler ChartsChanged;

        void RaiseCharts() { var h = ChartsChanged; if (h != null) h(this, EventArgs.Empty); }

        partial void OnSelectedResultChanged(SubbeamResult value)
        {
            OnPropertyChanged(nameof(SelectedReport));
            if (value != null && value.Reconstructed != null) MaxValue = value.Reconstructed.GetLength(1) - 1;
            RaiseCharts();
        }

        partial void OnSelectedValueChanged(int value) { OnPropertyChanged(nameof(SelectedValueName)); RaiseCharts(); }

        [RelayCommand]
        void BrowseLog()
        {
            string p = Dialogs.OpenFile("Delivery log", Dialogs.LogFilter, LogPath);
            if (p != null) LoadLog(p);
        }

        [RelayCommand]
        void BrowseOutput()
        {
            string p = Dialogs.PickFolder("Write the MLC files to", string.IsNullOrEmpty(OutputFolder) ? LogPath : OutputFolder);
            if (p != null) { OutputFolder = p; _outputChosen = true; }
        }

        [RelayCommand]
        void OpenOutput() { Dialogs.OpenFolder(OutputFolder); }

        public async void LoadLog(string path)
        {
            LogPath = path;
            IsBusy = true;
            Subbeams.Clear();
            Results.Clear();
            _record = null;
            try
            {
                bool ignoreCrc = IgnoreCrc;
                string scale = Scale;
                var loaded = await Task.Run(() =>
                {
                    DeliveryRecord r = DeliveryRecord.Load(path, ignoreCrc);
                    return Tuple.Create(r, TemplateMatcher.Describe(r, scale));
                });
                _record = loaded.Item1;
                _infos = loaded.Item2;
                if (!_outputChosen || string.IsNullOrEmpty(OutputFolder)) OutputFolder = Path.GetDirectoryName(Path.GetFullPath(path));
                foreach (SubbeamInfo i in _infos)
                    Subbeams.Add(new MlcSubbeamRow
                    {
                        Subbeam = i.Subbeam,
                        ControlPoints = i.ControlPoints,
                        Info = string.Format(CultureInfo.InvariantCulture, "{0} CPs (log CP {1}-{2}), {3:F1} MU, gantry {4:F1}° IEC",
                                             i.ControlPoints, i.FirstLogControlPoint, i.FirstLogControlPoint + i.ControlPoints - 1, i.Mu, i.GantryIec),
                    });
                var mlc = _record.Mlc;
                LogInfo = Path.GetFileName(path) + ": " + _infos.Count + " field(s); MLC: " +
                          (mlc != null ? mlc.Description : MlcGeometry_Unsupported(_record.LeafCount));
            }
            catch (Exception ex) { LogInfo = "Could not read the log: " + ex.Message; }
            finally { IsBusy = false; RaiseCharts(); }
        }

        static string MlcGeometry_Unsupported(int leaves) { return Core.MlcGeometry.UnsupportedMessage(leaves); }

        [RelayCommand]
        void AddTemplates()
        {
            if (Subbeams.Count == 0) { Dialogs.Warn("Choose the delivery log first."); return; }
            string[] files = Dialogs.OpenFiles("Eclipse MLC exports of the delivered beams", Dialogs.EclipseFilter, LogPath);
            if (files.Length == 0) return;
            var templates = files.Select(TemplateMatcher.Read).ToList();
            var free = _infos.Where(i => Subbeams.Any(s => s.Subbeam == i.Subbeam && string.IsNullOrEmpty(s.Template))).ToList();
            var match = TemplateMatcher.Match(free, templates);
            foreach (var m in match) SetTemplate(Subbeams.First(s => s.Subbeam == m.Key), m.Value);
            var unmatched = templates.Where(t => !match.Values.Contains(t)).ToList();
            if (unmatched.Count > 0)
                Dialogs.Warn("These files did not match a field (number of control points) and were not used:\n\n" +
                             string.Join("\n", unmatched.Select(t => Path.GetFileName(t.Path) +
                                 (t.Error != null ? ": " + t.Error : ": " + t.ControlPoints + " control points"))));
        }

        [RelayCommand]
        void BrowseTemplate(MlcSubbeamRow row)
        {
            if (row == null) return;
            string p = Dialogs.OpenFile("Eclipse MLC export for subbeam " + row.Subbeam, Dialogs.EclipseFilter, row.Template ?? LogPath);
            if (p != null) SetTemplate(row, TemplateMatcher.Read(p));
        }

        [RelayCommand]
        void ClearTemplate(MlcSubbeamRow row)
        {
            if (row == null) return;
            row.Template = null;
            row.TemplateInfo = "";
        }

        static void SetTemplate(MlcSubbeamRow row, TemplateInfo t)
        {
            row.Template = t.Path;
            if (t.Error != null) row.TemplateInfo = "Not readable: " + t.Error;
            else
            {
                row.TemplateInfo = string.Format(CultureInfo.InvariantCulture, "{0} CPs, gantry {1:F1}°, {2}", t.ControlPoints, t.GantryIec, t.MlcModel);
                if (t.ControlPoints != row.ControlPoints) row.TemplateInfo += "  - DOES NOT MATCH this field's " + row.ControlPoints + " CPs";
            }
        }

        [RelayCommand]
        async Task Run()
        {
            if (_record == null) { Dialogs.Warn("Choose a delivery log first."); return; }
            var selected = Subbeams.Where(s => s.Include).ToList();
            if (selected.Count == 0) { Dialogs.Warn("Tick at least one field."); return; }
            int withTemplate = selected.Count(s => !string.IsNullOrEmpty(s.Template));
            if (withTemplate > 0 && withTemplate < selected.Count)
            {
                Dialogs.Warn("Give a template for every ticked field, or for none.");
                return;
            }
            double gap = 0;
            if (MinGap.Trim().Length > 0 && !double.TryParse(MinGap, NumberStyles.Float, CultureInfo.InvariantCulture, out gap))
            {
                Dialogs.Warn("Minimum leaf gap must be a number (mm), or empty for the MLC's default.");
                return;
            }
            var o = new MlcReconstructionOptions
            {
                Positions = UseActualPositions ? "actual" : "expected",
                Axis = AxisMu ? "mu" : "cp",
                GantryMode = GantryFromPlan ? "plan" : "actual",
                Scale = Scale,
                IncludeHold = IncludeHold,
                MinGap = MinGap.Trim().Length > 0 ? gap : (double?)null,
                MlcModel = MlcModel.Trim().Length > 0 ? MlcModel.Trim() : null,
                Subbeams = string.Join(",", selected.Select(s => s.Subbeam)),
                OutputFolder = string.IsNullOrWhiteSpace(OutputFolder) ? null : OutputFolder.Trim(),
            };
            if (withTemplate > 0) o.Templates.AddRange(selected.Select(s => s.Template));
            if (o.OutputFolder != null) Directory.CreateDirectory(o.OutputFolder);

            IsBusy = true;
            Results.Clear();
            var log = new StringBuilder();
            try
            {
                DeliveryRecord record = _record;
                string path = LogPath;
                MlcReconstructionResult r = await Task.Run(() => MlcReconstructor.Run(record, path, o, s => { lock (log) log.AppendLine(s); }));
                foreach (var s in r.Subbeams) Results.Add(s);
                SelectedResult = Results.FirstOrDefault(s => s.Ok) ?? Results.FirstOrDefault();
                if (r.Failed > 0) Dialogs.Warn(r.Failed + " field(s) failed:\n\n" + string.Join("\n", r.Subbeams.Where(s => !s.Ok).Select(s => s.Summary)));
            }
            catch (Exception ex) { log.AppendLine("ERROR: " + ex.Message); Dialogs.Error(ex.Message); }
            finally
            {
                RunLog = log.ToString();
                IsBusy = false;
            }
        }
    }
}
