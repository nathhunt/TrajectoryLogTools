using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using TrajectoryLogTools.Core;

namespace TrajectoryLogTools.Tests
{
    /// <summary>
    /// A synthetic plan: fields (arcs or static gantry) with planned control points, meterset
    /// weights and SX2 leaf positions (IEC 61217, mm) that change linearly between control points.
    /// Deterministic, so the same logs can be rebuilt for the golden tests.
    /// </summary>
    public sealed class SimPlan
    {
        public sealed class Field
        {
            public string Name;
            public int Cps;
            public double Mu;
            public double Gantry0, Gantry1;   // Varian scale
            public double Coll;               // Varian scale
            public double[] Mw;               // meterset weight at each CP, 0 .. 1
            public double[,] X1, X2;          // [cp, pair], mm IEC
        }

        public const int Pairs = 57;
        public List<Field> Fields = new List<Field>();

        public static SimPlan Vmat()
        {
            var p = new SimPlan();
            p.Fields.Add(MakeField("Arc 1", 41, 150, 50, 310, 165, 0));
            p.Fields.Add(MakeField("Arc 2", 41, 120, 310, 50, 195, 1));
            return p;
        }

        public static SimPlan Imrt()
        {
            var p = new SimPlan();
            p.Fields.Add(MakeField("Field 1", 15, 60, 180, 180, 180, 2));
            p.Fields.Add(MakeField("Field 2", 15, 45, 60, 60, 180, 3));
            p.Fields.Add(MakeField("Field 3", 15, 80, 300, 300, 180, 4));
            return p;
        }

        static Field MakeField(string name, int cps, double mu, double g0, double g1, double coll, int seed)
        {
            var f = new Field { Name = name, Cps = cps, Mu = mu, Gantry0 = g0, Gantry1 = g1, Coll = coll };
            var w = new double[cps];
            for (int k = 1; k < cps; k++) w[k] = w[k - 1] + 1 + 0.5 * Math.Sin(k * 0.37 + seed);
            f.Mw = w.Select(v => v / w[cps - 1]).ToArray();
            f.X1 = new double[cps, Pairs];
            f.X2 = new double[cps, Pairs];
            for (int q = 0; q < Pairs; q++)
            {
                var closed = new bool[cps];
                for (int k = 0; k < cps; k++)
                {
                    double c = 15 * Math.Sin(0.21 * k + 0.4 * q + seed);
                    double raw = 12 * Math.Sin(0.13 * k + 0.25 * q + 0.7 * seed) + 2;
                    double x1, x2;
                    if (raw < 1) { closed[k] = true; x1 = x2 = c; }               // closed (position set below)
                    else if (raw < 2) { x1 = c - 0.25; x2 = c + 0.25; }          // minimum gap
                    else { x1 = c - raw; x2 = c + raw; }
                    f.X1[k, q] = Math.Round(x1, 4);
                    f.X2[k, q] = Math.Round(x2, 4);
                }
                // As Eclipse requires: a closed pair sits still for its whole closed run, inside the
                // open aperture of the control points before and after the run.
                for (int a = 0; a < cps; a++)
                {
                    if (!closed[a]) continue;
                    int b = a;
                    while (b + 1 < cps && closed[b + 1]) b++;
                    double lo = double.NegativeInfinity, hi = double.PositiveInfinity;
                    if (a > 0) { lo = Math.Max(lo, f.X1[a - 1, q]); hi = Math.Min(hi, f.X2[a - 1, q]); }
                    if (b < cps - 1) { lo = Math.Max(lo, f.X1[b + 1, q]); hi = Math.Min(hi, f.X2[b + 1, q]); }
                    double park = lo <= hi ? (double.IsInfinity(lo) ? hi - 1 : double.IsInfinity(hi) ? lo + 1 : (lo + hi) / 2) : f.X1[a, q];
                    if (double.IsInfinity(park)) park = 0;
                    park = Math.Round(park, 1);
                    for (int k = a; k <= b; k++) { f.X1[k, q] = park; f.X2[k, q] = park; }
                    foreach (int nb in new[] { a - 1, b + 1 })
                    {
                        if (nb < 0 || nb >= cps) continue;
                        if (f.X1[nb, q] > park) f.X1[nb, q] = park;
                        if (f.X2[nb, q] < park) f.X2[nb, q] = park;
                    }
                    a = b;
                }
            }
            return f;
        }

