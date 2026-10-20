// AnalysisCommands.cs - compare-delivery and reconstruct-mlc.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using TrajectoryLogTools.Analysis;
using TrajectoryLogTools.Charts;

namespace TrajectoryLogTools.Cli
{
    public static class AnalysisCommands
    {
        public static bool Handles(string command)
        {
            string c = command.ToLowerInvariant();
            return c == "compare-delivery" || c == "reconstruct-mlc";
        }

        public static int Run(string[] args)
        {
            try
            {
                return args[0].ToLowerInvariant() == "compare-delivery" ? CompareDelivery(args) : ReconstructMlc(args);
            }
            catch (ArgumentException ex)
            {
                Console.Error.WriteLine("Error: " + ex.Message);
                Console.Error.WriteLine();
                Usage.Print();
                return 2;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL " + ex.Message);
                return 1;
            }
        }

        static string Next(string[] args, ref int i)
        {
            if (i + 1 >= args.Length) throw new ArgumentException(args[i] + " needs a value");
            return args[++i];
        }

        // ---------------------------------------------------------------------------------------
        static int CompareDelivery(string[] args)
        {
            var inputs = new List<string>();
            string outDir = null;
            bool plot = true, keepFirst = false, ignoreCrc = false;
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                if (a.Length == 0) continue;
                switch (a)
                {
                    case "-h": case "--help": Usage.Print(); return 0;
                    case "-o": case "--output": outDir = Next(args, ref i); break;
                    case "--no-plot": plot = false; break;
                    case "--keep-first-snapshot": keepFirst = true; break;
                    case "--ignore-crc": ignoreCrc = true; break;
                    default:
                        if (a.StartsWith("-")) throw new ArgumentException("Unknown option " + a);
                        inputs.Add(a); break;
                }
            }
            if (inputs.Count != 2) throw new ArgumentException("compare-delivery needs two logs");
            foreach (string p in inputs)
                if (!File.Exists(p)) { Console.Error.WriteLine("Input not found: " + p); return 1; }

            var a0 = DeliveryRecord.Load(inputs[0], ignoreCrc);
            var b0 = DeliveryRecord.Load(inputs[1], ignoreCrc);
            DeliveryComparisonResult r = DeliveryComparison.Compare(a0, b0, dropStrayFirstSnapshot: !keepFirst);
            outDir = outDir ?? Path.Combine(Path.GetDirectoryName(Path.GetFullPath(inputs[0])), "delivery_comparison");
            Console.Write(DeliveryComparison.Report(r));
            DeliveryComparison.WriteResults(r, outDir);
            if (plot) DeliveryCharts.SaveFigure(r, Path.Combine(outDir, "expected_vs_actual_comparison.png"));
            Console.WriteLine();
            Console.WriteLine("Wrote results to " + Path.GetFullPath(outDir));
            return 0;
        }

        // ---------------------------------------------------------------------------------------
        static int ReconstructMlc(string[] args)
        {
            var o = new MlcReconstructionOptions();
            string log = null;
            bool ignoreCrc = false;
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                if (a.Length == 0) continue;
                switch (a.ToLowerInvariant())
                {
                    case "-h": case "--help": Usage.Print(); return 0;
                    case "-o": case "--out": o.OutPath = Next(args, ref i); break;
                    case "-t": case "--template": o.Templates.Add(Next(args, ref i)); break;
                    case "--positions": o.Positions = Next(args, ref i); break;
                    case "--axis": o.Axis = Next(args, ref i); break;
                    case "--gantry": o.GantryMode = Next(args, ref i); break;
                    case "--include-hold": o.IncludeHold = true; break;
                    case "--subbeam": o.Subbeams = Next(args, ref i); break;
                    case "--mlc-model": o.MlcModel = Next(args, ref i); break;
                    case "--scale": o.Scale = Next(args, ref i); break;
                    case "--report": o.ReportPath = Next(args, ref i); break;
                    case "--ignore-crc": ignoreCrc = true; break;
                    case "--min-gap":
                        double g;
                        if (!double.TryParse(Next(args, ref i), NumberStyles.Float, CultureInfo.InvariantCulture, out g))
                            throw new ArgumentException("--min-gap must be a number (mm)");
                        o.MinGap = g; break;
                    default:
                        if (a.StartsWith("-")) throw new ArgumentException("Unknown option " + a);
                        if (log != null) throw new ArgumentException("More than one log file given.");
                        log = a; break;
                }
            }
            if (log == null) throw new ArgumentException("No log file given.");
            o.Validate();
            if (!File.Exists(log)) { Console.Error.WriteLine("Input not found: " + log); return 1; }
            foreach (string t in o.Templates)
                if (!File.Exists(t)) { Console.Error.WriteLine("Template not found: " + t); return 1; }

            var result = MlcReconstructor.Run(DeliveryRecord.Load(log, ignoreCrc), log, o, Console.WriteLine);
            return result.Failed > 0 ? 1 : 0;
        }
    }
}
