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

        /// <summary>
        /// Expected and actual values of an axis (e.g. "GantryRtn_deg"); beam holds shaded. Gantry and
        /// collimator are shown in <paramref name="scale"/> (null = as recorded); other axes as recorded.
        /// </summary>
        public static void AxisTrace(Plot plt, DeliveryRecord d, string axis, bool vsControlPoint, AngleScale? scale = null)
        {
            ChartStyle.ClearAll(plt);
            double[] x = XValues(d, vsControlPoint);
            ShadeHolds(plt, d, x);
            double[] e = d.Column(axis + "_Expected"), a = d.Column(axis + "_Actual");
            bool angle = Angles.IsConverted(axis) && scale.HasValue;
            AngleScale from = Angles.ScaleOf(d.AxisScale);
            if (angle)
            {
                e = e.Select(v => Angles.Convert(v, from, scale.Value)).ToArray();
                a = a.Select(v => Angles.Convert(v, from, scale.Value)).ToArray();
            }
            double[] xe = x, xa = x;
            if (Angles.IsConverted(axis)) { BreakAtWrap(ref xe, ref e); BreakAtWrap(ref xa, ref a); }
            ChartStyle.BrokenLine(plt, xe, e, ChartStyle.Expected, "Expected", 1.4f);
            ChartStyle.BrokenLine(plt, xa, a, ChartStyle.Actual, "Actual", 1.1f);
            ChartStyle.Title(plt, AxisTitle(axis, d, scale));
            plt.XLabel(vsControlPoint ? "Control point" : "Time (s)");
            plt.ShowLegend(Alignment.UpperRight);
            ChartStyle.Finish(plt);
        }

        /// <summary>Actual - expected of an axis; for gantry and collimator in <paramref name="scale"/> (the sign follows it).</summary>
        public static void AxisError(Plot plt, DeliveryRecord d, string axis, bool vsControlPoint, AngleScale? scale = null)
        {
            ChartStyle.ClearAll(plt);
            double[] x = XValues(d, vsControlPoint);
            ShadeHolds(plt, d, x);
            double[] e = d.Column(axis + "_Expected"), a = d.Column(axis + "_Actual");
            bool angle = Angles.IsConverted(axis);
            double sign = angle && scale.HasValue ? Angles.DifferenceSign(Angles.ScaleOf(d.AxisScale), scale.Value) : 1.0;
            // angles: the difference the short way round (359.9 vs 0.1 is 0.2, not 359.8)
            double[] err = a.Select((v, i) => sign * (angle ? Angles.WrapDifference(v - e[i]) : v - e[i])).ToArray();
            ChartStyle.BrokenLine(plt, x, err, ChartStyle.Actual, "Actual − Expected", 1.1f);
            plt.Add.HorizontalLine(0, 0.6f, ChartStyle.Zero);
            ChartStyle.Title(plt, AxisTitle(axis, d, scale) + ": actual − expected");
            plt.XLabel(vsControlPoint ? "Control point" : "Time (s)");
            ChartStyle.Finish(plt);
        }

        /// <summary>"GantryRtn deg (IEC 61217)"; other axes "(as recorded)" when a scale is chosen.</summary>
        public static string AxisTitle(string axis, DeliveryRecord d, AngleScale? scale)
        {
            string name = axis.Replace("_", " ");
            if (!scale.HasValue) return name;
            return name + (Angles.IsConverted(axis) ? " (" + Angles.Name(scale.Value) + ")" : axis.EndsWith("_deg", StringComparison.Ordinal) || axis.EndsWith("_cm", StringComparison.Ordinal) ? " (as recorded)" : "");
        }

        /// <summary>Inserts a gap where an angle wraps past 0 / 360, so the line doesn't cross the chart.</summary>
        static void BreakAtWrap(ref double[] x, ref double[] y)
        {
            var nx = new List<double>(x.Length + 8);
            var ny = new List<double>(y.Length + 8);
            for (int i = 0; i < y.Length; i++)
            {
                if (i > 0 && !double.IsNaN(y[i]) && !double.IsNaN(y[i - 1]) && Math.Abs(y[i] - y[i - 1]) > 180)
                {
                    nx.Add((x[i] + x[i - 1]) / 2);
                    ny.Add(double.NaN);
                }
                nx.Add(x[i]);
                ny.Add(y[i]);
            }
            x = nx.ToArray();
            y = ny.ToArray();
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

        /// <summary>RMS of actual - expected per leaf (mm), beam on, as horizontal bars (leaf 1 at the top).</summary>
        public static void LeafRms(Plot plt, DeliveryRecord d)
        {
            ChartStyle.ClearAll(plt);
            int leaves = d == null ? 0 : d.LeafCount;
            if (leaves == 0) { ChartStyle.Empty(plt, ""); return; }
            var bars = new List<Bar>();
            double max = 0;
            for (int l = 1; l <= leaves; l++)
            {
                double[] a = d.Column(DeliveryRecord.LeafColumn(l, "Actual")), e = d.Column(DeliveryRecord.LeafColumn(l, "Expected"));
                double sum2 = 0; int n = 0;
                for (int s = 0; s < d.RowCount; s++)
                    if (d.IsBeamOn(s)) { double v = (a[s] - e[s]) * 10; sum2 += v * v; n++; }
                double rms = n > 0 ? Math.Sqrt(sum2 / n) : 0;
                max = Math.Max(max, rms);
                bars.Add(new Bar { Position = l, Value = rms, Size = 0.8, FillColor = ChartStyle.Expected, LineWidth = 0 });
            }
            var bp = plt.Add.Bars(bars);
            bp.Horizontal = true;
            ChartStyle.Title(plt, "RMS per leaf (mm)");
            plt.YLabel("Leaf number");
            ChartStyle.Finish(plt, new AxisLimits(0, Math.Max(max * 1.05, 0.001), leaves + 0.5, 0.5));
        }

        /// <summary>The leaves with the largest RMS actual - expected (beam on): (leaf, mm).</summary>
        public static List<KeyValuePair<int, double>> WorstLeaves(DeliveryRecord d, int count)
        {
            var r = new List<KeyValuePair<int, double>>();
            if (d == null) return r;
            for (int l = 1; l <= d.LeafCount; l++)
            {
                double[] a = d.Column(DeliveryRecord.LeafColumn(l, "Actual")), e = d.Column(DeliveryRecord.LeafColumn(l, "Expected"));
                double sum2 = 0; int n = 0;
                for (int s = 0; s < d.RowCount; s++)
                    if (d.IsBeamOn(s)) { double v = (a[s] - e[s]) * 10; sum2 += v * v; n++; }
                r.Add(new KeyValuePair<int, double>(l, n > 0 ? Math.Sqrt(sum2 / n) : 0));
            }
            return r.OrderByDescending(x => x.Value).Take(count).ToList();
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
