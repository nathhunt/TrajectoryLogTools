// AppSettings.cs - per-user settings and recent files, saved as JSON in
// %AppData%\TrajectoryLogTools\settings.json. No folder is remembered as a default: logs can be
// anywhere, so only recently opened FILES are kept (file dialogs open where Windows last browsed).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace TrajectoryLogTools.App.Services
{
    public sealed class AppSettings
    {
        public const int MaxRecent = 12;

        /// <summary>"System", "Light" or "Dark".</summary>
        public string Theme { get; set; } = "System";

        /// <summary>Compare tolerances; null = show the numbers without a within / over marker.</summary>
        public double? MlcToleranceMm { get; set; }
        public double? GantryToleranceDeg { get; set; }
        public double? MuTolerance { get; set; }

        public List<string> RecentFiles { get; set; } = new List<string>();

        static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrajectoryLogTools", "settings.json"); }
        }

        public static AppSettings Current { get; private set; } = Load();

        static AppSettings Load()
        {
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
            }
            catch (Exception) { }   // unreadable settings: start fresh
            return new AppSettings();
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception) { }   // settings are a convenience; never fail because of them
        }

        public void AddRecent(string path)
        {
            string full = Path.GetFullPath(path);
            RecentFiles.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
            RecentFiles.Insert(0, full);
            if (RecentFiles.Count > MaxRecent) RecentFiles.RemoveRange(MaxRecent, RecentFiles.Count - MaxRecent);
            Save();
        }
    }
}
