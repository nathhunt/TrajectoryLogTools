// Scenarios.cs - every screenshot of the user guide: each scenario sets up the real windows and
// view models with the demo logs, waits until they have settled, and takes its pictures. The file
// names are the names used in docs/user (images/<name>.png). Add a scenario here to add a picture;
// see docs/CONTRIBUTING-docs.md.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TrajectoryLogTools.App;
using TrajectoryLogTools.App.Services;
using TrajectoryLogTools.App.ViewModels;
using TrajectoryLogTools.App.Views;
using Conv = TrajectoryLogTools.Converter;

namespace TrajectoryLogTools.DocShots
{
    public static class Scenarios
    {
        /// <summary>The main window's client area: as the app opens it (1360 x 880 window).</summary>
        const double MainW = 1344, MainH = 840;

        public static readonly List<Scenario> All = new List<Scenario>
        {
            new Scenario { Name = "start", Description = "The start page, no log open", Run = Start },
            new Scenario { Name = "main", Description = "The main window with logs open; the log page and its tabs", Run = MainWindowAndLogPage },
            new Scenario { Name = "crc", Description = "A log whose CRC fails", Run = CrcFailed },
            new Scenario { Name = "add-folder", Description = "Add logs from a folder, with a period", Run = AddFromFolder },
            new Scenario { Name = "compare", Description = "Compare two deliveries", Run = Compare },
            new Scenario { Name = "file-compare", Description = "File comparison of a v4.0 conversion", Run = FileComparison },
            new Scenario { Name = "rebuild", Description = "Rebuild Eclipse MLC: fields, results, details", Run = Rebuild },
            new Scenario { Name = "rebuild-exports", Description = "Rebuild with Eclipse exports (one doesn't fit)", Run = RebuildWithExports },
            new Scenario { Name = "export", Description = "Export window, before and after", Run = Export },
            new Scenario { Name = "header", Description = "Export: Header details", Run = HeaderDetails },
            new Scenario { Name = "settings", Description = "Settings and the title bar", Run = SettingsAndTitleBar },
            new Scenario { Name = "converter", Description = "Trajectory Log Converter: first run, the day's logs, one file, settings, done", Run = Converter },
            new Scenario { Name = "dark", Description = "The main window in the dark theme (last: it switches the theme)", Run = Dark },
        };

        // =========================================================================================
        // Helpers
        // =========================================================================================

        /// <summary>The main window, with these logs open (and read, so their badges show); the last one shown.</summary>
        static async Task<(MainWindow w, MainViewModel vm)> OpenMain(Shots s, params string[] logs)
        {
            var w = await s.Show(new MainWindow(), MainW, MainH);
            var vm = new MainViewModel(new StartupArgs());
            w.DataContext = vm;
            if (logs.Length > 0)
            {
                vm.OpenPaths(logs);
                await Task.WhenAll(vm.Logs.Select(l => l.LoadAsync()));
                await WaitForLogPage(vm);
            }
            await Ui.Settle();
            return (w, vm);
        }

        static async Task WaitForLogPage(MainViewModel vm)
        {
            await Ui.WaitUntil(() => vm.CurrentPage is LogPageViewModel p && !p.IsLoading, "the log page");
        }

        static LogItem Item(MainViewModel vm, string path) { return vm.Logs.First(l => string.Equals(l.Path, Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)); }

        /// <summary>The element with this x:Name, in the window or any of its pages (user controls have their own names).</summary>
        static FrameworkElement Named(Window w, string name) { return (FrameworkElement)w.FindName(name) ?? Ui.Find<FrameworkElement>(w, e => e.Name == name); }

        /// <summary>The page area of the main window (right of the Open logs pane).</summary>
        static FrameworkElement PagePane(DependencyObject w) { return Ui.Up<Border>(Ui.Find<ContentControl>(w, c => c.Content is LogPageViewModel || c.Content is ComparePageViewModel || c.Content is RebuildPageViewModel || c.Content is StartPageViewModel)); }

