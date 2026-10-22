// SettingsViewModel.cs - the default folders, saved in TrajectoryLogConverter.settings.json beside the app.

using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using TrajectoryLogTools.Wpf;

namespace TrajectoryLogTools.Converter
{
    public partial class SettingsViewModel : ObservableObject
    {
        public ConverterSettings Settings { get; }

        [ObservableProperty] private string _inputFolder;
        [ObservableProperty] private string _outputFolder;
        [ObservableProperty] private bool _includeSubfolders;

        public string FilePath { get { return ConverterSettings.FilePath; } }

        public SettingsViewModel(ConverterSettings settings)
        {
            Settings = settings;
            _inputFolder = settings.InputFolder ?? "";
            _outputFolder = settings.OutputFolder ?? "";
            _includeSubfolders = settings.IncludeSubfolders;
        }

        [RelayCommand]
        void BrowseInput()
        {
            string p = Dialogs.PickFolder("Default folder with the v5.1 logs", InputFolder);
            if (p != null) InputFolder = p;
        }

        [RelayCommand]
        void BrowseOutput()
        {
            string p = Dialogs.PickFolder("Default folder for the v4.0 logs", OutputFolder);
            if (p != null) OutputFolder = p;
        }

        /// <summary>Checks and saves; returns false (after a message) when it can't.</summary>
        public bool Save()
        {
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
            string error = Settings.Save();
            if (error != null) { Dialogs.Warn(error); return false; }
            return true;
        }
    }
}
