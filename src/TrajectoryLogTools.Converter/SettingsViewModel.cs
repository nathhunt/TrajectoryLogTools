// SettingsViewModel.cs - the default folders and the filter and list the window opens with, saved in
// TrajectoryLogConverter.settings.json beside the app.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrajectoryLogTools.Core;
using TrajectoryLogTools.Wpf;

namespace TrajectoryLogTools.Converter
{
    public partial class SettingsViewModel : ObservableObject
    {
        public ConverterSettings Settings { get; }

        [ObservableProperty] private string _inputFolder;
        [ObservableProperty] private string _outputFolder;
        [ObservableProperty] private bool _includeSubfolders;
        [ObservableProperty] private bool _leaveOutImaging;
        [ObservableProperty] private bool _usePeriod;
        [ObservableProperty] private int _periodDays;
        [ObservableProperty] private TimeSpan _periodFrom;
        [ObservableProperty] private TimeSpan _periodTo;
        [ObservableProperty] private bool _useTimeOfDay;
        [ObservableProperty] private TimeSpan _dayFrom;
        [ObservableProperty] private TimeSpan _dayTo;
        [ObservableProperty] private bool _overwrite;
        [ObservableProperty] private bool _showSkipped;
        [ObservableProperty] private string _logFolder;
        [ObservableProperty] private int _logRetentionDays;
        public IReadOnlyList<KeyValuePair<int, string>> RetentionChoices { get { return AppLog.RetentionChoices; } }
        public string LogWhere { get { return AppLog.WhereText; } }
        public string DefaultLogFolder { get { return AppLog.DefaultFolder; } }

        /// <summary>The periods offered: the last N days, today included.</summary>
        public IReadOnlyList<KeyValuePair<int, string>> PeriodChoices { get; } = new[]
        {
            new KeyValuePair<int, string>(1, "Today"),
            new KeyValuePair<int, string>(2, "Today and yesterday"),
            new KeyValuePair<int, string>(3, "The last 3 days"),
            new KeyValuePair<int, string>(7, "The last 7 days"),
            new KeyValuePair<int, string>(14, "The last 14 days"),
            new KeyValuePair<int, string>(30, "The last 30 days"),
        };

        public string FilePath { get { return ConverterSettings.FilePath; } }

        /// <summary>Every setting here applies to everyone using this copy of the app: locked until Unlock is pressed.</summary>
        public SystemSettingsLock Lock { get; } = new SystemSettingsLock("the converter's settings");

        public SettingsViewModel(ConverterSettings settings)
        {
            Settings = settings;
            _inputFolder = settings.InputFolder ?? "";
            _outputFolder = settings.OutputFolder ?? "";
            _includeSubfolders = settings.IncludeSubfolders;
            _leaveOutImaging = settings.LeaveOutImaging;
            _usePeriod = settings.UsePeriod;
            _periodDays = PeriodChoices.Any(c => c.Key == settings.PeriodDays) ? settings.PeriodDays : 1;
            _periodFrom = ConverterSettings.Time(settings.PeriodFrom, TimeSpan.Zero);
            _periodTo = ConverterSettings.Time(settings.PeriodTo, new TimeSpan(23, 59, 0));
            _useTimeOfDay = settings.UseTimeOfDay;
            _dayFrom = ConverterSettings.Time(settings.DayFrom, new TimeSpan(8, 0, 0));
            _dayTo = ConverterSettings.Time(settings.DayTo, new TimeSpan(17, 0, 0));
            _overwrite = settings.Overwrite;
            _showSkipped = settings.ShowSkipped;
            _logFolder = settings.LogFolder ?? "";
            _logRetentionDays = AppLog.RetentionChoices.Any(c => c.Key == settings.LogRetentionDays) ? settings.LogRetentionDays : AppLog.DefaultRetentionDays;
        }

        [RelayCommand]
        void BrowseInput()
        {
            string p = Dialogs.PickFolder("Default folder with the v5.1 logs", InputFolder);
            if (p != null) InputFolder = p;
        }

        [RelayCommand]
        void BrowseLogFolder()
        {
            string p = Dialogs.PickFolder("Folder for the logs (empty = the Logs folder beside the app)", AppLog.Resolve(LogFolder));
            if (p != null) LogFolder = p;
        }

        [RelayCommand] void OpenLogFolder() { Dialogs.OpenFolder(AppLog.Folder ?? AppLog.Resolve(LogFolder)); }

        [RelayCommand]
        void BrowseOutput()
        {
            string p = Dialogs.PickFolder("Default folder for the v4.0 logs", OutputFolder);
            if (p != null) OutputFolder = p;
        }

        /// <summary>Checks and saves; returns false (after a message) when it can't.</summary>
        public bool Save()
        {
            if (!Lock.Unlocked) return false;   // nothing can have changed while locked
            if (UsePeriod && PeriodDays == 1 && PeriodTo < PeriodFrom)
            {
                Dialogs.Warn("The period ends before it starts: with Today, the end time must be after the start time.");
                return false;
            }
            string input = (InputFolder ?? "").Trim().Trim('"'), output = (OutputFolder ?? "").Trim().Trim('"');
            if (input.Length > 0 && output.Length > 0 && Core.LogFinder.PathEquals(Path.GetFullPath(input), Path.GetFullPath(output)))
            {
                Dialogs.Warn("The output folder must not be the input folder: the v4.0 files would replace the v5.1 logs.");
                return false;
            }
            if (input.Length > 0 && !Directory.Exists(input) &&
                !Dialogs.Confirm("The input folder was not found:\n" + input + "\n\nSave it anyway?")) return false;
            Settings.InputFolder = input;
            Settings.OutputFolder = output;
            Settings.IncludeSubfolders = IncludeSubfolders;
            Settings.LeaveOutImaging = LeaveOutImaging;
            Settings.UsePeriod = UsePeriod;
            Settings.PeriodDays = PeriodDays;
            Settings.PeriodFrom = ConverterSettings.Text(PeriodFrom);
            Settings.PeriodTo = ConverterSettings.Text(PeriodTo);
            Settings.UseTimeOfDay = UseTimeOfDay;
            Settings.DayFrom = ConverterSettings.Text(DayFrom);
            Settings.DayTo = ConverterSettings.Text(DayTo);
            Settings.Overwrite = Overwrite;
            Settings.ShowSkipped = ShowSkipped;
            bool logChanged = !string.Equals((Settings.LogFolder ?? "").Trim(), (LogFolder ?? "").Trim(), System.StringComparison.OrdinalIgnoreCase)
                              || Settings.LogRetentionDays != LogRetentionDays;
            Settings.LogFolder = (LogFolder ?? "").Trim().Trim('"');
            Settings.LogRetentionDays = LogRetentionDays;
            string error = Settings.Save();
            if (error != null) { Dialogs.Warn(error); return false; }
            if (logChanged) AppLog.Reconfigure(Settings.LogFolder, Settings.LogRetentionDays);
            AppLog.Info("Settings saved to " + ConverterSettings.FilePath + Environment.NewLine +
                        System.Text.Json.JsonSerializer.Serialize(Settings, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
            return true;
        }
    }
}
