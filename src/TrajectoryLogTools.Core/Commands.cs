// Commands.cs - the per-file commands shared by the command-line tool and the GUI.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TrajectoryLogTools.Core
{
    /// <summary>
    /// The per-file commands. The command-line tool and the GUI both call these, so they always
    /// produce identical output. Each returns the lines to show the user; the first starts with
    /// "OK", and a <see cref="SkipException"/> means the file needs no work.
    /// </summary>
    public static class Commands
    {
        /// <summary>The .bin files of a file or folder (top level only), sorted by name.</summary>
        public static bool TryListInputs(string path, out List<string> inputs, out bool folderInput, out string error)
        {
            inputs = new List<string>();
            error = null;
            folderInput = Directory.Exists(path);
            if (folderInput)
                inputs.AddRange(Directory.GetFiles(path, "*.bin").OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            else if (File.Exists(path))
                inputs.Add(path);
            else
            {
                error = "Input not found: " + path;
                return false;
            }
            if (inputs.Count == 0) { error = "No .bin files found in " + path; return false; }
            return true;
        }

        static string OutputFile(string input, bool folderInput, Options opt, string defaultSubfolder)
        {
            string name = Path.GetFileName(input);
            string output;
            if (opt.Output == null)
                output = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(input)), defaultSubfolder, name);
            else if (folderInput || Directory.Exists(opt.Output) || opt.Output.EndsWith(Path.DirectorySeparatorChar.ToString()))
                output = Path.Combine(opt.Output, name);
            else
                output = opt.Output;

            if (string.Equals(Path.GetFullPath(output), Path.GetFullPath(input), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Output would overwrite the input; choose a different folder.");
            if (File.Exists(output) && !opt.Overwrite)
                throw new IOException("Output exists (use --overwrite): " + output);
            return output;
        }

        public static List<string> RunToV4(string input, bool folderInput, Options opt)
        {
            string name = Path.GetFileName(input);
            string output = OutputFile(input, folderInput, opt, "v4.0");

            var log = TrajectoryLog.Parse(File.ReadAllBytes(input), opt.IgnoreCrc);
            if (log.Version == "4.0") throw new SkipException("already version 4.0");

            var notes = new List<string>();
            byte[] converted = V4Converter.Convert(log, opt, notes);
            FileUtil.WriteAtomic(output, converted);

            if (opt.TimeCsv && log.TimeAxisIndex >= 0)
            {
                string csvPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(output)),
                                              Path.GetFileNameWithoutExtension(output) + "_time.csv");
                // Expected / Actual hold the exact float values, so to-v5 --time-from can restore them.
                var sb = new StringBuilder("Snapshot,SecondsSinceMidnight,Clock,Expected,Actual\r\n");
                for (int s = 0; s < log.NumSnapshots; s++)
                {
                    float t = log.Expected(s, log.TimeAxisIndex, 0);
                    sb.Append(s + 1).Append(',').Append(Fmt.Fixed(t, 3)).Append(',').Append(Fmt.Clock(t))
                      .Append(',').Append(Fmt.Exact(t)).Append(',').Append(Fmt.Exact(log.Actual(s, log.TimeAxisIndex, 0)))
                      .Append("\r\n");
                }
                FileUtil.WriteAtomic(csvPath, Encoding.UTF8.GetBytes(sb.ToString()));
                notes.Add("Time axis saved to " + csvPath);
            }

            notes.Insert(0, "OK   " + name + " -> " + output);
            return notes.Select((n, i) => i == 0 ? n : "     " + n).ToList();
        }

        public static List<string> RunToV5(string input, bool folderInput, Options opt)
        {
            string name = Path.GetFileName(input);
            string output = OutputFile(input, folderInput, opt, "v5.1");

            var log = TrajectoryLog.Parse(File.ReadAllBytes(input), opt.IgnoreCrc);
            if (log.Version == "5.1") throw new SkipException("already version 5.1");

            var notes = new List<string>();
            byte[] converted = V5Converter.Convert(log, opt, input, notes);
            FileUtil.WriteAtomic(output, converted);

            notes.Insert(0, "OK   " + name + " -> " + output);
            return notes.Select((n, i) => i == 0 ? n : "     " + n).ToList();
        }

        public static List<string> RunToCsv(string input, Options opt)
        {
            string outDir = opt.Output ?? Path.GetDirectoryName(Path.GetFullPath(input));
            Directory.CreateDirectory(outDir);
            string baseName = Path.GetFileNameWithoutExtension(input);
            string dataPath = Path.Combine(outDir, baseName + (opt.Full ? "_full.csv" : ".csv"));
            if (!opt.Overwrite && File.Exists(dataPath))
                throw new IOException("Output exists (use --overwrite): " + dataPath);

            var log = TrajectoryLog.Parse(File.ReadAllBytes(input), opt.IgnoreCrc);
            string csv = opt.Full ? CsvExporter.FullCsv(log, input) : CsvExporter.DataCsv(log, opt);
            FileUtil.WriteAtomic(dataPath, Encoding.UTF8.GetBytes(csv));

            var notes = new List<string>
            {
                "OK   " + Path.GetFileName(input) + " (v" + log.Version + ") -> " + dataPath,
                "     " + string.Format(CultureInfo.InvariantCulture, "{0} snapshots, {1} axes, {2} subbeam(s)",
                                        log.NumSnapshots, log.NumAxes, log.Subbeams.Count)
            };
            bool[] hasData = CsvExporter.AxesWithData(log);
            string empty = string.Join(", ", Enumerable.Range(0, log.NumAxes).Where(i => !hasData[i])
                                                   .Select(i => TrajectoryLog.AxisName(log.AxisIds[i])));
            if (empty.Length > 0) notes.Add("     Axes with no data " + (opt.Full ? "(all \"no value\"): " : "left out: ") + empty);
            if (log.CrcWarning != null) notes.Add("     WARNING: " + log.CrcWarning);
            return notes;
        }

        /// <summary>Quick version check from the file's first 32 bytes; throws if it is not the --expect version.</summary>
        public static List<string> RunVersion(string input, Options opt)
        {
            string version = TrajectoryLog.ReadVersion(input);
            if (opt.ExpectVersion != null && version != opt.ExpectVersion)
                throw new InvalidDataException("version " + version + ", expected " + opt.ExpectVersion);
            return new List<string> { "v" + version + "  " + Path.GetFileName(input) };
        }

        /// <summary>Compares two logs. The first line starts with SAME or DIFF; <paramref name="same"/> gives the outcome.</summary>
        public static List<string> RunCompare(string fileA, string fileB, Options opt, out bool same)
        {
            var a = TrajectoryLog.Parse(File.ReadAllBytes(fileA), opt.IgnoreCrc);
            var b = TrajectoryLog.Parse(File.ReadAllBytes(fileB), opt.IgnoreCrc);
            CompareResult r = LogComparer.Compare(a, b, opt.Tolerance, opt.IgnoreVersion, opt.MaxDiffs);
            same = r.Same;

            string pair = Path.GetFileName(fileA) + " (v" + a.Version + ") vs " + Path.GetFileName(fileB) + " (v" + b.Version + ")";
            var notes = new List<string>();
            if (r.ByteIdentical) notes.Add("SAME " + pair + ": byte-for-byte identical");
            else if (r.Same) notes.Add("SAME " + pair + ": same content");
            else notes.Add("DIFF " + pair + ": " + r.DifferenceCount.ToString("N0", CultureInfo.InvariantCulture) + " difference(s)");
            foreach (string d in r.Differences) notes.Add("     " + d);
            foreach (string i in r.Info) notes.Add("     (" + i + ")");
            if (a.CrcWarning != null) notes.Add("     WARNING A: " + a.CrcWarning);
            if (b.CrcWarning != null) notes.Add("     WARNING B: " + b.CrcWarning);
            return notes;
        }


        /// <summary>
        /// Writes &lt;name&gt;.txt: the whole file in human-readable form, exactly as stored (see
        /// <see cref="RawDump"/>). Works on any version and on damaged files.
        /// </summary>
        public static List<string> RunDump(string input, Options opt)
        {
            string outDir = opt.Output ?? Path.GetDirectoryName(Path.GetFullPath(input));
            Directory.CreateDirectory(outDir);
            string dst = Path.Combine(outDir, Path.GetFileNameWithoutExtension(input) + ".txt");
            if (!opt.Overwrite && File.Exists(dst))
                throw new IOException("Output exists (use --overwrite): " + dst);

            var warnings = new List<string>();
            string text = RawDump.ToText(File.ReadAllBytes(input), input, warnings);
            FileUtil.WriteAtomic(dst, new UTF8Encoding(false).GetBytes(text));
            var notes = new List<string> { "OK   " + Path.GetFileName(input) + " -> " + dst };
            notes.AddRange(warnings.Select(w => "     WARNING: " + w));
            return notes;
        }
    }
}
