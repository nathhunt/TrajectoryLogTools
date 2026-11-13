// ConverterSettings.cs - the converter's defaults, in TrajectoryLogConverter.settings.json in the
// same folder as the app (so every user of a deployed folder gets the same folders):
//   {
//     "InputFolder":  "\\\\server\\logs\\Halcyon",     where the v5.1 logs are
//     "OutputFolder": "\\\\server\\logs\\Halcyon v4",  where the v4.0 logs go
//     "IncludeSubfolders": false,
//     "LeaveOutImaging": true,                         leave out kV imaging (CBCT) logs
//     "UsePeriod": true,                               the filter the window opens with: the period,
//     "PeriodDays": 1,                                 the last N days (1 = today),
//     "PeriodFrom": "00:00", "PeriodTo": "23:59",      from this time on the first day to this time today,
//     "UseTimeOfDay": false,                           and / or times of day on every day
//     "DayFrom": "08:00", "DayTo": "17:00",
//     "Overwrite": false,                              replace logs already in the output folder
//     "ShowSkipped": false                             show the logs that won't be converted
//   }
// A file from an earlier version, without some of these, gets their defaults.
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
        /// <summary>Leave kV imaging (CBCT) logs out of the list. On by default (also when an older file doesn't have it).</summary>
        public bool LeaveOutImaging { get; set; } = true;

        // ---- the filter and list the window opens with ----
        /// <summary>Only logs treated in the period: on by default.</summary>
        public bool UsePeriod { get; set; } = true;
        /// <summary>The period is the last N days, today included: 1 = today.</summary>
        public int PeriodDays { get; set; } = 1;
        /// <summary>Start time on the first day and end time today, "HH:mm".</summary>
        public string PeriodFrom { get; set; } = "00:00";
        public string PeriodTo { get; set; } = "23:59";
        public bool UseTimeOfDay { get; set; }
        public string DayFrom { get; set; } = "08:00";
        public string DayTo { get; set; } = "17:00";
        public bool Overwrite { get; set; }
        public bool ShowSkipped { get; set; }

        /// <summary>The period's first day (today minus PeriodDays - 1).</summary>
        public DateTime PeriodStartDate(DateTime today) { return today.Date.AddDays(-(Math.Max(1, PeriodDays) - 1)); }

        /// <summary>"HH:mm" as a time of day; <paramref name="fallback"/> when it isn't one.</summary>
        public static TimeSpan Time(string text, TimeSpan fallback)
        {
            TimeSpan t;
            if (TimeSpan.TryParseExact((text ?? "").Trim(), new[] { @"h\:mm", @"hh\:mm" }, System.Globalization.CultureInfo.InvariantCulture, out t)
                && t >= TimeSpan.Zero && t < TimeSpan.FromDays(1)) return t;
            return fallback;
        }

        public static string Text(TimeSpan t) { return t.ToString(@"hh\:mm"); }

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
