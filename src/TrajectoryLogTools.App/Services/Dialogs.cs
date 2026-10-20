// Dialogs.cs - file / folder pickers and message boxes, in one place.

using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace TrajectoryLogTools.App.Services
{
    public static class Dialogs
    {
        public const string Title = "Trajectory Log Tools";
        public const string LogFilter = "Trajectory logs (*.bin)|*.bin|Log CSV exports (*.csv)|*.csv|All files (*.*)|*.*";
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
