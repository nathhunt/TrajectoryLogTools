// MainViewModel.cs - the converter's one window: where the logs come from (the default folder,
// filtered by treatment date and time, or one file), the compact list of what will be converted,
// where they go, and Convert. The plan and the conversion are TrajectoryLogTools.Core.V4Batch.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrajectoryLogTools.Core;
using TrajectoryLogTools.Wpf;

namespace TrajectoryLogTools.Converter
{
    /// <summary>A log in the list.</summary>
    public partial class LogRow : ObservableObject
    {
        public V4BatchItem Item { get; }
        public string Treated { get { return Fmt.DateTimeMinutes(Item.Date); } }
        public string File { get { return Item.FileName; } }
        public string Folder { get { return Item.RelativeDir.Length == 0 ? "" : Item.RelativeDir; } }
        public string Plan
        {
            get
            {
                var parts = new[] { Item.PatientId, Item.PlanName, Item.FieldsText }.Where(s => !string.IsNullOrWhiteSpace(s));
                return string.Join(" · ", parts);
            }
        }
        public string Version { get { return Item.Version.Length > 0 ? "v" + Item.Version : ""; } }
        public bool CanConvert { get { return Item.WillConvert; } }

        /// <summary>Ticked: convert it (logs that can't be converted can't be ticked).</summary>
        [ObservableProperty] private bool _include;
        [ObservableProperty] private string _status;
        /// <summary>"neutral", "ok", "caution" or "critical".</summary>
        [ObservableProperty] private string _statusKind = "neutral";

        public LogRow(V4BatchItem item)
        {
            Item = item;
            _include = item.WillConvert;
            _status = item.WillConvert ? "To convert" : char.ToUpper(item.Reason[0]) + item.Reason.Substring(1);
            _statusKind = item.WillConvert ? "neutral" : item.State == V4BatchState.AlreadyConverted || item.State == V4BatchState.NotV51 ? "neutral" : "caution";
        }
    }

    public partial class MainViewModel : ObservableObject
    {
        ConverterSettings _settings;

        // ---- where from ----
        /// <summary>True: the logs of the input folder (filtered); false: one file.</summary>
        [ObservableProperty] private bool _folderMode = true;
        [ObservableProperty] private string _inputFolder = "";
        [ObservableProperty] private bool _includeSubfolders;
        [ObservableProperty] private string _singleFile = "";
        public LogFilter Filter { get; } = new LogFilter();

        // ---- where to ----
        [ObservableProperty] private string _outputFolder = "";
        [ObservableProperty] private bool _overwrite;

        // ---- the list ----
        public ObservableCollection<LogRow> Rows { get; } = new ObservableCollection<LogRow>();
        List<LogRow> _all = new List<LogRow>();
        [ObservableProperty] private bool _showSkipped;
        [ObservableProperty] private bool _isSearching;
        [ObservableProperty] private string _listSummary = "";
        [ObservableProperty] private string _message = "";

        // ---- converting ----
        [ObservableProperty] private bool _isBusy;
        [ObservableProperty] private double _progress;
        [ObservableProperty] private string _result = "";
        [ObservableProperty] private bool _isDone;

        public string FixedOptions { get; } = "Writes the v4.0 log only: machine TrueBeam, serial number removed, couch axes kept, everything else in the header as it is.";
        public int ToConvert { get { return _all.Count(r => r.Include && r.CanConvert); } }
        public int SkippedCount { get { return _all.Count(r => !r.CanConvert); } }
        public string ConvertText { get { int n = ToConvert; return n == 1 ? "Convert 1 log" : "Convert " + n + " logs"; } }
        public string ShowSkippedText { get { return "Show the logs that won't be converted (" + SkippedCount + ")"; } }
        public bool InputIsDefault { get { return SamePath(InputFolder, _settings.InputFolder); } }
        public bool OutputIsDefault { get { return SamePath(OutputFolder, _settings.OutputFolder); } }

        CancellationTokenSource _searchCts;
        int _searchVersion;

        public MainViewModel()
        {
            string error;
            _settings = ConverterSettings.Load(out error);
            ApplySettings();
            Filter.Changed += (s, e) => Refresh();
            if (error != null) Message = error;
            else if (string.IsNullOrWhiteSpace(_settings.InputFolder) || string.IsNullOrWhiteSpace(_settings.OutputFolder))
                Message = "Set the default input and output folders in Settings (they are saved beside the app, in " + ConverterSettings.FileName + ").";
            Refresh();
        }

