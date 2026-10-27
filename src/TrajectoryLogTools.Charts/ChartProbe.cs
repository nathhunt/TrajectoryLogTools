// ChartProbe.cs - what is under the mouse on a chart, for the app's data cursor:
//   * a line: the nearest point of the nearest line (within 24 pixels), its series, x and y;
//   * a difference map (MLC errors): the cell under the mouse, its value, x (time or control
//     point) and y (leaf number, or pair and bank for maps by Eclipse value index);
//   * bars (RMS per leaf): the bar's leaf and value.
// Axis titles name x and y, so the text reads e.g. "Control point: 87" / "deg: 0.052".

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using ScottPlot;
using ScottPlot.Plottables;

namespace TrajectoryLogTools.Charts
{
    public static class ChartProbe
    {
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        /// <summary>How near (pixels) a line must be to the mouse for its point to be shown.</summary>
        public const float NearPx = 24;

        public sealed class Hit
        {
            public string Text;
            /// <summary>The point of a line (to mark), or null for a map cell or a bar.</summary>
            public Coordinates? Point;
            public Color Color = Colors.Black;
        }

        /// <summary>What is at pixel <paramref name="px"/> of a rendered chart, or null (outside the data, or nothing near).</summary>
        public static Hit At(Plot plt, Pixel px)
        {
            RenderDetails rd = plt.LastRender;
            if (!rd.DataRect.Contains(px)) return null;
            Coordinates c = plt.GetCoordinates(px);
            string xName = Name(plt.Axes.Bottom.Label.Text, "x"), yName = Name(plt.Axes.Left.Label.Text, "y");
            Coordinates? point;
            Color color;
            string text = Line(plt, rd, px, c, xName, yName, out point, out color);
            if (text != null) return new Hit { Text = text, Point = point, Color = color };
            text = Bars(plt, c, yName) ?? Map(plt, c, xName, yName);
            return text == null ? null : new Hit { Text = text };
        }

        /// <summary>An axis title without its notes in brackets ("Time (s) (blank = ...)" -> "Time (s)").</summary>
        static string Name(string label, string fallback)
        {
            if (string.IsNullOrWhiteSpace(label)) return fallback;
            int i = label.IndexOf(" (blank", StringComparison.Ordinal);
            return (i > 0 ? label.Substring(0, i) : label).Trim();
        }

        static string N(double v)
        {
            if (double.IsNaN(v)) return "no data";
            double a = Math.Abs(v);
            return v.ToString(a >= 1000 ? "0.#" : a >= 1 ? "0.###" : "0.####", Inv);
        }

        // ---- lines: the nearest point of the nearest line ----
        static string Line(Plot plt, RenderDetails rd, Pixel px, Coordinates c, string xName, string yName, out Coordinates? point, out Color color)
        {
            point = null;
            color = Colors.Black;
            var scatters = plt.GetPlottables().OfType<Scatter>().Where(s => s.IsVisible).ToList();
            if (scatters.Count == 0) return null;
            // a line broken at gaps is several scatters; only the first carries the legend text
            var names = new Dictionary<string, string>();
            foreach (Scatter s in scatters)
                if (!string.IsNullOrEmpty(s.LegendText) && !names.ContainsKey(s.Color.ToHex())) names[s.Color.ToHex()] = s.LegendText;
            Scatter best = null;
            DataPoint bestPoint = DataPoint.None;
            double bestD = double.PositiveInfinity;
            foreach (Scatter s in scatters)
            {
                DataPoint dp = s.Data.GetNearest(c, rd, NearPx);
                if (!dp.IsReal) continue;
                Pixel p = plt.GetPixel(dp.Coordinates);
                double d = Math.Sqrt((p.X - px.X) * (p.X - px.X) + (p.Y - px.Y) * (p.Y - px.Y));
                if (d < bestD) { bestD = d; best = s; bestPoint = dp; }
            }
            if (best == null) return null;
            point = bestPoint.Coordinates;
            color = best.Color;
            string name;
            names.TryGetValue(best.Color.ToHex(), out name);
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(name)) sb.AppendLine(name);
            sb.AppendLine(xName + ": " + N(bestPoint.X));
            sb.Append(yName + ": " + N(bestPoint.Y));
            return sb.ToString();
        }

        // ---- bars: the bar under the mouse (horizontal bars by leaf, as RMS per leaf) ----
        static string Bars(Plot plt, Coordinates c, string yName)
        {
            foreach (BarPlot bp in plt.GetPlottables().OfType<BarPlot>().Where(b => b.IsVisible))
            {
                foreach (Bar b in bp.Bars)
                {
                    bool horizontal = b.Orientation == ScottPlot.Orientation.Horizontal;
                    double pos = horizontal ? c.Y : c.X;
                    if (Math.Abs(pos - b.Position) > b.Size / 2) continue;
                    string title = plt.Axes.Title.Label.Text;
                    return (horizontal ? yName : "x") + ": " + N(b.Position) + "\n" + (string.IsNullOrEmpty(title) ? "value" : title) + ": " + N(b.Value);
                }
            }
            return null;
        }

        // ---- difference maps: the cell under the mouse ----
        static string Map(Plot plt, Coordinates c, string xName, string yName)
        {
            Heatmap hm = plt.GetPlottables().OfType<Heatmap>().FirstOrDefault(h => h.IsVisible);
            if (hm == null || hm.Extent == null) return null;
            double[,] v = hm.Intensities;
            int rows = v.GetLength(0), cols = v.GetLength(1);
            if (rows == 0 || cols == 0) return null;
            CoordinateRect ext = hm.Extent.Value;
            double x0 = Math.Min(ext.Left, ext.Right), x1 = Math.Max(ext.Left, ext.Right);
            double y0 = Math.Min(ext.Top, ext.Bottom), y1 = Math.Max(ext.Top, ext.Bottom);
            if (c.X < x0 || c.X > x1 || c.Y < y0 || c.Y > y1) return null;
            // row 0 (leaf 1 / first value) is the smallest y, at the top of the inverted axis (ChartStyle.DiffHeatmap)
            int col = Math.Min(cols - 1, (int)Math.Floor((c.X - x0) / (x1 - x0) * cols));
            int row = Math.Min(rows - 1, (int)Math.Floor((c.Y - y0) / (y1 - y0) * rows));
            double xc = x0 + (col + 0.5) * (x1 - x0) / cols;
            double yc = y0 + (row + 0.5) * (y1 - y0) / rows;
            string unit = plt.Axes.GetPanels().OfType<ScottPlot.Panels.ColorBar>().Select(b => b.Label).FirstOrDefault(l => !string.IsNullOrEmpty(l)) ?? "";
            string yText;
            if (yName.StartsWith("Value index", StringComparison.Ordinal))
            {
                int k = (int)Math.Round(yc);
                yText = "Pair " + (k / 2 + 1) + " " + (k % 2 == 0 ? "X1" : "X2") + " (value " + k + ")";
            }
            else yText = yName + ": " + N(Math.Round(yc, 6));
            return "Value: " + N(v[row, col]) + (unit.Length > 0 && !double.IsNaN(v[row, col]) ? " " + unit : "") + "\n" +
                   xName + ": " + N(Math.Round(xc, 6)) + "\n" + yText;
        }
    }
}