        /// <summary>Planned value interpolated at local control point lc.</summary>
        public static double Lerp(double[,] v, int pair, double lc)
        {
            int n = v.GetLength(0);
            lc = Math.Max(0, Math.Min(n - 1, lc));
            int k = Math.Min((int)Math.Floor(lc), n - 2);
            double t = lc - k;
            return v[k, pair] * (1 - t) + v[k + 1, pair] * t;
        }

        public static double Lerp(double[] v, double lc)
        {
            int n = v.Length;
            lc = Math.Max(0, Math.Min(n - 1, lc));
            int k = Math.Min((int)Math.Floor(lc), n - 2);
            double t = lc - k;
            return v[k] * (1 - t) + v[k + 1] * t;
        }

        /// <summary>Eclipse MLC export (MLCPlan serialization) of field f, as Eclipse writes it.</summary>
        public string EclipseExport(int f)
        {
            Field fl = Fields[f];
            Func<double, string> F = v => (Math.Abs(v) < 5e-7 ? 0 : v).ToString("0.000000", CultureInfo.InvariantCulture);
            var sb = new StringBuilder();
            sb.Append("# VARIAN ARIA Eclipse - MLCPlan serialization\r\n# File format version: \r\n1\r\n");
            sb.Append("# Exported Monday, 1 June 2026 9:00:00 AM\r\n# All values are IEC 61217 in millimeters\r\n");
            sb.Append("# MLC Model - Must match on import\r\nSX2\r\n# Number of control points\r\n").Append(fl.Cps).Append("\r\n");
            sb.Append("# Control points\r\n# MetersetWeight,CollX1,CollX2,CollY1,CollY2,GantryRtn,CollRtn\r\n");
            sb.Append("# MLCLeafPositions in Bank X1, Bank X2 order. For dual-layer MLCs the distal(patient side) leaf positions come before the proximal(source side) leaf positions.\r\n");
            for (int k = 0; k < fl.Cps; k++)
            {
                double g = fl.Gantry0 + (fl.Gantry1 - fl.Gantry0) * k / (fl.Cps - 1);
                sb.Append(F(fl.Mw[k])).Append(",-140.000000,140.000000,-140.000000,140.000000,")
                  .Append(F(Iec(Math.Round(180 - g, 6)))).Append(',').Append(F(Iec(180 - fl.Coll)));
                for (int q = 0; q < Pairs; q++) sb.Append(',').Append(F(fl.X1[k, q])).Append(',').Append(F(fl.X2[k, q]));
                sb.Append("\r\n");
            }
            sb.Append("# EOF\r\n");
            return sb.ToString();
        }

        static double Iec(double a) { a %= 360; if (a < 0) a += 360; return a; }
    }

    /// <summary>How one delivery of a plan went: speed, holds, noise.</summary>
    public sealed class SimDelivery
    {
        public int Seed = 1;
        public int SnapshotsPerCp = 5;
        /// <summary>(field, local control point, snapshots) of each interruption.</summary>
        public List<Tuple<int, double, int>> Interruptions = new List<Tuple<int, double, int>>();
        public string Version = "5.1";
        /// <summary>v5.1 only: first snapshot recorded 2.48 s before the rest.</summary>
        public bool StrayFirstSnapshot;
        public double LeafLagCp = 0.05;
        /// <summary>
        /// Fields after the first open as Halcyon logs them: the subbeam header gives the previous
        /// field's last control point, the counter holds there through the set-up beam hold (the
        /// delivered MU creeping up by a few 0.0001 MU), then steps on by one with the beam on and
        /// no planned MU; the field's own control point 0 is the next one.
        /// </summary>
        public bool HalcyonTransition;
        /// <summary>Written to the metadata as "Plan UID" when set (the golden logs have none).</summary>
        public string PlanUid;
    }

