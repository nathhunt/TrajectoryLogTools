// NumPy.cs - the few NumPy / pandas operations the delivery comparison uses, with the same
// definitions (so results match the original Python script).

using System;
using System.Collections.Generic;
using System.Linq;

namespace TrajectoryLogTools.Analysis
{
    internal static class NumPy
    {
        /// <summary>np.interp: linear interpolation in increasing xp, clamped to the end values.</summary>
        public static double Interp(double x, double[] xp, double[] fp)
        {
            int n = xp.Length;
            if (n == 0) return double.NaN;
            if (x <= xp[0]) return fp[0];
            if (x >= xp[n - 1]) return fp[n - 1];
            int lo = 0, hi = n - 1;   // xp[lo] <= x < xp[hi]
            while (hi - lo > 1)
            {
                int mid = (lo + hi) / 2;
                if (xp[mid] <= x) lo = mid; else hi = mid;
            }
            if (x == xp[lo]) return fp[lo];
            double slope = (fp[lo + 1] - fp[lo]) / (xp[lo + 1] - xp[lo]);
            return slope * (x - xp[lo]) + fp[lo];
        }

        public static double[] Interp(double[] x, double[] xp, double[] fp)
        {
            var r = new double[x.Length];
            for (int i = 0; i < x.Length; i++) r[i] = Interp(x[i], xp, fp);
            return r;
        }

        /// <summary>np.linspace with endpoint.</summary>
        public static double[] Linspace(double start, double stop, int num)
        {
            if (num <= 0) return new double[0];
            var y = new double[num];
            if (num == 1) { y[0] = start; return y; }
            double step = (stop - start) / (num - 1);
            for (int i = 0; i < num; i++) y[i] = i * step + start;
            y[num - 1] = stop;
            return y;
        }

        /// <summary>np.median (NaN in, NaN out).</summary>
        public static double Median(IEnumerable<double> values)
        {
            double[] s = values.ToArray();
            if (s.Length == 0 || s.Any(double.IsNaN)) return double.NaN;
            Array.Sort(s);
            int m = s.Length / 2;
            return s.Length % 2 == 1 ? s[m] : (s[m - 1] + s[m]) / 2.0;
        }

        /// <summary>pandas Series.median: NaN values are skipped.</summary>
        public static double MedianSkipNaN(IEnumerable<double> values)
        {
            return Median(values.Where(v => !double.IsNaN(v)));
        }

        /// <summary>np.percentile, default "linear" method.</summary>
        public static double Percentile(double[] values, double p)
        {
            if (values.Length == 0) return double.NaN;
            double[] s = (double[])values.Clone();
            Array.Sort(s);
            double rank = p / 100.0 * (s.Length - 1);
            int lo = (int)Math.Floor(rank);
            int hi = Math.Min(lo + 1, s.Length - 1);
            double t = rank - lo;
            double a = s[lo], b = s[hi];
            // NumPy's lerp: a + (b - a) * t, computed from the upper end when t >= 0.5
            double d = b - a;
            return t >= 0.5 ? b - d * (1 - t) : a + d * t;
        }

        /// <summary>pandas Series.std (ddof = 1); NaN for fewer than 2 values.</summary>
        public static double Std(IList<double> values)
        {
            int n = values.Count;
            if (n < 2) return double.NaN;
            double mean = values.Sum() / n;
            double ss = 0;
            foreach (double v in values) ss += (v - mean) * (v - mean);
            return Math.Sqrt(ss / (n - 1));
        }

        /// <summary>np.corrcoef(x, y)[0, 1].</summary>
        public static double Corr(double[] x, double[] y)
        {
            int n = x.Length;
            double mx = x.Sum() / n, my = y.Sum() / n;
            double sxy = 0, sxx = 0, syy = 0;
            for (int i = 0; i < n; i++)
            {
                double dx = x[i] - mx, dy = y[i] - my;
                sxy += dx * dy; sxx += dx * dx; syy += dy * dy;
            }
            double c = sxy / (n - 1), sx = Math.Sqrt(sxx / (n - 1)), sy = Math.Sqrt(syy / (n - 1));
            double r = c / sx / sy;
            return Math.Max(-1.0, Math.Min(1.0, r));
        }

        /// <summary>Python / NumPy round(x, digits): half to even on x * 10^digits.</summary>
        public static double Round(double x, int digits)
        {
            double p = Math.Pow(10, digits);
            return Math.Round(x * p, MidpointRounding.ToEven) / p;
        }
    }
}
