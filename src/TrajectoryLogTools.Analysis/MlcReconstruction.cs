// MlcReconstruction.cs - rebuilds an Eclipse "MLCPlan serialization" file (the CSV Eclipse
// exports / imports for a beam's MLC sequence) from a delivery log. Ported from
// LogToEclipseMlc.cs; the method and the output are unchanged.
//
//  Method (per field / subbeam):
//    0. A log may hold several subbeams (e.g. a 5-field IMRT plan delivered as one log). Each
//       subbeam is one Eclipse field and gets its own output file. ControlPoint and MU in such
//       logs are cumulative over the whole delivery, and each subbeam opens with no-dose set-up
//       snapshots while the gantry/MLC move to the new field. Those set-up snapshots are skipped
//       and the subbeam's first control point and starting MU are subtracted, so every field is
//       fitted on its own 0..N-1 control points and 0..MU range.
//    1. Keep only beam-on snapshots (dose servo not in Hold). Snapshots taken during a beam hold
//       deliver no dose and, on a VMAT interrupt, the machine rewinds ControlPoint, so they must
//       not influence the fit.
//    2. Give every snapshot a fractional control-point coordinate:
//         axis "mu" (default): from the ACTUAL delivered MU, mapped onto the plan's
//                              MU-vs-control-point relation, i.e. "where were the leaves when
//                              this fraction of the dose was actually delivered"
//         axis "cp"          : the log's own ControlPoint column (which tracks the EXPECTED MU)
//    3. Eclipse models every axis as moving linearly in meterset between control points. So for
//       each leaf a piecewise-linear function whose knots sit exactly on the integer control
//       points is least-squares fitted to all the beam-on samples. The knot values are the
//       reconstructed control-point leaf positions. (Simply interpolating the 20 ms samples at
//       each integer CP cuts the corners of the trajectory - up to ~0.7 mm on clinical logs.)
//    4. Convert log values (Varian Standard scale, cm, both banks positive = open) to Eclipse
//       IEC 61217 mm in X1/X2 pair-interleaved order (see MlcGeometry), and write the file.
//
//  If an Eclipse export of the same beam is given as a template, its header, meterset weights,
//  jaws, gantry and collimator angles are copied verbatim and ONLY the leaf positions are
//  replaced. This is the safest way to get a file that Eclipse will accept back onto the beam.
//
//  Verified against Halcyon/Ethos SX2 (dual layer, 28 distal + 29 proximal pairs) logs. Other
//  MLC types are refused until they are defined (and verified) in MlcGeometry.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TrajectoryLogTools.Core;

namespace TrajectoryLogTools.Analysis
{
    public sealed class MlcReconstructionOptions
    {
        /// <summary>Output MLC file; null = &lt;log&gt;[_subN]_reconstructed_MLC.csv next to the log.</summary>
        public string OutPath;
        /// <summary>QA report; null = &lt;out&gt;_report.txt.</summary>
        public string ReportPath;
        /// <summary>Eclipse MLC exports of the same beams, one per processed subbeam, in subbeam order.</summary>
        public List<string> Templates = new List<string>();
        /// <summary>"actual" or "expected" leaf positions.</summary>
        public string Positions = "actual";
        /// <summary>"mu" (delivered MU) or "cp" (log control point) as the sample coordinate.</summary>
        public string Axis = "mu";
        /// <summary>Without template: gantry from the "plan" (expected) or "actual" values.</summary>
        public string GantryMode = "plan";
        /// <summary>Angle scale of the log: "auto" (from the .bin header; CSV = varian), "varian" or "iec".</summary>
        public string Scale = "auto";
        /// <summary>MLC model written without template; null = the log's MLC (e.g. SX2).</summary>
        public string MlcModel;
        /// <summary>Minimum dynamic leaf gap in mm; null = the MLC's (0.5 for SX2).</summary>
        public double? MinGap;
        public bool IncludeHold;
        /// <summary>Subbeams to process: null / "all", or e.g. "2" or "1,3".</summary>
        public string Subbeams;
        /// <summary>Folder for the default output names when OutPath is null; null = the log's folder.</summary>
        public string OutputFolder;