    /// <summary>Builds a Halcyon-style trajectory log (.bin) of a simulated delivery.</summary>
    public static class DeliverySimulator
    {
        const int Leaves = SimPlan.Pairs * 2;

        sealed class Snap
        {
            public double Cp;
            public double MuE, MuA, GE, GA, CE, CA;
            public bool Hold;
            public double[] LeafE = new double[Leaves], LeafA = new double[Leaves];   // log cm

            public Snap Copy()
            {
                var s = (Snap)MemberwiseClone();
                s.LeafE = (double[])LeafE.Clone();
                s.LeafA = (double[])LeafA.Clone();
                return s;
            }
        }

        public static byte[] Build(SimPlan plan, SimDelivery d)
        {
            var rnd = new Random(d.Seed);
            Func<double, double> noise = s => (rnd.NextDouble() * 2 - 1) * s;
            var snaps = new List<Snap>();
            int off = 0;
            double muStartE = 0, muA = 0;
            Snap prev = null;

            for (int f = 0; f < plan.Fields.Count; f++)
            {
                var fl = plan.Fields[f];
                // ---- set-up: beam hold while gantry / collimator / leaves move to the field ----
                Snap start = State(fl, off, 0, muStartE);
                int setup = f == 0 ? 8 : 10;
                bool halcyon = d.HalcyonTransition && f > 0;
                double creep = 0;
                for (int i = 0; i < setup; i++)
                {
                    double t = f == 0 ? 1 : (i + 1.0) / 6;
                    Snap s = prev == null || t >= 1 ? Clone(start) : Blend(prev, start, t);
                    if (prev != null && t < 1) s.Cp = (off - 1) + t;   // CP slides from the previous field's last
                    if (halcyon) { s.Cp = off - 1; s.MuE = muStartE; creep += 0.0002; }   // holds on the previous field's last
                    s.Hold = true;
                    s.MuA = muA + creep;
                    Actualise(s, noise, 0.02);
                    snaps.Add(s);
                }
                if (halcyon)
                    for (int i = 1; i <= 4; i++)   // the step to the field's control point 0: beam on, no planned MU
                    {
                        Snap s = Clone(start);
                        s.Cp = off - 1 + i / 4.0;
                        s.MuE = muStartE;
                        creep += 0.0002;
                        s.MuA = muA + creep;
                        Actualise(s, noise, 0.02);
                        snaps.Add(s);
                    }
                // ---- delivery ----
                var holds = d.Interruptions.Where(h => h.Item1 == f).ToList();
                int steps = (fl.Cps - 1) * d.SnapshotsPerCp;
                Snap last = null;
                for (int i = 0; i <= steps; i++)
                {
                    double lc = (double)i / d.SnapshotsPerCp;
                    foreach (var h in holds.Where(h => last != null && h.Item2 <= lc && h.Item2 > lc - 1.0 / d.SnapshotsPerCp).ToList())
                        for (int j = 0; j < h.Item3; j++)
                        {
                            Snap s = Clone(last);
                            s.Hold = true;
                            snaps.Add(s);
                        }
                    Snap e = State(fl, off, lc, muStartE);
                    Snap lag = State(fl, off, Math.Max(0, lc - d.LeafLagCp), muStartE);
                    e.GA = State(fl, off, Math.Max(0, lc - 0.1), muStartE).GE + noise(0.02);
                    e.CA = e.CE + noise(0.01);
                    double muLocal = fl.Mu * SimPlan.Lerp(fl.Mw, Math.Max(0, lc - 0.03));
                    e.MuA = muA + creep + muLocal;
                    for (int k = 0; k < Leaves; k++) e.LeafA[k] = lag.LeafE[k] + noise(0.002);
                    snaps.Add(e);
                    last = e;
                }
                muA = last.MuA;
                muStartE += fl.Mu;
                off += fl.Cps;
                prev = last;
            }
            for (int j = 0; j < 5; j++) { Snap s = Clone(prev); s.Hold = true; snaps.Add(s); }   // end of delivery

            return Write(plan, d, snaps);
        }

