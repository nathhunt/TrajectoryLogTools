// PlanComparison.cs - a delivery log compared with the DICOM RT Plan it delivered.
//
// The plan only gives values at its control points; between them Eclipse (and the machine)
// moves every axis linearly in meterset. So the plan is known at every moment of the delivery,
// and the comparison answers three questions:
//
//   1. Is this log a delivery of this plan? Patient ID and plan UID (the log's metadata holds
//      the plan's SOP Instance UID), plan and field names, numbers of control points.
//   2. Did the machine get the plan as approved (transfer check)? The log's EXPECTED values
//      against the plan: leaf positions, gantry, collimator and MU. The machine's expected
//      trajectory is the plan's straight lines, except that it rounds the corners where a leaf
//      reverses (by up to ~0.6 mm on clinical VMAT, right at the control point). So the check uses
//      the snapshots in the middle half of each control-point interval, where the expected values
//      lie on the plan's line (to ~0.002 mm); a change to any control point shows there.
//      A difference means another plan or plan version, or a transfer problem.
//   3. How accurately was it delivered? The log's ACTUAL values against the plan:
//        * every beam-on snapshot against the plan at the MU delivered so far ("where should the
//          leaves and gantry have been when this much dose had been delivered"), which covers the
//          whole delivery, between control points too. These are the headline numbers;
//        * at each control point: the actual positions there, from the least-squares fit the MLC
//          rebuild uses (knots on the integer control points, so corners are not cut), against the
//          plan's values.
//
// Each field (subbeam) of the log is paired with a treatment beam of the plan by name, else by
// number of control points and starting gantry angle. A log may cover only part of a beam (an
// interrupted delivery resumed in another log): the plan control points it covers are found by
// matching its expected values to the plan's.
//
// Positions are IEC 61217 mm in Eclipse value order (X1, X2 of pair 1, 2, ...; see MlcGeometry),
// angles IEC 61217 degrees.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TrajectoryLogTools.Core;

namespace TrajectoryLogTools.Analysis
{
    /// <summary>Absolute differences: largest, RMS, 95th percentile, and where the largest is.</summary>
    public sealed class DiffStat
    {
        public double Max = double.NaN, Rms = double.NaN, P95 = double.NaN;
        /// <summary>Where the largest difference is, e.g. "pair 12 X2 (distal), control point 87".</summary>
        public string MaxAt = "";
        public long Count;
        internal double Sum2;
    }

    public sealed class PlanFieldComparison
    {
        public int Subbeam;
        public string LogName = "";
        /// <summary>The plan beam this field was paired with; null when none fits (see <see cref="Error"/>).</summary>
        public RtBeam Beam;
        /// <summary>"name" or "control points and plan values".</summary>
        public string MatchedBy = "";
        /// <summary>Why the field was not compared, or null.</summary>
        public string Error;
        public bool Compared { get { return Error == null; } }

        /// <summary>Control points the log covers, and the plan control point of the first.</summary>
        public int ControlPoints, FirstPlanCp;
        public int PlanControlPoints { get { return Beam == null ? 0 : Beam.ControlPoints.Count; } }
        public bool Partial { get { return Beam != null && Compared && (FirstPlanCp > 0 || FirstPlanCp + ControlPoints < PlanControlPoints); } }

        public double MuPlan = double.NaN, MuPlanCovered = double.NaN, MuExpected = double.NaN, MuDelivered = double.NaN;

        // ---- per control point k = 0 .. ControlPoints-1 (plan control point FirstPlanCp + k) ----
        /// <summary>[k, value], IEC 61217 mm.</summary>
        public double[,] PlanLeaves, ExpectedLeaves, ActualLeaves;
        /// <summary>Gantry, IEC 61217 degrees (unwrapped: may run past 360 or below 0).</summary>
        public double[] PlanGantry, ExpectedGantry, ActualGantry;
        /// <summary>MU since the first covered control point.</summary>
        public double[] PlanMu, ExpectedMu, ActualMu;
        public double PlanCollimator = double.NaN, ExpectedCollimator = double.NaN, ActualCollimator = double.NaN;

