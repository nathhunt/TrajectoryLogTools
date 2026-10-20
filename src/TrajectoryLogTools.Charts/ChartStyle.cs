// ChartStyle.cs - colours and helpers shared by every chart.

using System;
using System.Collections.Generic;
using System.Linq;
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

        /// <summary>
        /// Clears everything a chart function adds: plottables, colour bars (panels, which Clear()
        /// leaves in place) and the pan / zoom boundary.
        /// </summary>
        public static void ClearAll(Plot plt)
        {
            plt.Clear();
            foreach (var p in plt.Axes.GetPanels())
                if (p is ScottPlot.Panels.ColorBar) plt.Axes.Remove(p);
            plt.Axes.Rules.RemoveAll(r => r is DataBoundary);
            plt.Axes.Title.Label.Text = "";
            plt.Axes.Bottom.Label.Text = "";
            plt.Axes.Left.Label.Text = "";
            plt.HideLegend();
        }

        /// <summary>
        /// Shows <paramref name="home"/> (default: the data, auto-scaled) and keeps pan / zoom inside
        /// it. <see cref="Home"/> returns to it (double-click in the GUI).
        /// </summary>
        /// <summary>Dark charts (the GUI sets this from its theme).</summary>
        public static bool Dark { get; set; }

        /// <summary>Background, axis, grid and legend colours for the current theme.</summary>
        public static void ApplyTheme(Plot plt)
        {
            Color fig = Dark ? Color.FromHex("#202020") : Colors.White;
            Color data = Dark ? Color.FromHex("#262626") : Colors.White;
            Color ink = Dark ? Color.FromHex("#E6E6E6") : Color.FromHex("#1A1A1A");
            Color grid = Dark ? Color.FromHex("#3A3A3A") : Color.FromHex("#E8E8E8");
            plt.FigureBackground.Color = fig;
            plt.DataBackground.Color = data;
            plt.Axes.Color(ink);
            plt.Grid.MajorLineColor = grid;
            plt.Legend.BackgroundColor = data;
            plt.Legend.FontColor = ink;
            plt.Legend.OutlineColor = grid;
            foreach (var p in plt.Axes.GetPanels())
            {
                var bar = p as ScottPlot.Panels.ColorBar;
                if (bar != null) bar.LabelStyle.ForeColor = ink;
            }
        }

        public static void Finish(Plot plt, AxisLimits? home = null)
        {
            ApplyTheme(plt);
            if (home == null)
            {
                plt.Axes.AutoScale();
                home = plt.Axes.GetLimits();
            }
            AxisLimits h = home.Value;
            if (!h.IsReal || h.Left == h.Right || h.Bottom == h.Top) return;
            plt.Axes.SetLimits(h);
            plt.Axes.Rules.Add(new DataBoundary(plt.Axes.Bottom, plt.Axes.Left, h));
        }

        /// <summary>Back to the view set by <see cref="Finish"/>.</summary>
        public static void Home(Plot plt)
        {
            var b = plt.Axes.Rules.OfType<DataBoundary>().FirstOrDefault();
            if (b != null) plt.Axes.SetLimits(b.HomeLimits);
            else plt.Axes.AutoScale();
        }

        /// <summary>Symmetric colour range: +/- the largest |value|, at least <paramref name="floor"/>.</summary>
        public static double SymmetricRange(double maxAbs, double floor = 0.001)
        {
            return double.IsNaN(maxAbs) || maxAbs < floor ? floor : maxAbs;
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
            ClearAll(plt);
            plt.Title(message, 13);
            ApplyTheme(plt);
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

    /// <summary>
    /// Keeps the view inside the home limits: zooming out stops at them and panning slides along
    /// them. Works for inverted axes (min > max) too.
    /// </summary>
    public sealed class DataBoundary : IAxisRule
    {
        readonly IXAxis _x;
        readonly IYAxis _y;
        public AxisLimits HomeLimits { get; private set; }

        public DataBoundary(IXAxis x, IYAxis y, AxisLimits home) { _x = x; _y = y; HomeLimits = home; }

        public void Apply(RenderPack rp, bool beforeLayout)
        {
            Clamp(_x, HomeLimits.Left, HomeLimits.Right);
            Clamp(_y, HomeLimits.Bottom, HomeLimits.Top);
        }

        static void Clamp(IAxis axis, double a, double b)
        {
            double lo = Math.Min(a, b), hi = Math.Max(a, b);
            bool inverted = axis.Min > axis.Max;
            double min = Math.Min(axis.Min, axis.Max), max = Math.Max(axis.Min, axis.Max);
            if (max - min >= hi - lo) { min = lo; max = hi; }
            else
            {
                if (min < lo) { max += lo - min; min = lo; }
                if (max > hi) { min -= max - hi; max = hi; }
            }
            if (inverted) axis.Range.Set(max, min);
            else axis.Range.Set(min, max);
        }
    }
}
