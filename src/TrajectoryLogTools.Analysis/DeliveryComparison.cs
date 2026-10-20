// DeliveryComparison.cs - compares two deliveries (trajectory logs) of the same field. Ported
// from compare_trajectory_logs.py; the method and the numbers are unchanged.
//
//   * Expected A vs Expected B  (controller setpoints)
//   * Actual   A vs Actual   B  (recorded machine positions)
//
// Logs are matched by fractional ControlPoint using beam-on samples only, so differences in
// timing, beam holds and sampling rate between the two deliveries don't affect the comparison.
// Control points passed through only while the beam is held (gantry moves between static IMRT
// beams, collimator moves between arcs) are excluded. Works for VMAT (arcs) and static-gantry
// IMRT (several subbeams).
//
// Also per log: one row per subbeam (gantry, MU, beam-on time, hold time, MLC error), every beam
// hold classified as start / end / move / interruption, the largest per-snapshot leaf step, total
// MU and couch tracking.
//
// Beam on means the dose servo is not in Hold. The Python script required "Normal"; the two only
// differ for Freeze / Disabled, which occur in Service mode only.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TrajectoryLogTools.Analysis
{
    public sealed class AxisStat
    {
        public string Axis;        // e.g. "Gantry (deg)", "MLC all leaves (mm)"
        public string Values;      // "Expected" or "Actual"
        public string Sampling;    // "nodes" (planned control points) or "continuous" (every 0.05 CP)
        public double Mean, Rms, Max;
        public double P95 = double.NaN, P99 = double.NaN;   // MLC only
    }

    public sealed class LeafStat
    {
        public int Leaf;
        public double MeanActual, MeanExpected, RmsActual, RmsExpected, MaxActual, MaxExpected;
    }

    /// <summary>One subbeam (field / arc) of one log.</summary>
    public sealed class BeamStat
    {
        public int Beam;
        /// <summary>Static gantry: the angle (2 decimals); arc: "start->stop".</summary>
        public string Gantry;
        public double GantryDeg = double.NaN;   // static gantry only
        public double CollDeg;
        public string ControlPoints;            // "min-max"
        public double MuPlanned, MuDelivered, BeamOnS, HoldS, MlcRmsErrMm, MlcMaxErrMm;
    }

    /// <summary>A run of consecutive beam-hold snapshots.</summary>
    public sealed class BeamHold
    {
        /// <summary>"start" / "end" (log edges), "move" (gantry or collimator moved: a planned
        /// transition between beams / arcs) or "interruption" (nothing moved).</summary>
        public string Type;
        public int Beam;
        public double StartS, DurationS, ControlPoint, Mu, MuDuring;
        public string Gantry, Coll;             // "a->b", 0.1 deg
        public double LeafErrDuringMm, LeafErr1sAfterMm;
    }

    public sealed class LogSummary
    {
        public string Label;
        public double MuPlanned, MuDelivered, TotalTimeS;
        public double MaxLeafStepExpected, P90LeafStepExpected, MaxLeafStepActual, P90LeafStepActual;
        /// <summary>Couch axis -> (expected, mean actual, actual - expected), cm.</summary>
        public List<Tuple<string, double, double, double>> Couch = new List<Tuple<string, double, double, double>>();
        public List<BeamStat> Beams = new List<BeamStat>();
        public List<BeamHold> Holds = new List<BeamHold>();
        public bool DroppedFirstSnapshot;
    }

    public sealed class DeliveryComparisonResult
    {
        public string LabelA, LabelB;
        public List<AxisStat> Summary = new List<AxisStat>();
        public List<LeafStat> PerLeaf = new List<LeafStat>();
        public double CorrExpectedActual, RmsActualMinusExpectedMm, MaxActualMinusExpectedMm;
        public LogSummary A, B;

        // ---- plot data, on the continuous grid (every 0.05 CP); NaN where not covered ----
        public double[] Grid;
        public bool[] GridCovered;
        public double[] GantryDiffExpected, GantryDiffActual, MuDiffExpected, MuDiffActual;
        /// <summary>[leaf, grid] A - B in mm.</summary>
        public double[,] LeafDiffExpected, LeafDiffActual;
        public int[] LeafNumbers;
    }

    public static class DeliveryComparison
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static readonly string[,] Axes =
        {
            { "Gantry (deg)", "GantryRtn_deg" },
            { "Collimator (deg)", "CollRtn_deg" },
            { "MU", "MU" },
            { "Couch Vrt (cm)", "CouchVrt_cm" },
            { "Couch Lng (cm)", "CouchLng_cm" },
            { "Couch Lat (cm)", "CouchLat_cm" },
        };

        // ------------------------------------------------------------------ loading
        /// <summary>
        /// Drops a stray first snapshot recorded well before the rest of the log (seen in one
        /// export: a 2.48 s gap between snapshots 1 and 2), and makes the time start at 0.
        /// </summary>
        public static DeliveryRecord Prepare(DeliveryRecord d, out bool dropped)
        {
            dropped = false;
            double[] t = d.Column("ElapsedTime_s");
            if (d.RowCount > 2 && t[1] - t[0] > 0.5)
            {
                dropped = true;
                DeliveryRecord r = d.Subset(Enumerable.Range(1, d.RowCount - 1).ToList());
                double[] t2 = r.Column("ElapsedTime_s");
                double t0 = t2[0];
                return r.WithColumn("ElapsedTime_s", t2.Select(v => v - t0).ToArray());
            }
            return d;
        }

        public static string[] LeafNames(DeliveryRecord d)
        {
            return d.ColumnNames.Where(c => c.StartsWith("MLC_Leaf", StringComparison.Ordinal))
                    .Select(c => c.Substring(0, c.IndexOf("_cm_", StringComparison.Ordinal)))
                    .Distinct().OrderBy(c => c, StringComparer.Ordinal).ToArray();
        }

        /// <summary>Beam-on rows with strictly increasing ControlPoint (needed for interpolation).</summary>
        internal static DeliveryRecord BeamOnByCp(DeliveryRecord d)
        {
            double[] cp = d.Column("ControlPoint");
            var on = Enumerable.Range(0, d.RowCount).Where(d.IsBeamOn).ToList();
            var rows = new List<int>();
            foreach (int i in on.OrderBy(i => cp[i]))   // stable: first occurrence of each value first
                if (rows.Count == 0 || cp[rows[rows.Count - 1]] != cp[i]) rows.Add(i);
            return d.Subset(rows);
        }

        static double[] AtCp(DeliveryRecord d, string col, double[] x)
        {
            return NumPy.Interp(x, d.Column("ControlPoint"), d.Column(col));
        }

        /// <summary>
        /// True where control point x lies within beam-on data of log d. Control points that are
        /// only passed through while the beam is held (e.g. the gantry moving between static IMRT
        /// beams, or the collimator turning between arcs) have no beam-on samples; interpolating
        /// across them would compare made-up values, so they are excluded.
        /// </summary>
        internal static bool[] Covered(DeliveryRecord d, double[] x)
        {
            double[] cp = d.Column("ControlPoint");
            var ok = new bool[x.Length];
            if (cp.Length == 0) return ok;
            double step = NumPy.Median(Enumerable.Range(0, cp.Length - 1).Select(i => cp[i + 1] - cp[i]));
            for (int j = 0; j < x.Length; j++) ok[j] = x[j] >= cp[0] && x[j] <= cp[cp.Length - 1];
            double gap = Math.Max(0.3, 4 * step);
            for (int i = 0; i + 1 < cp.Length; i++)
                if (cp[i + 1] - cp[i] > gap)
                    for (int j = 0; j < x.Length; j++)
                        if (x[j] > cp[i] && x[j] < cp[i + 1]) ok[j] = false;
            return ok;
        }

        static void Stats(IEnumerable<double> diff, out double mean, out double rms, out double max)
        {
            double[] d = diff.ToArray();
            mean = d.Length == 0 ? double.NaN : d.Sum() / d.Length;
            rms = d.Length == 0 ? double.NaN : Math.Sqrt(d.Sum(v => v * v) / d.Length);
            max = d.Length == 0 ? double.NaN : d.Max(v => Math.Abs(v));
        }

        static T[] Where<T>(T[] values, bool[] mask)
        {
            return values.Where((v, i) => mask[i]).ToArray();
        }

        // ------------------------------------------------------------------ analysis
        public static DeliveryComparisonResult Compare(DeliveryRecord logA, DeliveryRecord logB,
                                                       string labelA = null, string labelB = null,
                                                       bool dropStrayFirstSnapshot = true)
        {
            bool droppedA = false, droppedB = false;
            DeliveryRecord A = dropStrayFirstSnapshot ? Prepare(logA, out droppedA) : logA;
            DeliveryRecord B = dropStrayFirstSnapshot ? Prepare(logB, out droppedB) : logB;
            var namesB = new HashSet<string>(LeafNames(B));
            string[] leaves = LeafNames(A).Where(namesB.Contains).ToArray();

            var r = new DeliveryComparisonResult
            {
                LabelA = labelA ?? ShortLabel(logA.SourcePath),
                LabelB = labelB ?? ShortLabel(logB.SourcePath),
                LeafNumbers = leaves.Select(l => int.Parse(l.Substring(l.Length - 3), Inv)).ToArray(),
            };

            DeliveryRecord a = BeamOnByCp(A), b = BeamOnByCp(B);
            if (a.RowCount < 2 || b.RowCount < 2) throw new InvalidDataException("Too few beam-on snapshots to compare.");
            int nCp = (int)Math.Round(Math.Max(A.Column("ControlPoint").Max(), B.Column("ControlPoint").Max()), MidpointRounding.ToEven) + 1;
            double[] nodesAll = Enumerable.Range(0, nCp).Select(i => (double)i).ToArray();
            double[] fineAll = NumPy.Linspace(0.5, nCp - 1.5, Math.Max(0, (nCp - 2) * 20 + 1));
            bool[] na = Covered(a, nodesAll), nb = Covered(b, nodesAll);
            double[] nodes = nodesAll.Where((v, i) => na[i] && nb[i]).ToArray();
            bool[] fa = Covered(a, fineAll), fb = Covered(b, fineAll);
            bool[] fineOk = fa.Select((v, i) => v && fb[i]).ToArray();
            double[] fine = Where(fineAll, fineOk);

            for (int k = 0; k < Axes.GetLength(0); k++)
            {
                string name = Axes[k, 0], col = Axes[k, 1];
                if (!A.Has(col + "_Expected") || !B.Has(col + "_Expected")) continue;
                foreach (string kind in new[] { "Expected", "Actual" })
                    foreach (var grid in new[] { Tuple.Create("nodes", nodes), Tuple.Create("continuous", fine) })
                    {
                        double[] da = AtCp(a, col + "_" + kind, grid.Item2), db = AtCp(b, col + "_" + kind, grid.Item2);
                        var s = new AxisStat { Axis = name, Values = kind, Sampling = grid.Item1 };
                        Stats(da.Select((v, i) => v - db[i]), out s.Mean, out s.Rms, out s.Max);
                        r.Summary.Add(s);
                    }
            }

            var perLeaf = leaves.Select((l, i) => new LeafStat { Leaf = r.LeafNumbers[i] }).ToList();
            var D = new Dictionary<string, double[][]>();
            foreach (string kind in new[] { "Expected", "Actual" })
                foreach (var grid in new[] { Tuple.Create("nodes", nodes), Tuple.Create("continuous", fine) })
                {
                    double[] x = grid.Item2;
                    double[][] M = leaves.Select(l =>
                    {
                        double[] va = AtCp(a, l + "_cm_" + kind, x), vb = AtCp(b, l + "_cm_" + kind, x);
                        return va.Select((v, i) => (v - vb[i]) * 10.0).ToArray();   // cm -> mm
                    }).ToArray();
                    double[] flat = M.SelectMany(m => m).ToArray();
                    double[] abs = flat.Select(Math.Abs).ToArray();
                    var s = new AxisStat
                    {
                        Axis = "MLC all leaves (mm)", Values = kind, Sampling = grid.Item1,
                        P95 = NumPy.Percentile(abs, 95), P99 = NumPy.Percentile(abs, 99)
                    };
                    Stats(flat, out s.Mean, out s.Rms, out s.Max);
                    r.Summary.Add(s);
                    if (grid.Item1 == "nodes")
                        for (int i = 0; i < leaves.Length; i++)
                        {
                            double mean, rms, max;
                            Stats(M[i], out mean, out rms, out max);
                            if (kind == "Expected") { perLeaf[i].MeanExpected = mean; perLeaf[i].RmsExpected = rms; perLeaf[i].MaxExpected = max; }
                            else { perLeaf[i].MeanActual = mean; perLeaf[i].RmsActual = rms; perLeaf[i].MaxActual = max; }
                        }
                    else D[kind] = M;
                }
            r.PerLeaf = perLeaf.OrderBy(p => p.Leaf).ToList();

            // How much of the Actual difference is explained by the Expected difference?
            double[] de = D["Expected"].SelectMany(m => m).ToArray(), dact = D["Actual"].SelectMany(m => m).ToArray();
            r.CorrExpectedActual = NumPy.Corr(de, dact);
            r.RmsActualMinusExpectedMm = Math.Sqrt(dact.Select((v, i) => (v - de[i]) * (v - de[i])).Sum() / de.Length);
            r.MaxActualMinusExpectedMm = dact.Select((v, i) => Math.Abs(v - de[i])).Max();

            // ---- plot data: back on the full grid, gaps as NaN ----
            r.Grid = fineAll;
            r.GridCovered = fineOk;
            r.LeafDiffExpected = FullGrid(D["Expected"], fineOk);
            r.LeafDiffActual = FullGrid(D["Actual"], fineOk);
            r.GantryDiffExpected = DiffOnGrid(a, b, "GantryRtn_deg_Expected", fineAll, fineOk);
            r.GantryDiffActual = DiffOnGrid(a, b, "GantryRtn_deg_Actual", fineAll, fineOk);
            r.MuDiffExpected = DiffOnGrid(a, b, "MU_Expected", fineAll, fineOk);
            r.MuDiffActual = DiffOnGrid(a, b, "MU_Actual", fineAll, fineOk);

            r.A = Summarise(A, leaves, r.LabelA, droppedA);
            r.B = Summarise(B, leaves, r.LabelB, droppedB);
            return r;
        }

        static double[,] FullGrid(double[][] M, bool[] ok)
        {
            var full = new double[M.Length, ok.Length];
            for (int l = 0; l < M.Length; l++)
            {
                int j = 0;
                for (int g = 0; g < ok.Length; g++) full[l, g] = ok[g] ? M[l][j++] : double.NaN;
            }
            return full;
        }

        static double[] DiffOnGrid(DeliveryRecord a, DeliveryRecord b, string col, double[] grid, bool[] ok)
        {
            if (!a.Has(col) || !b.Has(col)) return grid.Select(_ => double.NaN).ToArray();
            double[] va = AtCp(a, col, grid), vb = AtCp(b, col, grid);
            return grid.Select((x, i) => ok[i] ? va[i] - vb[i] : double.NaN).ToArray();
        }

        /// <summary>Per-subbeam table, beam holds, MU, leaf steps and couch of a single log.</summary>
        public static LogSummary SummariseLog(DeliveryRecord d, string label = null)
        {
            return Summarise(d, LeafNames(d), label ?? ShortLabel(d.SourcePath), false);
        }

        static LogSummary Summarise(DeliveryRecord d, string[] leaves, string label, bool dropped)
        {
            var s = new LogSummary { Label = label, DroppedFirstSnapshot = dropped };
            s.MuPlanned = d.Column("MU_Expected").Max();
            s.MuDelivered = d.Column("MU_Actual").Max();
            s.TotalTimeS = d.Column("ElapsedTime_s").Max();
            MaxLeafStep(d, leaves, "Expected", out s.MaxLeafStepExpected, out s.P90LeafStepExpected);
            MaxLeafStep(d, leaves, "Actual", out s.MaxLeafStepActual, out s.P90LeafStepActual);
            foreach (string c in new[] { "CouchVrt_cm", "CouchLng_cm", "CouchLat_cm" })
            {
                if (!d.Has(c + "_Expected")) continue;
                double e = d.Column(c + "_Expected")[0], act = d.Column(c + "_Actual").Average();
                s.Couch.Add(Tuple.Create(c, e, act, act - e));
            }
            s.Beams = PerBeam(d, leaves);
            s.Holds = HoldSegments(d, leaves);
            return s;
        }

        /// <summary>Largest per-snapshot leaf step (mm); reveals the controller's leaf-speed cap.</summary>
        static void MaxLeafStep(DeliveryRecord d, string[] leaves, string kind, out double max, out double p90)
        {
            var steps = new List<double>();
            foreach (string l in leaves)
            {
                double[] v = d.Column(l + "_cm_" + kind);
                for (int i = 0; i + 1 < v.Length; i++) steps.Add(Math.Abs(v[i + 1] - v[i]) * 10);
            }
            max = steps.Count == 0 ? double.NaN : steps.Max();
            double[] moving = steps.Where(x => x > 0.5).ToArray();
            p90 = moving.Length > 0 ? NumPy.Percentile(moving, 90) : double.NaN;
        }

        /// <summary>Max |actual - expected| leaf position (mm) over the given rows; NaN for none.</summary>
        static double LeafErr(DeliveryRecord d, IList<int> rows, string[] leaves)
        {
            if (rows.Count == 0) return double.NaN;
            double m = 0;
            foreach (string l in leaves)
            {
                double[] act = d.Column(l + "_cm_Actual"), exp = d.Column(l + "_cm_Expected");
                foreach (int i in rows) m = Math.Max(m, Math.Abs(act[i] - exp[i]));
            }
            return m * 10;
        }

        static bool IsHold(DeliveryRecord d, int row) { return !d.IsBeamOn(row); }

        /// <summary>Every beam hold: when, where in the plan, what moved, and leaf behaviour.</summary>
        static List<BeamHold> HoldSegments(DeliveryRecord d, string[] leaves)
        {
            var result = new List<BeamHold>();
            int n = d.RowCount;
            double[] t = d.Column("ElapsedTime_s"), g = d.Column("GantryRtn_deg_Actual"), c = d.Column("CollRtn_deg_Actual");
            double[] sub = d.TryColumn("Subbeam"), cp = d.Column("ControlPoint"), mu = d.Column("MU_Actual");
            double dtMedian = NumPy.MedianSkipNaN(Enumerable.Range(1, Math.Max(0, n - 1)).Select(i => t[i] - t[i - 1]));
            int k = 0;
            while (k < n)
            {
                if (!IsHold(d, k)) { k++; continue; }
                int first = k, last = k;
                while (last + 1 < n && IsHold(d, last + 1)) last++;
                k = last + 1;

                double gMove = Math.Abs(g[last] - g[first]), cMove = Math.Abs(c[last] - c[first]);
                double t0 = t[first], t1 = t[last];
                string type;
                if (first <= 3) type = "start";
                else if (last >= n - 2) type = "end";
                else if (gMove > 0.5 || cMove > 0.5) type = "move";
                else type = "interruption";
                var during = Enumerable.Range(first, last - first + 1).ToList();
                var after = Enumerable.Range(0, n).Where(i => t[i] > t1 && t[i] <= t1 + 1.0).ToList();
                result.Add(new BeamHold
                {
                    Type = type,
                    Beam = sub == null ? 1 : (int)sub[first],
                    StartS = t0,
                    DurationS = t1 - t0 + dtMedian,
                    ControlPoint = cp[first],
                    Mu = mu[first],
                    MuDuring = mu[last] - mu[first],
                    Gantry = F1(g[first]) + "->" + F1(g[last]),
                    Coll = F1(c[first]) + "->" + F1(c[last]),
                    LeafErrDuringMm = LeafErr(d, during, leaves),
                    LeafErr1sAfterMm = LeafErr(d, after, leaves),
                });
            }
            return result;
        }

        /// <summary>One row per subbeam: gantry, collimator, MU, beam-on time, hold time, leaf error.</summary>
        static List<BeamStat> PerBeam(DeliveryRecord d, string[] leaves)
        {
            var rows = new List<BeamStat>();
            int n = d.RowCount;
            double[] sub = d.TryColumn("Subbeam") ?? new double[n].Select(_ => 1.0).ToArray();
            double[] t = d.Column("ElapsedTime_s"), ge = d.Column("GantryRtn_deg_Expected"), ce = d.Column("CollRtn_deg_Expected");
            double[] cp = d.Column("ControlPoint"), muE = d.Column("MU_Expected"), muA = d.Column("MU_Actual");
            double[] dt = Enumerable.Range(0, n).Select(i => i == 0 ? double.NaN : t[i] - t[i - 1]).ToArray();
            foreach (var grp in Enumerable.Range(0, n).GroupBy(i => sub[i]).OrderBy(x => x.Key))
            {
                var gIdx = grp.ToList();
                var on = gIdx.Where(d.IsBeamOn).ToList();
                double beamOn = 0, hold = 0;
                for (int j = 0; j < gIdx.Count; j++)
                {
                    int i = gIdx[j];
                    if (j > 0 && muA[i] - muA[gIdx[j - 1]] > 1e-6 && !double.IsNaN(dt[i])) beamOn += dt[i];
                    if (IsHold(d, i) && !double.IsNaN(dt[i])) hold += dt[i];
                }
                var err = new List<double>();
                foreach (string l in leaves)
                {
                    double[] act = d.Column(l + "_cm_Actual"), exp = d.Column(l + "_cm_Expected");
                    foreach (int i in on) err.Add((act[i] - exp[i]) * 10);
                }
                var gOn = on.Select(i => ge[i]).ToList();
                var s = new BeamStat
                {
                    Beam = (int)grp.Key,
                    CollDeg = NumPy.Round(NumPy.Median(on.Select(i => ce[i])), 2),
                    ControlPoints = gIdx.Min(i => cp[i]).ToString("F0", Inv) + "-" + gIdx.Max(i => cp[i]).ToString("F0", Inv),
                    MuPlanned = gIdx.Max(i => muE[i]) - gIdx.Min(i => muE[i]),
                    MuDelivered = gIdx.Max(i => muA[i]) - gIdx.Min(i => muA[i]),
                    BeamOnS = beamOn,
                    HoldS = hold,
                    MlcRmsErrMm = err.Count == 0 ? double.NaN : Math.Sqrt(err.Sum(v => v * v) / err.Count),
                    MlcMaxErrMm = err.Count == 0 ? double.NaN : err.Max(v => Math.Abs(v)),
                };
                if (gOn.Count == 0) s.Gantry = "";
                else if (NumPy.Std(gOn) < 0.5)
                {
                    s.GantryDeg = NumPy.Round(NumPy.Median(gOn), 2);
                    s.Gantry = s.GantryDeg.ToString("0.00", Inv);
                }
                else s.Gantry = gOn[0].ToString("F0", Inv) + "->" + gOn[gOn.Count - 1].ToString("F0", Inv);
                rows.Add(s);
            }
            return rows;
        }

        static string F1(double v) { return v.ToString("F1", Inv); }

        /// <summary>File name without upload-id prefix, extension or long middle part.</summary>
        public static string ShortLabel(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string name = Path.GetFileNameWithoutExtension(path);
            name = Regex.Replace(name, "^[0-9a-f]{8}-", "");
            return name.Length <= 45 ? name : name.Substring(0, 20) + "…" + name.Substring(name.Length - 22);
        }

        // ------------------------------------------------------------------ output
        /// <summary>
        /// Writes axis_summary.csv, per_leaf_A_minus_B.csv, per_beam_A/B.csv, beam_holds_A/B.csv and
        /// report.txt to <paramref name="folder"/>, in the layout the Python script wrote.
        /// </summary>
        public static List<string> WriteResults(DeliveryComparisonResult r, string folder)
        {
            Directory.CreateDirectory(folder);
            var written = new List<string>();
            Action<string, StringBuilder> save = (name, sb) =>
            {
                string p = Path.Combine(folder, name);
                File.WriteAllText(p, sb.ToString(), new UTF8Encoding(false));
                written.Add(p);
            };

            var sum = new StringBuilder("Axis,Values,Sampling,Mean A-B,RMS A-B,Max |A-B|,P95 |A-B|,P99 |A-B|\n");
            foreach (var s in r.Summary)
                sum.Append(Csv(s.Axis)).Append(',').Append(s.Values).Append(',').Append(s.Sampling).Append(',')
                   .Append(N(s.Mean, 5)).Append(',').Append(N(s.Rms, 5)).Append(',').Append(N(s.Max, 5)).Append(',')
                   .Append(N(s.P95, 5)).Append(',').Append(N(s.P99, 5)).Append('\n');
            save("axis_summary.csv", sum);

            var leaf = new StringBuilder("Leaf,Mean_Actual_mm,Mean_Expected_mm,RMS_Actual_mm,RMS_Expected_mm,Max_Actual_mm,Max_Expected_mm\n");
            foreach (var l in r.PerLeaf)
                leaf.Append(l.Leaf.ToString(Inv)).Append(',').Append(N(l.MeanActual, 4)).Append(',').Append(N(l.MeanExpected, 4)).Append(',')
                    .Append(N(l.RmsActual, 4)).Append(',').Append(N(l.RmsExpected, 4)).Append(',')
                    .Append(N(l.MaxActual, 4)).Append(',').Append(N(l.MaxExpected, 4)).Append('\n');
            save("per_leaf_A_minus_B.csv", leaf);

            foreach (var x in new[] { Tuple.Create("A", r.A), Tuple.Create("B", r.B) })
            {
                var pb = new StringBuilder("beam,gantry_deg,coll_deg,CP,MU_planned,MU_delivered,beam_on_s,hold_s,MLC_rms_err_mm,MLC_max_err_mm\n");
                foreach (var b in x.Item2.Beams)
                    pb.Append(b.Beam.ToString(Inv)).Append(',').Append(double.IsNaN(b.GantryDeg) ? b.Gantry : N(b.GantryDeg, 4)).Append(',')
                      .Append(N(b.CollDeg, 4)).Append(',').Append(b.ControlPoints).Append(',').Append(N(b.MuPlanned, 4)).Append(',')
                      .Append(N(b.MuDelivered, 4)).Append(',').Append(N(b.BeamOnS, 4)).Append(',').Append(N(b.HoldS, 4)).Append(',')
                      .Append(N(b.MlcRmsErrMm, 4)).Append(',').Append(N(b.MlcMaxErrMm, 4)).Append('\n');
                save("per_beam_" + x.Item1 + ".csv", pb);

                var bh = new StringBuilder("type,beam,t_start_s,duration_s,CP,MU,MU_during,gantry,coll,leaf_err_during_mm,leaf_err_1s_after_mm\n");
                foreach (var h in x.Item2.Holds)
                    bh.Append(h.Type).Append(',').Append(h.Beam.ToString(Inv)).Append(',').Append(N(h.StartS, 4)).Append(',')
                      .Append(N(h.DurationS, 4)).Append(',').Append(N(h.ControlPoint, 4)).Append(',').Append(N(h.Mu, 4)).Append(',')
                      .Append(N(h.MuDuring, 4)).Append(',').Append(h.Gantry).Append(',').Append(h.Coll).Append(',')
                      .Append(N(h.LeafErrDuringMm, 4)).Append(',').Append(N(h.LeafErr1sAfterMm, 4)).Append('\n');
                save("beam_holds_" + x.Item1 + ".csv", bh);
            }

            save("report.txt", new StringBuilder(Report(r)));
            return written;
        }

        static string N(double v, int decimals) { return double.IsNaN(v) ? "" : v.ToString("F" + decimals, Inv); }

        static string Csv(string s) { return s.IndexOfAny(new[] { ',', '"' }) < 0 ? s : "\"" + s.Replace("\"", "\"\"") + "\""; }

        /// <summary>The text report (what the Python script printed).</summary>
        public static string Report(DeliveryComparisonResult r)
        {
            var sb = new StringBuilder();
            Func<double, string> f = v => double.IsNaN(v) ? "NaN" : v.ToString("F4", Inv);
            sb.AppendLine("Delivery comparison - matched by control point, beam-on only");
            sb.AppendLine("A: " + r.LabelA);
            sb.AppendLine("B: " + r.LabelB);
            foreach (var s in new[] { r.A, r.B })
                if (s.DroppedFirstSnapshot) sb.AppendLine("(" + s.Label + ": stray first snapshot > 0.5 s before the rest left out)");
            sb.AppendLine();
            sb.AppendLine("=== A - B by axis (nodes = planned control points; continuous = every 0.05 CP) ===");
            sb.AppendLine(string.Format(Inv, "{0,-20} {1,-9} {2,-11} {3,10} {4,10} {5,10} {6,10} {7,10}",
                "Axis", "Values", "Sampling", "Mean A-B", "RMS A-B", "Max |A-B|", "P95 |A-B|", "P99 |A-B|"));
            foreach (var s in r.Summary)
                sb.AppendLine(string.Format(Inv, "{0,-20} {1,-9} {2,-11} {3,10} {4,10} {5,10} {6,10} {7,10}",
                    s.Axis, s.Values, s.Sampling, f(s.Mean), f(s.Rms), f(s.Max), f(s.P95), f(s.P99)));
            sb.AppendLine();
            sb.AppendLine("=== Actual difference vs Expected difference (MLC, continuous) ===");
            sb.AppendLine("  corr(ExpectedDiff, ActualDiff): " + f(r.CorrExpectedActual));
            sb.AppendLine("  RMS of (ActualDiff - ExpectedDiff) mm: " + f(r.RmsActualMinusExpectedMm));
            sb.AppendLine("  Max of |ActualDiff - ExpectedDiff| mm: " + f(r.MaxActualMinusExpectedMm));
            sb.AppendLine();
            sb.AppendLine("=== Leaves with largest max |A-B| at control points (mm) ===");
            Func<Func<LeafStat, double>, string> top = sel => string.Join(", ",
                r.PerLeaf.OrderByDescending(sel).Take(5).Select(l => l.Leaf + ": " + sel(l).ToString("F3", Inv)));
            sb.AppendLine("  Expected: " + top(l => l.MaxExpected));
            sb.AppendLine("  Actual:   " + top(l => l.MaxActual));
            sb.AppendLine();
            sb.AppendLine("=== Largest per-snapshot leaf step (mm): max, 90th pct of moving steps ===");
            foreach (var x in new[] { Tuple.Create("A", r.A), Tuple.Create("B", r.B) })
            {
                sb.AppendLine(string.Format(Inv, "  {0} Expected: max {1:F3}, p90 {2:F3}", x.Item1, x.Item2.MaxLeafStepExpected, x.Item2.P90LeafStepExpected));
                sb.AppendLine(string.Format(Inv, "  {0} Actual: max {1:F3}, p90 {2:F3}", x.Item1, x.Item2.MaxLeafStepActual, x.Item2.P90LeafStepActual));
            }
            sb.AppendLine();
            sb.AppendLine("=== Per beam (Subbeam) ===");
            foreach (var x in new[] { Tuple.Create("A", r.A), Tuple.Create("B", r.B) })
            {
                sb.AppendLine("  Log " + x.Item1 + ":");
                sb.AppendLine(string.Format(Inv, "  {0,4} {1,10} {2,8} {3,9} {4,10} {5,12} {6,9} {7,8} {8,14} {9,14}",
                    "beam", "gantry", "coll", "CP", "MU plan", "MU delivered", "beam on s", "hold s", "MLC rms err mm", "MLC max err mm"));
                foreach (var b in x.Item2.Beams)
                    sb.AppendLine(string.Format(Inv, "  {0,4} {1,10} {2,8:F2} {3,9} {4,10:F3} {5,12:F3} {6,9:F3} {7,8:F3} {8,14:F3} {9,14:F3}",
                        b.Beam, b.Gantry, b.CollDeg, b.ControlPoints, b.MuPlanned, b.MuDelivered, b.BeamOnS, b.HoldS, b.MlcRmsErrMm, b.MlcMaxErrMm));
            }
            sb.AppendLine();
            sb.AppendLine("=== Beam holds ('interruption' = hold with no planned gantry/collimator move) ===");
            foreach (var x in new[] { Tuple.Create("A", r.A), Tuple.Create("B", r.B) })
            {
                sb.AppendLine("  Log " + x.Item1 + ":");
                sb.AppendLine(string.Format(Inv, "  {0,-12} {1,4} {2,9} {3,10} {4,8} {5,9} {6,9} {7,-13} {8,-13} {9,12} {10,12}",
                    "type", "beam", "start s", "duration s", "CP", "MU", "MU during", "gantry", "coll", "leaf err mm", "1 s after mm"));
                foreach (var h in x.Item2.Holds)
                    sb.AppendLine(string.Format(Inv, "  {0,-12} {1,4} {2,9:F3} {3,10:F3} {4,8:F3} {5,9:F3} {6,9:F3} {7,-13} {8,-13} {9,12:F3} {10,12:F3}",
                        h.Type, h.Beam, h.StartS, h.DurationS, h.ControlPoint, h.Mu, h.MuDuring, h.Gantry, h.Coll, h.LeafErrDuringMm, h.LeafErr1sAfterMm));
            }
            sb.AppendLine();
            sb.AppendLine("=== Total MU, planned vs delivered ===");
            foreach (var x in new[] { Tuple.Create("A", r.A), Tuple.Create("B", r.B) })
                sb.AppendLine(string.Format(Inv, "  {0}: planned {1:F3}, delivered {2:F3}, diff {3:+0.000;-0.000;0.000} MU, total time {4:F2} s",
                    x.Item1, x.Item2.MuPlanned, x.Item2.MuDelivered, x.Item2.MuDelivered - x.Item2.MuPlanned, x.Item2.TotalTimeS));
            sb.AppendLine();
            sb.AppendLine("=== Couch (expected, mean actual, actual - expected) ===");
            foreach (var x in new[] { Tuple.Create("A", r.A), Tuple.Create("B", r.B) })
                foreach (var c in x.Item2.Couch)
                    sb.AppendLine(string.Format(Inv, "  {0} {1}: expected {2:F3}, actual {3:F4}, diff {4:+0.000;-0.000;0.000} mm",
                        x.Item1, c.Item1, c.Item2, c.Item3, c.Item4 * 10));
            return sb.ToString();
        }
    }
}
