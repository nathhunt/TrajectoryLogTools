// ExportViewModel.cs - Export the selected logs, wherever they are, to any of: v4.0 .bin, CSV,
// full dump CSV, raw dump text. The v4.0 header options (machine, serial number, couch axes)
// apply to every log; one log's patient, plan and field names can also be changed (Header details). Output goes to one folder, or next to each log in a "converted"
// subfolder. The work is done by TrajectoryLogTools.Core.Commands, the same code as the CLI.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrajectoryLogTools.App.Services;
using TrajectoryLogTools.Core;

namespace TrajectoryLogTools.App.ViewModels
{
    public partial class ExportRow : ObservableObject
    {
        public LogItem Log { get; set; }
        public string Name { get { return (Log.PatientId != null ? Log.PatientId + " · " : "") + Log.DisplayName + " · " + Log.DateText; } }
        public string Folder { get { return Log.FolderShort; } }
        [ObservableProperty] private string _status = "";
        /// <summary>"neutral", "ok", "caution" or "critical".</summary>
        [ObservableProperty] private string _statusKind = "neutral";
        [ObservableProperty] private string _detail = "";
    }

    public partial class ExportViewModel : ObservableObject
    {
        public const string ConvertedFolder = ExportOptions.ConvertedFolder;

        public ObservableCollection<ExportRow> Rows { get; } = new ObservableCollection<ExportRow>();

        [ObservableProperty] private bool _toV4 = true;
        // v4.0 header
        /// <summary>0 TrueBeam (0), 1 Halcyon (1).</summary>
        [ObservableProperty] private int _machineIndex;
        /// <summary>0 Remove, 1 Keep, 2 Change to.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ChangeSerial))]
        private int _serialIndex;
        [ObservableProperty] private string _newSerial = "";
        /// <summary>0 Remove, 1 Keep.</summary>
        [ObservableProperty] private int _couchIndex;
        /// <summary>Single log only: header details changed in the Header details window.</summary>
        [ObservableProperty] private HeaderEditViewModel _headerEdits;
        [ObservableProperty] private bool _timeCsv;
        [ObservableProperty] private bool _toCsv;
        [ObservableProperty] private bool _noMlc;
        [ObservableProperty] private bool _actualOnly;
        [ObservableProperty] private bool _fullCsv;
        [ObservableProperty] private bool _dump;
        [ObservableProperty] private bool _oneFolder = true;
        [ObservableProperty] private string _folder = "";
        [ObservableProperty] private bool _overwrite;
        /// <summary>Also save a run log (.log) with the files. Off by default.</summary>
        [ObservableProperty] private bool _saveRunLog;
        [ObservableProperty] private bool _ignoreCrc;
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private bool _isDone;
        [ObservableProperty] private double _progress;
        [ObservableProperty] private string _summary = "";

        public string Title { get { return Rows.Count == 1 ? "Export 1 log" : "Export " + Rows.Count + " logs"; } }
        public string V4Note { get; }
        public bool AnyCrcFailed { get; }
        public bool ShowProgress { get { return IsBusy || IsDone; } }
        public bool ChangeSerial { get { return SerialIndex == 2; } }
        public string[] MachineChoices { get; } = { "TrueBeam (0)", "Halcyon (1)" };
        public string[] SerialChoices { get; } = { "Remove", "Keep", "Change to…" };
        public string[] CouchChoices { get; } = { "Remove", "Keep" };
        /// <summary>Header details (patient, plan, field names) can be changed when exporting one v5.1 log.</summary>
        public bool CanEditHeader { get { return Rows.Count == 1 && Rows[0].Log.Header != null && Rows[0].Log.Header.Version == "5.1"; } }
        public string HeaderEditsText
        {
            get
            {
                var changes = HeaderEdits?.Changes() ?? new List<string>();
                return changes.Count == 0 ? "Patient, plan and field names as in the log." : "Changed: " + string.Join("; ", changes) + ".";
            }
        }

        CancellationTokenSource _cts;

        public ExportViewModel(List<LogItem> logs)
        {
            foreach (LogItem l in logs) Rows.Add(new ExportRow { Log = l });
            int v51 = logs.Count(l => l.Header != null && l.Header.Version == "5.1");
            int csv = logs.Count(l => l.IsCsv);
            V4Note = "For tools that read only HAL 2.0–4.0 logs. " +
                     (v51 == logs.Count ? (logs.Count == 1 ? "This log is v5.1." : "All are v5.1.")
                      : v51 + " of " + logs.Count + " are v5.1; the others are skipped (" + (csv > 0 ? "already v4.0 or CSV" : "already v4.0") + ").");
            AnyCrcFailed = logs.Any(l => l.CrcFailed);
            IgnoreCrc = false;
        }

        partial void OnHeaderEditsChanged(HeaderEditViewModel value) { OnPropertyChanged(nameof(HeaderEditsText)); }

        [RelayCommand]
        void EditHeader()
        {
            if (!CanEditHeader) return;
            var vm = HeaderEdits?.Copy() ?? new HeaderEditViewModel(Rows[0].Log.Header);
            if (Dialogs.ShowDialog(new Views.HeaderEditWindow { DataContext = vm }) == true) HeaderEdits = vm;
        }

        partial void OnIsBusyChanged(bool value) { OnPropertyChanged(nameof(ShowProgress)); }
        partial void OnIsDoneChanged(bool value) { OnPropertyChanged(nameof(ShowProgress)); }