        // ---- 2. transfer check: expected vs plan, mid-interval snapshots ----
        /// <summary>Largest |expected - plan| per value and control-point interval (mid-interval snapshots), mm: [value, interval]; NaN = no snapshot.</summary>
        public double[,] TransferIntervalMax;
        public double TransferLeafMaxMm = double.NaN, TransferGantryMaxDeg = double.NaN, TransferMuMax = double.NaN, TransferCollimatorDeg = double.NaN;
        public string TransferLeafMaxAt = "";
        public bool TransferOk
        {
            get
            {
                return Compared && TransferLeafMaxMm <= PlanComparison.TransferLeafTolMm && TransferGantryMaxDeg <= PlanComparison.TransferGantryTolDeg
                       && TransferMuMax <= PlanComparison.TransferMuTol && !(Math.Abs(TransferCollimatorDeg) > PlanComparison.TransferGantryTolDeg);
            }
        }

        // ---- 3. delivery: actual vs plan ----
        /// <summary>Every beam-on snapshot against the plan at the MU delivered so far.</summary>
        public DiffStat Leaves = new DiffStat(), Gantry = new DiffStat();
        /// <summary>At the control points (fitted actual positions).</summary>
        public DiffStat CpLeaves = new DiffStat();
        /// <summary>RMS of actual - plan per value (Eclipse order) over the beam-on snapshots, mm.</summary>
        public double[] LeafRms;

        public int Snapshots, HoldSnapshots, SetupSnapshots;
        public string Label { get { return LogName.Length > 0 ? LogName : "Field " + Subbeam; } }
    }

    public sealed class PlanComparisonResult
    {
        public RtPlan Plan;
        public string LogPath = "", LogLabel = "";
        public MlcGeometry Mlc;
        public int AxisScale;
        public string LogPatientId, LogPlanName, LogPlanUid;
        /// <summary>Null when the log doesn't say (e.g. a CSV export).</summary>
        public bool? SamePatient, SamePlanUid;
        public List<PlanFieldComparison> Fields = new List<PlanFieldComparison>();
        /// <summary>Treatment beams of the plan with no field in this log.</summary>
        public List<RtBeam> BeamsNotInLog = new List<RtBeam>();

        public IEnumerable<PlanFieldComparison> Compared { get { return Fields.Where(f => f.Compared); } }
        public bool AnyCompared { get { return Fields.Any(f => f.Compared); } }
        public bool TransferOk { get { return AnyCompared && Compared.All(f => f.TransferOk); } }

        /// <summary>All compared fields together.</summary>
        public DiffStat Leaves { get { return PlanComparison.Combine(Compared.Select(f => f.Leaves)); } }
        public DiffStat Gantry { get { return PlanComparison.Combine(Compared.Select(f => f.Gantry)); } }
        public DiffStat CpLeaves { get { return PlanComparison.Combine(Compared.Select(f => f.CpLeaves)); } }
    }

    public static class PlanComparison
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        static string Fmt(string format, params object[] args) { return string.Format(Inv, format, args); }

        // The log stores expected values as float32 (leaves in cm); the plan has 0.1 mm / 0.1 deg
        // steps at most. Agreement within these is "the same plan".
        public const double TransferLeafTolMm = 0.05;
        public const double TransferGantryTolDeg = 0.05;
        public const double TransferMuTol = 0.05;

