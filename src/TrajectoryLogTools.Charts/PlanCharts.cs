// PlanCharts.cs - charts of a log-vs-plan comparison, one field at a time: leaf positions minus
// the plan by control point (as delivered, and the transfer check), and the gantry and MU
// differences along the plan's control points.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ScottPlot;
using TrajectoryLogTools.Analysis;

namespace TrajectoryLogTools.Charts
{
    public static class PlanCharts
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static double[] PlanCps(PlanFieldComparison f) { return Enumerable.Range(f.FirstPlanCp, f.ControlPoints).Select(k => (double)k).ToArray(); }

        /// <summary>
        /// Actual minus plan leaf positions at the control points (mm, fitted actual positions), or,
        /// with <paramref name="actual"/> false, the transfer check: the largest |log expected - plan|
        /// in each control-point interval. Rows are Eclipse values (X1, X2 of pair 1, 2, ...).
        /// </summary>
        public static void LeafDifference(Plot plt, PlanFieldComparison f, bool actual)
        {
            ChartStyle.ClearAll(plt);
            if (f == null || !f.Compared) { ChartStyle.Empty(plt, "No comparison"); return; }
            int n = f.ControlPoints, nv = f.PlanLeaves.GetLength(1);
            double[,] m;
            double x0, x1, max = 0;
            if (actual)
            {
                m = new double[nv, n];
                for (int k = 0; k < n; k++)
                    for (int v = 0; v < nv; v++) m[v, k] = f.ActualLeaves[k, v] - f.PlanLeaves[k, v];
                x0 = f.FirstPlanCp - 0.5; x1 = f.FirstPlanCp + n - 0.5;
            }
            else
            {
                m = (double[,])f.TransferIntervalMax.Clone();
                x0 = f.FirstPlanCp; x1 = f.FirstPlanCp + n - 1;
            }
            foreach (double v in m) if (!double.IsNaN(v)) max = Math.Max(max, Math.Abs(v));
            // the transfer map is near zero when the plan matches: keep a 0.05 mm scale so noise isn't magnified
            double range = ChartStyle.SymmetricRange(max, actual ? 0.001 : PlanComparison.TransferLeafTolMm);
            ChartStyle.DiffHeatmap(plt, m, new CoordinateRect(x0, x1, nv - 0.5, -0.5), range, "mm");
            ChartStyle.Finish(plt, new AxisLimits(x0, x1, nv - 0.5, -0.5));
            ChartStyle.Title(plt, actual
                ? string.Format(Inv, "Actual − plan leaf positions at control points   (RMS {0:F3} mm, max {1:F2} mm)", f.CpLeaves.Rms, f.CpLeaves.Max)
                : string.Format(Inv, "Transfer check: |log planned − plan| between control points   (max {0:F3} mm)", f.TransferLeafMaxMm));
            plt.XLabel("Plan control point");
            plt.YLabel("Value index (X1, X2 of pair 1, 2, ...)");
        }

        /// <summary>Gantry, actual and log planned, minus the plan, along the plan's control points.</summary>
        public static void Gantry(Plot plt, PlanFieldComparison f, bool legend = true, AngleScale? scale = null)
        {
            ChartStyle.ClearAll(plt);
            if (f == null || !f.Compared) { ChartStyle.Empty(plt, "No comparison"); return; }
            // the comparison is in IEC 61217; in Varian Standard an angle difference changes sign
            double sign = scale.HasValue ? Angles.DifferenceSign(AngleScale.Iec, scale.Value) : 1.0;
            double[] x = PlanCps(f);
            ChartStyle.BrokenLine(plt, x, x.Select((_, k) => sign * Angles.WrapDifference(f.ActualGantry[k] - f.PlanGantry[k])).ToArray(), ChartStyle.Actual, "Actual − plan", 1.1f);
            ChartStyle.BrokenLine(plt, x, x.Select((_, k) => sign * Angles.WrapDifference(f.ExpectedGantry[k] - f.PlanGantry[k])).ToArray(), ChartStyle.Expected, "Log planned − plan", 1.4f);
            plt.Add.HorizontalLine(0, 0.6f, ChartStyle.Zero);
            ChartStyle.Title(plt, "Gantry angle difference" + (scale.HasValue ? " (" + Angles.Name(scale.Value) + ")" : " (IEC 61217)"));
            plt.XLabel("Plan control point");
            plt.YLabel("deg");
            if (legend) plt.ShowLegend(Alignment.UpperRight); else plt.HideLegend();
            ChartStyle.Finish(plt);
        }

        /// <summary>Cumulative MU, delivered and log planned, minus the plan, along the plan's control points.</summary>
        public static void Mu(Plot plt, PlanFieldComparison f, bool legend = true)
        {
            ChartStyle.ClearAll(plt);
            if (f == null || !f.Compared) { ChartStyle.Empty(plt, "No comparison"); return; }
            double[] x = PlanCps(f);
            ChartStyle.BrokenLine(plt, x, x.Select((_, k) => f.ActualMu[k] - f.PlanMu[k]).ToArray(), ChartStyle.Actual, "Delivered − plan", 1.1f);
            ChartStyle.BrokenLine(plt, x, x.Select((_, k) => f.ExpectedMu[k] - f.PlanMu[k]).ToArray(), ChartStyle.Expected, "Log planned − plan", 1.4f);
            plt.Add.HorizontalLine(0, 0.6f, ChartStyle.Zero);
            ChartStyle.Title(plt, "Cumulative MU difference");
            plt.XLabel("Plan control point");
            plt.YLabel("MU");
            if (legend) plt.ShowLegend(Alignment.UpperRight); else plt.HideLegend();
            ChartStyle.Finish(plt);
        }

        /// <summary>One 2 x 2 figure per compared field (plan_comparison_&lt;field&gt;.png). Returns the files written.</summary>
        public static List<string> SaveFigures(PlanComparisonResult r, string folder, int width = 1950, int height = 1275, AngleScale? scale = null)
        {
            bool dark = ChartStyle.Dark;
            ChartStyle.Dark = false;   // saved figures are always light
            var files = new List<string>();
            try
            {
                Directory.CreateDirectory(folder);
                foreach (PlanFieldComparison f in r.Compared)
                {
                    var mp = new Multiplot();
                    mp.AddPlots(4);
                    mp.Layout = new ScottPlot.MultiplotLayouts.Grid(2, 2);
                    Gantry(mp.GetPlot(0), f, true, scale);
                    Mu(mp.GetPlot(1), f);
                    LeafDifference(mp.GetPlot(2), f, true);
                    LeafDifference(mp.GetPlot(3), f, false);
                    mp.GetPlot(0).Title("Log: " + r.LogLabel + "   Plan: " + r.Plan.DisplayName + ", beam " + f.Beam.Label + "\nGantry angle difference" +
                                        (scale.HasValue ? " (" + Angles.Name(scale.Value) + ")" : " (IEC 61217)"), 12);
                    string name = new string(f.Label.Select(c => char.IsLetterOrDigit(c) || c == '-' || c == '_' ? c : '_').ToArray());
                    string path = Path.Combine(folder, "plan_comparison_" + name + ".png");
                    mp.SavePng(path, width, height);
                    files.Add(path);
                }
            }
            finally { ChartStyle.Dark = dark; }
            return files;
        }
    }
}
