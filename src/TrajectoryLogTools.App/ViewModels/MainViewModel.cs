// MainViewModel.cs - the window: the Open logs list on the left, the page on the right (start,
// log, compare, rebuild), and the notification bar.
//
// Logs are opened from anywhere (Open, Add from folder, drag and drop, the Eclipse launcher, the
// command line) and each keeps its own location; no folder is remembered as a default.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrajectoryLogTools.App.Services;

namespace TrajectoryLogTools.App.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        public ObservableCollection<LogItem> Logs { get; } = new ObservableCollection<LogItem>();
        public ICollectionView LogsView { get; }
        public ObservableCollection<string> RecentFiles { get; } = new ObservableCollection<string>();

        [ObservableProperty] private string _filter = "";
        [ObservableProperty] private LogItem _currentLog;
        [ObservableProperty] private object _currentPage;
        [ObservableProperty] private bool _listCollapsed;
        [ObservableProperty] private string _notice = "";
        /// <summary>"info", "caution" or "critical".</summary>
        [ObservableProperty] private string _noticeKind = "info";

        public int CheckedCount { get { return Logs.Count(l => l.IsChecked); } }
        public string SelectionText
        {
            get
            {
                int n = CheckedCount;
                return n == 0 ? Logs.Count + " open" : n + " selected";
            }
        }

        public string Title { get; } = "Trajectory Log Tools " + typeof(MainViewModel).Assembly.GetName().Version.ToString(3);

        /// <summary>Where and on what the app is running (handy when checking a new workstation).</summary>
        public string EnvironmentText { get; } =
            System.Runtime.InteropServices.RuntimeInformation.OSDescription + "  ·  " +
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + "  ·  " + AppContext.BaseDirectory;

        /// <summary>Folder the open dialogs start in when the launcher gives one; otherwise Windows decides.</summary>
        public string StartFolder { get; }

        public StartPageViewModel StartPage { get; }

        public MainViewModel(StartupArgs args)
        {
            LogsView = CollectionViewSource.GetDefaultView(Logs);
            LogsView.Filter = o => Matches((LogItem)o);
            Logs.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null) foreach (LogItem l in e.NewItems) l.PropertyChanged += OnLogChanged;
                RaiseSelection();
            };
            foreach (string r in AppSettings.Current.RecentFiles.Where(File.Exists)) RecentFiles.Add(r);
            StartFolder = args.StartFolder;
            StartPage = new StartPageViewModel(this);
            CurrentPage = StartPage;

            foreach (string p in args.Files) OpenPaths(new[] { p });
            if (args.Folder != null) AddFromFolder(args.Folder);
        }

        void OnLogChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(LogItem.IsChecked)) RaiseSelection();
        }

        void RaiseSelection()
        {
            OnPropertyChanged(nameof(CheckedCount));
            OnPropertyChanged(nameof(SelectionText));
            CompareCheckedCommand.NotifyCanExecuteChanged();
            ExportCommand.NotifyCanExecuteChanged();
            RebuildCommand.NotifyCanExecuteChanged();
            CloseCommand.NotifyCanExecuteChanged();
        }

        bool Matches(LogItem l)
        {
            string f = (Filter ?? "").Trim();
            if (f.Length == 0) return true;
            return new[] { l.PatientId, l.DisplayName, l.FieldsText, l.DateText, l.Version, l.Path }
                .Any(s => s != null && s.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        partial void OnFilterChanged(string value) { LogsView.Refresh(); }

        partial void OnCurrentLogChanged(LogItem value)
        {
            if (value == null) { if (Logs.Count == 0) CurrentPage = StartPage; return; }
            ShowLog(value);
        }

        // =========================================================================================
        // Pages
        // =========================================================================================

        public void ShowLog(LogItem item)
        {
            if (item.Page == null) item.Page = new LogPageViewModel(item, this);
            if (CurrentLog != item) { CurrentLog = item; return; }   // comes back here through OnCurrentLogChanged
            CurrentPage = item.Page;
            item.Page.Activate();
        }

        public void ShowCompare(LogItem a, LogItem b)
        {
            CurrentPage = new ComparePageViewModel(a, b, this);
        }

        public void ShowRebuild(IEnumerable<LogItem> items)
        {
            CurrentPage = new RebuildPageViewModel(items, this);
        }

        /// <summary>The ticked logs if <paramref name="item"/> is one of them, else just it.</summary>
        public List<LogItem> WithChecked(LogItem item)
        {
            return item.IsChecked ? Logs.Where(l => l.IsChecked).ToList() : new List<LogItem> { item };
        }

        // =========================================================================================
        // Opening and closing
        // =========================================================================================

        /// <summary>Opens .bin / .csv files; a folder opens the Add from folder dialog for it. Returns the logs added or already open.</summary>
        public List<LogItem> OpenPaths(IEnumerable<string> paths)
        {
            var result = new List<LogItem>();
            var bad = new List<string>();
            foreach (string raw in paths)
            {
                string p = raw.Trim().Trim('"');
                if (Directory.Exists(p)) { AddFromFolder(p); continue; }
                if (!File.Exists(p)) { bad.Add(System.IO.Path.GetFileName(p) + ": not found"); continue; }
                LogItem existing = Logs.FirstOrDefault(l => string.Equals(l.Path, System.IO.Path.GetFullPath(p), StringComparison.OrdinalIgnoreCase));
                if (existing != null) { result.Add(existing); continue; }
                try
                {
                    LogItem item = LogItem.Create(p);
                    Logs.Add(item);
                    result.Add(item);
                    AddRecent(item.Path);
                }
                catch (Exception ex) { bad.Add(System.IO.Path.GetFileName(p) + ": " + ex.Message); }
            }
            if (bad.Count > 0) ShowNotice("Not opened: " + string.Join("; ", bad), "caution");
            if (result.Count > 0) ShowLog(result[result.Count - 1]);
            return result;
        }

        void AddRecent(string path)
        {
            AppSettings.Current.AddRecent(path);
            RecentFiles.Clear();
            foreach (string r in AppSettings.Current.RecentFiles) RecentFiles.Add(r);
        }

        [RelayCommand]
        void Open()
        {
            string[] files = Dialogs.OpenFiles("Open trajectory logs", Dialogs.LogFilter, StartFolder);
            if (files.Length > 0) OpenPaths(files);
        }

        [RelayCommand]
        void OpenRecent(string path)
        {
            if (path != null) OpenPaths(new[] { path });
        }

        [RelayCommand]
        void AddFromFolderDialog() { AddFromFolder(null); }

        public void AddFromFolder(string folder)
        {
            var vm = new AddFromFolderViewModel(folder ?? StartFolder);
            if (Dialogs.ShowDialog(new Views.AddFromFolderWindow { DataContext = vm }) != true) return;
            OpenPaths(vm.Found.Select(f => f.FullPath).ToList());
        }

        List<LogItem> Targets()
        {
            var checkedLogs = Logs.Where(l => l.IsChecked).ToList();
            if (checkedLogs.Count > 0) return checkedLogs;
            return CurrentLog != null ? new List<LogItem> { CurrentLog } : new List<LogItem>();
        }

        bool CanClose() { return Logs.Count > 0; }

        [RelayCommand(CanExecute = nameof(CanClose))]
        void Close()
        {
            foreach (LogItem l in Targets()) CloseLog(l);
        }

        public void CloseLog(LogItem l)
        {
            int i = Logs.IndexOf(l);
            if (i < 0) return;
            bool wasCurrent = CurrentLog == l;
            Logs.RemoveAt(i);
            if (Logs.Count == 0) { CurrentLog = null; CurrentPage = StartPage; return; }
            if (wasCurrent || CurrentPage is ComparePageViewModel || CurrentPage is RebuildPageViewModel)
                ShowLog(Logs[Math.Min(i, Logs.Count - 1)]);
        }

        bool CanCompareChecked() { return CheckedCount == 2; }

        [RelayCommand(CanExecute = nameof(CanCompareChecked))]
        void CompareChecked()
        {
            var two = Logs.Where(l => l.IsChecked).ToList();
            if (two.Count == 2) ShowCompare(two[0], two[1]);
        }

        bool CanExport() { return Logs.Count > 0; }

        [RelayCommand(CanExecute = nameof(CanExport))]
        void Export() { ExportLogs(Targets()); }

        public void ExportLogs(List<LogItem> logs)
        {
            if (logs.Count == 0) return;
            var vm = new ExportViewModel(logs);
            Dialogs.ShowDialog(new Views.ExportWindow { DataContext = vm });
        }

        [RelayCommand(CanExecute = nameof(CanExport))]
        void Rebuild()
        {
            var logs = Targets();
            if (logs.Count > 0) ShowRebuild(logs);
        }

        [RelayCommand]
        void ToggleList() { ListCollapsed = !ListCollapsed; }

        [RelayCommand]
        void CheckAll()
        {
            bool all = Logs.All(l => l.IsChecked);
            foreach (LogItem l in LogsView.Cast<LogItem>()) l.IsChecked = !all;
        }

        // =========================================================================================
        // Settings, theme, notices
        // =========================================================================================

        [RelayCommand]
        void ShowSettings()
        {
            var vm = new SettingsViewModel(this);
            Dialogs.ShowDialog(new Views.SettingsWindow { DataContext = vm });
        }

        [RelayCommand]
        void ToggleTheme()
        {
            AppSettings.Current.Theme = Theme.IsDark ? "Light" : "Dark";
            AppSettings.Current.Save();
            Theme.Apply(AppSettings.Current.Theme);
        }

        public void ClearRecent()
        {
            AppSettings.Current.RecentFiles.Clear();
            AppSettings.Current.Save();
            RecentFiles.Clear();
        }

        public void ShowNotice(string message, string kind = "info")
        {
            NoticeKind = kind;
            Notice = message;
        }

        [RelayCommand]
        void DismissNotice() { Notice = ""; }
    }
}
