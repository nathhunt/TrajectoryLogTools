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

        /// <summary>"System", "Light" or "Dark". Light unless chosen in Settings or with the theme button.</summary>
        public string Theme { get; set; } = "Light";

        /// <summary>
        /// True once the theme was chosen (Settings or the theme button). Settings saved by earlier
        /// versions hold "System" without having been chosen; those start in Light.
        /// </summary>
        public bool ThemeChosen { get; set; }

        /// <summary>Angles shown in "IEC" (IEC 61217, the default) or "Varian" (Varian Standard).</summary>
        public string Angles { get; set; } = "IEC";

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
                {
                    AppSettings s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings();
                    if (!s.ThemeChosen) s.Theme = "Light";
                    return s;
                }
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