        void ApplySettings()
        {
            InputFolder = _settings.InputFolder ?? "";
            OutputFolder = _settings.OutputFolder ?? "";
            IncludeSubfolders = _settings.IncludeSubfolders;
            OnPropertyChanged(nameof(InputIsDefault));
            OnPropertyChanged(nameof(OutputIsDefault));
        }

        static bool SamePath(string a, string b)
        {
            a = (a ?? "").Trim().Trim('"').TrimEnd('\\', '/');
            b = (b ?? "").Trim().Trim('"').TrimEnd('\\', '/');
            return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
        }

        partial void OnFolderModeChanged(bool value) { Refresh(); }
        partial void OnInputFolderChanged(string value) { OnPropertyChanged(nameof(InputIsDefault)); Refresh(); }
        partial void OnIncludeSubfoldersChanged(bool value) { Refresh(); }
        partial void OnSingleFileChanged(string value) { Refresh(); }
        partial void OnOutputFolderChanged(string value) { OnPropertyChanged(nameof(OutputIsDefault)); Refresh(); }
        partial void OnOverwriteChanged(bool value) { Refresh(); }
        partial void OnShowSkippedChanged(bool value) { FillRows(); }

        // =========================================================================================
        // The list
        // =========================================================================================

        /// <summary>Finds and plans again (in the background; a newer request replaces an older one).</summary>
        async void Refresh()
        {
            if (IsBusy) return;
            _searchCts?.Cancel();
            var cts = _searchCts = new CancellationTokenSource();
            int version = ++_searchVersion;
            IsDone = false;
            Result = "";
            string input = (InputFolder ?? "").Trim().Trim('"'), output = (OutputFolder ?? "").Trim().Trim('"'), single = (SingleFile ?? "").Trim().Trim('"');
            bool folderMode = FolderMode, subfolders = IncludeSubfolders, overwrite = Overwrite;
            var settings = new FindSettings { FolderMode = true, Input = input, IncludeSubfolders = subfolders, Output = output };
            string error = folderMode ? Filter.ApplyTo(settings) : null;

            List<V4BatchItem> plan = null;
            string problem = null;
            if (error != null) problem = error;
            else if (output.Length == 0) problem = "Choose the output folder (the default is set in Settings).";
            else if (folderMode && input.Length == 0) problem = "Choose the input folder (the default is set in Settings).";
            else if (folderMode && !Directory.Exists(input)) problem = "The input folder was not found: " + input;
            else if (!folderMode && single.Length == 0) problem = "Choose the log to convert.";
            else if (!folderMode && !System.IO.File.Exists(single)) problem = "The file was not found: " + single;
            if (problem == null)
            {
                IsSearching = true;
                try
                {
                    plan = await Task.Run(() =>
                    {
                        if (!folderMode) return new List<V4BatchItem> { V4Batch.PlanOne(single, output, overwrite) };
                        List<LogFile> found = LogFinder.Find(settings);
                        cts.Token.ThrowIfCancellationRequested();
                        return V4Batch.Plan(found, output, overwrite);
                    }, cts.Token);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex) { problem = ex.Message; }
                finally { if (version == _searchVersion) IsSearching = false; }
            }
            if (version != _searchVersion) return;   // a newer search has started

            _all = (plan ?? new List<V4BatchItem>()).OrderBy(i => i.Date).Select(i => new LogRow(i)).ToList();
            foreach (LogRow r in _all) r.PropertyChanged += (s, e) => { if (e.PropertyName == nameof(LogRow.Include)) Counts(); };
            FillRows();
            if (problem != null) ListSummary = problem;
            else if (_all.Count == 0) ListSummary = folderMode ? "No logs found" + (Filter.IsActive ? " for this filter." : ".") : "";
            else
            {
                int convert = _all.Count(r => r.CanConvert), already = _all.Count(r => r.Item.State == V4BatchState.AlreadyConverted),
                    v4 = _all.Count(r => r.Item.State == V4BatchState.NotV51), other = _all.Count - convert - already - v4;
                var parts = new List<string> { convert + (convert == 1 ? " log to convert" : " logs to convert") };
                if (already > 0) parts.Add(already + " already converted");
                if (v4 > 0) parts.Add(v4 + " already v4.0");
                if (other > 0) parts.Add(other + " that can't be converted");
                ListSummary = string.Join(" · ", parts) + (folderMode && Filter.IsActive ? "  (" + Filter.Describe() + ")" : "");
            }
            Counts();
        }

        void FillRows()
        {
            Rows.Clear();
            foreach (LogRow r in _all.Where(r => ShowSkipped || r.CanConvert)) Rows.Add(r);
            OnPropertyChanged(nameof(ShowSkippedText));
        }

