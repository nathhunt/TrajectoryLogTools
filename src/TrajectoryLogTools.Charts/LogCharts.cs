// LogCharts.cs - charts of a single log for the log viewer.

using System;
using System.Collections.Generic;
using System.Linq;
using ScottPlot;
using TrajectoryLogTools.Analysis;

namespace TrajectoryLogTools.Charts
{
    public static class LogCharts
    {
        /// <summary>Axes the viewer can plot: column base name -> display name.</summary>
        public static List<KeyValuePair<string, string>> PlottableAxes(DeliveryRecord d)
        {
            var axes = new List<KeyValuePair<string, string>>();
            foreach (string c in d.ColumnNames)
            {
                if (!c.EndsWith("_Expected", StringComparison.Ordinal) || c.StartsWith("MLC_", StringComparison.Ordinal)) continue;
                string b = c.Substring(0, c.Length - "_Expected".Length);
                if (b == "BeamHold" || !d.Has(b + "_Actual")) continue;
                if (d.Column(c).All(double.IsNaN)) continue;
                axes.Add(new KeyValuePair<string, string>(b, b.Replace("_", " ")));
            }
            return axes;
        }

        static double[] XValues(DeliveryRecord d, bool vsControlPoint)
        {
            return d.Column(vsControlPoint ? "ControlPoint" : "ElapsedTime_s");
        }

        /// <summary>Expected and actual values of an axis (e.g. "GantryRtn_deg"); beam holds shaded.</summary>
        public static void AxisTrace(Plot plt, DeliveryRecord d, string axis, bool vsControlPoint)
        {
            plt.Clear();
            double[] x = XValues(d, vsControlPoint);
            ShadeHolds(plt, d, x);
            ChartStyle.BrokenLine(plt, x, d.Column(axis + "_Expected"), ChartStyle.Expected, "Expected", 1.4f);
            ChartStyle.BrokenLine(plt, x, d.Column(axis + "_Actual"), ChartStyle.Actual, "Actual", 1.1f);
            ChartStyle.Title(plt, axis.Replace("_", " "));
            plt.XLabel(vsControlPoint ? "Control point" : "Time (s)");
            plt.ShowLegend(Alignment.UpperRight);
        }

        /// <summary>Actual - expected of an axis.</summary>
        public static void AxisError(Plot plt, DeliveryRecord d, string axis, bool vsControlPoint)
        {
            plt.Clear();
            double[] x = XValues(d, vsControlPoint);
            ShadeHolds(plt, d, x);
            double[] e = d.Column(axis + "_Expected"), a = d.Column(axis + "_Actual");
            ChartStyle.BrokenLine(plt, x, a.Select((v, i) => v - e[i]).ToArray(), ChartStyle.Actual, "Actual − Expected", 1.1f);
            plt.Add.HorizontalLine(0, 0.6f, ChartStyle.Zero);
            ChartStyle.Title(plt, axis.Replace("_", " ") + ": actual − expected");
            plt.XLabel(vsControlPoint ? "Control point" : "Time (s)");
        }

        /// <summary>MLC actual - expected (mm), leaf by snapshot; beam-hold snapshots blank.</summary>
        public static void MlcError(Plot plt, DeliveryRecord d, bool vsControlPoint, double clipMm = 1.0)
        {
            plt.Clear();
            int leaves = d.LeafCount;
            if (leaves == 0 || d.RowCount == 0) { ChartStyle.Empty(plt, "No MLC data"); return; }
            var m = new double[leaves, d.RowCount];
            double sum2 = 0, max = 0; long n = 0;
            for (int l = 0; l < leaves; l++)
            {
                double[] a = d.Column(DeliveryRecord.LeafColumn(l + 1, "Actual")), e = d.Column(DeliveryRecord.LeafColumn(l + 1, "Expected"));
                for (int s = 0; s < d.RowCount; s++)
                {
                    double v = d.IsBeamOn(s) ? (a[s] - e[s]) * 10 : double.NaN;
                    m[l, s] = v;
                    if (!double.IsNaN(v)) { sum2 += v * v; n++; max = Math.Max(max, Math.Abs(v)); }
                }
            }
            double x0 = 0.5, x1 = d.RowCount + 0.5;
            string xl = "Snapshot (blank = beam hold)";
            if (!vsControlPoint)
            {
                double[] t = d.Column("ElapsedTime_s");
                x0 = t[0]; x1 = t[t.Length - 1];
                xl = "Time (s), snapshot order (blank = beam hold)";
            }
            ChartStyle.DiffHeatmap(plt, ChartStyle.Clip(m, clipMm), new CoordinateRect(x0, x1, leaves + 0.5, 0.5), clipMm,
                                   "mm (clipped at ±" + clipMm.ToString(System.Globalization.CultureInfo.InvariantCulture) + ")");
            plt.Axes.SetLimitsY(leaves + 0.5, 0.5);
            ChartStyle.Title(plt, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "MLC actual − expected, beam on   (RMS {0:F3} mm, max {1:F2} mm)", n > 0 ? Math.Sqrt(sum2 / n) : double.NaN, max));
            plt.XLabel(xl);
            plt.YLabel("Leaf number");
        }

        static void ShadeHolds(Plot plt, DeliveryRecord d, double[] x)
        {
            int s = 0;
            while (s < d.RowCount)
            {
                if (d.IsBeamOn(s)) { s++; continue; }
                int a = s;
                while (s + 1 < d.RowCount && !d.IsBeamOn(s + 1)) s++;
                if (!double.IsNaN(x[a]) && !double.IsNaN(x[s]) && x[s] > x[a])
                {
                    var span = plt.Add.HorizontalSpan(x[a], x[s]);
                    span.FillStyle.Color = ChartStyle.Hold.WithAlpha(0.15);
                    span.LineStyle.Width = 0;
                }
                s++;
            }
        }
    }
}
