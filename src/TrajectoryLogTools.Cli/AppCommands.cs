// AppCommands.cs - the commands that do what the app does, with the app's options and defaults:
//
//   export              Export window: v4.0 / CSV / full CSV / dump of any number of logs
//   compare-deliveries  Compare page: two deliveries matched by control point (= compare-delivery)
//   compare-files       File comparison window: every stored value (= compare)
//   rebuild-mlc         Rebuild Eclipse MLC page: the fields of any number of logs
//
// They call the same code as the app (Exporter, DeliveryComparison, LogComparer, MlcReconstructor,
// TemplateMatcher), so the files written are the same.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TrajectoryLogTools.Analysis;
using TrajectoryLogTools.Core;

namespace TrajectoryLogTools.Cli
{
    public static class AppCommands
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static bool Handles(string command)
        {
            string c = command.ToLowerInvariant();
            return c == "export" || c == "rebuild-mlc" || c == "compare-deliveries" || c == "compare-files";
        }

        public static int Run(string[] args)
        {
            string c = args[0].ToLowerInvariant();
            // The comparisons are the existing commands under the app's names.
            if (c == "compare-deliveries") return AnalysisCommands.Run(Renamed(args, "compare-delivery"));
            if (c == "compare-files") return Program.Main(Renamed(args, "compare"));
            try
            {
                return c == "export" ? Export(args) : RebuildMlc(args);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine("Error: " + ex.Message);
                Console.Error.WriteLine();
                Usage.Print();
                return 2;
            }
        }

        static string[] Renamed(string[] args, string command)
        {
            var a = (string[])args.Clone();
            a[0] = command;
            return a;
        }

        static string Next(string[] args, ref int i)
        {
            if (i + 1 >= args.Length) throw new ArgumentException(args[i] + " needs a value");
            return args[++i];
        }

        static string OneOf(string value, string option, params string[] allowed)
        {
            string v = value.Trim().ToLowerInvariant();
            if (!allowed.Contains(v)) throw new ArgumentException(option + " must be " + string.Join(" or ", allowed));
            return v;
        }