        static FrameworkElement ItemsFor(DependencyObject root, object source) { return Ui.Find<ItemsControl>(root, i => ReferenceEquals(i.ItemsSource, source)); }

        // =========================================================================================
        // Trajectory Log Tools
        // =========================================================================================

        static async Task Start(Shots s)
        {
            AppSettings.Current.RecentFiles = new List<string> { s.Demo.ProstateFx3, s.Demo.HeadNeck, s.Demo.Pelvis[0] };
            var (w, vm) = await OpenMain(s);
            await s.Shoot(w, "start-page", new ShotOptions()
                .Mark(1, r => Ui.Button(r, "Open files…"), "top")
                .Mark(2, r => Ui.Button(r, "Add from folder…"), "top")
                .Mark(3, r => Ui.Text(r, "Recent logs"))
                .Mark(4, r => Named(w, "HelpButton"), "bottom"));
        }

        static async Task MainWindowAndLogPage(Shots s)
        {
            DemoData d = s.Demo;
            var (w, vm) = await OpenMain(s, d.ProstateFx3, d.HeadNeck, d.Pelvis[0], d.Pelvis[1], d.Pelvis[2], d.Brain, d.ProstateFx4);
            Item(vm, d.ProstateFx3).IsChecked = true;
            Item(vm, d.ProstateFx4).IsChecked = true;
            await s.Shoot(w, "main-window", new ShotOptions()
                .Mark(1, r => Ui.Up<Button>(Ui.Text(r, "Open")), "top")
                .Mark(2, r => Named(w, "MoreButton"), "top")
                .Mark(3, r => Ui.Find<TextBox>(r), "right")
                .Mark(4, r => Ui.Find<ListBoxItem>(Named(w, "LogList")), "left")
                .Mark(5, r => Ui.Up<UniformGrid>(Ui.Button(r, "Compare")), "top")
                .Mark(6, r => ItemsFor(r, ((LogPageViewModel)vm.CurrentPage).Facts), "top")
                .Mark(7, r => Named(w, "HelpButton"), "bottom"));

            // The Open logs list alone: badges, patient IDs, ticks
            await s.Shoot(w, "open-logs-list", new ShotOptions { Crop = r => Ui.Up<Border>(Named(w, "LogList")), Padding = 0 }
                .Mark(1, r => Ui.Find<CheckBox>(Named(w, "LogList")), "left"));

            var page = (LogPageViewModel)vm.CurrentPage;
            await s.Shoot(w, "log-overview", new ShotOptions { Crop = PagePane, Padding = 0 }
                .Mark(1, r => ItemsFor(r, page.Facts), "topleft")
                .Mark(2, r => Named(w, "TimelineBar"), "topleft")
                .Mark(3, r => Ui.Find<DataGrid>(r, g => ReferenceEquals(g.ItemsSource, page.Fields)), "topleft")
                .Mark(4, r => Ui.Text(PagePane(w), "Raw"), "right"));

            page.SelectedTab = 1;
            await Ui.Settle(800);
            await s.Shoot(w, "log-axes", new ShotOptions { Crop = PagePane, Padding = 0 }
                .Mark(1, r => Ui.Find<ListBox>(PagePane(w), l => ReferenceEquals(l.ItemsSource, page.Axes)), "topleft")
                .Mark(2, r => Ui.Button(PagePane(w), "Plot against control point (instead of time)"), "left"));

            page.SelectedTab = 2;
            await Ui.Settle(800);
            await s.Shoot(w, "log-mlc", new ShotOptions { Crop = PagePane, Padding = 0 });

            page.SelectedTab = 3;
            await Ui.WaitUntil(() => !page.RawLoading && page.RawDump.Length > 0, "the raw dump");
            await s.Shoot(w, "log-raw", new ShotOptions { Crop = PagePane, Padding = 0 });

            // Overview with the beam holds open, the page made tall enough to show it all
            page.SelectedTab = 0;
            await Ui.Settle();
            Ui.Expander(w, "Beam holds").IsExpanded = true;
            await Ui.Settle();
            await Ui.ScrollToTop(Ui.Expander(w, "Beam holds"));
            await s.Shoot(w, "log-beam-holds", new ShotOptions { Crop = r => Ui.Expander(r, "Beam holds"), Padding = 8 });
        }