        public void Validate()
        {
            Positions = (Positions ?? "actual").ToLowerInvariant();
            Axis = (Axis ?? "mu").ToLowerInvariant();
            GantryMode = (GantryMode ?? "plan").ToLowerInvariant();
            Scale = (Scale ?? "auto").ToLowerInvariant();
            if (Positions != "actual" && Positions != "expected") throw new ArgumentException("positions must be actual or expected");
            if (Axis != "mu" && Axis != "cp") throw new ArgumentException("axis must be mu or cp");
            if (GantryMode != "plan" && GantryMode != "actual") throw new ArgumentException("gantry must be plan or actual");
            if (Scale != "auto" && Scale != "varian" && Scale != "iec") throw new ArgumentException("scale must be auto, varian or iec");
            if (MinGap.HasValue && (MinGap.Value < 0 || double.IsNaN(MinGap.Value))) throw new ArgumentException("min-gap must be a number >= 0");
        }
    }

    /// <summary>Outcome for one subbeam (one Eclipse field).</summary>
    public sealed class SubbeamResult
    {
        public int Subbeam { get; set; }
        public bool Ok { get; set; }
        public string Error { get; set; }
        /// <summary>One-line summary, e.g. "Subbeam 1: 41 CPs (log CP 0-40), 150.00 MU, gantry 130.0 deg IEC -> x.csv".</summary>
        public string Summary { get; set; }
        public string OutputPath { get; set; }
        public string ReportPath { get; set; }
        public string TemplatePath { get; set; }
        public string Report { get; set; }
        public int ControlPoints { get; set; }
        /// <summary>Log control point of this field's control point 0.</summary>
        public int FirstLogControlPoint { get; set; }
        /// <summary>Written leaf positions [cp, Eclipse value index], mm IEC.</summary>
        public double[,] Reconstructed { get; set; }
        /// <summary>The same reconstruction from the log's expected positions (method check).</summary>
        public double[,] ReconstructedExpected { get; set; }
        /// <summary>Leaf positions of the template, or null without template.</summary>
        public double[,] TemplatePlan { get; set; }

        /// <summary>Ready (all checks passed), CheckFirst (written, but see Problems) or Failed.</summary>
        public RebuildStatus Status { get; set; }
        /// <summary>Why the status is CheckFirst, one sentence each; empty when Ready.</summary>
        public List<string> Problems { get; set; } = new List<string>();
        /// <summary>Planned MU of the field and gantry of its first control point (IEC).</summary>
        public double Mu { get; set; }
        public double GantryStartIec { get; set; }
        /// <summary>How the gantry angles were written, e.g. "evenly spaced arc 130.0 -> 230.0 deg ...".</summary>
        public string GantryNote { get; set; }
        /// <summary>Control-point intervals k..k+1 (field CP numbering) with no beam-on sample to fit.</summary>
        public List<int> EmptyIntervals { get; set; } = new List<int>();
        /// <summary>Leaf gaps or closed-pair moves Eclipse would reject, after the gap rules were applied.</summary>
        public int RemainingGapProblems { get; set; }
        /// <summary>Gaps closed exactly / opened to the minimum gap by the rules.</summary>
        public int GapsClosed { get; set; }
        public int GapsOpened { get; set; }
        /// <summary>Written (actual) minus planned (expected) positions: RMS and max |difference|, mm, and where.</summary>
        public double DeliveredRmsMm { get; set; }
        public double DeliveredMaxMm { get; set; }
        public string DeliveredMaxAt { get; set; }
        /// <summary>With a template: reconstruction from expected positions vs the template, max |difference| mm.</summary>
        public double MethodCheckMaxMm { get; set; } = double.NaN;

