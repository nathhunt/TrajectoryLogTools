// =============================================================================
//  LogToEclipseMlc.cs
//
//  Reconstructs an Eclipse "MLCPlan serialization" file (the CSV that Eclipse
//  exports / imports for a beam's MLC sequence) from a machine delivery log
//  that has been converted to CSV (Snapshot, ElapsedTime_s, ..., ControlPoint,
//  MLC_LeafNNN_cm_Expected / _Actual columns).
//
//  Method (per field / subbeam):
//    0. A log may hold several subbeams (e.g. a 5-field IMRT plan delivered as
//       one log). Each subbeam is one Eclipse field and gets its own output file.
//       ControlPoint and MU in such logs are cumulative over the whole delivery,
//       and each subbeam opens with no-dose set-up snapshots while the gantry/MLC
//       move to the new field. Those set-up snapshots are skipped and the
//       subbeam's first control point and starting MU are subtracted, so every
//       field is fitted on its own 0..N-1 control points and 0..MU range.
//    1. Keep only beam-on snapshots (BeamHold_Actual == 0). Snapshots taken
//       during a beam hold deliver no dose and, on a VMAT interrupt, the machine
//       rewinds ControlPoint, so they must not influence the fit.
//    2. Give every snapshot a fractional control-point coordinate:
//         --axis mu (default): from the ACTUAL delivered MU, mapped onto the
//                              plan's MU-vs-control-point relation, i.e. "where
//                              were the leaves when this fraction of the dose
//                              was actually delivered"
//         --axis cp          : the log's own ControlPoint column (which tracks
//                              the EXPECTED MU)
//    3. Eclipse models every axis as moving linearly in meterset between
//       control points. So for each leaf we least-squares fit a piecewise-linear
//       function whose knots sit exactly on the integer control points to all
//       the beam-on samples. The knot values are the reconstructed control-point
//       leaf positions. (Simply interpolating the 20 ms samples at each integer
//       CP cuts the corners of the trajectory - up to ~0.7 mm on these logs.)
//    4. Convert log (Varian Standard scale, cm, both banks positive = open) to
//       Eclipse IEC 61217 mm, X1/X2 pair-interleaved order, and write the file.
//
//  If an Eclipse export of the same beam is given with --template, its header,
//  meterset weights, jaws, gantry and collimator angles are copied verbatim and
//  ONLY the leaf positions are replaced. This is the safest way to get a file
//  that Eclipse will accept back onto the original beam.
//
//  Written to compile with the C# 5 compiler that ships with .NET Framework 4.x:
//    C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /out:LogToEclipseMlc.exe LogToEclipseMlc.cs
//  (also builds with any newer csc / dotnet / mono mcs)
//
//  Usage:
//    LogToEclipseMlc.exe <log.csv> [options]
//      -o, --out <file>        output MLC file (default: <log>_reconstructed_MLC.csv,
//                              or <log>_subN_reconstructed_MLC.csv for multi-subbeam
//                              logs; with several subbeams _subN is added to -o)
//      -t, --template <file>   Eclipse MLC export of the same beam (recommended).
//                              Repeat once per processed subbeam, in subbeam order.
//      --positions actual|expected   leaf positions to use (default: actual)
//      --axis mu|cp            sample coordinate (default: mu, see above)
//      --gantry plan|actual    without template: gantry from expected (plan) or
//                              actual log values (default: plan)
//      --include-hold          also use beam-hold snapshots (not recommended)
//      --subbeam all|n|n,m     subbeam(s) to process (default: all, one file each)
//      --mlc-model <name>      MLC model written when no template (default: SX2)
//      --scale varian|iec      angle scale of the log (default: varian)
//      --report <file>         QA report path (default: <out>_report.txt)
//      --min-gap <mm>          minimum dynamic leaf gap (default 0.5 for SX2); fitted
//                              gaps below it are closed exactly or opened to it
//
//  Verified against Halcyon/Ethos SX2 (dual layer, 28 distal + 29 proximal
//  pairs) logs. For other MLC types check the bank/sign mapping in
//  LeafToIec() against an export before clinical use.
// =============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace LogToEclipseMlc
{
    internal class LogData
    {
        public string Path;
        public List<string> Header;
        public Dictionary<string, int> Col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public List<double[]> Rows = new List<double[]>();
        public List<string> ServoState = new List<string>();
        public int NumLeaves;

        public int Need(string name)
        {
            int i;
            if (!Col.TryGetValue(name, out i))
                throw new Exception("Log file is missing column '" + name + "'.");
            return i;
        }

        public bool Has(string name) { return Col.ContainsKey(name); }

        public static LogData Load(string path)
        {
            var log = new LogData();
            log.Path = path;
            using (var sr = new StreamReader(path, Encoding.UTF8, true))
            {
                string headerLine = sr.ReadLine();
                if (headerLine == null) throw new Exception("Log file is empty.");
                if (headerLine.TrimStart(Program.Bom).Trim().StartsWith("#"))
                    throw new Exception("'" + System.IO.Path.GetFileName(path) + "' looks like an Eclipse MLC export, not a machine log.\n" +
                        "       The first argument must be the delivery log (e.g. PHYS_VMAT_QA_..._20260915170414.csv);\n" +
                        "       pass the Eclipse export with -t, e.g.:\n" +
                        "       LogToEclipseMlc.exe <log.csv> -t " + System.IO.Path.GetFileName(path));
                log.Header = headerLine.Split(Program.Comma).Select(s => s.Trim().Trim(Program.Quote)).ToList();
                for (int i = 0; i < log.Header.Count; i++)
                    if (!log.Col.ContainsKey(log.Header[i])) log.Col[log.Header[i]] = i;

                int servoCol = -1;
                int tmp;
                if (log.Col.TryGetValue("DoseServoState", out tmp)) servoCol = tmp;

                string line;
                int lineNo = 1;
                while ((line = sr.ReadLine()) != null)
                {
                    lineNo++;
                    if (line.Trim().Length == 0) continue;
                    string[] f = line.Split(Program.Comma);
                    var v = new double[log.Header.Count];
                    for (int i = 0; i < v.Length; i++)
                    {
                        if (i >= f.Length) { v[i] = double.NaN; continue; }
                        if (i == servoCol) { v[i] = double.NaN; continue; }
                        double d;
                        v[i] = double.TryParse(f[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : double.NaN;
                    }
                    log.Rows.Add(v);
                    log.ServoState.Add(servoCol >= 0 && servoCol < f.Length ? f[servoCol].Trim() : "");
                }
            }
            int n = 0;
            while (log.Has(LeafCol(n + 1, "Actual")) && log.Has(LeafCol(n + 1, "Expected"))) n++;
            if (n == 0 || n % 2 != 0)
                throw new Exception("Could not find an even number of MLC_LeafNNN_cm_Expected/_Actual columns.");
            log.NumLeaves = n;
            return log;
        }

        public static string LeafCol(int oneBased, string kind)
        {
            return "MLC_Leaf" + oneBased.ToString("000") + "_cm_" + kind;
        }
    }

    internal class EclipseMlcFile
    {
        public List<string> Lines = new List<string>();   // full file, data lines included
        public List<int> DataLineIdx = new List<int>();   // indices into Lines
        public List<double[]> Cps = new List<double[]>(); // parsed data lines
        public string MlcModel;
        public int DeclaredCps = -1;

        public const int NonLeafFields = 7; // MetersetWeight,CollX1,CollX2,CollY1,CollY2,GantryRtn,CollRtn

        public int NumLeafValues { get { return Cps.Count > 0 ? Cps[0].Length - NonLeafFields : 0; } }

        public static EclipseMlcFile Load(string path)
        {
            var f = new EclipseMlcFile();
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            string prev = "";
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i].TrimStart(Program.Bom);
                f.Lines.Add(l);
                string t = l.Trim();
                if (t.Length == 0) { continue; }
                if (t.StartsWith("#")) { prev = t; continue; }
                if (t.Contains(","))
                {
                    double[] v = t.Split(Program.Comma).Select(s => double.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
                    f.Cps.Add(v);
                    f.DataLineIdx.Add(i);
                }
                else if (prev.IndexOf("MLC Model", StringComparison.OrdinalIgnoreCase) >= 0) f.MlcModel = t;
                else if (prev.IndexOf("Number of control points", StringComparison.OrdinalIgnoreCase) >= 0)
                    f.DeclaredCps = int.Parse(t, CultureInfo.InvariantCulture);
                prev = "";
            }
            if (f.Cps.Count == 0) throw new Exception("No control point lines found in template " + path);
            return f;
        }
    }

    internal static class Program
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        // char[] overloads only: .NET Framework has no Split(char)/Trim(char) overloads
        // always the (IFormatProvider, string, object[]) overload, which exists in .NET Framework 4.0+
        static string Fmt(string format, params object[] args) { return string.Format(Inv, format, args); }
        internal static readonly char[] Comma = new char[] { ',' };
        internal static readonly char[] Quote = new char[] { '"' };
        internal static readonly char[] Bom = new char[] { '\uFEFF' };

        static int Main(string[] args)
        {
            try
            {
                return Run(args);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 1;
            }
        }

        static void PrintUsage()
        {
            Console.WriteLine("Usage: LogToEclipseMlc <log.csv> [-o out.csv] [-t eclipse_template.csv ...]");
            Console.WriteLine("       [--positions actual|expected] [--axis mu|cp] [--gantry plan|actual]");
            Console.WriteLine("       [--include-hold] [--subbeam all|n|n,m,...] [--mlc-model SX2] [--scale varian|iec] [--min-gap 0.5] [--report file]");
            Console.WriteLine("  A log with several subbeams is processed one output file per subbeam (default: all).");
            Console.WriteLine("  Give one -t per processed subbeam, in subbeam order, or a single -t with a single --subbeam.");
        }

        internal class Options
        {
            public string LogPath, OutPath, ReportPath;
            public List<string> Templates = new List<string>();
            public double MinGap = 0.5;
            public string Positions = "actual", Axis = "mu", GantryMode = "plan", MlcModel = "SX2", Scale = "varian";
            public bool IncludeHold;
        }

        static int Run(string[] args)
        {
            if (args.Length == 0) { PrintUsage(); return 1; }

            var o = new Options();
            string subArg = null;

            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                Func<string> next = () =>
                {
                    if (i + 1 >= args.Length) throw new Exception("Missing value after " + a);
                    return args[++i];
                };
                switch (a.ToLowerInvariant())
                {
                    case "-o": case "--out": o.OutPath = next(); break;
                    case "-t": case "--template": o.Templates.Add(next()); break;
                    case "--positions": o.Positions = next().ToLowerInvariant(); break;
                    case "--axis": o.Axis = next().ToLowerInvariant(); break;
                    case "--gantry": o.GantryMode = next().ToLowerInvariant(); break;
                    case "--include-hold": o.IncludeHold = true; break;
                    case "--subbeam": subArg = next().Trim().ToLowerInvariant(); break;
                    case "--mlc-model": o.MlcModel = next(); break;
                    case "--scale": o.Scale = next().ToLowerInvariant(); break;
                    case "--report": o.ReportPath = next(); break;
                    case "--min-gap": o.MinGap = double.Parse(next(), NumberStyles.Float, Inv); break;
                    case "-h": case "--help": PrintUsage(); return 0;
                    default:
                        if (a.StartsWith("-")) throw new Exception("Unknown option " + a);
                        if (o.LogPath != null) throw new Exception("More than one log file given.");
                        o.LogPath = a; break;
                }
            }
            if (o.LogPath == null) throw new Exception("No log file given.");
            if (o.Positions != "actual" && o.Positions != "expected") throw new Exception("--positions must be actual or expected");
            if (o.Axis != "mu" && o.Axis != "cp") throw new Exception("--axis must be mu or cp");
            if (o.GantryMode != "plan" && o.GantryMode != "actual") throw new Exception("--gantry must be plan or actual");
            if (o.Scale != "varian" && o.Scale != "iec") throw new Exception("--scale must be varian or iec");

            Console.WriteLine("Reading log " + o.LogPath);
            LogData log = LogData.Load(o.LogPath);

            // ---------------- choose the subbeam(s) ----------------
            int cSub = log.Has("Subbeam") ? log.Col["Subbeam"] : -1;
            var subbeams = cSub < 0 ? new List<int> { 0 } :
                log.Rows.Where(r => !double.IsNaN(r[cSub])).Select(r => (int)Math.Round(r[cSub])).Distinct().OrderBy(x => x).ToList();
            string subList = string.Join(",", subbeams);
            var todo = new List<int>();
            if (subArg == null || subArg == "all")
            {
                todo.AddRange(subbeams);
                if (todo.Count > 1 && o.Templates.Count == 1)
                    throw new Exception("Log contains subbeams " + subList + " but only one template was given.\n" +
                        "       Choose the subbeam that template belongs to with --subbeam n,\n" +
                        "       or give one -t per subbeam, in subbeam order.");
            }
            else
            {
                foreach (string part in subArg.Split(Comma))
                {
                    int n;
                    if (!int.TryParse(part.Trim(), NumberStyles.Integer, Inv, out n))
                        throw new Exception("--subbeam must be 'all', a number or a comma-separated list, not '" + subArg + "'.");
                    if (!subbeams.Contains(n)) throw new Exception("Subbeam " + n + " not in log (log contains " + subList + ").");
                    if (!todo.Contains(n)) todo.Add(n);
                }
            }
            if (o.Templates.Count > 0 && o.Templates.Count != todo.Count && !(o.Templates.Count == 1 && todo.Count == 1))
                throw new Exception(Fmt("{0} templates given for {1} subbeam(s) ({2}). Give one -t per subbeam, in the same order.",
                    o.Templates.Count, todo.Count, string.Join(",", todo)));

            bool suffix = subbeams.Count > 1;   // log holds several fields -> tag file names with the subbeam
            int failed = 0;
            var summary = new List<string>();
            for (int i = 0; i < todo.Count; i++)
            {
                int sb = todo[i];
                string tpl = o.Templates.Count == 0 ? null : o.Templates[i];
                string tag = suffix ? "_sub" + sb : "";

                string outP;
                if (o.OutPath != null)
                    outP = todo.Count == 1 ? o.OutPath : InsertSuffix(o.OutPath, tag);
                else
                    outP = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(o.LogPath)),
                        System.IO.Path.GetFileNameWithoutExtension(o.LogPath) + tag + "_reconstructed_MLC.csv");
                string repP;
                if (o.ReportPath != null)
                    repP = todo.Count == 1 ? o.ReportPath : InsertSuffix(o.ReportPath, tag);
                else
                    repP = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(outP)),
                        System.IO.Path.GetFileNameWithoutExtension(outP) + "_report.txt");

                if (todo.Count > 1) { Console.WriteLine(); Console.WriteLine("===== Subbeam " + sb + " of " + subList + " ====="); }
                try
                {
                    summary.Add(ProcessSubbeam(log, o, cSub, sb, subList, tpl, outP, repP));
                }
                catch (Exception ex)
                {
                    failed++;
                    Console.Error.WriteLine("ERROR (subbeam " + sb + "): " + ex.Message);
                    summary.Add("Subbeam " + sb + ": FAILED - " + ex.Message);
                }
            }
            if (todo.Count > 1)
            {
                Console.WriteLine();
                Console.WriteLine("===== Summary =====");
                foreach (string s in summary) Console.WriteLine(s);
            }
            return failed > 0 ? 1 : 0;
        }

        static string InsertSuffix(string path, string tag)
        {
            string dir = System.IO.Path.GetDirectoryName(path);
            string name = System.IO.Path.GetFileNameWithoutExtension(path) + tag + System.IO.Path.GetExtension(path);
            return string.IsNullOrEmpty(dir) ? name : System.IO.Path.Combine(dir, name);
        }

        // MU_Actual must rise by more than this above its value at the start of the subbeam
        // before a snapshot counts as part of the delivery (log resolution is 1e-4 MU).
        internal const double SetupMuTol = 0.001;

        // Processes one subbeam (one Eclipse field) and returns a one-line summary.
        static string ProcessSubbeam(LogData log, Options o, int cSub, int subbeam, string subList,
                                     string templatePath, string outPath, string reportPath)
        {
            string logPath = o.LogPath, positions = o.Positions, axis = o.Axis, gantryMode = o.GantryMode;
            string mlcModel = o.MlcModel, scale = o.Scale;
            double minGap = o.MinGap;
            bool includeHold = o.IncludeHold;
            EclipseMlcFile template = templatePath != null ? EclipseMlcFile.Load(templatePath) : null;

            int cCp = log.Need("ControlPoint"), cMuE = log.Need("MU_Expected"), cMuA = log.Need("MU_Actual");
            int cGE = log.Need("GantryRtn_deg_Expected"), cGA = log.Need("GantryRtn_deg_Actual");
            int cCE = log.Need("CollRtn_deg_Expected");
            int cHold = log.Has("BeamHold_Actual") ? log.Col["BeamHold_Actual"] : -1;

            // ---------------- snapshots of this subbeam (copies: offsets are removed in place) ----------------
            var subRows = new List<double[]>();
            var subServo = new List<string>();
            for (int r = 0; r < log.Rows.Count; r++)
            {
                double[] row = log.Rows[r];
                if (cSub >= 0 && (double.IsNaN(row[cSub]) || (int)Math.Round(row[cSub]) != subbeam)) continue;
                if (double.IsNaN(row[cCp]) || double.IsNaN(row[cMuE]) || double.IsNaN(row[cMuA])) continue;
                subRows.Add((double[])row.Clone());
                subServo.Add(log.ServoState[r]);
            }
            if (subRows.Count == 0) throw new Exception("No snapshots for subbeam " + subbeam + ".");

            // ---------------- subbeam offsets ----------------
            // In a multi-subbeam log ControlPoint and MU are cumulative over the whole
            // delivery: subbeam 2 of a 5 x 166 CP log starts at CP 166 and at the MU where
            // subbeam 1 ended. Each subbeam also opens with set-up snapshots that deliver no
            // dose (gantry rotates / MLC moves to the new field, mostly in beam hold) while
            // ControlPoint slides from the previous field's last CP to this field's first.
            // So: skip everything before MU_Actual starts rising, take the field's first CP
            // as the integer just below the first dose snapshot, and make CP and MU local.
            double mu0E = subRows[0][cMuE], mu0A = subRows[0][cMuA];
            int nSetup = 0;
            while (nSetup < subRows.Count && subRows[nSetup][cMuA] - mu0A < SetupMuTol) nSetup++;
            if (nSetup >= subRows.Count) throw new Exception("Subbeam " + subbeam + " delivered no MU.");
            double cpFirst = double.PositiveInfinity;
            for (int s = nSetup; s < subRows.Count; s++) cpFirst = Math.Min(cpFirst, subRows[s][cCp]);
            int cp0 = (int)Math.Floor(cpFirst + 1e-6);

            var rows = new List<double[]>();
            int nHoldDropped = 0;
            for (int s = nSetup; s < subRows.Count; s++)
            {
                double[] row = subRows[s];
                bool beamOn = cHold >= 0 ? row[cHold] == 0 : string.Equals(subServo[s], "Normal", StringComparison.OrdinalIgnoreCase);
                if (!beamOn && !includeHold) { nHoldDropped++; continue; }
                row[cCp] -= cp0; row[cMuE] -= mu0E; row[cMuA] -= mu0A;
                rows.Add(row);
            }
            if (rows.Count < 10) throw new Exception("Too few beam-on snapshots in subbeam " + subbeam + ".");

            int nCp = (int)Math.Round(rows.Max(r => r[cCp])) + 1;
            if (template != null && template.Cps.Count != nCp)
                throw new Exception(Fmt("Template {0} has {1} control points but subbeam {2} covers log control points {3}-{4} ({5} control points).",
                    System.IO.Path.GetFileName(templatePath), template.Cps.Count, subbeam, cp0, cp0 + nCp - 1, nCp));
            int nLeaf = log.NumLeaves, nPair = nLeaf / 2;
            if (template != null && template.NumLeafValues != nLeaf)
                throw new Exception("Template has " + template.NumLeafValues + " leaf values per control point, log has " + nLeaf + " leaves.");

            // ---------------- planned MU at each control point ----------------
            double[] cpOfRow = rows.Select(r => r[cCp]).ToArray();
            double[] muKnots = FitPiecewiseLinear(cpOfRow, new[] { rows.Select(r => r[cMuE]).ToArray() }, nCp)[0];
            muKnots[0] = Math.Max(0.0, muKnots[0]);
            for (int k = 1; k < nCp; k++) if (muKnots[k] < muKnots[k - 1]) muKnots[k] = muKnots[k - 1];
            double muTotal = muKnots[nCp - 1];
            if (muTotal <= 0) throw new Exception("Total MU in log is zero.");

            // ---------------- sample coordinate ----------------
            double[] coord = new double[rows.Count];
            for (int s = 0; s < rows.Count; s++)
                coord[s] = axis == "cp" ? rows[s][cCp] : InverseInterp(muKnots, rows[s][cMuA]);

            // ---------------- leaf fits ----------------
            var ys = new double[nLeaf * 2][];   // [0..nLeaf) chosen positions, [nLeaf..2nLeaf) expected (for QA)
            for (int j = 0; j < nLeaf; j++)
            {
                int ca = log.Need(LogData.LeafCol(j + 1, positions == "actual" ? "Actual" : "Expected"));
                int ce = log.Need(LogData.LeafCol(j + 1, "Expected"));
                ys[j] = rows.Select(r => r[ca] * 10.0).ToArray();
                ys[nLeaf + j] = rows.Select(r => r[ce] * 10.0).ToArray();
            }
            // gantry, unwrapped, in IEC
            double[] gE = Unwrap(rows.Select(r => ToIec(r[cGE], scale)).ToArray());
            double[] gA = Unwrap(rows.Select(r => ToIec(r[cGA], scale)).ToArray());
            // expected-position QA fit and expected gantry use the log's own CP axis
            double[][] fitSel = FitPiecewiseLinear(coord, ys.Take(nLeaf).Concat(new[] { gA }).ToArray(), nCp);
            double[][] fitExp = FitPiecewiseLinear(cpOfRow, ys.Skip(nLeaf).Concat(new[] { gE }).ToArray(), nCp);

            double[,] recon = LeafToIec(fitSel, nCp, nPair);   // [cp, value index in Eclipse order]
            double[,] reconExp = LeafToIec(fitExp, nCp, nPair);
            int nClosed, nOpened, nTmp1, nTmp2; double gapShift, tmpShift;
            EnforceLeafGap(recon, nCp, nPair, minGap, out nClosed, out nOpened, out gapShift);
            EnforceLeafGap(reconExp, nCp, nPair, minGap, out nTmp1, out nTmp2, out tmpShift);
            int nRuns, nWidened, tmpRuns, tmpW; double pinShift;
            PinClosedRuns(recon, nCp, nPair, out nRuns, out nWidened, out pinShift);
            PinClosedRuns(reconExp, nCp, nPair, out tmpRuns, out tmpW, out tmpShift);
            int nGapViol = CountGapViolations(recon, nCp, nPair, minGap);
            int nDynViol = CountDynamicGapViolations(recon, nCp, nPair, minGap, 0.0);

            double coll = Median(rows.Select(r => ToIec(r[cCE], scale)).ToArray());
            string gantryHow;
            double[] gantryOut;
            if (gantryMode == "actual")
            {
                gantryOut = fitSel[nLeaf].Select(g => Wrap360(Math.Round(g, 2))).ToArray();
                gantryHow = "actual gantry from log, rounded to 0.01 deg";
            }
            else gantryOut = SnapGantry(fitExp[nLeaf], out gantryHow);

            // ---------------- write ----------------
            var outLines = new List<string>();
            if (template != null)
            {
                var dataSet = new HashSet<int>(template.DataLineIdx);
                int cp = 0;
                for (int i = 0; i < template.Lines.Count; i++)
                {
                    if (dataSet.Contains(i))
                    {
                        double[] t = template.Cps[cp];
                        var sb = new StringBuilder();
                        for (int k = 0; k < EclipseMlcFile.NonLeafFields; k++) { sb.Append(F(t[k])); sb.Append(','); }
                        AppendLeaves(sb, recon, cp, nLeaf);
                        outLines.Add(sb.ToString());
                        cp++;
                    }
                    else if (template.Lines[i].StartsWith("# Exported")) outLines.Add("# Exported " + ExportDate());
                    else outLines.Add(template.Lines[i]);
                }
            }
            else
            {
                outLines.Add("# VARIAN ARIA Eclipse - MLCPlan serialization");
                outLines.Add("# File format version: ");
                outLines.Add("1");
                outLines.Add("# Exported " + ExportDate());
                outLines.Add("# All values are IEC 61217 in millimeters");
                outLines.Add("# MLC Model - Must match on import");
                outLines.Add(mlcModel);
                outLines.Add("# Number of control points");
                outLines.Add(nCp.ToString(Inv));
                outLines.Add("# Control points");
                outLines.Add("# MetersetWeight,CollX1,CollX2,CollY1,CollY2,GantryRtn,CollRtn");
                outLines.Add("# MLCLeafPositions in Bank X1, Bank X2 order. For dual-layer MLCs the distal(patient side) leaf positions come before the proximal(source side) leaf positions.");
                for (int cp = 0; cp < nCp; cp++)
                {
                    double mw = cp == 0 ? 0.0 : (cp == nCp - 1 ? 1.0 : muKnots[cp] / muTotal);
                    var sb = new StringBuilder();
                    sb.Append(F(mw)).Append(',');
                    // No jaw data in the log. Halcyon/Ethos have no jaws (Eclipse uses +/-140 mm).
                    sb.Append(F(-140)).Append(',').Append(F(140)).Append(',').Append(F(-140)).Append(',').Append(F(140)).Append(',');
                    sb.Append(F(gantryOut[cp])).Append(',').Append(F(coll)).Append(',');
                    AppendLeaves(sb, recon, cp, nLeaf);
                    outLines.Add(sb.ToString());
                }
                outLines.Add("# EOF");
            }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(outPath)));
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(reportPath)));
            File.WriteAllText(outPath, string.Join("\r\n", outLines) + "\r\n", new UTF8Encoding(true));
            Console.WriteLine("Wrote " + outPath);

            // ---------------- QA report ----------------
            var rep = new StringBuilder();
            rep.AppendLine("Log -> Eclipse MLC reconstruction report");
            rep.AppendLine("Generated     : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", Inv));
            rep.AppendLine("Log file      : " + System.IO.Path.GetFullPath(logPath));
            rep.AppendLine("Output file   : " + System.IO.Path.GetFullPath(outPath));
            rep.AppendLine("Template      : " + (templatePath ?? "(none - header/MW/gantry derived from log)"));
            rep.AppendLine("Subbeam       : " + subbeam + "   (log contains " + subList + ")");
            rep.AppendLine(Fmt("Subbeam offset: log control points {0}-{1} -> 0-{2}; MU offset {3:F4} expected / {4:F4} actual",
                cp0, cp0 + nCp - 1, nCp - 1, mu0E, mu0A));
            rep.AppendLine("Positions     : " + positions + "    Axis: " + axis + "    Gantry: " + (template != null ? "template" : gantryMode));
            rep.AppendLine(Fmt("Snapshots     : {0} used, {1} beam-hold snapshots excluded, {2} set-up snapshots before dose skipped",
                rows.Count, nHoldDropped, nSetup));
            rep.AppendLine(Fmt("Control points: {0}   Leaves: {1}   Total MU (expected): {2:F2}   Delivered MU: {3:F2}",
                nCp, nLeaf, muTotal, rows.Max(r => r[cMuA])));
            rep.AppendLine(Fmt("Min dynamic leaf gap {0:F2} mm: {1} pair x CP closed exactly, {2} opened to {3:F3} mm (max leaf shift {4:F3} mm)",
                minGap, nClosed, nOpened, minGap + GapMargin, gapShift));
            rep.AppendLine(Fmt("Closed runs pinned stationary: {0} (neighbour apertures widened: {1}, max leaf shift {2:F3} mm)", nRuns, nWidened, pinShift));
            rep.AppendLine(Fmt("Remaining gaps between 0 and {0:F2} mm: {1}   Segments with a sub-{0:F2} mm pair translating: {2}", minGap, nGapViol, nDynViol));
            if (template == null) rep.AppendLine("Gantry        : " + gantryHow);
            rep.AppendLine();
            rep.AppendLine("Reconstructed vs reconstructed-expected (log expected positions, CP axis):");
            rep.AppendLine(DiffSummary(recon, reconExp, nCp, nLeaf, nPair));
            if (template != null)
            {
                double[,] plan = new double[nCp, nLeaf];
                for (int cp = 0; cp < nCp; cp++)
                    for (int k = 0; k < nLeaf; k++) plan[cp, k] = template.Cps[cp][EclipseMlcFile.NonLeafFields + k];
                rep.AppendLine("Method check - reconstructed EXPECTED vs template plan (should be ~0):");
                rep.AppendLine(DiffSummary(reconExp, plan, nCp, nLeaf, nPair));
                rep.AppendLine("Written file vs template plan:");
                rep.AppendLine(DiffSummary(recon, plan, nCp, nLeaf, nPair));
                double gMax = 0, mwMax = 0;
                for (int cp = 0; cp < nCp; cp++)
                {
                    gMax = Math.Max(gMax, Math.Abs(AngleDiff(fitExp[nLeaf][cp], template.Cps[cp][5])));
                    mwMax = Math.Max(mwMax, Math.Abs(muKnots[cp] / muTotal - template.Cps[cp][0]));
                }
                rep.AppendLine(Fmt("Log vs template consistency: max gantry diff {0:F3} deg, max meterset weight diff {1:F5}", gMax, mwMax));
                if (gMax > 0.5 || mwMax > 0.002)
                    rep.AppendLine("WARNING: the template does not look like the beam that was delivered in this log!");
                double collT = template.Cps[0][6];
                if (Math.Abs(AngleDiff(collT, coll)) > 0.5)
                    rep.AppendLine(Fmt("WARNING: collimator in log ({0:F1}) differs from template ({1:F1}).", coll, collT));
            }
            double gActMax = 0;
            for (int cp = 0; cp < nCp; cp++) gActMax = Math.Max(gActMax, Math.Abs(fitSel[nLeaf][cp] - fitExp[nLeaf][cp]));
            rep.AppendLine(Fmt("Max actual-vs-expected gantry at control points: {0:F3} deg", gActMax));
            File.WriteAllText(reportPath, rep.ToString(), new UTF8Encoding(false));
            Console.Write(rep.ToString());
            Console.WriteLine("Report: " + reportPath);
            double g0 = template != null ? template.Cps[0][5] : gantryOut[0];
            return Fmt("Subbeam {0}: {1} CPs (log CP {2}-{3}), {4:F2} MU, gantry {5:F1} deg IEC -> {6}",
                subbeam, nCp, cp0, cp0 + nCp - 1, muTotal, g0, System.IO.Path.GetFileName(outPath));
        }

        // Log leaf j (0-based): j < nPair  -> X2 of pair j  (same sign)
        //                       j >= nPair -> X1 of pair j-nPair (sign flipped)
        // Eclipse line order: X1(p0),X2(p0),X1(p1),X2(p1),... distal pairs first, then proximal.
        static double[,] LeafToIec(double[][] fit, int nCp, int nPair)
        {
            var o = new double[nCp, nPair * 2];
            for (int cp = 0; cp < nCp; cp++)
                for (int p = 0; p < nPair; p++)
                {
                    o[cp, 2 * p] = -fit[nPair + p][cp];   // X1
                    o[cp, 2 * p + 1] = fit[p][cp];        // X2
                }
            return o;
        }

        // Eclipse rejects any opposing-leaf gap that is neither exactly closed nor at
        // least the minimum dynamic leaf gap (0.5 mm for SX2). The fitted gaps scatter
        // by a few hundredths of a mm around the plan's 0 / 0.5 mm values, so:
        //   gap <  minGap/2 -> close the pair exactly at its centre
        //   gap <  minGap   -> open the pair symmetrically to minGap (+1 um margin)
        internal const double GapMargin = 0.001;
        static void EnforceLeafGap(double[,] v, int nCp, int nPair, double minGap,
                                   out int nClosed, out int nOpened, out double maxShift)
        {
            nClosed = 0; nOpened = 0; maxShift = 0;
            for (int cp = 0; cp < nCp; cp++)
                for (int p = 0; p < nPair; p++)
                {
                    double x1 = v[cp, 2 * p], x2 = v[cp, 2 * p + 1];
                    double gap = x2 - x1;
                    if (gap >= minGap + GapMargin * 0.5) continue;
                    double c = Math.Round(0.5 * (x1 + x2), 4);
                    double n1, n2;
                    if (gap < 0.5 * minGap)
                    {
                        n1 = c; n2 = c;
                        if (gap != 0) nClosed++;
                    }
                    else
                    {
                        double h = 0.5 * (minGap + GapMargin);
                        n1 = Math.Round(c - h, 6); n2 = Math.Round(c + h, 6);
                        nOpened++;
                    }
                    maxShift = Math.Max(maxShift, Math.Max(Math.Abs(n1 - x1), Math.Abs(n2 - x2)));
                    v[cp, 2 * p] = n1; v[cp, 2 * p + 1] = n2;
                }
        }

        // Eclipse also checks the motion between control points: while a pair is below
        // the minimum gap at either end of a segment, its two leaves may not travel in
        // the same direction (only converge / diverge / stay). A closed pair therefore
        // has to sit still for its whole closed run, at a position inside the open
        // aperture of the control point before and after the run. The fitted closed
        // positions wander by microns, so each closed run is pinned to one position.
        static void PinClosedRuns(double[,] v, int nCp, int nPair, out int nRuns, out int nWidened, out double maxShift)
        {
            nRuns = 0; nWidened = 0; maxShift = 0;
            for (int p = 0; p < nPair; p++)
            {
                int i1 = 2 * p, i2 = 2 * p + 1;
                int cp = 0;
                while (cp < nCp)
                {
                    if (v[cp, i1] != v[cp, i2]) { cp++; continue; }
                    int a = cp, b = cp;
                    while (b + 1 < nCp && v[b + 1, i1] == v[b + 1, i2]) b++;
                    double mean = 0;
                    for (int k = a; k <= b; k++) mean += v[k, i1];
                    mean /= (b - a + 1);
                    double lo = double.NegativeInfinity, hi = double.PositiveInfinity;
                    if (a > 0) { lo = Math.Max(lo, v[a - 1, i1]); hi = Math.Min(hi, v[a - 1, i2]); }
                    if (b < nCp - 1) { lo = Math.Max(lo, v[b + 1, i1]); hi = Math.Min(hi, v[b + 1, i2]); }
                    double c = lo <= hi ? Math.Min(Math.Max(mean, lo), hi) : mean;
                    c = Math.Round(c, 4);
                    for (int k = a; k <= b; k++)
                    {
                        maxShift = Math.Max(maxShift, Math.Abs(v[k, i1] - c));
                        v[k, i1] = c; v[k, i2] = c;
                    }
                    // if no single position fits both neighbours, open the neighbour's
                    // aperture just enough to contain c (its gap only gets bigger)
                    foreach (int nb in new int[] { a - 1, b + 1 })
                    {
                        if (nb < 0 || nb >= nCp) continue;
                        if (v[nb, i1] > c) { maxShift = Math.Max(maxShift, v[nb, i1] - c); v[nb, i1] = c; nWidened++; }
                        if (v[nb, i2] < c) { maxShift = Math.Max(maxShift, c - v[nb, i2]); v[nb, i2] = c; nWidened++; }
                    }
                    nRuns++;
                    cp = b + 1;
                }
            }
        }

        // Segments Eclipse would flag: gap below minGap at either end and both leaves
        // moving the same way by more than tol.
        static int CountDynamicGapViolations(double[,] v, int nCp, int nPair, double minGap, double tol)
        {
            int n = 0;
            for (int cp = 0; cp + 1 < nCp; cp++)
                for (int p = 0; p < nPair; p++)
                {
                    double g0 = v[cp, 2 * p + 1] - v[cp, 2 * p], g1 = v[cp + 1, 2 * p + 1] - v[cp + 1, 2 * p];
                    if (g0 >= minGap - 1e-6 && g1 >= minGap - 1e-6) continue;
                    double d1 = v[cp + 1, 2 * p] - v[cp, 2 * p], d2 = v[cp + 1, 2 * p + 1] - v[cp, 2 * p + 1];
                    if (d1 * d2 > 0 && Math.Min(Math.Abs(d1), Math.Abs(d2)) > tol) n++;
                }
            return n;
        }

        static int CountGapViolations(double[,] v, int nCp, int nPair, double minGap)
        {
            int n = 0;
            for (int cp = 0; cp < nCp; cp++)
                for (int p = 0; p < nPair; p++)
                {
                    // compare as Eclipse stores them (single precision)
                    float x1 = (float)Math.Round(v[cp, 2 * p], 6), x2 = (float)Math.Round(v[cp, 2 * p + 1], 6);
                    float g = x2 - x1;
                    if (g != 0f && g < (float)minGap) n++;
                }
            return n;
        }

        // VMAT arcs in Eclipse have equally spaced control points between a start and
        // stop angle given to 0.1 deg. Rebuild that exactly from the fitted gantry;
        // fall back to 0.01 deg rounding if the log is not an evenly spaced arc.
        static double[] SnapGantry(double[] gUnwrapped, out string how)
        {
            int n = gUnwrapped.Length;
            double start = Math.Round(gUnwrapped[0], 1), stop = Math.Round(gUnwrapped[n - 1], 1);
            double step = (stop - start) / (n - 1);
            var lin = new double[n];
            double dev = 0;
            for (int k = 0; k < n; k++)
            {
                lin[k] = start + k * step;
                dev = Math.Max(dev, Math.Abs(lin[k] - gUnwrapped[k]));
            }
            var o = new double[n];
            if (dev < 0.05)
            {
                how = step == 0
                    ? Fmt("static gantry {0:F1} deg (max adjustment {1:F4} deg)", Wrap360(start), dev)
                    : Fmt("evenly spaced arc {0:F1} -> {1:F1} deg, step {2:F4} deg (max adjustment {3:F4} deg)",
                        Wrap360(start), Wrap360(stop), step, dev);
                for (int k = 0; k < n; k++) o[k] = Wrap360(Math.Round(lin[k], 6));
            }
            else
            {
                how = Fmt("not evenly spaced (max deviation {0:F3} deg) - rounded to 0.01 deg", dev);
                for (int k = 0; k < n; k++) o[k] = Wrap360(Math.Round(gUnwrapped[k], 2));
            }
            return o;
        }

        static void AppendLeaves(StringBuilder sb, double[,] v, int cp, int nLeaf)
        {
            for (int k = 0; k < nLeaf; k++)
            {
                if (k > 0) sb.Append(',');
                sb.Append(F(v[cp, k]));
            }
        }

        static string DiffSummary(double[,] a, double[,] b, int nCp, int nVal, int nPair)
        {
            double max = 0, sum2 = 0; int worstCp = 0, worstK = 0; long n = 0;
            int over05 = 0, over1 = 0;
            for (int cp = 0; cp < nCp; cp++)
                for (int k = 0; k < nVal; k++)
                {
                    double d = a[cp, k] - b[cp, k];
                    double ad = Math.Abs(d);
                    sum2 += d * d; n++;
                    if (ad > 0.5) over05++;
                    if (ad > 1.0) over1++;
                    if (ad > max) { max = ad; worstCp = cp; worstK = k; }
                }
            int pair = worstK / 2;
            string layer = pair < nPair / 2 ? "distal" : "proximal";   // SX2: 28 distal, 29 proximal
            return Fmt(
                "  RMS {0:F3} mm | max {1:F3} mm at CP {2}, pair {3} ({4}) {5} | values >0.5 mm: {6}, >1 mm: {7}",
                Math.Sqrt(sum2 / n), max, worstCp, pair + 1, layer, worstK % 2 == 0 ? "X1" : "X2", over05, over1);
        }

        // ---------------------------------------------------------------------
        //  Least-squares piecewise-linear fit with knots at 0..n-1.
        //  sample s at coordinate c in [k,k+1]:  y ~ (1-t) x_k + t x_{k+1}
        //  A tiny second-difference penalty keeps the system well posed when an
        //  interval has no samples (it then bridges linearly) without biasing
        //  well-sampled intervals.
        // ---------------------------------------------------------------------
        static double[][] FitPiecewiseLinear(double[] c, double[][] ys, int n)
        {
            const double lambda = 1e-4;
            var M = new double[n, n];
            var B = new double[ys.Length][];
            for (int q = 0; q < ys.Length; q++) B[q] = new double[n];

            for (int s = 0; s < c.Length; s++)
            {
                double x = Math.Min(Math.Max(c[s], 0.0), n - 1);
                int k = (int)Math.Floor(x);
                if (k >= n - 1) k = n - 2;
                double t = x - k, w0 = 1 - t, w1 = t;
                M[k, k] += w0 * w0; M[k, k + 1] += w0 * w1; M[k + 1, k] += w0 * w1; M[k + 1, k + 1] += w1 * w1;
                for (int q = 0; q < ys.Length; q++)
                {
                    double y = ys[q][s];
                    if (double.IsNaN(y)) continue;
                    B[q][k] += w0 * y; B[q][k + 1] += w1 * y;
                }
            }
            for (int k = 1; k < n - 1; k++)
            {
                int[] idx = { k - 1, k, k + 1 };
                double[] d = { 1, -2, 1 };
                for (int a = 0; a < 3; a++)
                    for (int b = 0; b < 3; b++) M[idx[a], idx[b]] += lambda * d[a] * d[b];
            }
            for (int k = 0; k < n; k++) M[k, k] += 1e-12;

            double[,] L = Cholesky(M, n);
            var res = new double[ys.Length][];
            for (int q = 0; q < ys.Length; q++) res[q] = CholSolve(L, B[q], n);
            return res;
        }

        static double[,] Cholesky(double[,] A, int n)
        {
            var L = new double[n, n];
            for (int i = 0; i < n; i++)
                for (int j = 0; j <= i; j++)
                {
                    double s = A[i, j];
                    for (int k = 0; k < j; k++) s -= L[i, k] * L[j, k];
                    if (i == j)
                    {
                        if (s <= 0) throw new Exception("Fit matrix not positive definite (control point " + i + " has no data).");
                        L[i, i] = Math.Sqrt(s);
                    }
                    else L[i, j] = s / L[j, j];
                }
            return L;
        }

        static double[] CholSolve(double[,] L, double[] b, int n)
        {
            var y = new double[n];
            for (int i = 0; i < n; i++)
            {
                double s = b[i];
                for (int k = 0; k < i; k++) s -= L[i, k] * y[k];
                y[i] = s / L[i, i];
            }
            var x = new double[n];
            for (int i = n - 1; i >= 0; i--)
            {
                double s = y[i];
                for (int k = i + 1; k < n; k++) s -= L[k, i] * x[k];
                x[i] = s / L[i, i];
            }
            return x;
        }

        // fractional index at which monotone 'knots' reaches 'value'
        static double InverseInterp(double[] knots, double value)
        {
            int n = knots.Length;
            if (value <= knots[0]) return 0;
            if (value >= knots[n - 1]) return n - 1;
            int lo = 0, hi = n - 1;
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (knots[mid] < value) lo = mid; else hi = mid;
            }
            double span = knots[hi] - knots[lo];
            return span <= 0 ? hi : lo + (value - knots[lo]) / span;
        }

        static double ToIec(double angle, string scale)
        {
            return scale == "varian" ? Wrap360(180.0 - angle) : Wrap360(angle);
        }

        static double Wrap360(double a)
        {
            a %= 360.0;
            if (a < 0) a += 360.0;
            if (a >= 359.99995) a = 0;
            return a;
        }

        static double AngleDiff(double a, double b)
        {
            double d = (a - b) % 360.0;
            if (d > 180) d -= 360; if (d < -180) d += 360;
            return d;
        }

        static double[] Unwrap(double[] a)
        {
            var o = new double[a.Length];
            if (a.Length == 0) return o;
            o[0] = a[0];
            for (int i = 1; i < a.Length; i++) o[i] = o[i - 1] + AngleDiff(a[i], a[i - 1]);
            return o;
        }

        static double Median(double[] a)
        {
            var s = a.OrderBy(x => x).ToArray();
            return s[s.Length / 2];
        }

        static string F(double v)
        {
            if (Math.Abs(v) < 5e-7) v = 0;
            return v.ToString("0.000000", Inv);
        }

        static string ExportDate()
        {
            return DateTime.Now.ToString("dddd, d MMMM yyyy h:mm:ss tt", CultureInfo.GetCultureInfo("en-US"));
        }
    }
}
