// DeliveryCharts.cs - the delivery comparison figure (as compare_trajectory_logs.py drew it):
// gantry and MU differences along the control points, and the A - B leaf position differences
// (expected and actual) as heatmaps.

using System;
using System.Linq;
using ScottPlot;
using TrajectoryLogTools.Analysis;

namespace TrajectoryLogTools.Charts
{
    public static class DeliveryCharts
    {
        /// <summary>
        /// A - B of the gantry ("Gantry") or MU ("MU") along the control points. Without
        /// <paramref name="legend"/> the caller shows one legend for both charts (the app does).
        /// </summary>
        public static void AxisDifference(Plot plt, DeliveryComparisonResult r, string axis, bool legend = true, AngleScale? scale = null)
        {
            ChartStyle.ClearAll(plt);
            bool gantry = axis == "Gantry";
            // In the other scale an angle difference only changes sign.
            double sign = gantry && scale.HasValue ? Angles.DifferenceSign(Angles.ScaleOf(r.AxisScale), scale.Value) : 1.0;
            Func<double[], double[]> s = v => sign == 1.0 ? v : v.Select(x => sign * x).ToArray();
            ChartStyle.BrokenLine(plt, r.Grid, gantry ? s(r.GantryDiffActual) : r.MuDiffActual, ChartStyle.Actual, "Actual A − Actual B", 1.1f);
            ChartStyle.BrokenLine(plt, r.Grid, gantry ? s(r.GantryDiffExpected) : r.MuDiffExpected, ChartStyle.Expected, "Expected A − Expected B", 1.4f);
            plt.Add.HorizontalLine(0, 0.6f, ChartStyle.Zero);
            ChartStyle.Title(plt, gantry ? "Gantry angle difference" + (scale.HasValue ? " (" + Angles.Name(scale.Value) + ")" : "") : "MU difference");
            plt.XLabel("Control point");
            plt.YLabel(gantry ? "deg" : "MU");
            if (legend) plt.ShowLegend(Alignment.UpperRight);
            else plt.HideLegend();
            ChartStyle.Finish(plt);
        }

        /// <summary>Expected or actual leaf positions, A - B (mm), leaf by control point.</summary>
        public static void LeafDifference(Plot plt, DeliveryComparisonResult r, bool actual)
        {
            ChartStyle.ClearAll(plt);
            double[,] m = actual ? r.LeafDiffActual : r.LeafDiffExpected;
            int leaves = m.GetLength(0);
            if (leaves == 0 || r.Grid.Length == 0) { ChartStyle.Empty(plt, "No leaf data"); return; }
            double sum2 = 0, max = 0; long n = 0;
            foreach (double v in m) if (!double.IsNaN(v)) { sum2 += v * v; n++; max = Math.Max(max, Math.Abs(v)); }
            // Leaf 1 at the top; colour scale +/- the largest |difference|.
            ChartStyle.DiffHeatmap(plt, m, new CoordinateRect(r.Grid[0], r.Grid[r.Grid.Length - 1], leaves + 0.5, 0.5),
                ChartStyle.SymmetricRange(max), "mm");
            ChartStyle.Finish(plt, new AxisLimits(r.Grid[0], r.Grid[r.Grid.Length - 1], leaves + 0.5, 0.5));
            ChartStyle.Title(plt, string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0} leaf positions, A − B   (RMS {1:F3} mm, max {2:F2} mm)", actual ? "Actual" : "Expected",
                n > 0 ? Math.Sqrt(sum2 / n) : double.NaN, max));
            plt.XLabel("Control point (blank = no beam-on data, e.g. gantry move)");
            plt.YLabel("Leaf number");
        }

        /// <summary>The 2 x 2 figure: gantry and MU differences, expected and actual leaf differences.</summary>
        /// <param name="scale">Scale of the gantry difference; null = as recorded (the command line).</param>
        public static void SaveFigure(DeliveryComparisonResult r, string path, int width = 1950, int height = 1275, AngleScale? scale = null)
        {
            bool dark = ChartStyle.Dark;
            ChartStyle.Dark = false;   // saved figures are always light
            try { Save(r, path, width, height, scale); }
            finally { ChartStyle.Dark = dark; }
        }

        static void Save(DeliveryComparisonResult r, string path, int width, int height, AngleScale? scale)
        {
            var mp = new Multiplot();
            mp.AddPlots(4);
            mp.Layout = new ScottPlot.MultiplotLayouts.Grid(2, 2);
            AxisDifference(mp.GetPlot(0), r, "Gantry", true, scale);
            AxisDifference(mp.GetPlot(1), r, "MU");
            LeafDifference(mp.GetPlot(2), r, false);
            LeafDifference(mp.GetPlot(3), r, true);
            mp.GetPlot(0).Title("A: " + r.LabelA + "   B: " + r.LabelB + "\nGantry angle difference" + (scale.HasValue ? " (" + Angles.Name(scale.Value) + ")" : ""), 12);
            mp.SavePng(path, width, height);
        }
    }
}