        // The numbers of the report, for showing without the text.
        /// <summary>"actual" / "expected", "mu" / "cp", and where the gantry came from ("plan", "actual", "template").</summary>
        public string Positions { get; set; }
        public string Axis { get; set; }
        public string GantrySource { get; set; }
        public int Leaves { get; set; }
        public double DeliveredMu { get; set; }
        public int SnapshotsUsed { get; set; }
        public int HoldSnapshotsExcluded { get; set; }
        public int SetupSnapshotsSkipped { get; set; }
        public double MinGapMm { get; set; }
        /// <summary>Largest leaf move made by the gap rules and by pinning closed runs, mm.</summary>
        public double MaxGapShiftMm { get; set; }
        public double MaxPinShiftMm { get; set; }
        public int ClosedRunsPinned { get; set; }
        public double CollimatorDeg { get; set; }
        /// <summary>Largest actual - expected gantry at the control points, degrees.</summary>
        public double GantryActualVsExpectedMaxDeg { get; set; }
        /// <summary>With a template: largest gantry and meterset weight difference between the log and the template (NaN without).</summary>
        public double TemplateGantryMaxDeg { get; set; } = double.NaN;
        public double TemplateMetersetMax { get; set; } = double.NaN;
    }

    public enum RebuildStatus { Ready, CheckFirst, Failed }

    public sealed class MlcReconstructionResult
    {
        public List<int> SubbeamsInLog = new List<int>();
        public List<SubbeamResult> Subbeams = new List<SubbeamResult>();
        public MlcGeometry Mlc;
        public int Failed { get { return Subbeams.Count(s => !s.Ok); } }
    }

    /// <summary>The delivery log as rows of numbers, in the layout the reconstruction was written for.</summary>
    internal sealed class LogData
    {
        public List<string> Header;
        public Dictionary<string, int> Col = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        public List<double[]> Rows = new List<double[]>();
        public List<string> ServoState = new List<string>();
        public int NumLeaves;

        public int Need(string name)
        {
            int i;
            if (!Col.TryGetValue(name, out i))
                throw new InvalidDataException("Log file is missing column '" + name + "'.");
            return i;
        }

        public bool Has(string name) { return Col.ContainsKey(name); }

        public static LogData From(DeliveryRecord r)
        {
            var log = new LogData { Header = r.ColumnNames.ToList() };
            for (int i = 0; i < log.Header.Count; i++)
                if (!log.Col.ContainsKey(log.Header[i])) log.Col[log.Header[i]] = i;
            double[][] cols = log.Header.Select(r.Column).ToArray();
            for (int s = 0; s < r.RowCount; s++)
            {
                var v = new double[cols.Length];
                for (int i = 0; i < cols.Length; i++) v[i] = cols[i][s];
                log.Rows.Add(v);
                log.ServoState.Add(r.ServoState[s]);
            }
            int n = r.LeafCount;
            if (n == 0 || n % 2 != 0)
                throw new InvalidDataException("Could not find an even number of MLC_LeafNNN_cm_Expected/_Actual columns.");
            log.NumLeaves = n;
            return log;
        }
    }

    public sealed class EclipseMlcFile
    {
        public List<string> Lines = new List<string>();   // full file, data lines included
        public List<int> DataLineIdx = new List<int>();   // indices into Lines
        public List<double[]> Cps = new List<double[]>(); // parsed data lines
        public string MlcModel;
        public int DeclaredCps = -1;

        static readonly char[] Bom = { '\uFEFF' };
        static readonly char[] Comma = { ',' };

        public const int NonLeafFields = 7; // MetersetWeight,CollX1,CollX2,CollY1,CollY2,GantryRtn,CollRtn

        public int NumLeafValues { get { return Cps.Count > 0 ? Cps[0].Length - NonLeafFields : 0; } }

        public static EclipseMlcFile Load(string path)
        {
            var f = new EclipseMlcFile();
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            string prev = "";
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i].TrimStart(Bom);
                f.Lines.Add(l);
                string t = l.Trim();
                if (t.Length == 0) { continue; }
                if (t.StartsWith("#")) { prev = t; continue; }
                if (t.Contains(","))
                {
                    double[] v = t.Split(Comma).Select(s => double.Parse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture)).ToArray();
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

    public static class MlcReconstructor
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string Fmt(string format, params object[] args) { return string.Format(Inv, format, args); }

        /// <summary>Subbeam numbers in the log (one Eclipse field each); { 0 } if it has no Subbeam column.</summary>
        public static List<int> SubbeamsIn(DeliveryRecord record)
        {
            double[] sub = record.TryColumn("Subbeam");
            if (sub == null) return new List<int> { 0 };
            return sub.Where(v => !double.IsNaN(v)).Select(v => (int)Math.Round(v)).Distinct().OrderBy(x => x).ToList();
        }

