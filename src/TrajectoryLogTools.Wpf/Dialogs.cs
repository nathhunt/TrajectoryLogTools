// Dialogs.cs - file / folder pickers and message boxes, in one place.

using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace TrajectoryLogTools.Wpf
{
    public static class Dialogs
    {
        /// <summary>The title of message boxes: the app's name (each app sets its own at start-up).</summary>
        public static string Title { get; set; } = "Trajectory Log Tools";
        public const string LogFilter = "Trajectory logs (*.bin)|*.bin|Log CSV exports (*.csv)|*.csv|All files (*.*)|*.*";
        /// <summary>A log, or a DICOM RT Plan to compare a log with.</summary>
        public const string LogOrPlanFilter = "Trajectory logs and RT Plans (*.bin;*.csv;*.dcm)|*.bin;*.csv;*.dcm|Trajectory logs (*.bin)|*.bin|DICOM RT Plans (*.dcm)|*.dcm|Log CSV exports (*.csv)|*.csv|All files (*.*)|*.*";
        public const string BinFilter = "Trajectory logs (*.bin)|*.bin|All files (*.*)|*.*";
        public const string EclipseFilter = "Eclipse MLC exports (*.csv)|*.csv|All files (*.*)|*.*";

        public static string OpenFile(string title, string filter, string current)
        {
            var dlg = new OpenFileDialog { Title = title, Filter = filter };
            string start = StartFolder(current);
            if (start != null) dlg.InitialDirectory = start;
            return dlg.ShowDialog(Application.Current.MainWindow) == true ? dlg.FileName : null;
        }

        public static string[] OpenFiles(string title, string filter, string current)
        {
            var dlg = new OpenFileDialog { Title = title, Filter = filter, Multiselect = true };
            string start = StartFolder(current);
            if (start != null) dlg.InitialDirectory = start;
            return dlg.ShowDialog(Application.Current.MainWindow) == true ? dlg.FileNames : new string[0];
        }

        public static string PickFolder(string title, string current)
        {
            var dlg = new OpenFolderDialog { Title = title };
            string start = StartFolder(current);
            if (start != null) dlg.InitialDirectory = start;
            return dlg.ShowDialog(Application.Current.MainWindow) == true ? dlg.FolderName : null;
        }

        /// <summary>Shows a dialog window over the main window.</summary>
        public static bool? ShowDialog(Window w)
        {
            Window active = null;
            foreach (Window x in Application.Current.Windows) if (x.IsActive && x != w) active = x;
            w.Owner = active ?? Application.Current.MainWindow;
            w.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            return w.ShowDialog();
        }

        public static void Warn(string message)
        {
            MessageBox.Show(Application.Current.MainWindow, message, Title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public static void Error(string message)
        {
            MessageBox.Show(Application.Current.MainWindow, message, Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public static void Info(string message)
        {
            MessageBox.Show(Application.Current.MainWindow, message, Title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public static bool Confirm(string message)
        {
            return MessageBox.Show(Application.Current.MainWindow, message, Title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        public static void OpenFolder(string path)
        {
            if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", "\"" + path + "\"") { UseShellExecute = true });
            else Info("The folder does not exist yet:\n" + path);
        }

        /// <summary>Folder to start a dialog in: the path itself, or its folder; null if neither exists.</summary>
        public static string StartFolder(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                path = path.Trim().Trim('"');
                if (Directory.Exists(path)) return path;
                string dir = Path.GetDirectoryName(path);
                return !string.IsNullOrEmpty(dir) && Directory.Exists(dir) ? dir : null;
            }
            catch (ArgumentException) { return null; }
        }
    }
}