        public static PlanComparisonResult Compare(DeliveryRecord record, RtPlan plan, string logLabel = null)
        {
            MlcGeometry geom = record.Mlc;
            if (geom == null) throw new InvalidDataException(MlcGeometry.UnsupportedMessage(record.LeafCount));
            var r = new PlanComparisonResult
            {
                Plan = plan, Mlc = geom, AxisScale = record.AxisScale,
                LogPath = record.SourcePath ?? "",
                LogLabel = logLabel ?? DeliveryComparison.ShortLabel(record.SourcePath),
            };
            bool varian = record.AxisScale != 2;

            // ---- 1. identity ----
            var names = new Dictionary<int, string>();
            if (record.Log != null)
            {
                for (int k = 0; k < record.Log.Subbeams.Count; k++) names[k + 1] = (record.Log.Subbeams[k].Name ?? "").Trim();
                var meta = new LogHeader { MetaDataText = record.Log.MetaDataText ?? "" };
                r.LogPatientId = Blank(meta.PatientId);
                r.LogPlanName = Blank(meta.PlanName);
                r.LogPlanUid = Blank(meta.PlanUid);
                // a one-field log may name its field only in the metadata
                string beamName = Blank(meta.Meta("BeamName") ?? meta.Meta("Beam Name"));
                if (record.Log.Subbeams.Count <= 1 && beamName != null && (!names.ContainsKey(1) || names[1].Length == 0)) names[1] = beamName.Trim();
            }
            if (r.LogPatientId != null && plan.PatientId.Length > 0) r.SamePatient = string.Equals(r.LogPatientId.Trim(), plan.PatientId, StringComparison.OrdinalIgnoreCase);
            if (r.LogPlanUid != null && plan.SopInstanceUid.Length > 0) r.SamePlanUid = r.LogPlanUid.Trim() == plan.SopInstanceUid;

            // ---- fields, paired with plan beams ----
            var beams = plan.TreatmentBeams.ToList();
            var used = new HashSet<RtBeam>();
            var fields = new List<Tuple<PlanFieldComparison, FieldData>>();
            foreach (int sb in MlcReconstructor.SubbeamsIn(record))
            {
                var f = new PlanFieldComparison { Subbeam = sb, LogName = names.ContainsKey(sb) ? names[sb] : "" };
                FieldData d = null;
                try { d = FieldData.From(record, sb, geom, varian); }
                catch (Exception ex) { f.Error = ex.Message; }
                fields.Add(Tuple.Create(f, d));
                r.Fields.Add(f);
            }
            // by name first, then by control points and gantry
            foreach (var t in fields)
            {
                if (t.Item1.LogName.Length == 0) continue;
                RtBeam b = beams.FirstOrDefault(x => !used.Contains(x) && string.Equals(x.Name.Trim(), t.Item1.LogName, StringComparison.OrdinalIgnoreCase));
                if (b != null) { t.Item1.Beam = b; t.Item1.MatchedBy = "name"; used.Add(b); }
            }
            // Without a name: the beams with enough control points, the one whose plan values the
            // log's expected values match best (same number of control points first; the starting
            // gantry alone can't tell apart arcs that start at the same angle).
            foreach (var t in fields)
            {
                if (t.Item1.Beam != null || t.Item2 == null) continue;
                FieldData d = t.Item2;
                RtBeam b = beams.Where(x => !used.Contains(x) && x.ControlPoints.Count >= d.ControlPoints)
                                .OrderBy(x => x.ControlPoints.Count == d.ControlPoints ? 0 : 1)
                                .ThenBy(x => TrialScore(d, x, geom))
                                .ThenBy(x => Math.Abs(Angles.WrapDifference(x.ControlPoints[0].GantryIec - d.ExpectedGantryStart)))
                                .FirstOrDefault();
                if (b != null) { t.Item1.Beam = b; t.Item1.MatchedBy = "control points and plan values"; used.Add(b); }
            }
            foreach (var t in fields)
            {
                PlanFieldComparison f = t.Item1;
                if (f.Error != null) continue;
                if (f.Beam == null) { f.Error = "No treatment beam of the plan fits this field (by name, or by number of control points)."; continue; }
                try { CompareField(f, t.Item2, f.Beam, geom); }
                catch (Exception ex) { f.Error = ex.Message; }
            }
            r.BeamsNotInLog.AddRange(beams.Where(b => !used.Contains(b)));
            return r;
        }

        static string Blank(string s) { return string.IsNullOrWhiteSpace(s) ? null : s.Trim(); }

        // =========================================================================================
        // One field of the log, as the comparison sees it (same selection as the MLC rebuild)
        // =========================================================================================

        sealed class FieldData
        {
            public int ControlPoints, SetupSnapshots, HoldSnapshots;
            public double MuDelivered;
            // beam-on snapshots: local control point, local MU (expected / actual), gantry IEC, leaves IEC mm
            public double[] Cp, MuE, MuA, GantryE, GantryA, CollE, CollA;
            public double[][] LeafE, LeafA;   // [value][snapshot], Eclipse order
            public double ExpectedGantryStart = double.NaN;

