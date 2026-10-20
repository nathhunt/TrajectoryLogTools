// ConvertViewModel.cs - Convert & export: v5.1 -> v4.0, CSV, full CSV and raw dumps for a single
// log or a folder of logs (optionally with subfolders and a treatment date range). The work is
// done by TrajectoryLogTools.Core.Commands, so the output is identical to the command-line tool.

using System;
using System.Collections.Generic;
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
    public partial class ConvertViewModel : ObservableObject
    {
        // ---- input ----
        [ObservableProperty] private bool _folderMode = true;
        [ObservableProperty] private string _input = "";
        [ObservableProperty] private bool _includeSubfolders;
        [ObservableProperty] private bool _useDateRange;
        [ObservableProperty] private DateTime? _fromDate = DateTime.Today.AddDays(-7);
        [ObservableProperty] private DateTime? _toDate = DateTime.Today;

        // ---- output ----
        [ObservableProperty] private string _output = "";
        [ObservableProperty] private bool _toV4 = true;
        [ObservableProperty] private bool _keepMachineInfo;
        [ObservableProperty] private bool _timeCsv;
        [ObservableProperty] private bool _toCsv;
        [ObservableProperty] private bool _fullCsv;
        [ObservableProperty] private bool _noMlc;
        [ObservableProperty] private bool _actualOnly;
        [ObservableProperty] private bool _dump;
        [ObservableProperty] private bool _overwrite;
        [ObservableProperty] private bool _ignoreCrc;

        // ---- run ----
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsIdle))]
        [NotifyCanExecuteChangedFor(nameof(RunCommand), nameof(FindCommand), nameof(CancelCommand))]
        private bool _isBusy;
        [ObservableProperty] private double _progress;
        [ObservableProperty] private string _status = "Ready.";
        [ObservableProperty] private string _logText = "";

        public bool IsIdle { get { return !IsBusy; } }
        public bool CsvOptionsEnabled { get { return ToCsv && !FullCsv; } }

        readonly StringBuilder _log = new StringBuilder();
        CancellationTokenSource _cts;

        public ConvertViewModel(StartupArgs args)
        {
            if (args.Output != null) Output = args.Output;
            if (args.Mode != null)
            {
                ToV4 = args.Mode == "v4" || args.Mode == "both";
                ToCsv = args.Mode == "csv" || args.Mode == "both";
                Dump = args.Mode == "dump";
            }
            if (args.Input != null)
            {
                FolderMode = !File.Exists(args.Input);
                Input = args.Input;
            }
        }

        // A file path is not a valid folder and vice versa, so switching clears the input.
        partial void OnFolderModeChanged(bool value) { Input = ""; }
        partial void OnToCsvChanged(bool value) { OnPropertyChanged(nameof(CsvOptionsEnabled)); }
        partial void OnFullCsvChanged(bool value) { OnPropertyChanged(nameof(CsvOptionsEnabled)); }

        [RelayCommand]
        void BrowseInput()
        {
            string p = FolderMode
                ? Dialogs.PickFolder("Select the folder containing the trajectory logs", Input)
                : Dialogs.OpenFile("Select a trajectory log", Dialogs.BinFilter, Input);
            if (p != null) Input = p;
        }

        [RelayCommand]
        void BrowseOutput()
        {
            string p = Dialogs.PickFolder("Select the folder to export to", Output);
            if (p != null) Output = p;
        }

        [RelayCommand]
        void OpenOutput() { Dialogs.OpenFolder(Clean(Output)); }

        static string Clean(string s) { return (s ?? "").Trim().Trim('"'); }

        FindSettings ReadSettings(bool forRun)
        {
            var s = new FindSettings
            {
                FolderMode = FolderMode,
                Input = Clean(Input),
                IncludeSubfolders = IncludeSubfolders,
                UseDateRange = FolderMode && UseDateRange,
                From = (FromDate ?? DateTime.Today).Date,
                To = (ToDate ?? DateTime.Today).Date.AddDays(1).AddTicks(-1),
                Output = Clean(Output),
            };
            string error = null;
            if (s.Input.Length == 0) error = s.FolderMode ? "Select an input folder." : "Select a log file.";
            else if (s.FolderMode && !Directory.Exists(s.Input)) error = "Input folder not found:\n" + s.Input;
            else if (!s.FolderMode && !File.Exists(s.Input)) error = "Log file not found:\n" + s.Input;
            else if (s.UseDateRange && s.From > s.To) error = "The start date is after the end date.";
            else if (forRun)
            {
                if (!ToV4 && !ToCsv && !Dump) error = "Tick at least one of 'Convert to v4.0', 'Export CSV' or 'Raw dump'.";
                else if (s.Output.Length == 0) error = "Select a folder to export to.";
                else if (ToV4 && LogFinder.PathEquals(s.Output, s.FolderMode ? s.Input : Path.GetDirectoryName(Path.GetFullPath(s.Input))))
                    error = "The export folder is the same as the input folder, so the v4.0 files would overwrite the originals.\nChoose a different export folder.";
            }
            if (error != null) { Dialogs.Warn(error); return null; }
            return s;
        }

        static string Describe(FindSettings s, int count)
        {
            var sb = new StringBuilder();
            sb.Append(count).Append(" log file(s) in ").Append(s.Input);
            if (s.FolderMode && s.IncludeSubfolders) sb.Append(" (including subfolders)");
            if (s.UseDateRange)
                sb.Append(string.Format(CultureInfo.InvariantCulture, ", treated {0:dd/MM/yyyy} to {1:dd/MM/yyyy}", s.From, s.To));
            return sb.ToString();
        }

        void ClearLog() { _log.Clear(); LogText = ""; }

        void AppendLog(string line)
        {
            _log.Append(line).Append(Environment.NewLine);
            LogText = _log.ToString();
        }

        bool CanStart() { return !IsBusy; }

        [RelayCommand(CanExecute = nameof(CanStart))]
        void Find()
        {
            FindSettings s = ReadSettings(false);
            if (s == null) return;
            List<LogFile> files;
            try { files = LogFinder.Find(s); }
            catch (Exception ex) { Dialogs.Error(ex.Message); return; }

            ClearLog();
            AppendLog(Describe(s, files.Count));
            foreach (LogFile f in files)
                AppendLog(string.Format(CultureInfo.InvariantCulture, "  {0:dd/MM/yyyy HH:mm:ss}{1}  {2}",
                                        f.Date, f.DateFromName ? "  " : " *", Path.Combine(f.RelativeDir, Path.GetFileName(f.FullPath))));
            if (files.Any(f => !f.DateFromName)) AppendLog("  * no time stamp in the file name; file modified date used");
            Status = files.Count + " log file(s) found.";
        }

        sealed class RunResult { public int Files, Ok, Skipped, Failed; public bool Cancelled; public string LogPath; }

        [RelayCommand(CanExecute = nameof(CanStart))]
        async Task Run()
        {
            FindSettings s = ReadSettings(true);
            if (s == null) return;
            try { Directory.CreateDirectory(s.Output); }
            catch (Exception ex) { Dialogs.Error("Cannot create the export folder:\n" + ex.Message); return; }

            ClearLog();
            Progress = 0;
            Status = "Working...";
            IsBusy = true;
            _cts = new CancellationTokenSource();
            var jobs = new Jobs { ToV4 = ToV4, ToCsv = ToCsv, Dump = Dump, Overwrite = Overwrite, IgnoreCrc = IgnoreCrc,
                                  KeepMachineInfo = KeepMachineInfo, TimeCsv = TimeCsv, FullCsv = FullCsv, NoMlc = NoMlc, ActualOnly = ActualOnly };
            var progress = new Progress<Tuple<double, string>>(p =>
            {
                if (p.Item1 >= 0) Progress = p.Item1;
                if (p.Item2 == null) return;
                if (p.Item2.StartsWith("#status ", StringComparison.Ordinal)) Status = p.Item2.Substring(8);
                else AppendLog(p.Item2);
            });
            try
            {
                CancellationToken token = _cts.Token;
                RunResult r = await Task.Run(() => Work(s, jobs, progress, token));
                Status = string.Format(CultureInfo.InvariantCulture, "{0} OK, {1} skipped, {2} failed{3}.",
                                       r.Ok, r.Skipped, r.Failed, r.Cancelled ? " (cancelled)" : "");
                if (r.LogPath != null) AppendLog("Run log saved to " + r.LogPath);
                if (r.Failed > 0) Dialogs.Warn(r.Failed + " file(s) failed - see the log for details.");
            }
            catch (Exception ex)
            {
                Status = "Error.";
                AppendLog("ERROR: " + ex.Message);
                Dialogs.Error(ex.Message);
            }
            finally
            {
                IsBusy = false;
                _cts.Dispose();
                _cts = null;
            }
        }

        bool CanCancel() { return IsBusy; }

        [RelayCommand(CanExecute = nameof(CanCancel))]
        void Cancel() { if (_cts != null) _cts.Cancel(); }

        /// <summary>The options, copied on the UI thread for the worker.</summary>
        sealed class Jobs
        {
            public bool ToV4, ToCsv, Dump, Overwrite, IgnoreCrc, KeepMachineInfo, TimeCsv, FullCsv, NoMlc, ActualOnly;
        }

        static RunResult Work(FindSettings s, Jobs j, IProgress<Tuple<double, string>> progress, CancellationToken token)
        {
            var r = new RunResult();
            var runLog = new StringBuilder();
            Action<double, string> report = (pct, line) =>
            {
                if (line != null && !line.StartsWith("#status ", StringComparison.Ordinal)) runLog.Append(line).Append("\r\n");
                progress.Report(Tuple.Create(pct, line));
            };

            List<LogFile> files = LogFinder.Find(s);
            r.Files = files.Count;
            report(0, string.Format(CultureInfo.InvariantCulture, "{0:dd/MM/yyyy HH:mm:ss}  {1}", DateTime.Now, Describe(s, files.Count)));
            report(0, "Export folder: " + s.Output);
            if (files.Count == 0) { report(100, "Nothing to do."); return r; }

            for (int i = 0; i < files.Count; i++)
            {
                if (token.IsCancellationRequested) { r.Cancelled = true; report(100.0 * i / files.Count, "Cancelled."); break; }
                LogFile f = files[i];
                string outDir = f.RelativeDir.Length == 0 ? s.Output : Path.Combine(s.Output, f.RelativeDir);
                report(100.0 * i / files.Count, "#status " + (i + 1) + " / " + files.Count + ": " + Path.GetFileName(f.FullPath));

                bool anyFail = false, anyOk = false;
                if (j.ToV4)
                {
                    Options opt = MakeOptions(j, Command.ToV4, outDir);
                    RunOne(report, f.FullPath, () => Commands.RunToV4(f.FullPath, true, opt), ref anyOk, ref anyFail);
                }
                if (j.ToCsv)
                {
                    Options opt = MakeOptions(j, Command.ToCsv, outDir);
                    RunOne(report, f.FullPath, () => Commands.RunToCsv(f.FullPath, opt), ref anyOk, ref anyFail);
                }
                if (j.Dump)
                {
                    Options opt = MakeOptions(j, Command.Dump, outDir);
                    RunOne(report, f.FullPath, () => Commands.RunDump(f.FullPath, opt), ref anyOk, ref anyFail);
                }
                if (anyFail) r.Failed++;
                else if (anyOk) r.Ok++;
                else r.Skipped++;
                report(100.0 * (i + 1) / files.Count, null);
            }

            report(100, "");
            report(100, string.Format(CultureInfo.InvariantCulture, "Done: {0} file(s) - {1} OK, {2} skipped, {3} failed{4}.",
                                      r.Files, r.Ok, r.Skipped, r.Failed, r.Cancelled ? " (cancelled)" : ""));
            // Keep a record of the run next to the exported files.
            try
            {
                r.LogPath = Path.Combine(s.Output, "TrajectoryLogConversion_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture) + ".log");
                File.WriteAllText(r.LogPath, runLog.ToString(), Encoding.UTF8);
            }
            catch (Exception ex)
            {
                r.LogPath = null;
                report(100, "Could not write the run log: " + ex.Message);
            }
            return r;
        }

        static void RunOne(Action<double, string> report, string input, Func<List<string>> action, ref bool anyOk, ref bool anyFail)
        {
            try
            {
                foreach (string line in action()) report(-1, line);
                anyOk = true;
            }
            catch (SkipException ex) { report(-1, "SKIP " + Path.GetFileName(input) + ": " + ex.Message); }
            catch (Exception ex) { anyFail = true; report(-1, "FAIL " + Path.GetFileName(input) + ": " + ex.Message); }
        }

        static Options MakeOptions(Jobs j, Command command, string outDir)
        {
            return new Options
            {
                Command = command,
                Output = outDir,
                Overwrite = j.Overwrite,
                IgnoreCrc = j.IgnoreCrc,
                KeepMachineInfo = command == Command.ToV4 && j.KeepMachineInfo,
                TimeCsv = command == Command.ToV4 && j.TimeCsv,
                Full = command == Command.ToCsv && j.FullCsv,
                NoMlc = command == Command.ToCsv && j.NoMlc && !j.FullCsv,
                ActualOnly = command == Command.ToCsv && j.ActualOnly && !j.FullCsv
            };
        }
    }
}