        static async Task CrcFailed(Shots s)
        {
            var (w, vm) = await OpenMain(s, s.Demo.Brain);
            await s.Shoot(w, "log-crc-failed", new ShotOptions { Crop = PagePane, Padding = 0 });
        }

        static async Task AddFromFolder(Shots s)
        {
            var (main, _) = await OpenMain(s);
            var vm = new AddFromFolderViewModel(s.Demo.Logs);
            vm.IncludeSubfolders = false;
            vm.Filter.FromDate = DemoData.Today;
            vm.Filter.ToDate = DemoData.Today;
            vm.Filter.FromTime = new TimeSpan(8, 0, 0);
            vm.Filter.ToTime = new TimeSpan(12, 0, 0);
            vm.Filter.UseDateRange = true;
            var w = await s.Show(new AddFromFolderWindow { DataContext = vm }, 584);
            await s.Shoot(w, "add-from-folder", new ShotOptions()
                .Mark(1, r => Ui.Find<TextBox>(r), "inside")
                .Mark(2, r => Ui.Button(r, "Include subfolders"), "left")
                .Mark(3, r => Ui.Button(r, "Only logs treated in this period"), "left")
                .Mark(4, r => Ui.Button(r, "Only at times of day between"), "left")
                .Mark(5, r => Ui.Button(r, "Add"), "top"));
        }

        static async Task Compare(Shots s)
        {
            DemoData d = s.Demo;
            AppSettings.Current.MlcToleranceMm = 1.0;
            AppSettings.Current.GantryToleranceDeg = 0.5;
            AppSettings.Current.MuTolerance = 0.5;
            try
            {
                var (w, vm) = await OpenMain(s, d.ProstateFx3, d.HeadNeck, d.ProstateFx4);
                Item(vm, d.ProstateFx3).IsChecked = true;
                Item(vm, d.ProstateFx4).IsChecked = true;
                vm.CompareCheckedCommand.Execute(null);
                var page = (ComparePageViewModel)vm.CurrentPage;
                await Ui.WaitUntil(() => !page.IsBusy && page.HasResult, "the comparison");
                await Ui.Settle(800);
                await s.Shoot(w, "compare-page", new ShotOptions()
                    .Mark(1, r => Ui.Text(r, "Compare deliveries"), "right")
                    .Mark(2, r => Ui.Find<Border>(r, b => b.Name == "DropA"), "left")
                    .Mark(3, r => Ui.Text(r, page.Verdict), "left")
                    .Mark(4, r => ItemsFor(r, page.Cards), "topleft")
                    .Mark(5, r => Ui.Button(r, "Save report…"), "bottom"));
                await s.Shoot(w, "compare-cards", new ShotOptions
                {
                    CropAll = r => new[] { Ui.Up<Border>(Ui.Text(r, page.Verdict)), ItemsFor(r, page.Cards) },
                    Padding = 6,
                });

                // The whole page, details open
                // The details, opened, from the top of the first
                foreach (string e in new[] { "Per field", "By axis" }) Ui.Expander(w, e).IsExpanded = true;
                await Ui.Settle(800);
                await Ui.ScrollToTop(Ui.Expander(w, "Planned (expected)"));
                await s.Shoot(w, "compare-details", new ShotOptions { Crop = r => Ui.Up<ScrollViewer>(Ui.Expander(r, "Planned (expected)")), Padding = 0 });
                Ui.Expander(w, "File differences").IsExpanded = true;
                await Ui.Settle();
                await Ui.ScrollToTop(Ui.Expander(w, "File differences"));
                await s.Shoot(w, "compare-file-differences", new ShotOptions { Crop = r => Ui.Expander(r, "File differences"), Padding = 8 }
                    .Mark(1, r => Ui.Button(r, "Compare the files…"), "left"));
            }
            finally
            {
                AppSettings.Current.MlcToleranceMm = AppSettings.Current.GantryToleranceDeg = AppSettings.Current.MuTolerance = null;
            }
        }

