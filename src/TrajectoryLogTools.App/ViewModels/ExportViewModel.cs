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
        public const string ConvertedFolder = "converted";

        public ObservableCollection<ExportRow> Rows { get; } = new ObservableCollection<ExportRow>();

        [ObservableProperty] private bool _toV4 = true;
        // v4.0 header
        [ObservableProperty] private bool _machineHalcyon;
        [ObservableProperty] private bool _removeSerial = true;
        [ObservableProperty] private bool _keepSerial;
        [ObservableProperty] private bool _changeSerial;
        [ObservableProperty] private string _newSerial = "";
        [ObservableProperty] private bool _removeCouch = true;
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
        [ObservableProperty] private bool _ignoreCrc;
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private bool _isDone;
        [ObservableProperty] private double _progress;
        [ObservableProperty] private string _summary = "";

        public string Title { get { return Rows.Count == 1 ? "Export 1 log" : "Export " + Rows.Count + " logs"; } }
        public string V4Note { get; }
        public bool AnyCrcFailed { get; }
        public bool ShowProgress { get { return IsBusy || IsDone; } }
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
            if (!ToV4 && !ToCsv && !FullCsv && !Dump) return "Choose at least one thing to write.";
            if (ToV4 && ChangeSerial && (NewSerial.Length > 6 || NewSerial.Any(c => c < 32 || c > 126)))
                return "The serial number must be up to 6 letters, digits or other plain (ASCII) characters.";
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
            var jobs = new Jobs { ToV4 = ToV4, ToCsv = ToCsv, FullCsv = FullCsv, Dump = Dump, TimeCsv = TimeCsv,
                                  NoMlc = NoMlc, ActualOnly = ActualOnly, Overwrite = Overwrite, IgnoreCrc = IgnoreCrc,
                                  Machine = MachineHalcyon ? 1 : 0, KeepSerial = KeepSerial, Serial = ChangeSerial ? NewSerial : null,
                                  DropCouch = RemoveCouch,
                                  MetaEdits = HeaderEdits?.MetaEdits() ?? new List<KeyValuePair<string, string>>(),
                                  SubbeamNames = HeaderEdits?.SubbeamNames() ?? new Dictionary<int, string>() };
            string folder = OneFolder ? Folder.Trim().Trim('"') : null;
            int ok = 0, failed = 0, skipped = 0;
            var runLog = new StringBuilder();
            runLog.AppendLine(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss", CultureInfo.InvariantCulture) + "  Export of " + Rows.Count + " log(s)");
            foreach (ExportRow r in Rows) { r.Status = "Waiting"; r.StatusKind = "neutral"; r.Detail = ""; }
            for (int i = 0; i < Rows.Count; i++)
            {
                if (_cts.IsCancellationRequested) { Rows[i].Status = "Cancelled"; continue; }
                ExportRow row = Rows[i];
                row.Status = "Working…";
                string outDir = folder ?? Path.Combine(row.Log.Folder, ConvertedFolder);
                var lines = await Task.Run(() => Work(row.Log.Path, outDir, jobs));
                runLog.AppendLine(row.Log.Path);
                foreach (string l in lines.Item2) runLog.AppendLine("  " + l);
                row.Detail = string.Join("  ", lines.Item2.Where(l => l.StartsWith("OK") || l.StartsWith("SKIP") || l.StartsWith("FAIL"))
                                                        .Select(Short));
                switch (lines.Item1)
                {
                    case 0: row.Status = "Done"; row.StatusKind = "ok"; ok++; break;
                    case 1: row.Status = "Skipped"; row.StatusKind = "neutral"; skipped++; break;
                    default: row.Status = "Failed"; row.StatusKind = "critical"; failed++; break;
                }
                Progress = 100.0 * (i + 1) / Rows.Count;
            }
            Summary = ok + " done, " + skipped + " skipped, " + failed + " failed" + (_cts.IsCancellationRequested ? " (cancelled)" : "") + ".";
            if (folder != null)
            {
                try
                {
                    File.WriteAllText(Path.Combine(folder, "TrajectoryLogConversion_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".log"),
                                      runLog.ToString(), Encoding.UTF8);
                    Summary += " A run log was saved with the files.";
                }
                catch (Exception ex) { Summary += " (Run log not saved: " + ex.Message + ")"; }
            }
            IsBusy = false;
            IsDone = true;
            _cts.Dispose();
            _cts = null;
        }

        static string Short(string line)
        {
            // "OK   a.bin -> C:\x\a.csv" -> "a.csv"
            int arrow = line.IndexOf("->", StringComparison.Ordinal);
            if (line.StartsWith("OK", StringComparison.Ordinal) && arrow > 0) return Path.GetFileName(line.Substring(arrow + 2).Trim());
            return line.Trim();
        }

        sealed class Jobs
        {
            public bool ToV4, ToCsv, FullCsv, Dump, TimeCsv, NoMlc, ActualOnly, Overwrite, IgnoreCrc, KeepSerial, DropCouch;
            public int Machine;
            public string Serial;
            public List<KeyValuePair<string, string>> MetaEdits;
            public Dictionary<int, string> SubbeamNames;
        }

        /// <summary>(0 = all OK, 1 = nothing to do, 2 = something failed, lines).</summary>
        static Tuple<int, List<string>> Work(string path, string outDir, Jobs j)
        {
            var lines = new List<string>();
            bool anyOk = false, anyFail = false;
            Action<Func<List<string>>> run = f =>
            {
                try { lines.AddRange(f()); anyOk = true; }
                catch (SkipException ex) { lines.Add("SKIP " + ex.Message); }
                catch (Exception ex) { lines.Add("FAIL " + ex.Message); anyFail = true; }
            };
            bool csvInput = string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase);
            if (csvInput) { lines.Add("SKIP a CSV export can't be converted (open the .bin log)"); return Tuple.Create(1, lines); }
            Func<Command, Options> opt = c => new Options
            {
                Command = c, Output = outDir, Overwrite = j.Overwrite, IgnoreCrc = j.IgnoreCrc,
                TimeCsv = c == Command.ToV4 && j.TimeCsv,
                NoMlc = c == Command.ToCsv && j.NoMlc, ActualOnly = c == Command.ToCsv && j.ActualOnly,
            };
            if (j.ToV4)
            {
                Options o = opt(Command.ToV4);
                o.MachineSpecifier = j.Machine;
                o.KeepSerial = j.KeepSerial;
                o.Serial = j.Serial;
                o.DropCouchRotations = j.DropCouch;
                o.MetaEdits = j.MetaEdits;
                o.SubbeamNames = j.SubbeamNames;
                run(() => Commands.RunToV4(path, true, o));
            }
            if (j.ToCsv) run(() => Commands.RunToCsv(path, opt(Command.ToCsv)));
            if (j.FullCsv) { var o = opt(Command.ToCsv); o.Full = true; o.NoMlc = false; o.ActualOnly = false; run(() => Commands.RunToCsv(path, o)); }
            if (j.Dump) run(() => Commands.RunDump(path, opt(Command.Dump)));
            return Tuple.Create(anyFail ? 2 : anyOk ? 0 : 1, lines);
        }
    }
}