        void Counts()
        {
            OnPropertyChanged(nameof(ToConvert));
            OnPropertyChanged(nameof(SkippedCount));
            OnPropertyChanged(nameof(ConvertText));
            OnPropertyChanged(nameof(ShowSkippedText));
            ConvertCommand.NotifyCanExecuteChanged();
        }

        [RelayCommand] void SelectAll() { foreach (LogRow r in _all.Where(r => r.CanConvert)) r.Include = true; }
        [RelayCommand] void SelectNone() { foreach (LogRow r in _all) r.Include = false; }
        [RelayCommand] void RefreshList() { Refresh(); }

        // =========================================================================================
        // Folders and file
        // =========================================================================================

        [RelayCommand]
        void BrowseInput()
        {
            string p = Dialogs.PickFolder("Folder with the v5.1 logs (this session; the default is in Settings)", InputFolder);
            if (p != null) InputFolder = p;
        }

        [RelayCommand]
        void BrowseOutput()
        {
            string p = Dialogs.PickFolder("Folder for the v4.0 logs (this session; the default is in Settings)", OutputFolder);
            if (p != null) OutputFolder = p;
        }

        [RelayCommand] void DefaultInput() { InputFolder = _settings.InputFolder ?? ""; }
        [RelayCommand] void DefaultOutput() { OutputFolder = _settings.OutputFolder ?? ""; }

        [RelayCommand]
        void PickFile()
        {
            string p = Dialogs.OpenFile("Log to convert", Dialogs.BinFilter, SingleFile.Length > 0 ? SingleFile : InputFolder);
            if (p == null) return;
            FolderMode = false;
            SingleFile = p;
        }

        [RelayCommand] void OpenOutput() { Dialogs.OpenFolder((OutputFolder ?? "").Trim().Trim('"')); }

        [RelayCommand]
        void ShowSettings()
        {
            var vm = new SettingsViewModel(_settings.Copy());
            if (Dialogs.ShowDialog(new SettingsWindow { DataContext = vm }) != true) return;
            _settings = vm.Settings;
            ApplySettings();
            Message = "";
            Refresh();
        }

        [RelayCommand] void DismissMessage() { Message = ""; }

        // =========================================================================================
        // Convert
        // =========================================================================================

        bool CanConvert() { return !IsBusy && ToConvert > 0; }

        [RelayCommand(CanExecute = nameof(CanConvert))]
        async Task Convert()
        {
            string output = (OutputFolder ?? "").Trim().Trim('"');
            try { Directory.CreateDirectory(output); }
            catch (Exception ex) { Dialogs.Warn("The output folder can't be used: " + ex.Message); return; }

            var todo = _all.Where(r => r.Include && r.CanConvert).ToList();
            IsBusy = true;
            IsDone = false;
            ConvertCommand.NotifyCanExecuteChanged();
            int ok = 0, failed = 0, skipped = 0;
            bool overwrite = Overwrite;
            for (int i = 0; i < todo.Count; i++)
            {
                LogRow row = todo[i];
                row.Status = "Converting…";
                var lines = new List<string>();
                V4BatchItem item = row.Item;
                ExportOutcome outcome;
                try { outcome = await Task.Run(() => V4Batch.Convert(item, overwrite, lines)); }
                catch (Exception ex) { outcome = ExportOutcome.Failed; lines.Add("FAIL " + ex.Message); }
                string detail = lines.FirstOrDefault(l => l.StartsWith("FAIL", StringComparison.Ordinal) || l.StartsWith("SKIP", StringComparison.Ordinal)) ?? "";
                int colon = detail.IndexOf(": ", StringComparison.Ordinal);
                if (colon > 0) detail = detail.Substring(colon + 2);
                switch (outcome)
                {
                    case ExportOutcome.Done: row.Status = "Converted"; row.StatusKind = "ok"; ok++; break;
                    case ExportOutcome.Skipped: row.Status = "Skipped: " + detail; row.StatusKind = "neutral"; skipped++; break;
                    default: row.Status = "Failed: " + detail; row.StatusKind = "critical"; failed++; break;
                }
                Progress = 100.0 * (i + 1) / todo.Count;
            }
            Result = ok + " converted" + (skipped > 0 ? ", " + skipped + " skipped" : "") + (failed > 0 ? ", " + failed + " failed" : "") + ".";
            IsBusy = false;
            IsDone = true;
            foreach (LogRow r in todo) r.Include = false;   // done; a later Convert won't repeat them
            ConvertCommand.NotifyCanExecuteChanged();
        }
    }
}