            public static FieldData From(DeliveryRecord rec, int subbeam, MlcGeometry geom, bool varian)
            {
                double[] cp = rec.Column("ControlPoint"), muE = rec.Column("MU_Expected"), muA = rec.Column("MU_Actual");
                double[] gE = rec.Column("GantryRtn_deg_Expected"), gA = rec.Column("GantryRtn_deg_Actual");
                double[] cE = rec.TryColumn("CollRtn_deg_Expected"), cA = rec.TryColumn("CollRtn_deg_Actual");
                double[] sub = rec.TryColumn("Subbeam");
                int nLeaf = geom.LeafCount;
                var le = new double[nLeaf][];
                var la = new double[nLeaf][];
                for (int j = 0; j < nLeaf; j++)
                {
                    le[j] = rec.Column(DeliveryRecord.LeafColumn(j + 1, "Expected"));
                    la[j] = rec.Column(DeliveryRecord.LeafColumn(j + 1, "Actual"));
                }

                var rows = new List<int>();
                for (int s = 0; s < rec.RowCount; s++)
                {
                    if (sub != null && (double.IsNaN(sub[s]) || (int)Math.Round(sub[s]) != subbeam)) continue;
                    if (double.IsNaN(cp[s]) || double.IsNaN(muE[s]) || double.IsNaN(muA[s])) continue;
                    rows.Add(s);
                }
                if (rows.Count == 0) throw new InvalidDataException("No snapshots for this field.");
                double mu0E = muE[rows[0]], mu0A = muA[rows[0]];
                double fieldStart = FieldSpan.FirstControlPoint(rows.Select(s => cp[s]).ToList(), rows.Select(s => muE[s]).ToList());
                int nSetup = 0;
                while (nSetup < rows.Count && (muA[rows[nSetup]] - mu0A < MlcReconstructor.SetupMuTol ||
                                               (!double.IsNaN(fieldStart) && cp[rows[nSetup]] < fieldStart - 1e-6))) nSetup++;
                if (nSetup >= rows.Count) throw new InvalidDataException("The field delivered no MU.");
                double cpFirst = double.PositiveInfinity;
                for (int i = nSetup; i < rows.Count; i++) cpFirst = Math.Min(cpFirst, cp[rows[i]]);
                int cp0 = (int)Math.Floor(cpFirst + 1e-6);

                var on = new List<int>();
                var d = new FieldData { SetupSnapshots = nSetup, MuDelivered = muA[rows[rows.Count - 1]] - mu0A };
                for (int i = nSetup; i < rows.Count; i++)
                {
                    if (rec.IsBeamOn(rows[i])) on.Add(rows[i]);
                    else d.HoldSnapshots++;
                }
                if (on.Count < 10) throw new InvalidDataException("Too few beam-on snapshots in this field.");

                Func<double, double> iec = a => varian ? 180.0 - a : a;
                d.Cp = on.Select(s => cp[s] - cp0).ToArray();
                d.MuE = on.Select(s => muE[s] - mu0E).ToArray();
                d.MuA = on.Select(s => muA[s] - mu0A).ToArray();
                d.GantryE = Unwrap(on.Select(s => iec(gE[s])).ToArray());
                d.GantryA = Unwrap(on.Select(s => iec(gA[s])).ToArray());
                d.CollE = cE == null ? null : on.Select(s => iec(cE[s])).ToArray();
                d.CollA = cA == null ? null : on.Select(s => iec(cA[s])).ToArray();
                d.LeafE = new double[nLeaf][];
                d.LeafA = new double[nLeaf][];
                for (int j = 0; j < nLeaf; j++)
                {
                    LeafMapping m = geom.ToIec(j);
                    d.LeafE[m.EclipseIndex] = on.Select(s => m.Sign * le[j][s] * 10.0).ToArray();
                    d.LeafA[m.EclipseIndex] = on.Select(s => m.Sign * la[j][s] * 10.0).ToArray();
                }
                d.ControlPoints = (int)Math.Round(d.Cp.Max()) + 1;
                if (d.ControlPoints < 2) throw new InvalidDataException("The field covers fewer than 2 control points.");
                d.ExpectedGantryStart = Wrap360(d.GantryE[0]);
                return d;
            }
        }

        // =========================================================================================
        // Comparing one field with its plan beam
        // =========================================================================================