        /// <summary>
        /// Reconstructs every selected subbeam, writing the MLC file and QA report of each.
        /// Progress and the reports go to <paramref name="say"/>. Errors in a single subbeam are
        /// recorded in its result; errors in the options or the log throw.
        /// </summary>
        public static MlcReconstructionResult Run(DeliveryRecord record, string logPath, MlcReconstructionOptions o, Action<string> say = null)
        {
            say = say ?? (s => { });
            o.Validate();
            MlcGeometry geom = record.Mlc;
            if (geom == null) throw new InvalidDataException(MlcGeometry.UnsupportedMessage(record.LeafCount));
            string scale = o.Scale != "auto" ? o.Scale : (record.AxisScale == 2 ? "iec" : "varian");
            string mlcModel = string.IsNullOrEmpty(o.MlcModel) ? geom.Name : o.MlcModel;
            double minGap = o.MinGap ?? geom.MinDynamicGapMm;
            LogData log = LogData.From(record);
            var result = new MlcReconstructionResult { Mlc = geom };

            // ---------------- choose the subbeam(s) ----------------
            int cSub = log.Has("Subbeam") ? log.Col["Subbeam"] : -1;
            var subbeams = SubbeamsIn(record);
            result.SubbeamsInLog.AddRange(subbeams);
            string subList = string.Join(",", subbeams);
            string subArg = string.IsNullOrWhiteSpace(o.Subbeams) ? null : o.Subbeams.Trim().ToLowerInvariant();
            var todo = new List<int>();
            if (subArg == null || subArg == "all")
            {
                todo.AddRange(subbeams);
                if (todo.Count > 1 && o.Templates.Count == 1)
                    throw new ArgumentException("Log contains subbeams " + subList + " but only one template was given. " +
                        "Choose the subbeam that template belongs to, or give one template per subbeam, in subbeam order.");
            }
            else
            {
                foreach (string part in subArg.Split(','))
                {
                    int n;
                    if (!int.TryParse(part.Trim(), NumberStyles.Integer, Inv, out n))
                        throw new ArgumentException("subbeam must be 'all', a number or a comma-separated list, not '" + subArg + "'.");
                    if (!subbeams.Contains(n)) throw new ArgumentException("Subbeam " + n + " not in log (log contains " + subList + ").");
                    if (!todo.Contains(n)) todo.Add(n);
                }
            }
            if (o.Templates.Count > 0 && o.Templates.Count != todo.Count && !(o.Templates.Count == 1 && todo.Count == 1))
                throw new ArgumentException(Fmt("{0} templates given for {1} subbeam(s) ({2}). Give one template per subbeam, in the same order.",
                    o.Templates.Count, todo.Count, string.Join(",", todo)));

            say("Log " + logPath + "  (MLC: " + geom.Description + ", angle scale: " + scale + ")");
            bool suffix = subbeams.Count > 1;   // log holds several fields -> tag file names with the subbeam
            for (int i = 0; i < todo.Count; i++)
            {
                int sb = todo[i];
                string tpl = o.Templates.Count == 0 ? null : o.Templates[i];
                string tag = suffix ? "_sub" + sb : "";

                string outP;
                if (o.OutPath != null)
                    outP = todo.Count == 1 ? o.OutPath : InsertSuffix(o.OutPath, tag);
                else
                    outP = Path.Combine(o.OutputFolder ?? Path.GetDirectoryName(Path.GetFullPath(logPath)),
                        Path.GetFileNameWithoutExtension(logPath) + tag + "_reconstructed_MLC.csv");
                string repP;
                if (o.ReportPath != null)
                    repP = todo.Count == 1 ? o.ReportPath : InsertSuffix(o.ReportPath, tag);
                else
                    repP = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outP)),
                        Path.GetFileNameWithoutExtension(outP) + "_report.txt");

