// ConverterSettings.cs - the converter's defaults, in TrajectoryLogConverter.settings.json in the
// same folder as the app (so every user of a deployed folder gets the same folders):
//   {
//     "InputFolder":  "\\\\server\\logs\\Halcyon",     where the v5.1 logs are
//     "OutputFolder": "\\\\server\\logs\\Halcyon v4",  where the v4.0 logs go
//     "IncludeSubfolders": false
//   }
// The file is written by Settings > Save. It is not part of the deployed files, so redeploying the
// app keeps it.

using System;
using System.IO;
using System.Text.Json;

namespace TrajectoryLogTools.Converter
{
    public sealed class ConverterSettings
    {
        public string InputFolder { get; set; } = "";
        public string OutputFolder { get; set; } = "";
        public bool IncludeSubfolders { get; set; }

        public const string FileName = "TrajectoryLogConverter.settings.json";

        /// <summary>The settings file: beside the app.</summary>
        public static string FilePath { get { return Path.Combine(AppContext.BaseDirectory, FileName); } }

        /// <summary>The saved settings; defaults when there is no file. <paramref name="error"/> says why a file couldn't be read.</summary>
        public static ConverterSettings Load(out string error)
        {
            error = null;
            try
            {
                if (File.Exists(FilePath))
                    return JsonSerializer.Deserialize<ConverterSettings>(File.ReadAllText(FilePath)) ?? new ConverterSettings();
            }
            catch (Exception ex) { error = "The settings file could not be read (" + ex.Message + "): " + FilePath; }
            return new ConverterSettings();
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
                return "The settings could not be saved to " + FilePath + ": " + ex.Message +
                       "\n\nThe settings file lives beside the app, so the app's folder must be writable for whoever changes the settings.";
            }
        }

        public ConverterSettings Copy() { return (ConverterSettings)MemberwiseClone(); }
    }
}