        static void CompareField(PlanFieldComparison f, FieldData d, RtBeam beam, MlcGeometry geom)
        {
            int n = d.ControlPoints, P = beam.ControlPoints.Count, nv = geom.LeafCount;
            if (n > P) throw new InvalidDataException(Fmt("The log covers {0} control points; the plan beam {1} has {2}.", n, beam.Label, P));
            if (double.IsNaN(beam.Mu)) throw new InvalidDataException("The plan gives no MU for beam " + beam.Label + ".");
            double[,] plan = beam.Leaves(geom);
            double[] planG = Unwrap(beam.ControlPoints.Select(c => c.GantryIec).ToArray());
            double[] planMu = beam.ControlPoints.Select(c => c.Mu).ToArray();

            f.ControlPoints = n;
            f.SetupSnapshots = d.SetupSnapshots;
            f.HoldSnapshots = d.HoldSnapshots;
            f.Snapshots = d.Cp.Length;
            f.MuPlan = beam.Mu;
            f.MuDelivered = d.MuDelivered;

            // expected values at the control points (they move linearly between them in the log's control point)
            double[][] fitE = MlcReconstructor.FitPiecewiseLinear(d.Cp, d.LeafE.Concat(new[] { d.GantryE, d.MuE }).ToArray(), n);

            // which plan control points the log covers: the offset whose plan values match the expected ones best
            int best = 0;
            double bestScore = double.PositiveInfinity;
            for (int o = 0; o + n <= P; o++)
            {
                double score = 0;
                for (int k = 0; k < n && score < bestScore; k++)
                {
                    for (int v = 0; v < nv; v++) score = Math.Max(score, Math.Abs(fitE[v][k] - plan[o + k, v]));
                    score = Math.Max(score, Math.Abs(Angles.WrapDifference(fitE[nv][k] - planG[o + k])));
                }
                if (score < bestScore) { bestScore = score; best = o; }
            }
            int o0 = f.FirstPlanCp = best;

            f.PlanLeaves = new double[n, nv];
            f.ExpectedLeaves = new double[n, nv];
            f.PlanGantry = new double[n];
            f.ExpectedGantry = new double[n];
            f.PlanMu = new double[n];
            f.ExpectedMu = new double[n];
            // keep the plan's gantry in the same turn as the log's
            double turn = 360.0 * Math.Round((fitE[nv][0] - planG[o0]) / 360.0);
            for (int k = 0; k < n; k++)
            {
                for (int v = 0; v < nv; v++) { f.PlanLeaves[k, v] = plan[o0 + k, v]; f.ExpectedLeaves[k, v] = fitE[v][k]; }
                f.PlanGantry[k] = planG[o0 + k] + turn;
                f.ExpectedGantry[k] = fitE[nv][k];
                f.PlanMu[k] = planMu[o0 + k] - planMu[o0];
                f.ExpectedMu[k] = fitE[nv + 1][k];
            }
            for (int k = 1; k < n; k++) if (f.PlanMu[k] < f.PlanMu[k - 1]) f.PlanMu[k] = f.PlanMu[k - 1];
            f.MuPlanCovered = f.PlanMu[n - 1];
            f.MuExpected = f.ExpectedMu[n - 1];

            // ---- 2. transfer check: the snapshots in the middle half of each interval ----
            double lmax = 0; int lk = 0, lv = 0;
            double gmax = 0, mmax = 0;
            f.TransferIntervalMax = new double[nv, n - 1];
            for (int v = 0; v < nv; v++) for (int k = 0; k < n - 1; k++) f.TransferIntervalMax[v, k] = double.NaN;
            int used = 0;
            for (int s = 0; s < d.Cp.Length; s++)
            {
                double c = d.Cp[s];
                int k = Math.Max(0, Math.Min((int)Math.Floor(c), n - 2));
                double t = c - k;
                if (t < 0.25 || t > 0.75) continue;
                used++;
                for (int v = 0; v < nv; v++)
                {
                    double e = Math.Abs(d.LeafE[v][s] - (f.PlanLeaves[k, v] * (1 - t) + f.PlanLeaves[k + 1, v] * t));
                    if (!(f.TransferIntervalMax[v, k] >= e)) f.TransferIntervalMax[v, k] = e;
                    if (e > lmax) { lmax = e; lk = k; lv = v; }
                }
                gmax = Math.Max(gmax, Math.Abs(Angles.WrapDifference(d.GantryE[s] - (f.PlanGantry[k] * (1 - t) + f.PlanGantry[k + 1] * t))));
                mmax = Math.Max(mmax, Math.Abs(d.MuE[s] - (f.PlanMu[k] * (1 - t) + f.PlanMu[k + 1] * t)));
            }
            if (used == 0) throw new InvalidDataException("No beam-on snapshots between control points to check the plan with.");
            f.TransferLeafMaxMm = lmax;
            f.TransferLeafMaxAt = Where(geom, lv, o0 + lk).Replace("control point " + (o0 + lk), Fmt("between control points {0} and {1}", o0 + lk, o0 + lk + 1));
            f.TransferGantryMaxDeg = gmax;
            f.TransferMuMax = mmax;
            f.PlanCollimator = Wrap360(beam.ControlPoints[o0].CollimatorIec);
            if (d.CollE != null) f.ExpectedCollimator = Wrap360(NumPy.Median(d.CollE));
            if (d.CollA != null) f.ActualCollimator = Wrap360(NumPy.Median(d.CollA));
            if (!double.IsNaN(f.PlanCollimator) && !double.IsNaN(f.ExpectedCollimator))
                f.TransferCollimatorDeg = Angles.WrapDifference(f.ExpectedCollimator - f.PlanCollimator);

            // ---- 3a. every beam-on snapshot against the plan at the MU delivered so far ----
            var coord = new double[d.Cp.Length];
            var abs = new List<double>(d.Cp.Length * nv);
            var sum2 = new double[nv];
            double max = -1; int maxS = 0, maxV = 0;
            double gSum2 = 0, gMax = -1; int gMaxS = 0;
            var gAbs = new List<double>(d.Cp.Length);
            for (int s = 0; s < d.Cp.Length; s++)
            {
                double c = coord[s] = MlcReconstructor.InverseInterp(f.PlanMu, d.MuA[s]);
                int k = Math.Min((int)Math.Floor(c), n - 2);
                double t = c - k;
                for (int v = 0; v < nv; v++)
                {
                    double p = f.PlanLeaves[k, v] * (1 - t) + f.PlanLeaves[k + 1, v] * t;
                    double e = d.LeafA[v][s] - p;
                    double a = Math.Abs(e);
                    sum2[v] += e * e;
                    abs.Add(a);
                    if (a > max) { max = a; maxS = s; maxV = v; }
                }
                double pg = f.PlanGantry[k] * (1 - t) + f.PlanGantry[k + 1] * t;
                double ge = Math.Abs(Angles.WrapDifference(d.GantryA[s] - pg));
                gSum2 += ge * ge;
                gAbs.Add(ge);
                if (ge > gMax) { gMax = ge; gMaxS = s; }
            }
            f.LeafRms = sum2.Select(x => Math.Sqrt(x / d.Cp.Length)).ToArray();
            f.Leaves = Stat(abs, sum2.Sum(), Where(geom, maxV, o0 + (int)Math.Round(coord[maxS])));
            f.Gantry = Stat(gAbs, gSum2, Fmt("control point {0}", o0 + (int)Math.Round(coord[gMaxS])));

            // ---- 3b. at the control points: fitted actual positions (on the plan's MU axis) ----
            double[][] fitA = MlcReconstructor.FitPiecewiseLinear(coord, d.LeafA.Concat(new[] { d.GantryA }).ToArray(), n);
            double[][] fitMuA = MlcReconstructor.FitPiecewiseLinear(d.Cp, new[] { d.MuA }, n);
            f.ActualLeaves = new double[n, nv];
            f.ActualGantry = new double[n];
            f.ActualMu = new double[n];
            var cpAbs = new List<double>(n * nv);
            double cpSum2 = 0, cpMax = -1; int cpK = 0, cpV = 0;
            for (int k = 0; k < n; k++)
            {
                for (int v = 0; v < nv; v++)
                {
                    f.ActualLeaves[k, v] = fitA[v][k];
                    double e = fitA[v][k] - f.PlanLeaves[k, v];
                    cpSum2 += e * e;
                    cpAbs.Add(Math.Abs(e));
                    if (Math.Abs(e) > cpMax) { cpMax = Math.Abs(e); cpK = k; cpV = v; }
                }
                f.ActualGantry[k] = fitA[nv][k];
                f.ActualMu[k] = fitMuA[0][k];
            }
            f.CpLeaves = Stat(cpAbs, cpSum2, Where(geom, cpV, o0 + cpK));
        }