        static async Task FileComparison(Shots s)
        {
            var (main, _) = await OpenMain(s);
            LogItem a = LogItem.Create(s.Demo.ProstateFx4), b = LogItem.Create(s.Demo.ProstateFx4V4);
            var vm = new FileCompareViewModel(a, b, true);
            var w = await s.Show(new FileCompareWindow { DataContext = vm }, 1164, 800);
            await Ui.WaitUntil(() => !vm.IsBusy && vm.Verdict.Length > 0, "the file comparison");
            await s.Shoot(w, "file-compare-same", new ShotOptions()
                .Mark(1, r => Ui.Button(r, "Ignore what differs between v4.0 and v5.1 by design (version, time axis, axis order, machine and serial)"), "left")
                .Mark(2, r => Ui.Text(r, vm.Verdict), "left"));

            vm.IgnoreVersion = false;
            await Ui.Settle();
            await Ui.WaitUntil(() => !vm.IsBusy, "the file comparison");
            await s.Shoot(w, "file-compare-differences", new ShotOptions()
                .Mark(1, r => Ui.Find<DataGrid>(r, g => ReferenceEquals(g.ItemsSource, vm.Axes)), "topleft")
                .Mark(2, r => Ui.Find<DataGrid>(r, g => ReferenceEquals(g.ItemsSource, vm.Examples)), "topleft"));

            Ui.Find<TabControl>(w).SelectedIndex = 1;
            await Ui.Settle();
            await s.Shoot(w, "file-compare-header");
        }

