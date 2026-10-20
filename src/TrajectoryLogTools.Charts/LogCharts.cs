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
            ChartStyle.ClearAll(plt);
            double[] x = XValues(d, vsControlPoint);
            ShadeHolds(plt, d, x);
            ChartStyle.BrokenLine(plt, x, d.Column(axis + "_Expected"), ChartStyle.Expected, "Expected", 1.4f);
            ChartStyle.BrokenLine(plt, x, d.Column(axis + "_Actual"), ChartStyle.Actual, "Actual", 1.1f);
            ChartStyle.Title(plt, axis.Replace("_", " "));
            plt.XLabel(vsControlPoint ? "Control point" : "Time (s)");
            plt.ShowLegend(Alignment.UpperRight);
            ChartStyle.Finish(plt);
        }

        /// <summary>Actual - expected of an axis.</summary>
        public static void AxisError(Plot plt, DeliveryRecord d, string axis, bool vsControlPoint)
        {
            ChartStyle.ClearAll(plt);
            double[] x = XValues(d, vsControlPoint);
            ShadeHolds(plt, d, x);
            double[] e = d.Column(axis + "_Expected"), a = d.Column(axis + "_Actual");
            ChartStyle.BrokenLine(plt, x, a.Select((v, i) => v - e[i]).ToArray(), ChartStyle.Actual, "Actual − Expected", 1.1f);
            plt.Add.HorizontalLine(0, 0.6f, ChartStyle.Zero);
            ChartStyle.Title(plt, axis.Replace("_", " ") + ": actual − expected");
            plt.XLabel(vsControlPoint ? "Control point" : "Time (s)");
            ChartStyle.Finish(plt);
        }

        /// <summary>
        /// MLC actual - expected (mm) for every leaf, beam on only, against time or control point.
        /// The colour scale is symmetric, +/- the largest |error|.
        /// </summary>
        public static void MlcError(Plot plt, DeliveryRecord d, bool vsControlPoint)
        {
            ChartStyle.ClearAll(plt);
            if (d == null || d.LeafCount == 0 || d.RowCount == 0) { ChartStyle.Empty(plt, "No MLC data"); return; }
            LeafErrorMap map = vsControlPoint ? LeafErrorMap.ByControlPoint(d) : LeafErrorMap.ByTime(d);
            if (map.Columns == 0 || double.IsNaN(map.MaxAbs)) { ChartStyle.Empty(plt, "No beam-on MLC data"); return; }
            double range = ChartStyle.SymmetricRange(map.MaxAbs);
            ChartStyle.DiffHeatmap(plt, map.Values, new CoordinateRect(map.X0, map.X1, map.Leaves + 0.5, 0.5), range, "mm");
            ChartStyle.Finish(plt, new AxisLimits(map.X0, map.X1, map.Leaves + 0.5, 0.5));
            ChartStyle.Title(plt, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "MLC actual − expected, beam on   (RMS {0:F3} mm, max {1:F3} mm)", map.Rms, map.MaxAbs));
            plt.XLabel(vsControlPoint ? "Control point (blank = no beam-on data)" : "Time (s) (blank = beam hold or pause)");
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
