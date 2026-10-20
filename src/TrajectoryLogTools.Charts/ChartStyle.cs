// ChartStyle.cs - colours and helpers shared by every chart.

using System;
using System.Collections.Generic;
using ScottPlot;

namespace TrajectoryLogTools.Charts
{
    public static class ChartStyle
    {
        public static readonly Color Expected = Color.FromHex("#2a78d6");
        public static readonly Color Actual = Color.FromHex("#eb6834");
        public static readonly Color Plan = Color.FromHex("#3a9d5d");
        public static readonly Color Hold = Color.FromHex("#9a9a9a");
        public static readonly Color Zero = Color.FromHex("#999999");

        /// <summary>Blue - near white - red, for signed differences.</summary>
        public static readonly IColormap Diverging = new ScottPlot.Colormaps.CustomInterpolated(new[]
        {
            Color.FromHex("#1f5fae"), Color.FromHex("#f0efec"), Color.FromHex("#c43c3b")
        });

        /// <summary>
        /// A line through (xs, ys) that breaks at NaN values: one scatter per unbroken run, the
        /// legend text on the first only.
        /// </summary>
        public static void BrokenLine(Plot plt, double[] xs, double[] ys, Color color, string legend, float width = 1.2f)
        {
            bool first = true;
            int i = 0;
            while (i < xs.Length)
            {
                while (i < xs.Length && (double.IsNaN(ys[i]) || double.IsNaN(xs[i]))) i++;
                int start = i;
                while (i < xs.Length && !double.IsNaN(ys[i]) && !double.IsNaN(xs[i])) i++;
                if (i <= start) continue;
                var x = new double[i - start];
                var y = new double[i - start];
                Array.Copy(xs, start, x, 0, x.Length);
                Array.Copy(ys, start, y, 0, y.Length);
                var sc = plt.Add.ScatterLine(x, y, color);
                sc.LineWidth = width;
                if (first && legend != null) sc.LegendText = legend;
                first = false;
            }
        }

        /// <summary>Signed difference heatmap clipped at +/- <paramref name="clip"/>, with a colour bar.</summary>
        public static ScottPlot.Plottables.Heatmap DiffHeatmap(Plot plt, double[,] values, CoordinateRect extent, double clip, string barLabel)
        {
            var hm = plt.Add.Heatmap(values);
            hm.Colormap = Diverging;
            hm.ManualRange = new ScottPlot.Range(-clip, clip);
            hm.Extent = extent;
            hm.NaNCellColor = Colors.White;
            hm.FlipVertically = true;   // row 0 (leaf 1 / first value) at the top, as the Y axis is inverted
            var bar = plt.Add.ColorBar(hm);
            bar.Label = barLabel;
            return hm;
        }

        public static void Title(Plot plt, string title)
        {
            plt.Title(title, 13);
        }

        public static void Empty(Plot plt, string message)
        {
            plt.Clear();
            plt.Title(message, 13);
        }

        /// <summary>Clips an array of values to +/- clip, leaving NaN alone.</summary>
        public static double[,] Clip(double[,] v, double clip)
        {
            var r = (double[,])v.Clone();
            for (int i = 0; i < r.GetLength(0); i++)
                for (int j = 0; j < r.GetLength(1); j++)
                    if (!double.IsNaN(r[i, j])) r[i, j] = Math.Max(-clip, Math.Min(clip, r[i, j]));
            return r;
        }

        public static IEnumerable<int> Range(int n) { for (int i = 0; i < n; i++) yield return i; }
    }
}