        static async Task Rebuild(Shots s)
        {
            DemoData d = s.Demo;
            var (w, vm) = await OpenMain(s, d.HeadNeck, d.Pelvis[0], d.Pelvis[1], d.Pelvis[2]);
            foreach (LogItem l in vm.Logs) l.IsChecked = true;
            vm.RebuildCommand.Execute(null);
            var page = (RebuildPageViewModel)vm.CurrentPage;
            await Ui.WaitUntil(() => !page.IsBusy && page.Groups.Count > 0, "the rebuild page");
            await s.Shoot(w, "rebuild-page", new ShotOptions()
                .Mark(1, r => Ui.Text(r, "1  Fields"), "left")
                .Mark(2, r => Ui.Text(r, "2  Save to"), "left")
                .Mark(3, r => Ui.Text(r, "Advanced"), "left")
                .Mark(4, r => Ui.Button(r, page.RebuildText), "left"));

            await page.RebuildCommand.ExecuteAsync(null);
            await Ui.WaitUntil(() => !page.IsBusy && page.Results.Count > 0, "the rebuild");
            await s.Shoot(w, "rebuild-results", new ShotOptions()
                .Mark(1, r => Ui.Find<ContentPresenter>(ItemsFor(r, page.Results)), "topleft")
                .Mark(2, r => Ui.Button(r, "Details…"), "top"));

            var details = new RebuildDetailsViewModel(page.Results.ToList(), 0);
            var dw = await s.Show(new RebuildDetailsWindow { DataContext = details }, 1184, 820);
            await Ui.Settle(800);
            await s.Shoot(dw, "rebuild-details", new ShotOptions()
                .Mark(1, r => Ui.Find<StatusChip>(r), "left")
                .Mark(2, r => ItemsFor(r, details.Cards), "topleft")
                .Mark(3, r => Ui.Text(r, "Full report"), "right"));

            // The three statuses, as the chips show them
            var chips = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8) };
            foreach (var c in new[] { Tuple.Create("Ready to import", "ok"), Tuple.Create("Check first", "caution"), Tuple.Create("Failed", "critical") })
                chips.Children.Add(new StatusChip { Text = c.Item1, Kind = c.Item2, Margin = new Thickness(0, 0, 12, 0) });
            var holder = new Window { Content = chips };
            await s.Show(holder, 420, 50);
            await Ui.Settle();
            s.ShootElement(chips, "rebuild-statuses");
        }

        static async Task RebuildWithExports(Shots s)
        {
            DemoData d = s.Demo;
            var (w, vm) = await OpenMain(s, d.Pelvis[0], d.Pelvis[1], d.Pelvis[2]);
            foreach (LogItem l in vm.Logs) l.IsChecked = true;
            vm.RebuildCommand.Execute(null);
            var page = (RebuildPageViewModel)vm.CurrentPage;
            await Ui.WaitUntil(() => !page.IsBusy && page.Groups.Count > 0, "the rebuild page");
            Ui.Expander(w, "Advanced").IsExpanded = true;
            page.AddExportFiles(d.PelvisExports);
            await Ui.Settle(600);
            // Advanced, with Use Eclipse exports ticked
            await Ui.ScrollToTop(Ui.Expander(w, "Advanced"));
            await s.Shoot(w, "rebuild-advanced", new ShotOptions { Crop = r => Ui.Up<ScrollViewer>(Ui.Text(r, "1  Fields")), Padding = 0 }
                .Mark(1, r => Ui.Text(r, "Leaf positions"), "left")
                .Mark(2, r => Ui.Text(r, "Where each sample sits between control points"), "left")
                .Mark(3, r => Ui.Text(r, "Gantry angles"), "left")
                .Mark(4, r => Ui.Button(r, "Use Eclipse exports of these beams"), "left")
                .Mark(5, r => Ui.Button(r, "Add exports…"), "left"));
            // The fields, each with its export
            await Ui.ScrollToTop(Ui.Text(w, "1  Fields"));
            await s.Shoot(w, "rebuild-exports", new ShotOptions { Crop = r => Ui.Up<ScrollViewer>(Ui.Text(r, "1  Fields")), Padding = 0 }
                .Mark(1, r => Ui.Text(r, "Eclipse export: ", true), "left")
                .Mark(2, r => Ui.Button(r, "Choose…"), "top"));

            await page.RebuildCommand.ExecuteAsync(null);
            await Ui.WaitUntil(() => !page.IsBusy && page.Results.Count > 0, "the rebuild");
            await s.Shoot(w, "rebuild-results-check-first", new ShotOptions { Crop = r => Ui.Up<DockPanel>(ItemsFor(r, page.Results)), Padding = 0 });

            int check = Math.Max(0, page.Results.ToList().FindIndex(r => r.StatusKind != "ok"));
            var details = new RebuildDetailsViewModel(page.Results.ToList(), check);
            var dw = await s.Show(new RebuildDetailsWindow { DataContext = details }, 1184, 820);
            await Ui.Settle(800);
            await s.Shoot(dw, "rebuild-details-check-first", new ShotOptions()
                .Mark(1, r => Ui.Text(r, "Check before importing"), "left"));
        }

        static async Task Export(Shots s)
        {
            DemoData d = s.Demo;
            var (main, mvm) = await OpenMain(s, d.ProstateFx4, d.HeadNeck);
            var vm = new ExportViewModel(mvm.Logs.ToList()) { Folder = d.Exports };
            var w = await s.Show(new ExportWindow { DataContext = vm }, 764, 720);
            await s.Shoot(w, "export-window", new ShotOptions()
                .Mark(1, r => Ui.Button(r, "v4.0 log (.bin)"), "left")
                .Mark(2, r => Ui.Button(r, "CSV (.csv)"), "left")
                .Mark(3, r => Ui.Button(r, "Full dump as CSV (_full.csv)"), "left")
                .Mark(4, r => Ui.Button(r, "Raw dump as text (.txt)"), "left")
                .Mark(5, r => Ui.Text(r, "Where"), "left")
                .Mark(6, r => Ui.Button(r, "Export"), "top"));

            vm.ToCsv = true;
            await vm.ExportCommand.ExecuteAsync(null);
            await Ui.WaitUntil(() => vm.IsDone, "the export");
            await s.Shoot(w, "export-done", new ShotOptions()
                .Mark(1, r => Ui.Text(r, "Logs"), "left")
                .Mark(2, r => Ui.Button(r, "Open folder"), "top"));
        }

        static async Task HeaderDetails(Shots s)
        {
            var (main, mvm) = await OpenMain(s, s.Demo.HeadNeck);
            LogItem item = mvm.Logs[0];
            var edits = new HeaderEditViewModel(item.Header);
            edits.Details[0].Value = "QA-PHANTOM";
            edits.Fields[0].Value = "G0 (QA)";
            var hw = await s.Show(new HeaderEditWindow { DataContext = edits }, 704, 600);
            await s.Shoot(hw, "header-details", new ShotOptions()
                .Mark(1, r => Ui.Text(r, "In the log"), "left")
                .Mark(2, r => Ui.Text(r, "In the export"), "left")
                .Mark(3, r => Ui.Text(r, "Field 1"), "left")
                .Mark(4, r => Ui.Button(r, "OK"), "top"));
            s.Close(hw);

            var vm = new ExportViewModel(new List<LogItem> { item }) { Folder = s.Demo.Exports, HeaderEdits = edits };
            var w = await s.Show(new ExportWindow { DataContext = vm }, 764, 720);
            await s.Shoot(w, "export-header-button", new ShotOptions { Crop = r => Ui.Up<Border>(Ui.Button(r, "v4.0 log (.bin)")), Padding = 6 }
                .Mark(1, r => Ui.Button(r, "Header details…"), "right"));
        }

        static async Task SettingsAndTitleBar(Shots s)
        {
            var (main, mvm) = await OpenMain(s, s.Demo.ProstateFx4);
            await s.Shoot(main, "title-bar", new ShotOptions { Crop = r => Ui.Up<DockPanel>(Named(main, "HelpButton")), Padding = 30 }
                .Mark(1, r => Named(main, "ThemeButton"), "bottom")
                .Mark(2, r => Named(main, "AnglesBox"), "bottom")
                .Mark(3, r => Named(main, "SettingsButton"), "bottom")
                .Mark(4, r => Named(main, "HelpButton"), "bottom"));

            var vm = new App.ViewModels.SettingsViewModel(mvm) { MlcTolerance = "1", GantryTolerance = "0.5", MuTolerance = "0.5" };
            var w = await s.Show(new SettingsWindow { DataContext = vm }, 544);
            await s.Shoot(w, "settings", new ShotOptions()
                .Mark(1, r => Ui.Text(r, "Theme"), "left")
                .Mark(2, r => Ui.Text(r, "Angles"), "left")
                .Mark(3, r => Ui.Text(r, "Compare tolerances"), "left")
                .Mark(4, r => Ui.Text(r, "Recent logs"), "left")
                .Mark(5, r => Ui.Button(r, "Save"), "top"));
        }

        static async Task Dark(Shots s)
        {
            DemoData d = s.Demo;
            Theme.Apply("Dark");
            try
            {
                await Ui.Settle();
                var (w, vm) = await OpenMain(s, d.ProstateFx3, d.HeadNeck, d.ProstateFx4);
                await Ui.Settle(800);
                await s.Shoot(w, "main-window-dark");
            }
            finally
            {
                s.CloseAll();
                Theme.Apply("Light");
                await Ui.Settle();
            }
        }

        // =========================================================================================
        // Trajectory Log Converter
        // =========================================================================================

        const double ConvW = 984, ConvH = 820;

        static async Task Converter(Shots s)
        {
            DemoData d = s.Demo;
            string settingsFile = Conv.ConverterSettings.FilePath;
            if (File.Exists(settingsFile)) File.Delete(settingsFile);

            // First run: no default folders yet
            var first = await ShowConverter(s);
            await s.Shoot(first.w, "converter-first-run", new ShotOptions()
                .Mark(1, r => Ui.Text(r, first.vm.Message), "left")
                .Mark(2, r => Named(first.w, "SettingsButton"), "left"));
            s.Close(first.w);

            var settings = new Conv.ConverterSettings { InputFolder = d.Logs, OutputFolder = d.LogsV4, IncludeSubfolders = true };
            string error = settings.Save();
            if (error != null) throw new InvalidOperationException(error);
            var sw = await s.Show(new Conv.SettingsWindow { DataContext = new Conv.SettingsViewModel(settings.Copy()) }, 624);
            await s.Shoot(sw, "converter-settings", new ShotOptions()
                .Mark(1, r => Ui.Text(r, "Default input folder (v5.1 logs)"), "left")
                .Mark(2, r => Ui.Button(r, "Include subfolders"), "left")
                .Mark(3, r => Ui.Text(r, "Default output folder (v4.0 logs)"), "left")
                .Mark(4, r => Ui.Button(r, "Save"), "top"));
            s.Close(sw);

            // The day's logs
            var (w, vm) = await ShowConverter(s);
            vm.Filter.FromDate = DemoData.Today;
            vm.Filter.ToDate = DemoData.Today;
            vm.Filter.FromTime = TimeSpan.Zero;
            vm.Filter.ToTime = new TimeSpan(23, 59, 0);
            vm.Filter.UseDateRange = true;
            await WaitForList(vm);
            await s.Shoot(w, "converter-main", new ShotOptions()
                .Mark(1, r => Ui.Find<TextBlock>(r, t => t.IsVisible && t.Text == "From" && t.FontSize > 13), "left")
                .Mark(2, r => Ui.Button(r, "Only logs treated in this period"), "left")
                .Mark(3, r => Ui.Find<TextBlock>(r, t => t.IsVisible && t.Text == "To" && t.FontSize > 13), "left")
                .Mark(4, r => Ui.Text(r, vm.ListSummary), "top")
                .Mark(5, r => Ui.Up<ButtonBase>(Ui.Text(r, vm.ShowSkippedText)), "left")
                .Mark(6, r => Ui.Button(r, vm.ConvertText), "top"));

            vm.ShowSkipped = true;
            await Ui.Settle();
            await s.Shoot(w, "converter-skipped", new ShotOptions { Crop = r => Ui.Find<DataGrid>(r), Padding = 4 });

            // One file
            vm.FolderMode = false;
            vm.SingleFile = d.Pelvis[0];
            await WaitForList(vm);
            await s.Shoot(w, "converter-one-file", new ShotOptions()
                .Mark(1, r => Ui.Button(r, "One file"), "left")
                .Mark(2, r => Ui.Button(r, "Choose file…"), "left")
                .Mark(3, r => Ui.Button(r, vm.ConvertText), "top"));

            // Convert the day's logs
            vm.FolderMode = true;
            await WaitForList(vm);
            vm.ShowSkipped = false;
            await vm.ConvertCommand.ExecuteAsync(null);
            await Ui.WaitUntil(() => vm.IsDone, "the conversion");
            await s.Shoot(w, "converter-done", new ShotOptions()
                .Mark(1, r => Ui.Text(r, vm.Result), "left")
                .Mark(2, r => Ui.Button(r, "Open output folder"), "top"));
        }

        static async Task<(Conv.MainWindow w, Conv.MainViewModel vm)> ShowConverter(Shots s)
        {
            var w = await s.Show(new Conv.MainWindow(), ConvW, ConvH);
            var vm = new Conv.MainViewModel();
            w.DataContext = vm;
            await WaitForList(vm);
            return (w, vm);
        }

        static async Task WaitForList(Conv.MainViewModel vm)
        {
            await Ui.Settle(700);   // the list is refreshed half a second after a folder is typed
            await Ui.WaitUntil(() => !vm.IsSearching, "the converter's list");
        }
    }
}