        /// <summary>How well the field's expected values fit a beam (largest mid-interval leaf difference, mm); infinity when they can't be compared.</summary>
        static double TrialScore(FieldData d, RtBeam beam, MlcGeometry geom)
        {
            try
            {
                var f = new PlanFieldComparison();
                CompareField(f, d, beam, geom);
                return f.TransferLeafMaxMm + f.TransferGantryMaxDeg;
            }
            catch (Exception) { return double.PositiveInfinity; }
        }

        static DiffStat Stat(List<double> abs, double sum2, string maxAt)
        {
            var s = new DiffStat { Count = abs.Count, Sum2 = sum2, MaxAt = maxAt };
            if (abs.Count == 0) return s;
            s.Max = abs.Max();
            s.Rms = Math.Sqrt(sum2 / abs.Count);
            s.P95 = NumPy.Percentile(abs.ToArray(), 95);
            return s;
        }

        /// <summary>Several fields together: largest max and its place, pooled RMS, largest P95.</summary>
        internal static DiffStat Combine(IEnumerable<DiffStat> stats)
        {
            var list = stats.Where(x => x.Count > 0).ToList();
            var r = new DiffStat();
            if (list.Count == 0) return r;
            DiffStat worst = list.OrderByDescending(x => x.Max).First();
            r.Max = worst.Max;
            r.MaxAt = worst.MaxAt;
            r.Count = list.Sum(x => x.Count);
            r.Sum2 = list.Sum(x => x.Sum2);
            r.Rms = Math.Sqrt(r.Sum2 / r.Count);
            r.P95 = list.Max(x => x.P95);
            return r;
        }