        static Snap State(SimPlan.Field fl, int off, double lc, double muStartE)
        {
            var s = new Snap
            {
                Cp = off + lc,
                MuE = muStartE + fl.Mu * SimPlan.Lerp(fl.Mw, lc),
                GE = fl.Gantry0 + (fl.Gantry1 - fl.Gantry0) * lc / (fl.Cps - 1),
                CE = fl.Coll,
            };
            for (int q = 0; q < SimPlan.Pairs; q++)
            {
                s.LeafE[q] = SimPlan.Lerp(fl.X2, q, lc) / 10.0;                      // X2 bank: same sign
                s.LeafE[SimPlan.Pairs + q] = -SimPlan.Lerp(fl.X1, q, lc) / 10.0;     // X1 bank: sign flipped
            }
            s.GA = s.GE; s.CA = s.CE; s.MuA = s.MuE;
            Array.Copy(s.LeafE, s.LeafA, Leaves);
            return s;
        }

        static Snap Clone(Snap a) { return a.Copy(); }

        static Snap Blend(Snap a, Snap b, double t)
        {
            var s = Clone(b);
            s.GE = a.GE + (b.GE - a.GE) * t;
            s.CE = a.CE + (b.CE - a.CE) * t;
            for (int k = 0; k < Leaves; k++) s.LeafE[k] = a.LeafE[k] + (b.LeafE[k] - a.LeafE[k]) * t;
            s.MuE = a.MuE;
            return s;
        }

        static void Actualise(Snap s, Func<double, double> noise, double g)
        {
            s.GA = s.GE + noise(g);
            s.CA = s.CE + noise(0.01);
            for (int k = 0; k < Leaves; k++) s.LeafA[k] = s.LeafE[k] + noise(0.002);
        }

        static byte[] Write(SimPlan plan, SimDelivery d, List<Snap> snaps)
        {
            var b = d.Version == "5.1" ? LogBuilder.V51(snaps.Count) : LogBuilder.V40(snaps.Count);
            b.Subbeams.Clear();
            int off = 0;
            for (int f = 0; f < plan.Fields.Count; f++)
            {
                var fl = plan.Fields[f];
                b.Subbeams.Add(new Subbeam { ControlPoint = d.HalcyonTransition && f > 0 ? off - 1 : off, MU = (float)fl.Mu, RadTime = 60f, Seq = f, Name = fl.Name });
                off += fl.Cps;
            }
            b.Metadata = "Patient ID:\tSIM001\r\nPlan Name:\tSimulated\r\n" + (d.PlanUid != null ? "Plan UID:\t" + d.PlanUid + "\r\n" : "");
            b.Value = (s, axis, k, actual) =>
            {
                Snap x = snaps[s];
                switch (axis)
                {
                    case 43: return actual ? 0f : (float)(LogBuilder.TimeStart + (d.StrayFirstSnapshot && s == 0 ? -2.48 : s * 0.02));
                    case 42: return (float)x.Cp;
                    case 40: return (float)(actual ? x.MuA : x.MuE);
                    case 41: return actual && x.Hold ? 2f : 0f;
                    case 1: return (float)(actual ? x.GA : x.GE);
                    case 0: return (float)(actual ? x.CA : x.CE);
                    case 2: case 3: case 4: case 5: return float.MaxValue;
                    case 6: return actual ? -12.301f + 0.001f * (s % 3) : -12.3f;
                    case 7: return actual ? 98.702f : 98.7f;
                    case 8: return actual ? 0.398f : 0.4f;
                    case 9: return 180f;
                    case 10: case 11: return 0f;
                    case 50:
                        if (k < 2) return 0f;
                        return (float)(actual ? x.LeafA[k - 2] : x.LeafE[k - 2]);
                    default: return 0f;
                }
            };
            return b.Build();
        }
    }
}