        /// <summary>The logs named: files as given, folders as their .bin files (top level), in name order.</summary>
        static List<string> Logs(List<string> inputs)
        {
            var logs = new List<string>();
            foreach (string p in inputs)
            {
                if (Directory.Exists(p))
                {
                    var found = Directory.GetFiles(p, "*.bin").OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                    if (found.Count == 0) throw new ArgumentException("No .bin files in " + p);
                    logs.AddRange(found);
                }
                else if (File.Exists(p)) logs.Add(p);
                else throw new ArgumentException("Input not found: " + p);
            }
            return logs.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        // =========================================================================================
        // export
        // =========================================================================================

        static int Export(string[] args)
        {
            var j = new ExportOptions { ToV4 = false };
            var inputs = new List<string>();
            string folder = null;
            string patientId = null, planName = null, planUid = null;
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                if (a.Length == 0) continue;
                switch (a.ToLowerInvariant())
                {
                    case "-h": case "--help": Usage.Print(); return 0;
                    case "-o": case "--output": folder = Next(args, ref i); break;
                    case "--v4": j.ToV4 = true; break;
                    case "--csv": j.ToCsv = true; break;
                    case "--full-csv": j.FullCsv = true; break;
                    case "--dump": j.Dump = true; break;
                    case "--machine": j.Machine = Options.ParseMachine(Next(args, ref i)); break;
                    case "--serial": j.KeepSerial = OneOf(Next(args, ref i), "--serial", "remove", "keep") == "keep"; break;
                    case "--new-serial": j.NewSerial = Next(args, ref i); break;
                    case "--couch": j.KeepCouch = OneOf(Next(args, ref i), "--couch", "remove", "keep") == "keep"; break;
                    case "--save-time": j.TimeCsv = true; break;
                    case "--patient-id": patientId = Next(args, ref i); break;
                    case "--plan-name": planName = Next(args, ref i); break;
                    case "--plan-uid": planUid = Next(args, ref i); break;
                    case "--field-name":
                    {
                        string kv = Next(args, ref i);
                        int eq = kv.IndexOf('=');
                        int n;
                        if (eq <= 0 || !int.TryParse(kv.Substring(0, eq), NumberStyles.Integer, Inv, out n) || n < 1)
                            throw new ArgumentException("--field-name needs N=NAME, with N the field (subbeam) number from 1");
                        j.SubbeamNames[n] = kv.Substring(eq + 1);
                        break;
                    }
                    case "--no-mlc": j.NoMlc = true; break;
                    case "--actual-only": j.ActualOnly = true; break;
                    case "--overwrite": j.Overwrite = true; break;
                    case "--ignore-crc": j.IgnoreCrc = true; break;
                    default:
                        if (a.StartsWith("-")) throw new ArgumentException("Unknown option " + a);
                        inputs.Add(a); break;
                }
            }
            if (inputs.Count == 0) throw new ArgumentException("export needs at least one log or folder");
            if (!j.ToV4 && !j.ToCsv && !j.FullCsv && !j.Dump) j.ToV4 = true;   // as the app: v4.0 unless asked otherwise
            List<string> logs = Logs(inputs);

            // Header details: one log at a time, as in the app (they name one patient and plan).
            if (patientId != null || planName != null || planUid != null || j.SubbeamNames.Count > 0)
            {
                if (logs.Count != 1) throw new ArgumentException("--patient-id, --plan-name, --plan-uid and --field-name change one log at a time; give one log");
                if (!j.ToV4) throw new ArgumentException("--patient-id, --plan-name, --plan-uid and --field-name apply to the v4.0 file; add --v4");
                string meta = LogHeader.Read(logs[0]).MetaDataText ?? "";
                if (patientId != null) j.MetaEdits.Add(new KeyValuePair<string, string>(MetaData.KeyFor(meta, MetaData.PatientIdKeys), patientId));
                if (planName != null) j.MetaEdits.Add(new KeyValuePair<string, string>(MetaData.KeyFor(meta, MetaData.PlanNameKeys), planName));
                if (planUid != null) j.MetaEdits.Add(new KeyValuePair<string, string>(MetaData.KeyFor(meta, MetaData.PlanUidKeys), planUid));
            }
            string error = j.Validate();
            if (error != null) throw new ArgumentException(error);
            if (folder != null && j.ToV4 && logs.Any(l => LogFinder.PathEquals(Path.GetDirectoryName(l), Path.GetFullPath(folder))))
                throw new ArgumentException("-o is the folder of one of the logs, so its v4.0 file would replace the original. Choose another folder, or leave out -o (next to each log, in a \"converted\" subfolder).");

            int ok = 0, skipped = 0, failed = 0;
            var runLog = new StringBuilder();
            runLog.AppendLine(DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss", Inv) + "  Export of " + logs.Count + " log(s)");
            foreach (string log in logs)
            {
                var lines = new List<string>();
                ExportOutcome outcome = Exporter.Run(log, Exporter.OutputFolder(log, folder), j, lines);
                runLog.AppendLine(log);
                foreach (string l in lines)
                {
                    runLog.AppendLine("  " + l);
                    (l.StartsWith("FAIL", StringComparison.Ordinal) ? Console.Error : Console.Out).WriteLine(l);
                }
                if (outcome == ExportOutcome.Done) ok++; else if (outcome == ExportOutcome.Skipped) skipped++; else failed++;
            }
            Console.WriteLine(ok + " done, " + skipped + " skipped, " + failed + " failed.");
            if (folder != null)
            {
                // A run log with the files, as the app saves.
                try
                {
                    Directory.CreateDirectory(folder);
                    File.WriteAllText(Path.Combine(folder, "TrajectoryLogConversion_" + DateTime.Now.ToString("yyyyMMdd_HHmmss", Inv) + ".log"), runLog.ToString(), Encoding.UTF8);
                }
                catch (Exception ex) { Console.Error.WriteLine("Run log not saved: " + ex.Message); }
            }
            return failed == 0 ? 0 : 1;
        }

        // =========================================================================================
        // rebuild-mlc
        // =========================================================================================

        static int RebuildMlc(string[] args)
        {
            var o = new MlcReconstructionOptions();
            var inputs = new List<string>();
            var exports = new List<string>();
            string folder = null;
            bool ignoreCrc = false, verbose = false;
            string fields = null;
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                if (a.Length == 0) continue;
                switch (a.ToLowerInvariant())
                {
                    case "-h": case "--help": Usage.Print(); return 0;
                    case "-o": case "--output": folder = Next(args, ref i); break;
                    case "-e": case "--export": exports.Add(Next(args, ref i)); break;
                    case "--positions": o.Positions = Next(args, ref i); break;
                    case "--axis": o.Axis = Next(args, ref i); break;
                    case "--gantry": o.GantryMode = Next(args, ref i); break;
                    case "--include-hold": o.IncludeHold = true; break;
                    case "--field": fields = Next(args, ref i); break;
                    case "--scale": o.Scale = Next(args, ref i); break;
                    case "--verbose": verbose = true; break;
                    case "--ignore-crc": ignoreCrc = true; break;
                    case "--min-gap":
                        double g;
                        if (!double.TryParse(Next(args, ref i), NumberStyles.Float, Inv, out g))
                            throw new ArgumentException("--min-gap must be a number (mm)");
                        o.MinGap = g; break;
                    default:
                        if (a.StartsWith("-")) throw new ArgumentException("Unknown option " + a);
                        inputs.Add(a); break;
                }
            }
            if (inputs.Count == 0) throw new ArgumentException("rebuild-mlc needs at least one log or folder");
            o.Validate();
            List<string> logs = Logs(inputs);
            if (fields != null && logs.Count != 1) throw new ArgumentException("--field picks fields of one log; give one log");
            foreach (string t in exports)
                if (!File.Exists(t)) throw new ArgumentException("Eclipse export not found: " + t);
            if (folder != null)
            {
                var clash = logs.GroupBy(l => Path.GetFileName(l), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
                if (clash != null) throw new ArgumentException("Two logs are both called " + clash.Key + ", so their files would overwrite each other in one folder. Leave out -o (next to each log).");
            }

            // Load the logs and find their fields, as the Rebuild page lists them.
            var loaded = new List<Tuple<string, DeliveryRecord, List<SubbeamInfo>>>();
            int failed = 0;
            foreach (string log in logs)
            {
                try
                {
                    DeliveryRecord d = DeliveryRecord.Load(log, ignoreCrc);
                    if (d.Mlc == null) throw new InvalidDataException(MlcGeometry.UnsupportedMessage(d.LeafCount));
                    var infos = TemplateMatcher.Describe(d);
                    if (fields != null)
                    {
                        var want = fields.Split(',').Select(x => x.Trim()).ToList();
                        infos = infos.Where(x => want.Contains(x.Subbeam.ToString(Inv))).ToList();
                        if (infos.Count == 0) throw new ArgumentException("--field " + fields + ": no such field in " + Path.GetFileName(log));
                    }
                    loaded.Add(Tuple.Create(log, d, infos));
                }
                catch (ArgumentException) { throw; }
                catch (Exception ex) { failed++; Console.Error.WriteLine("NOT INCLUDED " + Path.GetFileName(log) + ": " + ex.Message); }
            }

            // Eclipse exports are matched to the fields of all the logs by control points and gantry angle.
            var templateFor = new Dictionary<Tuple<string, int>, string>();
            if (exports.Count > 0)
            {
                var all = loaded.SelectMany(l => l.Item3.Select(i => Tuple.Create(l.Item1, i))).ToList();
                var keyed = all.Select((f, k) => new SubbeamInfo { Subbeam = k, ControlPoints = f.Item2.ControlPoints, GantryIec = f.Item2.GantryIec }).ToList();
                var templates = exports.Select(TemplateMatcher.Read).ToList();
                var match = TemplateMatcher.Match(keyed, templates);
                foreach (var m in match) templateFor[Tuple.Create(all[m.Key].Item1, all[m.Key].Item2.Subbeam)] = m.Value.Path;
                foreach (TemplateInfo t in templates.Where(t => !match.Values.Contains(t)))
                    Console.Error.WriteLine("NOT USED " + Path.GetFileName(t.Path) + ": " + (t.Error ?? "no field with " + t.ControlPoints + " control points"));
                var missing = all.Where(f => !templateFor.ContainsKey(Tuple.Create(f.Item1, f.Item2.Subbeam))).ToList();
                if (missing.Count > 0)
                    throw new ArgumentException("With --export, every field needs an export; none fits " +
                        string.Join(", ", missing.Select(f => Path.GetFileName(f.Item1) + " field " + f.Item2.Subbeam)));
            }

            int ready = 0, check = 0;
            foreach (var l in loaded)
            {
                string log = l.Item1;
                var run = new MlcReconstructionOptions
                {
                    Positions = o.Positions, Axis = o.Axis, GantryMode = o.GantryMode, Scale = o.Scale, MinGap = o.MinGap, IncludeHold = o.IncludeHold,
                    Subbeams = string.Join(",", l.Item3.Select(i => i.Subbeam)),
                    OutputFolder = folder ?? Path.GetDirectoryName(log),
                };
                if (exports.Count > 0) run.Templates.AddRange(l.Item3.Select(i => templateFor[Tuple.Create(log, i.Subbeam)]));
                Console.WriteLine(Path.GetFileName(log));
                try
                {
                    Directory.CreateDirectory(run.OutputFolder);
                    MlcReconstructionResult r = MlcReconstructor.Run(l.Item2, log, run, verbose ? (Action<string>)Console.WriteLine : null);
                    foreach (SubbeamResult s in r.Subbeams)
                    {
                        string status = s.Status == RebuildStatus.Ready ? "Ready to import" : s.Status == RebuildStatus.CheckFirst ? "Check first" : "Failed";
                        if (s.Status == RebuildStatus.Ready) ready++; else if (s.Status == RebuildStatus.CheckFirst) check++; else failed++;
                        string name = FieldName(l.Item2, s.Subbeam);
                        Console.WriteLine("  " + status.PadRight(16) + name + (s.Ok ? " -> " + s.OutputPath : ""));
                        if (s.Ok)
                            Console.WriteLine(string.Format(Inv, "                  delivered vs planned RMS {0:F3} mm, max {1:F2} mm at {2}; gap rules: {3}",
                                s.DeliveredRmsMm, s.DeliveredMaxMm, s.DeliveredMaxAt, s.RemainingGapProblems == 0 ? "all pass" : s.RemainingGapProblems + " problem(s)"));
                        foreach (string p in s.Ok ? s.Problems : new List<string> { s.Error })
                            Console.WriteLine("                  ! " + p);
                    }
                }
                catch (Exception ex)
                {
                    failed += l.Item3.Count;
                    Console.Error.WriteLine("  Failed          " + ex.Message);
                }
            }
            Console.WriteLine(ready + " ready to import, " + check + " to check first, " + failed + " failed.");
            return failed == 0 ? 0 : 1;
        }

        static string FieldName(DeliveryRecord d, int subbeam)
        {
            var subs = d.Log != null ? d.Log.Subbeams : new List<Subbeam>();
            if (subbeam >= 1 && subbeam <= subs.Count && !string.IsNullOrWhiteSpace(subs[subbeam - 1].Name)) return subs[subbeam - 1].Name.Trim();
            return subbeam == 0 ? "Field" : "Field " + subbeam;
        }
    }
}