        /// <summary>"pair 12 X2 (distal), control point 87" for an Eclipse value index.</summary>
        public static string Where(MlcGeometry geom, int value, int planCp)
        {
            int pair = value / 2;
            string layer = geom.LayerOfPair(pair);
            return Fmt("pair {0} {1}{2}, control point {3}", pair + 1, value % 2 == 0 ? "X1" : "X2", layer.Length > 0 ? " (" + layer + ")" : "", planCp);
        }

        static double Wrap360(double a)
        {
            if (double.IsNaN(a)) return a;
            a %= 360.0;
            if (a < 0) a += 360.0;
            return a >= 359.99995 ? 0 : a;
        }

        static double[] Unwrap(double[] a)
        {
            var o = new double[a.Length];
            if (a.Length == 0) return o;
            o[0] = a[0];
            for (int i = 1; i < a.Length; i++)
                o[i] = double.IsNaN(a[i]) ? double.NaN : (double.IsNaN(o[i - 1]) ? a[i] : o[i - 1] + Angles.WrapDifference(a[i] - a[i - 1]));
            return o;
        }

        // =========================================================================================
        // Report
        // =========================================================================================

        static string Mm(double v) { return double.IsNaN(v) ? "-" : v.ToString("F3", Inv) + " mm"; }
        static string Deg(double v) { return double.IsNaN(v) ? "-" : v.ToString("F3", Inv) + " deg"; }
        static string Mu(double v) { return double.IsNaN(v) ? "-" : v.ToString("F3", Inv); }

        /// <summary>What was compared, the identity checks and, per field, the transfer check and the delivery differences.</summary>
        public static string Report(PlanComparisonResult r)
        {
            var sb = new StringBuilder();
            Action<string> L = s => sb.AppendLine(s);
            RtPlan p = r.Plan;
            L("LOG VS PLAN COMPARISON");
            L("Log  : " + r.LogLabel + (r.LogPath.Length > 0 ? "  (" + r.LogPath + ")" : ""));
            L("Plan : " + p.DisplayName + (p.Name.Length > 0 && p.Name != p.Label ? " / " + p.Name : "") +
              (p.ApprovalStatus.Length > 0 ? "  [" + p.ApprovalStatus + "]" : "") + "  (" + p.Path + ")");
            L("Positions IEC 61217 mm (X1, X2 per leaf pair; " + r.Mlc.Description + "), angles IEC 61217 degrees.");
            L("");
            L("IDENTITY");
            L("  Patient ID : log " + (r.LogPatientId ?? "(not recorded)") + ", plan " + (p.PatientId.Length > 0 ? p.PatientId : "(none)") +
              (r.SamePatient == true ? "  -> same" : r.SamePatient == false ? "  -> DIFFERENT PATIENTS" : ""));
            L("  Plan UID   : log " + (r.LogPlanUid ?? "(not recorded)") + ", plan " + p.SopInstanceUid +
              (r.SamePlanUid == true ? "  -> same plan" : r.SamePlanUid == false ? "  -> DIFFERENT PLAN (or plan version)" : ""));
            L("  Plan name  : log " + (r.LogPlanName ?? "(not recorded)") + ", plan " + p.DisplayName);
            L("");
            foreach (PlanFieldComparison f in r.Fields)
            {
                L("FIELD " + f.Label + (f.Beam != null ? "  <->  plan beam " + f.Beam.Label + " (matched by " + f.MatchedBy + ")" : ""));
                if (!f.Compared) { L("  Not compared: " + f.Error); L(""); continue; }
                L(Fmt("  Control points : log covers plan {0}-{1} of {2} (0-{3}){4}", f.FirstPlanCp, f.FirstPlanCp + f.ControlPoints - 1,
                    f.PlanControlPoints, f.PlanControlPoints - 1, f.Partial ? "  PARTIAL" : ""));
                L("  MU             : plan " + Mu(f.MuPlan) + (f.Partial ? " (covered " + Mu(f.MuPlanCovered) + ")" : "") +
                  ", log planned " + Mu(f.MuExpected) + ", delivered " + Mu(f.MuDelivered));
                L("  Transfer check (log expected vs plan, between control points): " + (f.TransferOk ? "MATCHES" : "DIFFERS"));
                L("    leaves max " + Mm(f.TransferLeafMaxMm) + " (" + f.TransferLeafMaxAt + "), gantry max " + Deg(f.TransferGantryMaxDeg) +
                  ", MU max " + Mu(f.TransferMuMax) + ", collimator " + Deg(f.TransferCollimatorDeg));
                L("  Delivery (log actual vs plan):");
                L(Fmt("    every beam-on snapshot ({0}), against the plan at the MU delivered:", f.Snapshots));
                L("      leaves  max " + Mm(f.Leaves.Max) + " (" + f.Leaves.MaxAt + "), RMS " + Mm(f.Leaves.Rms) + ", 95% under " + Mm(f.Leaves.P95));
                L("      gantry  max " + Deg(f.Gantry.Max) + " (" + f.Gantry.MaxAt + "), RMS " + Deg(f.Gantry.Rms));
                L("    at control points (fitted actual positions):");
                L("      leaves  max " + Mm(f.CpLeaves.Max) + " (" + f.CpLeaves.MaxAt + "), RMS " + Mm(f.CpLeaves.Rms) + ", 95% under " + Mm(f.CpLeaves.P95));
                L("    collimator actual " + Deg(f.ActualCollimator) + ", plan " + Deg(f.PlanCollimator));
                L(Fmt("  Snapshots      : {0} beam-on used, {1} in beam hold excluded, {2} set-up skipped", f.Snapshots, f.HoldSnapshots, f.SetupSnapshots));
                L("");
            }
            if (r.BeamsNotInLog.Count > 0)
            {
                L("Plan treatment beams not in this log: " + string.Join(", ", r.BeamsNotInLog.Select(b => b.Label)));
                L("(usual when each field is delivered in its own log)");
            }
            return sb.ToString();
        }