                if (todo.Count > 1) { say(""); say("===== Subbeam " + sb + " of " + subList + " ====="); }
                var res = new SubbeamResult { Subbeam = sb, TemplatePath = tpl };
                try
                {
                    res.Summary = ProcessSubbeam(log, o, logPath, geom, scale, mlcModel, minGap, cSub, sb, subList, tpl, outP, repP, res, say);
                    res.Ok = true;
                }
                catch (Exception ex)
                {
                    res.Status = RebuildStatus.Failed;
                    res.Error = ex.Message;
                    res.Summary = "Subbeam " + sb + ": FAILED - " + ex.Message;
                    say("ERROR (subbeam " + sb + "): " + ex.Message);
                }
                result.Subbeams.Add(res);
            }
            if (todo.Count > 1)
            {
                say("");
                say("===== Summary =====");
                foreach (var r in result.Subbeams) say(r.Summary);
            }
            return result;
        }

        static string InsertSuffix(string path, string tag)
        {
            string dir = Path.GetDirectoryName(path);
            string name = Path.GetFileNameWithoutExtension(path) + tag + Path.GetExtension(path);
            return string.IsNullOrEmpty(dir) ? name : Path.Combine(dir, name);
        }

        // MU_Actual must rise by more than this above its value at the start of the subbeam
        // before a snapshot counts as part of the delivery (log resolution is 1e-4 MU).
        public const double SetupMuTol = 0.001;

        // Processes one subbeam (one Eclipse field) and returns a one-line summary.
        static string ProcessSubbeam(LogData log, MlcReconstructionOptions o, string logPath, MlcGeometry geom,
                                     string scale, string mlcModel, double minGap, int cSub, int subbeam, string subList,
                                     string templatePath, string outPath, string reportPath,
                                     SubbeamResult res, Action<string> say)
        {
            string positions = o.Positions, axis = o.Axis, gantryMode = o.GantryMode;
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
            if (subRows.Count == 0) throw new InvalidDataException("No snapshots for subbeam " + subbeam + ".");

            // ---------------- subbeam offsets ----------------
            // In a multi-subbeam log ControlPoint and MU are cumulative over the whole
            // delivery: subbeam 2 of a 5 x 166 CP log starts at CP 166 and at the MU where
            // subbeam 1 ended. Each subbeam also opens with set-up snapshots that deliver no
            // dose (gantry rotates / MLC moves to the new field, mostly in beam hold) while
            // ControlPoint slides from the previous field's last CP to this field's first.
            // So: skip everything before MU_Actual starts rising, take the field's first CP
            // as the integer just below the first dose snapshot, and make CP and MU local.
            // The set-up also includes the transition step at the start of every field after the
            // first (see FieldSpan): it lasts until the PLANNED MU starts rising, which the
            // delivered MU does not show reliably (it creeps up during the set-up beam hold).
            double mu0E = subRows[0][cMuE], mu0A = subRows[0][cMuA];
            double fieldStart = FieldSpan.FirstControlPoint(subRows.Select(r => r[cCp]).ToList(), subRows.Select(r => r[cMuE]).ToList());
            int nSetup = 0;
            while (nSetup < subRows.Count && (subRows[nSetup][cMuA] - mu0A < SetupMuTol ||
                                               (!double.IsNaN(fieldStart) && subRows[nSetup][cCp] < fieldStart - 1e-6))) nSetup++;
            if (nSetup >= subRows.Count) throw new InvalidDataException("Subbeam " + subbeam + " delivered no MU.");
            double cpFirst = double.PositiveInfinity;
            for (int s = nSetup; s < subRows.Count; s++) cpFirst = Math.Min(cpFirst, subRows[s][cCp]);
            int cp0 = (int)Math.Floor(cpFirst + 1e-6);

            var rows = new List<double[]>();
            int nHoldDropped = 0;
            for (int s = nSetup; s < subRows.Count; s++)
            {
                double[] row = subRows[s];
                // Beam on = dose servo not in Hold (Normal, or Freeze / Disabled in Service mode).
                bool beamOn = cHold >= 0 && !double.IsNaN(row[cHold]) ? TrajectoryLog.IsBeamOn(row[cHold])
                                         : !string.Equals(subServo[s], "Hold", StringComparison.OrdinalIgnoreCase);
                if (!beamOn && !includeHold) { nHoldDropped++; continue; }
                row[cCp] -= cp0; row[cMuE] -= mu0E; row[cMuA] -= mu0A;
                rows.Add(row);
            }
            if (rows.Count < 10) throw new InvalidDataException("Too few beam-on snapshots in subbeam " + subbeam + ".");

            int nCp = (int)Math.Round(rows.Max(r => r[cCp])) + 1;
            if (template != null && template.Cps.Count != nCp)
                throw new InvalidDataException(Fmt("Template {0} has {1} control points but subbeam {2} covers log control points {3}-{4} ({5} control points).",
                    System.IO.Path.GetFileName(templatePath), template.Cps.Count, subbeam, cp0, cp0 + nCp - 1, nCp));
            int nLeaf = log.NumLeaves, nPair = nLeaf / 2;
            if (template != null && template.NumLeafValues != nLeaf)
                throw new InvalidDataException("Template has " + template.NumLeafValues + " leaf values per control point, log has " + nLeaf + " leaves.");

            // ---------------- planned MU at each control point ----------------
            double[] cpOfRow = rows.Select(r => r[cCp]).ToArray();
            double[] muKnots = FitPiecewiseLinear(cpOfRow, new[] { rows.Select(r => r[cMuE]).ToArray() }, nCp)[0];
            muKnots[0] = Math.Max(0.0, muKnots[0]);
            for (int k = 1; k < nCp; k++) if (muKnots[k] < muKnots[k - 1]) muKnots[k] = muKnots[k - 1];
            double muTotal = muKnots[nCp - 1];
            if (muTotal <= 0) throw new InvalidDataException("Total MU in log is zero.");

            // ---------------- sample coordinate ----------------
            double[] coord = new double[rows.Count];
            for (int s = 0; s < rows.Count; s++)
                coord[s] = axis == "cp" ? rows[s][cCp] : InverseInterp(muKnots, rows[s][cMuA]);
            // control-point intervals without a sample (the fit bridges them linearly)
            var perInterval = new int[Math.Max(1, nCp - 1)];
            foreach (double c in coord)
            {
                double x = Math.Min(Math.Max(c, 0.0), nCp - 1);
                int k = Math.Min((int)Math.Floor(x), nCp - 2);
                if (k >= 0) perInterval[k]++;
            }

            // ---------------- leaf fits ----------------
            var ys = new double[nLeaf * 2][];   // [0..nLeaf) chosen positions, [nLeaf..2nLeaf) expected (for QA)
            for (int j = 0; j < nLeaf; j++)
            {
                int ca = log.Need(DeliveryRecord.LeafColumn(j + 1, positions == "actual" ? "Actual" : "Expected"));
                int ce = log.Need(DeliveryRecord.LeafColumn(j + 1, "Expected"));
                ys[j] = rows.Select(r => r[ca] * 10.0).ToArray();
                ys[nLeaf + j] = rows.Select(r => r[ce] * 10.0).ToArray();
            }
            // gantry, unwrapped, in IEC
            double[] gE = Unwrap(rows.Select(r => ToIec(r[cGE], scale)).ToArray());
            double[] gA = Unwrap(rows.Select(r => ToIec(r[cGA], scale)).ToArray());
            // expected-position QA fit and expected gantry use the log's own CP axis
            double[][] fitSel = FitPiecewiseLinear(coord, ys.Take(nLeaf).Concat(new[] { gA }).ToArray(), nCp);
            double[][] fitExp = FitPiecewiseLinear(cpOfRow, ys.Skip(nLeaf).Concat(new[] { gE }).ToArray(), nCp);

            double[,] recon = LeafToIec(fitSel, nCp, geom);   // [cp, value index in Eclipse order]
            double[,] reconExp = LeafToIec(fitExp, nCp, geom);
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
            say("Wrote " + outPath);

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
            rep.AppendLine(DiffSummary(recon, reconExp, nCp, nLeaf, geom));
            if (template != null)
            {
                double[,] plan = new double[nCp, nLeaf];
                for (int cp = 0; cp < nCp; cp++)
                    for (int k = 0; k < nLeaf; k++) plan[cp, k] = template.Cps[cp][EclipseMlcFile.NonLeafFields + k];
                rep.AppendLine("Method check - reconstructed EXPECTED vs template plan (should be ~0):");
                rep.AppendLine(DiffSummary(reconExp, plan, nCp, nLeaf, geom));
                rep.AppendLine("Written file vs template plan:");
                rep.AppendLine(DiffSummary(recon, plan, nCp, nLeaf, geom));
                double gMax = 0, mwMax = 0;
                for (int cp = 0; cp < nCp; cp++)
                {
                    gMax = Math.Max(gMax, Math.Abs(AngleDiff(fitExp[nLeaf][cp], template.Cps[cp][5])));
                    mwMax = Math.Max(mwMax, Math.Abs(muKnots[cp] / muTotal - template.Cps[cp][0]));
                }
                rep.AppendLine(Fmt("Log vs template consistency: max gantry diff {0:F3} deg, max meterset weight diff {1:F5}", gMax, mwMax));
                res.TemplateGantryMaxDeg = gMax;
                res.TemplateMetersetMax = mwMax;
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
            say(rep.ToString().TrimEnd());
            say("Report: " + reportPath);
            res.OutputPath = outPath; res.ReportPath = reportPath; res.Report = rep.ToString();
            res.Mu = muTotal;
            res.Positions = positions;
            res.Axis = axis;
            res.GantrySource = template != null ? "template" : gantryMode;
            res.Leaves = nLeaf;
            res.DeliveredMu = rows.Max(r => r[cMuA]);
            res.SnapshotsUsed = rows.Count;
            res.HoldSnapshotsExcluded = nHoldDropped;
            res.SetupSnapshotsSkipped = nSetup;
            res.MinGapMm = minGap;
            res.MaxGapShiftMm = gapShift;
            res.MaxPinShiftMm = pinShift;
            res.ClosedRunsPinned = nRuns;
            res.CollimatorDeg = coll;
            res.GantryActualVsExpectedMaxDeg = gActMax;
            res.GantryStartIec = template != null ? template.Cps[0][5] : gantryOut[0];
            res.GantryNote = template != null ? "from the template" : gantryHow;
            for (int k = 0; k < nCp - 1; k++) if (perInterval[k] == 0) res.EmptyIntervals.Add(k);
            res.RemainingGapProblems = nGapViol + nDynViol;
            res.GapsClosed = nClosed; res.GapsOpened = nOpened;
            DiffStats(recon, reconExp, nCp, nLeaf, geom, res);
            if (template != null)
            {
                double mc = 0;
                for (int cp = 0; cp < nCp; cp++)
                    for (int k = 0; k < nLeaf; k++)
                        mc = Math.Max(mc, Math.Abs(reconExp[cp, k] - template.Cps[cp][EclipseMlcFile.NonLeafFields + k]));
                res.MethodCheckMaxMm = mc;
                double gMax = 0, mwMax = 0;
                for (int cp = 0; cp < nCp; cp++)
                {
                    gMax = Math.Max(gMax, Math.Abs(AngleDiff(fitExp[nLeaf][cp], template.Cps[cp][5])));
                    mwMax = Math.Max(mwMax, Math.Abs(muKnots[cp] / muTotal - template.Cps[cp][0]));
                }
                if (gMax > 0.5 || mwMax > 0.002)
                    res.Problems.Add(Fmt("The Eclipse export does not look like this beam (gantry differs by up to {0:F2} deg, meterset weight by up to {1:F4}).", gMax, mwMax));
                if (Math.Abs(AngleDiff(template.Cps[0][6], coll)) > 0.5)
                    res.Problems.Add(Fmt("The collimator in the log ({0:F1} deg) differs from the Eclipse export ({1:F1} deg).", coll, template.Cps[0][6]));
                if (mc > MethodCheckLimitMm)
                    res.Problems.Add(Fmt("Method check: rebuilding the planned positions differs from the Eclipse export by up to {0:F3} mm (expected about 0).", mc));
            }
            if (res.RemainingGapProblems > 0)
                res.Problems.Add(res.RemainingGapProblems + " leaf gap(s) remain that Eclipse may reject (see the report).");
            if (res.EmptyIntervals.Count > 0)
                res.Problems.Add("No beam-on samples between control points " + DescribeIntervals(res.EmptyIntervals) +
                                 "; leaf positions there were interpolated.");
            if (template == null && gantryHow.StartsWith("not evenly spaced", StringComparison.Ordinal))
                res.Problems.Add("The gantry is not an evenly spaced arc, so its angles were rounded to 0.01 deg instead of rebuilt exactly.");
            res.Status = res.Problems.Count == 0 ? RebuildStatus.Ready : RebuildStatus.CheckFirst;
            res.ControlPoints = nCp; res.FirstLogControlPoint = cp0;
            res.Reconstructed = recon; res.ReconstructedExpected = reconExp;
            if (template != null)
            {
                res.TemplatePlan = new double[nCp, nLeaf];
                for (int cp = 0; cp < nCp; cp++)
                    for (int k = 0; k < nLeaf; k++) res.TemplatePlan[cp, k] = template.Cps[cp][EclipseMlcFile.NonLeafFields + k];
            }
            double g0 = template != null ? template.Cps[0][5] : gantryOut[0];
            return Fmt("Subbeam {0}: {1} CPs (log CP {2}-{3}), {4:F2} MU, gantry {5:F1} deg IEC -> {6}",
                subbeam, nCp, cp0, cp0 + nCp - 1, muTotal, g0, System.IO.Path.GetFileName(outPath));
        }

        /// <summary>Method check above this (mm) makes a field "check first".</summary>
        public const double MethodCheckLimitMm = 0.05;

        static void DiffStats(double[,] a, double[,] b, int nCp, int nVal, MlcGeometry geom, SubbeamResult res)
        {
            double max = 0, sum2 = 0; int worstCp = 0, worstK = 0; long n = 0;
            for (int cp = 0; cp < nCp; cp++)
                for (int k = 0; k < nVal; k++)
                {
                    double d = a[cp, k] - b[cp, k];
                    sum2 += d * d; n++;
                    if (Math.Abs(d) > max) { max = Math.Abs(d); worstCp = cp; worstK = k; }
                }
            res.DeliveredRmsMm = n > 0 ? Math.Sqrt(sum2 / n) : 0;
            res.DeliveredMaxMm = max;
            string layer = geom.LayerOfPair(worstK / 2);
            res.DeliveredMaxAt = Fmt("CP {0}, pair {1} {2}{3}", worstCp, worstK / 2 + 1, worstK % 2 == 0 ? "X1" : "X2",
                                     layer.Length > 0 ? " (" + layer + ")" : "");
        }

        /// <summary>e.g. [3, 4, 5, 9] -> "3-6 and 9-10".</summary>
        static string DescribeIntervals(List<int> ks)
        {
            var parts = new List<string>();
            int i = 0;
            while (i < ks.Count)
            {
                int j = i;
                while (j + 1 < ks.Count && ks[j + 1] == ks[j] + 1) j++;
                parts.Add(ks[i] + "-" + (ks[j] + 1));
                i = j + 1;
            }
            return parts.Count == 1 ? parts[0] : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[parts.Count - 1];
        }

        // Log leaf j (0-based) -> Eclipse value index, sign flipped where the MLC needs it
        // (SX2: j < nPair -> X2 of pair j, j >= nPair -> X1 of pair j-nPair, sign flipped).
        // Eclipse line order: X1(p0),X2(p0),X1(p1),X2(p1),... distal pairs first, then proximal.
        static double[,] LeafToIec(double[][] fit, int nCp, MlcGeometry geom)
        {
            var o = new double[nCp, geom.LeafCount];
            for (int j = 0; j < geom.LeafCount; j++)
            {
                LeafMapping m = geom.ToIec(j);
                for (int cp = 0; cp < nCp; cp++) o[cp, m.EclipseIndex] = m.Sign * fit[j][cp];
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

        static string DiffSummary(double[,] a, double[,] b, int nCp, int nVal, MlcGeometry geom)
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
            string layer = geom.LayerOfPair(pair);
            return Fmt(
                "  RMS {0:F3} mm | max {1:F3} mm at CP {2}, pair {3}{4} {5} | values >0.5 mm: {6}, >1 mm: {7}",
                Math.Sqrt(sum2 / n), max, worstCp, pair + 1, layer.Length > 0 ? " (" + layer + ")" : "",
                worstK % 2 == 0 ? "X1" : "X2", over05, over1);
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
                        if (s <= 0) throw new InvalidDataException("Fit matrix not positive definite (control point " + i + " has no data).");
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
