// Program.cs - TrajectoryLogCli, the command-line tool.
//
//   As in the app (AppCommands, the app's options and defaults):
//     export            Export: v4.0 / CSV / full CSV / dump of any number of logs
//     compare-deliveries, compare-files, rebuild-mlc
//   Single conversions (used by scripts and the tests):
//     to-v4             v5.1 -> v4.0
//     to-csv            data CSV, or with --full the whole file as CSV
//     dump              the whole file as readable text (<name>.txt)
//   Analysis (also in the GUI):
//     compare-delivery  compare two deliveries of the same field, matched by control point
//     reconstruct-mlc   rebuild an Eclipse MLC file from a log
//   Validation / testing:
//     to-v5             v4.0 -> v5.1
//     version           print v4.0 / v5.1
//     compare           compare two logs (or folders) value by value
//
// Exit code: 0 = all OK (compare: all the same), 1 = at least one failure or difference,
//            2 = bad arguments.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using TrajectoryLogTools.Core;

namespace TrajectoryLogTools.Cli
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length > 0 && AppCommands.Handles(args[0])) return AppCommands.Run(args);
            if (args.Length > 0 && AnalysisCommands.Handles(args[0])) return AnalysisCommands.Run(args);

            Options opt;
            try { opt = Options.Parse(args); }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine("Error: " + ex.Message);
                Console.Error.WriteLine();
                Usage.Print();
                return 2;
            }
            if (opt == null) { Usage.Print(); return 2; }

            if (opt.Command == Command.Compare) return CompareMain(opt);

            // Work out the list of input files
            List<string> inputs;
            bool folderInput;
            string error;
            if (!Commands.TryListInputs(opt.Input, out inputs, out folderInput, out error))
            {
                Console.Error.WriteLine(error);
                return 1;
            }

            int failures = 0;
            foreach (string input in inputs)
            {
                try
                {
                    List<string> notes;
                    switch (opt.Command)
                    {
                        case Command.ToV4: notes = Commands.RunToV4(input, folderInput, opt); break;
                        case Command.ToV5: notes = Commands.RunToV5(input, folderInput, opt); break;
                        case Command.Version: notes = Commands.RunVersion(input, opt); break;
                        case Command.Dump: notes = Commands.RunDump(input, opt); break;
                        default: notes = Commands.RunToCsv(input, opt); break;
                    }
                    foreach (string line in notes) Console.WriteLine(line);
                }
                catch (SkipException ex)
                {
                    Console.WriteLine("SKIP " + Path.GetFileName(input) + ": " + ex.Message);
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.Error.WriteLine("FAIL " + Path.GetFileName(input) + ": " + ex.Message);
                }
            }
            return failures == 0 ? 0 : 1;
        }

        static int CompareMain(Options opt)
        {
            bool dirA = Directory.Exists(opt.Input), dirB = Directory.Exists(opt.Input2);
            foreach (string p in new[] { opt.Input, opt.Input2 })
                if (!Directory.Exists(p) && !File.Exists(p)) { Console.Error.WriteLine("Input not found: " + p); return 1; }
            if (dirA != dirB) { Console.Error.WriteLine("compare needs two files or two folders."); return 2; }

            // Pairs of files to compare; folders are matched by file name (top level only).
            var pairs = new List<KeyValuePair<string, string>>();
            int failures = 0;
            if (!dirA)
                pairs.Add(new KeyValuePair<string, string>(opt.Input, opt.Input2));
            else
            {
                var namesA = Directory.GetFiles(opt.Input, "*.bin").Select(Path.GetFileName).ToList();
                var namesB = new HashSet<string>(Directory.GetFiles(opt.Input2, "*.bin").Select(Path.GetFileName),
                                                 StringComparer.OrdinalIgnoreCase);
                foreach (string n in namesA.Union(namesB, StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    bool inA = namesA.Contains(n, StringComparer.OrdinalIgnoreCase), inB = namesB.Contains(n);
                    if (inA && inB) pairs.Add(new KeyValuePair<string, string>(Path.Combine(opt.Input, n), Path.Combine(opt.Input2, n)));
                    else
                    {
                        failures++;
                        Console.WriteLine("MISSING " + n + ": only in " + (inA ? opt.Input : opt.Input2));
                    }
                }
                if (pairs.Count == 0 && failures == 0) { Console.Error.WriteLine("No .bin files found."); return 1; }
            }

            int same = 0, different = 0;
            foreach (var pair in pairs)
            {
                try
                {
                    bool isSame;
                    foreach (string line in Commands.RunCompare(pair.Key, pair.Value, opt, out isSame)) Console.WriteLine(line);
                    if (isSame) same++; else different++;
                }
                catch (Exception ex)
                {
                    failures++;
                    Console.Error.WriteLine("FAIL " + Path.GetFileName(pair.Key) + ": " + ex.Message);
                }
            }
            if (dirA)
                Console.WriteLine(string.Format(CultureInfo.InvariantCulture,
                    "{0} same, {1} different, {2} missing / failed", same, different, failures));
            return different == 0 && failures == 0 ? 0 : 1;
        }
    }
}