        /// <summary>
        /// Writes the report (plan_comparison_report.txt) and, per compared field, a table by control
        /// point (plan_comparison_&lt;field&gt;.csv). Returns the files written.
        /// </summary>
        public static List<string> WriteResults(PlanComparisonResult r, string folder)
        {
            Directory.CreateDirectory(folder);
            var files = new List<string>();
            string rep = Path.Combine(folder, "plan_comparison_report.txt");
            File.WriteAllText(rep, Report(r), Encoding.UTF8);
            files.Add(rep);
            foreach (PlanFieldComparison f in r.Compared)
            {
                var sb = new StringBuilder("PlanControlPoint,PlanMU,LogPlannedMU,DeliveredMU,PlanGantry_IEC,LogPlannedGantry_IEC,ActualGantry_IEC," +
                                           "MaxLeaf_ExpectedMinusPlan_mm,MaxLeaf_ActualMinusPlan_mm,RmsLeaf_ActualMinusPlan_mm\n");
                int nv = f.PlanLeaves.GetLength(1);
                for (int k = 0; k < f.ControlPoints; k++)
                {
                    double me = 0, ma = 0, s2 = 0;
                    for (int v = 0; v < nv; v++)
                    {
                        me = Math.Max(me, Math.Abs(f.ExpectedLeaves[k, v] - f.PlanLeaves[k, v]));
                        double a = f.ActualLeaves[k, v] - f.PlanLeaves[k, v];
                        ma = Math.Max(ma, Math.Abs(a));
                        s2 += a * a;
                    }
                    sb.Append(string.Join(",", new[]
                    {
                        (f.FirstPlanCp + k).ToString(Inv), N(f.PlanMu[k]), N(f.ExpectedMu[k]), N(f.ActualMu[k]),
                        N(Wrap360(f.PlanGantry[k])), N(Wrap360(f.ExpectedGantry[k])), N(Wrap360(f.ActualGantry[k])), N(me), N(ma), N(Math.Sqrt(s2 / nv)),
                    })).Append('\n');
                }
                string name = new string(f.Label.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
                string path = Path.Combine(folder, "plan_comparison_" + name + ".csv");
                File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
                files.Add(path);
            }
            return files;
        }

        static string N(double v) { return double.IsNaN(v) ? "" : v.ToString("0.#####", Inv); }
    }
}
