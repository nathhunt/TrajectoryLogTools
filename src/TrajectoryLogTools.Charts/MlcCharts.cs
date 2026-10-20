// MlcCharts.cs - charts of an Eclipse MLC reconstruction.

using System;
using ScottPlot;
using TrajectoryLogTools.Analysis;

namespace TrajectoryLogTools.Charts
{
    public static class MlcCharts
    {
        /// <summary>
        /// Written positions minus the template plan (or minus the reconstruction from the log's
        /// expected positions, without a template), mm, Eclipse value (X1/X2 per pair) by control point.
        /// </summary>
        public static void Difference(Plot plt, SubbeamResult r)
        {
            plt.Clear();
            if (r == null || r.Reconstructed == null) { ChartStyle.Empty(plt, "No reconstruction"); return; }
            double[,] reference = r.TemplatePlan ?? r.ReconstructedExpected;
            int cps = r.Reconstructed.GetLength(0), values = r.Reconstructed.GetLength(1);
            var m = new double[values, cps];
            for (int cp = 0; cp < cps; cp++)
                for (int k = 0; k < values; k++) m[k, cp] = r.Reconstructed[cp, k] - reference[cp, k];
            ChartStyle.DiffHeatmap(plt, ChartStyle.Clip(m, 0.5), new CoordinateRect(-0.5, cps - 0.5, values - 0.5, -0.5), 0.5, "mm (clipped at ±0.5)");
            plt.Axes.SetLimitsY(values - 0.5, -0.5);
            ChartStyle.Title(plt, "Written leaf positions − " + (r.TemplatePlan != null ? "template plan" : "reconstruction from expected positions"));
            plt.XLabel("Control point");
            plt.YLabel("Value index (X1, X2 of pair 1, 2, ...)");
        }

        /// <summary>One leaf's position by control point: written, from expected positions, and template.</summary>
        public static void Leaf(Plot plt, SubbeamResult r, int valueIndex)
        {
            plt.Clear();
            if (r == null || r.Reconstructed == null) { ChartStyle.Empty(plt, "No reconstruction"); return; }
            int cps = r.Reconstructed.GetLength(0);
            var x = new double[cps];
            var w = new double[cps];
            var e = new double[cps];
            var t = new double[cps];
            for (int cp = 0; cp < cps; cp++)
            {
                x[cp] = cp;
                w[cp] = r.Reconstructed[cp, valueIndex];
                e[cp] = r.ReconstructedExpected[cp, valueIndex];
                t[cp] = r.TemplatePlan != null ? r.TemplatePlan[cp, valueIndex] : double.NaN;
            }
            if (r.TemplatePlan != null) ChartStyle.BrokenLine(plt, x, t, ChartStyle.Plan, "Template plan", 2.5f);
            ChartStyle.BrokenLine(plt, x, e, ChartStyle.Expected, "From expected positions", 1.4f);
            ChartStyle.BrokenLine(plt, x, w, ChartStyle.Actual, "Written", 1.2f);
            ChartStyle.Title(plt, "Pair " + (valueIndex / 2 + 1) + (valueIndex % 2 == 0 ? " X1" : " X2"));
            plt.XLabel("Control point");
            plt.YLabel("mm (IEC 61217)");
            plt.ShowLegend(Alignment.UpperRight);
        }
    }
}
