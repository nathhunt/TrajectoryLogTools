// LeafErrorMap.cs - MLC actual - expected (mm) for every leaf on a regular grid of time or of
// control point, beam-on samples only, for the log viewer's MLC map.
//
//   By time:          each grid step (the sampling interval) takes the snapshot recorded then;
//                     beam holds and pauses (no records) are left blank.
//   By control point: beam-on samples matched by control point, as the delivery comparison does
//                     (first sample at each control point, linear between); control points passed
//                     only during a beam hold are left blank.

using System;
using System.Linq;

namespace TrajectoryLogTools.Analysis
{
    public sealed class LeafErrorMap
    {
        /// <summary>[leaf, column], mm; NaN = no beam-on data.</summary>
        public double[,] Values;
        /// <summary>X coordinate of the left edge of the first column and the right edge of the last.</summary>
        public double X0, X1;
        public int Leaves;
        public double Rms = double.NaN, MaxAbs = double.NaN;

        public int Columns { get { return Values.GetLength(1); } }

        public static LeafErrorMap ByTime(DeliveryRecord d, int maxColumns = 20000)
        {
            int leaves = d.LeafCount, n = d.RowCount;
            double[] t = d.Column("ElapsedTime_s");
            var map = new LeafErrorMap { Leaves = leaves };
            if (n == 0 || leaves == 0) { map.Values = new double[leaves, 0]; return map; }
            double dt = NumPy.Median(Enumerable.Range(1, Math.Max(0, n - 1)).Select(i => t[i] - t[i - 1]).Where(x => x > 0));
            if (double.IsNaN(dt) || dt <= 0) dt = 0.02;
            double t0 = t[0], span = t[n - 1] - t0;
            int cols = (int)Math.Round(span / dt) + 1;
            if (cols > maxColumns) { cols = maxColumns; dt = span / (cols - 1); }
            var err = Errors(d);
            map.Values = new double[leaves, cols];
            int s = 0;
            for (int g = 0; g < cols; g++)
            {
                double tg = t0 + g * dt;
                while (s + 1 < n && Math.Abs(t[s + 1] - tg) <= Math.Abs(t[s] - tg)) s++;
                bool ok = Math.Abs(t[s] - tg) <= dt / 2 + 1e-9 && d.IsBeamOn(s);
                for (int l = 0; l < leaves; l++) map.Values[l, g] = ok ? err[l][s] : double.NaN;
            }
            map.X0 = t0 - dt / 2;
            map.X1 = t0 + (cols - 1) * dt + dt / 2;
            map.Stats();
            return map;
        }

        public static LeafErrorMap ByControlPoint(DeliveryRecord d, double step = 0.05, int maxColumns = 20000)
        {
            int leaves = d.LeafCount;
            var map = new LeafErrorMap { Leaves = leaves };
            DeliveryRecord a = d.RowCount > 0 ? DeliveryComparison.BeamOnByCp(d) : d;
            if (a.RowCount < 2 || leaves == 0) { map.Values = new double[leaves, 0]; return map; }
            double[] cp = a.Column("ControlPoint");
            double first = Math.Floor(cp[0]), last = Math.Ceiling(cp[cp.Length - 1]);
            int cols = (int)Math.Round((last - first) / step) + 1;
            if (cols > maxColumns) { cols = maxColumns; step = (last - first) / (cols - 1); }
            double[] grid = Enumerable.Range(0, cols).Select(i => first + i * step).ToArray();
            bool[] covered = DeliveryComparison.Covered(a, grid);
            var err = Errors(a);
            map.Values = new double[leaves, cols];
            for (int l = 0; l < leaves; l++)
                for (int g = 0; g < cols; g++)
                    map.Values[l, g] = covered[g] ? NumPy.Interp(grid[g], cp, err[l]) : double.NaN;
            map.X0 = first - step / 2;
            map.X1 = first + (cols - 1) * step + step / 2;
            map.Stats();
            return map;
        }

        static double[][] Errors(DeliveryRecord d)
        {
            return Enumerable.Range(1, d.LeafCount).Select(l =>
            {
                double[] a = d.Column(DeliveryRecord.LeafColumn(l, "Actual")), e = d.Column(DeliveryRecord.LeafColumn(l, "Expected"));
                return a.Select((v, i) => (v - e[i]) * 10.0).ToArray();
            }).ToArray();
        }

        void Stats()
        {
            double sum2 = 0, max = 0; long n = 0;
            foreach (double v in Values)
                if (!double.IsNaN(v)) { sum2 += v * v; n++; max = Math.Max(max, Math.Abs(v)); }
            if (n > 0) { Rms = Math.Sqrt(sum2 / n); MaxAbs = max; }
        }
    }
}