        [RelayCommand]
        void Browse()
        {
            string p = Dialogs.PickFolder("Export to", Folder);
            if (p != null) { Folder = p; OneFolder = true; }
        }

        [RelayCommand]
        void OpenFolder()
        {
            if (OneFolder) Dialogs.OpenFolder(Folder.Trim().Trim('"'));
            else if (Rows.Count > 0) Dialogs.OpenFolder(Path.Combine(Rows[0].Log.Folder, ConvertedFolder));
        }

        [RelayCommand]
        void Cancel() { if (_cts != null) _cts.Cancel(); }

        /// <summary>Checks the choices; returns an error message, or null when OK to start.</summary>
        public string Validate()
        {
            string e0 = Jobs().Validate();
            if (e0 != null) return e0;
            if (ToV4 && HeaderEdits != null)
            {
                string e = HeaderEdits.Validate();
                if (e != null) return e;
            }
            if (OneFolder)
            {
                string f = Folder.Trim().Trim('"');
                if (f.Length == 0) return "Choose the folder to export to.";
                if (ToV4 && Rows.Any(r => LogFinder.PathEquals(r.Log.Folder, f)))
                    return "That is the folder of one of the logs, so its v4.0 file would replace the original. Choose another folder, or 'next to each log'.";
            }
            return null;
        }

        [RelayCommand]
        async Task Export()
        {
            string error = Validate();
            if (error != null) { Dialogs.Warn(error); return; }
            IsBusy = true;
            IsDone = false;
            _cts = new CancellationTokenSource();
            ExportOptions jobs = Jobs();
            string folder = OneFolder ? Folder.Trim().Trim('"') : null;
            int ok = 0, failed = 0, skipped = 0;
            DateTime started = DateTime.Now;
            var records = new List<ExportRecord>();
            foreach (ExportRow r in Rows) { r.Status = "Waiting"; r.StatusKind = "neutral"; r.Detail = ""; }
            for (int i = 0; i < Rows.Count; i++)
            {
                if (_cts.IsCancellationRequested) { Rows[i].Status = "Cancelled"; continue; }
                ExportRow row = Rows[i];
                row.Status = "Working…";
                string outDir = Exporter.OutputFolder(row.Log.Path, folder);
                var lines = new List<string>();
                string path = row.Log.Path;
                ExportOutcome outcome = await Task.Run(() => Exporter.Run(path, outDir, jobs, lines));
                records.Add(new ExportRecord { Log = path, OutputFolder = outDir, Lines = lines });
                row.Detail = string.Join("  ", lines.Where(l => l.StartsWith("OK") || l.StartsWith("SKIP") || l.StartsWith("FAIL"))
                                                    .Select(Short));
                switch (outcome)
                {
                    case ExportOutcome.Done: row.Status = "Done"; row.StatusKind = "ok"; ok++; break;
                    case ExportOutcome.Skipped: row.Status = "Skipped"; row.StatusKind = "neutral"; skipped++; break;
                    default: row.Status = "Failed"; row.StatusKind = "critical"; failed++; break;
                }
                Progress = 100.0 * (i + 1) / Rows.Count;
            }
            Summary = ok + " done, " + skipped + " skipped, " + failed + " failed" + (_cts.IsCancellationRequested ? " (cancelled)" : "") + ".";
            if (SaveRunLog && records.Count > 0)
            {
                var errors = new List<string>();
                List<string> logs = Exporter.WriteRunLogs(records, started, errors);
                if (logs.Count > 0) Summary += logs.Count == 1 ? " A run log was saved with the files." : " A run log was saved in each of the " + logs.Count + " folders.";
                if (errors.Count > 0) Summary += " (Run log not saved: " + string.Join("; ", errors) + ")";
            }
            IsBusy = false;
            IsDone = true;
            _cts.Dispose();
            _cts = null;
        }

        static string Short(string line)
        {
            // "OK   a.bin -> C:\x\a.csv" -> "a.csv"; "SKIP a.bin: already version 4.0" -> "SKIP already version 4.0"
            int arrow = line.IndexOf("->", StringComparison.Ordinal);
            if (line.StartsWith("OK", StringComparison.Ordinal) && arrow > 0) return Path.GetFileName(line.Substring(arrow + 2).Trim());
            int colon = line.IndexOf(": ", StringComparison.Ordinal);
            if ((line.StartsWith("SKIP", StringComparison.Ordinal) || line.StartsWith("FAIL", StringComparison.Ordinal)) && colon > 0)
                return line.Substring(0, 4) + " " + line.Substring(colon + 2);
            return line.Trim();
        }

        /// <summary>The choices made, for the exporter shared with the CLI.</summary>
        ExportOptions Jobs()
        {
            return new ExportOptions
            {
                ToV4 = ToV4, ToCsv = ToCsv, FullCsv = FullCsv, Dump = Dump, TimeCsv = TimeCsv,
                NoMlc = NoMlc, ActualOnly = ActualOnly, Overwrite = Overwrite, IgnoreCrc = IgnoreCrc,
                Machine = MachineIndex == 1 ? 1 : 0, KeepSerial = SerialIndex == 1, NewSerial = SerialIndex == 2 ? NewSerial : null,
                KeepCouch = CouchIndex == 1,
                MetaEdits = HeaderEdits?.MetaEdits() ?? new List<KeyValuePair<string, string>>(),
                SubbeamNames = HeaderEdits?.SubbeamNames() ?? new Dictionary<int, string>(),
            };
        }
    }
}
