// SharedSettings.cs - settings for everyone using this copy of the app, in
// TrajectoryLogTools.settings.json beside the app (personal choices such as the theme stay in
// AppSettings, per user):
//   {
//     "LogFolder": "",            the log folder; empty = the Logs folder beside the app
//     "LogRetentionDays": 30      logs older than this are deleted when the app starts
//   }
// The file is written by Settings > Save, so whoever changes it needs write access to the app's
// folder. It is not part of the deployed files, so redeploying keeps it.

using System;
using System.IO;
using System.Text.Json;
using TrajectoryLogTools.Core;

namespace TrajectoryLogTools.App.Services
{
    public sealed class SharedSettings
    {
        public string LogFolder { get; set; } = "";
        public int LogRetentionDays { get; set; } = AppLog.DefaultRetentionDays;

        public const string FileName = "TrajectoryLogTools.settings.json";
        public static string FilePath { get { return Path.Combine(AppContext.BaseDirectory, FileName); } }

        public static SharedSettings Current { get; private set; } = Load();

        static SharedSettings Load()
        {
            try
            {
                if (File.Exists(FilePath)) return JsonSerializer.Deserialize<SharedSettings>(File.ReadAllText(FilePath)) ?? new SharedSettings();
            }
            catch (Exception) { }   // unreadable: the defaults
            return new SharedSettings();
        }

        /// <summary>Saves beside the app; returns an error message (e.g. the folder is read-only), or null.</summary>
        public string Save()
        {
            try
            {
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
                return null;
            }
            catch (Exception ex)
            {
                return "The settings for everyone (log folder, how long logs are kept) could not be saved to " + FilePath + ": " + ex.Message +
                       ". The app's folder must be writable for whoever changes them.";
            }
        }
    }
}
