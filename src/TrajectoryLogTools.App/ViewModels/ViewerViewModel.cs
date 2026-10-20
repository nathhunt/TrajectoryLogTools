// ViewerViewModel.cs - Log viewer: one log (.bin, or a CSV export) - header, subbeams, per-beam
// delivery summary, beam holds, axis and MLC charts, and the raw dump.

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
    public partial class ViewerViewModel : ObservableObject
    {
        const int MaxDumpChars = 4000000;

        [ObservableProperty] private string _path = "";
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private string _headerText = "Open a trajectory log (.bin) or a CSV export of one.";
        [ObservableProperty] private string _totalsText = "";
        [ObservableProperty] private string _rawDump = "";
        [ObservableProperty] private bool _ignoreCrc;
        [ObservableProperty] private bool _vsControlPoint;
        [ObservableProperty] private KeyValuePair<string, string> _selectedAxis;

        public ObservableCollection<SubbeamRow> Subbeams { get; } = new ObservableCollection<SubbeamRow>();
        public ObservableCollection<BeamRow> Beams { get; } = new ObservableCollection<BeamRow>();
        public ObservableCollection<HoldRow> Holds { get; } = new ObservableCollection<HoldRow>();
        public ObservableCollection<KeyValuePair<string, string>> Axes { get; } = new ObservableCollection<KeyValuePair<string, string>>();

        /// <summary>The loaded log, or null.</summary>
        public DeliveryRecord Record { get; private set; }
        public bool HasLog { get { return Record != null; } }
        public bool IsBin { get { return Record != null && Record.Log != null; } }

        /// <summary>Raised when the charts need redrawing (the view owns the plot controls).</summary>
        public event EventHandler ChartsChanged;

        public Action<string> OpenInMlc, OpenInCompare;

        partial void OnVsControlPointChanged(bool value) { RaiseCharts(); }
        partial void OnSelectedAxisChanged(KeyValuePair<string, string> value) { RaiseCharts(); }

        void RaiseCharts() { var h = ChartsChanged; if (h != null) h(this, EventArgs.Empty); }

        [RelayCommand]
        void Browse()
        {
            string p = Dialogs.OpenFile("Open a trajectory log", Dialogs.LogFilter, Path);
            if (p != null) Open(p);
        }

        [RelayCommand]
        void Reload() { if (File.Exists(Path)) Open(Path); }

        public async void Open(string path)
        {
            Path = path;
            IsBusy = true;
            try
            {
                bool ignoreCrc = IgnoreCrc;
                var loaded = await Task.Run(() =>
                {
                    DeliveryRecord r = DeliveryRecord.Load(path, ignoreCrc);
                    LogSummary s = r.Has("MU_Actual") && r.Has("ControlPoint") ? DeliveryComparison.SummariseLog(r) : null;
                    return Tuple.Create(r, s);
                });
                Show(loaded.Item1, loaded.Item2);
            }
            catch (Exception ex)
            {
                Record = null;
                Clear();
                HeaderText = "Could not read " + System.IO.Path.GetFileName(path) + ":\n" + ex.Message +
                             (ex.Message.Contains("CRC") ? "\n\nTick 'Ignore CRC' to open it anyway, or look at it with the raw dump." : "");
            }
            finally
            {
                IsBusy = false;
                OnPropertyChanged(nameof(HasLog));
                OnPropertyChanged(nameof(IsBin));
                RaiseCharts();
            }
        }

        void Clear()
        {
            Subbeams.Clear(); Beams.Clear(); Holds.Clear(); Axes.Clear();
            TotalsText = ""; RawDump = "";
        }

        void Show(DeliveryRecord r, LogSummary s)
        {
            Record = r;
            Clear();
            HeaderText = Describe(r);
            if (r.Log != null)
                for (int i = 0; i < r.Log.Subbeams.Count; i++) Subbeams.Add(SubbeamRow.From(i, r.Log.Subbeams[i]));
            if (s != null)
            {
                foreach (var b in s.Beams) Beams.Add(BeamRow.From(b));
                foreach (var h in s.Holds) Holds.Add(HoldRow.From(h));
                var sb = new StringBuilder();
                sb.AppendFormat(CultureInfo.InvariantCulture, "MU planned {0:F3}, delivered {1:F3} ({2:+0.000;-0.000;0.000}), total time {3:F2} s",
                                s.MuPlanned, s.MuDelivered, s.MuDelivered - s.MuPlanned, s.TotalTimeS);
                sb.AppendFormat(CultureInfo.InvariantCulture, ".   Largest leaf step per snapshot: expected {0:F3} mm, actual {1:F3} mm",
                                s.MaxLeafStepExpected, s.MaxLeafStepActual);
                foreach (var c in s.Couch)
                    sb.AppendFormat(CultureInfo.InvariantCulture, "\n{0}: expected {1:F3} cm, mean actual {2:F4} cm ({3:+0.000;-0.000;0.000} mm)",
                                    c.Item1, c.Item2, c.Item3, c.Item4 * 10);
                TotalsText = sb.ToString();
            }
            foreach (var a in LogCharts.PlottableAxes(r)) Axes.Add(a);
            SelectedAxis = Axes.FirstOrDefault(a => a.Key == "GantryRtn_deg");
            if (SelectedAxis.Key == null && Axes.Count > 0) SelectedAxis = Axes[0];
        }

        static string Describe(DeliveryRecord r)
        {
            var sb = new StringBuilder();
            Action<string, object> line = (k, v) => sb.Append(k.PadRight(22)).Append(v).Append('\n');
            line("File", r.SourcePath);
            TrajectoryLog log = r.Log;
            if (log == null)
            {
                line("Type", "CSV export (header details are only in the .bin log)");
                line("Snapshots", r.RowCount);
            }
            else
            {
                line("Version", log.Version);
                if (log.HasMachineInfo || log.MachineSerial.Length > 0)
                    line("Machine", "specifier " + log.MachineSpecifier + ", serial '" + log.MachineSerial + "'");
                line("Sampling interval", log.SamplingIntervalMs + " ms");
                line("Snapshots", log.NumSnapshots + (log.Truncated != 0 ? "  (TRUNCATED)" : ""));
                line("Axes", string.Join(", ", log.AxisIds.Select(TrajectoryLog.AxisName)));
                line("Axis scale", log.AxisScale + " - " + TrajectoryLog.AxisScaleName(log.AxisScale));
                MlcGeometry g = MlcGeometry.FromLog(log);
                int mlc = Array.IndexOf(log.AxisIds, TrajectoryLog.MlcAxisId);
                line("MLC", g != null ? g.Description : mlc >= 0 ? log.SamplesPerAxis[mlc] + " samples (not a supported MLC)" : "none");
                line("MLC model field", log.MlcModel);
                line("CRC", log.CrcWarning ?? "verifies (" + log.CrcVariant.Name + ")");
            }
            double[] t = r.TryColumn("ElapsedTime_s");
            if (t != null && t.Length > 0) line("Duration", t[t.Length - 1].ToString("F2", CultureInfo.InvariantCulture) + " s (time of the last snapshot)");
            if (log != null && log.MetaDataText.Trim().Length > 0)
            {
                sb.Append("Metadata\n");
                foreach (string m in log.MetaDataText.Replace("\r\n", "\n").Split('\n').Where(x => x.Trim().Length > 0))
                    sb.Append("    ").Append(m.Replace("\t", "  ")).Append('\n');
            }
            return sb.ToString().TrimEnd();
        }

        [RelayCommand]
        async Task ShowRawDump()
        {
            if (!File.Exists(Path)) return;
            IsBusy = true;
            try
            {
                string path = Path;
                RawDump = await Task.Run(() =>
                {
                    var warnings = new List<string>();
                    string text = Core.RawDump.ToText(File.ReadAllBytes(path), path, warnings);
                    string head = warnings.Count > 0 ? "WARNINGS:\n  " + string.Join("\n  ", warnings) + "\n\n" : "";
                    if (text.Length > MaxDumpChars)
                        text = text.Substring(0, MaxDumpChars) + "\r\n\r\n... (shown up to " + MaxDumpChars / 1000000 +
                               " million characters; use Export > Raw dump for the whole file)";
                    return head + text;
                });
            }
            catch (Exception ex) { RawDump = "Could not dump the file: " + ex.Message; }
            finally { IsBusy = false; }
        }

        [RelayCommand] void ExportCsv() { Export(Command.ToCsv, false); }
        [RelayCommand] void ExportFullCsv() { Export(Command.ToCsv, true); }
        [RelayCommand] void ExportDump() { Export(Command.Dump, false); }

        void Export(Command command, bool full)
        {
            if (!File.Exists(Path) || !Path.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            {
                Dialogs.Warn("Open a .bin log first.");
                return;
            }
            string folder = Dialogs.PickFolder("Export to folder", Path);
            if (folder == null) return;
            var opt = new Options { Command = command, Output = folder, Full = full, Overwrite = true, IgnoreCrc = IgnoreCrc };
            try
            {
                List<string> notes = command == Command.Dump ? Commands.RunDump(Path, opt) : Commands.RunToCsv(Path, opt);
                Dialogs.Info(string.Join("\n", notes));
            }
            catch (Exception ex) { Dialogs.Error(ex.Message); }
        }

        [RelayCommand]
        void SendToMlc() { if (HasLog && OpenInMlc != null) OpenInMlc(Path); }

        [RelayCommand]
        void SendToCompare() { if (HasLog && OpenInCompare != null) OpenInCompare(Path); }
    }
}
