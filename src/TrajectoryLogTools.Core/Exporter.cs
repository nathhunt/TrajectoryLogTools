// Exporter.cs - Export, as the app's Export window and the CLI's export command do it: one log
// to any of v4.0 .bin, CSV, full dump CSV and raw dump text, with the same options and defaults
// in both. The work is done by Commands (RunToV4, RunToCsv, RunDump).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TrajectoryLogTools.Core
{
    /// <summary>What to write and how. The defaults are the app's.</summary>
    public sealed class ExportOptions
    {
        /// <summary>Subfolder next to each log used when no output folder is given.</summary>
        public const string ConvertedFolder = "converted";

        public bool ToV4 = true, ToCsv, FullCsv, Dump;
        // v4.0
        /// <summary>0 TrueBeam (default), 1 Halcyon / Ethos.</summary>
        public int Machine;
        /// <summary>Serial number: removed (default), kept, or replaced by NewSerial when set.</summary>
        public bool KeepSerial;
        public string NewSerial;
        /// <summary>Couch rotation, pitch and roll are removed unless this is set.</summary>
        public bool KeepCouch;
        public bool TimeCsv;
        /// <summary>Header details (one log at a time): metadata key -> value, subbeam number -> name.</summary>
        public List<KeyValuePair<string, string>> MetaEdits = new List<KeyValuePair<string, string>>();
        public Dictionary<int, string> SubbeamNames = new Dictionary<int, string>();
        // CSV
        public bool NoMlc, ActualOnly;
        // both
        public bool Overwrite, IgnoreCrc;

        public bool HasHeaderEdits { get { return MetaEdits.Count > 0 || SubbeamNames.Count > 0; } }

        /// <summary>Checks the choices; returns an error message, or null.</summary>
        public string Validate()
        {
            if (!ToV4 && !ToCsv && !FullCsv && !Dump) return "Choose at least one thing to write.";
            if (ToV4 && NewSerial != null && (NewSerial.Length > 6 || NewSerial.Any(c => c < 32 || c > 126)))
                return "The serial number must be up to 6 letters, digits or other plain (ASCII) characters.";
            return null;
        }
    }

    public enum ExportOutcome { Done, Skipped, Failed }

    /// <summary>What one log's export printed, for the run log.</summary>
    public sealed class ExportRecord
    {
        public string Log;
        public string OutputFolder;
        public List<string> Lines = new List<string>();
    }

    public static class Exporter
    {
        /// <summary>The output folder for a log: <paramref name="folder"/>, or the "converted" subfolder next to the log.</summary>
        public static string OutputFolder(string log, string folder)
        {
            return folder ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(log)), ExportOptions.ConvertedFolder);
        }

        /// <summary>
        /// Writes the run log (optional, off by default in the app and the CLI): one
        /// TrajectoryLogConversion_&lt;yyyyMMdd_HHmmss&gt;.log in each output folder, listing the logs
        /// exported to it and what was written. Returns the files written; a folder that can't be
        /// written to is reported in <paramref name="errors"/>.
        /// </summary>
        public static List<string> WriteRunLogs(IEnumerable<ExportRecord> records, DateTime started, List<string> errors)
        {
            var written = new List<string>();
            foreach (var g in records.GroupBy(r => Path.GetFullPath(r.OutputFolder), StringComparer.OrdinalIgnoreCase))
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine(Fmt.DateTimeSeconds(started) + "  Export of " + g.Count() + " log(s)");
                foreach (ExportRecord r in g)
                {
                    sb.AppendLine(r.Log);
                    foreach (string l in r.Lines) sb.AppendLine("  " + l);
                }
                string file = Path.Combine(g.Key, "TrajectoryLogConversion_" + started.ToString("yyyyMMdd_HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".log");
                try
                {
                    Directory.CreateDirectory(g.Key);
                    File.WriteAllText(file, sb.ToString(), System.Text.Encoding.UTF8);
                    written.Add(file);
                }
                catch (Exception ex) { errors.Add(g.Key + ": " + ex.Message); }
            }
            return written;
        }

        /// <summary>
        /// Exports one log. The lines are as the CLI prints them (OK / SKIP / FAIL, with notes);
        /// the outcome is Done when everything asked for was written, Skipped when there was nothing
        /// to do (e.g. v4.0 asked for an already-v4.0 log), Failed when anything failed.
        /// </summary>
        public static ExportOutcome Run(string path, string outDir, ExportOptions j, List<string> lines)
        {
            bool anyOk = false, anyFail = false;
            Action<Func<List<string>>> run = f =>
            {
                try { lines.AddRange(f()); anyOk = true; }
                catch (SkipException ex) { lines.Add("SKIP " + Path.GetFileName(path) + ": " + ex.Message); }
                catch (Exception ex) { lines.Add("FAIL " + Path.GetFileName(path) + ": " + ex.Message); anyFail = true; }
            };
            if (string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase))
            {
                lines.Add("SKIP " + Path.GetFileName(path) + ": a CSV export can't be converted (use the .bin log)");
                return ExportOutcome.Skipped;
            }
            Func<Command, Options> opt = c => new Options { Command = c, Output = outDir, Overwrite = j.Overwrite, IgnoreCrc = j.IgnoreCrc };
            if (j.ToV4)
            {
                Options o = opt(Command.ToV4);
                o.MachineSpecifier = j.Machine;
                o.KeepSerial = j.KeepSerial;
                o.Serial = j.NewSerial;
                o.DropCouchRotations = !j.KeepCouch;
                o.TimeCsv = j.TimeCsv;
                o.MetaEdits = j.MetaEdits;
                o.SubbeamNames = j.SubbeamNames;
                run(() => Commands.RunToV4(path, true, o));
            }
            if (j.ToCsv)
            {
                Options o = opt(Command.ToCsv);
                o.NoMlc = j.NoMlc;
                o.ActualOnly = j.ActualOnly;
                run(() => Commands.RunToCsv(path, o));
            }
            if (j.FullCsv) { Options o = opt(Command.ToCsv); o.Full = true; run(() => Commands.RunToCsv(path, o)); }
            if (j.Dump) run(() => Commands.RunDump(path, opt(Command.Dump)));
            return anyFail ? ExportOutcome.Failed : anyOk ? ExportOutcome.Done : ExportOutcome.Skipped;
        }
    }
}
