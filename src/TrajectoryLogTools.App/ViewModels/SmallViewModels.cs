// SmallViewModels.cs - the start page, Add from folder and Settings.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrajectoryLogTools.App.Services;
using TrajectoryLogTools.Core;

namespace TrajectoryLogTools.App.ViewModels
{
    /// <summary>Shown while no log is open.</summary>
    public sealed class StartPageViewModel
    {
        public MainViewModel Main { get; }
        public StartPageViewModel(MainViewModel main) { Main = main; }
    }

    /// <summary>Adds the logs found in a folder (optionally with subfolders and a treatment date range).</summary>
    public partial class AddFromFolderViewModel : ObservableObject
    {
        [ObservableProperty] private string _folder = "";
        [ObservableProperty] private bool _includeSubfolders = true;
        [ObservableProperty] private bool _useDateRange;
        [ObservableProperty] private DateTime? _fromDate = DateTime.Today.AddDays(-7);
        [ObservableProperty] private DateTime? _toDate = DateTime.Today;
        // The period's start and end times (with FromDate / ToDate): the whole of both days by default.
        [ObservableProperty] private TimeSpan _fromTime = TimeSpan.Zero;
        [ObservableProperty] private TimeSpan _toTime = new TimeSpan(23, 59, 0);
        // Time of day, on every day (within the period when that is on too).
        [ObservableProperty] private bool _useTimeOfDay;
        [ObservableProperty] private TimeSpan _dayFromTime = new TimeSpan(8, 0, 0);
        [ObservableProperty] private TimeSpan _dayToTime = new TimeSpan(17, 0, 0);
        [ObservableProperty] private string _result = "";

        public List<LogFile> Found { get; private set; } = new List<LogFile>();

        public AddFromFolderViewModel(string folder)
        {
            if (folder != null && Directory.Exists(folder)) { _folder = folder; Search(); }
        }

        partial void OnFolderChanged(string value) { Search(); }
        partial void OnIncludeSubfoldersChanged(bool value) { Search(); }
        partial void OnUseDateRangeChanged(bool value) { Search(); }
        partial void OnFromDateChanged(DateTime? value) { Search(); }
        partial void OnToDateChanged(DateTime? value) { Search(); }
        partial void OnUseTimeOfDayChanged(bool value) { Search(); }
        partial void OnFromTimeChanged(TimeSpan value) { Search(); }
        partial void OnToTimeChanged(TimeSpan value) { Search(); }
        partial void OnDayFromTimeChanged(TimeSpan value) { Search(); }
        partial void OnDayToTimeChanged(TimeSpan value) { Search(); }

        [RelayCommand]
        void Browse()
        {
            string p = Dialogs.PickFolder("Folder with trajectory logs", Folder);
            if (p != null) Folder = p;
        }

        void Search()
        {
            Found = new List<LogFile>();
            string f = (Folder ?? "").Trim().Trim('"');
            if (f.Length == 0) { Result = ""; return; }
            if (!Directory.Exists(f)) { Result = "Folder not found."; return; }
            // Period: from the start date and time to the end date and time (to the end of that minute).
            TimeSpan p0 = FromTime, p1 = ToTime, t0 = DayFromTime, t1 = DayToTime;
            DateTime from = (FromDate ?? DateTime.Today).Date + p0;
            DateTime to = (ToDate ?? DateTime.Today).Date + p1 + TimeSpan.FromMinutes(1) - TimeSpan.FromTicks(1);
            if (UseDateRange && to < from)
            {
                Result = "The period ends before it starts.";
                Found = new List<LogFile>();
                OnPropertyChanged(nameof(CanAdd));
                return;
            }
            try
            {
                Found = LogFinder.Find(new FindSettings
                {
                    FolderMode = true,
                    Input = f,
                    IncludeSubfolders = IncludeSubfolders,
                    UseDateRange = UseDateRange,
                    From = from,
                    To = to,
                    UseTimeOfDay = UseTimeOfDay,
                    TimeFrom = t0,
                    TimeTo = t1.Add(TimeSpan.FromMinutes(1)).Subtract(TimeSpan.FromTicks(1)),   // to the end of that minute
                });
                Result = Found.Count == 0 ? "No logs found." : Found.Count + (Found.Count == 1 ? " log found." : " logs found.");
                if (Found.Count > 200) Result += " That's a lot to open at once; consider a date range.";
            }
            catch (Exception ex) { Result = ex.Message; }
            OnPropertyChanged(nameof(CanAdd));
        }

        public bool CanAdd { get { return Found.Count > 0; } }
    }

    public partial class SettingsViewModel : ObservableObject
    {
        readonly MainViewModel _main;
        public string[] Themes { get; } = { "System", "Light", "Dark" };

        [ObservableProperty] private string _theme;
        /// <summary>0 IEC 61217, 1 Varian Standard.</summary>
        [ObservableProperty] private int _angleIndex;
        public string[] AngleChoices { get; } = { "IEC 61217 (default)", "Varian Standard" };
        [ObservableProperty] private string _mlcTolerance;
        [ObservableProperty] private string _gantryTolerance;
        [ObservableProperty] private string _muTolerance;

        public SettingsViewModel(MainViewModel main)
        {
            _main = main;
            var s = AppSettings.Current;
            _theme = s.Theme;
            _angleIndex = Display.Angles == TrajectoryLogTools.Analysis.AngleScale.Varian ? 1 : 0;
            _mlcTolerance = Text(s.MlcToleranceMm);
            _gantryTolerance = Text(s.GantryToleranceDeg);
            _muTolerance = Text(s.MuTolerance);
        }

        static string Text(double? v) { return v.HasValue ? v.Value.ToString(CultureInfo.InvariantCulture) : ""; }

        static double? Parse(string s)
        {
            double v;
            return double.TryParse((s ?? "").Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v > 0 ? v : (double?)null;
        }


        /// <summary>Saves; returns false (with a message) if a tolerance isn't a number.</summary>
        public bool Save()
        {
            foreach (string t in new[] { MlcTolerance, GantryTolerance, MuTolerance })
                if (!string.IsNullOrWhiteSpace(t) && Parse(t) == null)
                {
                    Dialogs.Warn("Tolerances must be numbers greater than 0, or empty for none.");
                    return false;
                }
            var s = AppSettings.Current;
            if (s.Theme != Theme) s.ThemeChosen = true;
            s.Theme = Theme;
            Display.SetAngles(AngleIndex == 1 ? TrajectoryLogTools.Analysis.AngleScale.Varian : TrajectoryLogTools.Analysis.AngleScale.Iec);
            s.MlcToleranceMm = Parse(MlcTolerance);
            s.GantryToleranceDeg = Parse(GantryTolerance);
            s.MuTolerance = Parse(MuTolerance);
            s.Save();
            return true;
        }

        [RelayCommand]
        void ClearRecent() { _main.ClearRecent(); }
    }
}
