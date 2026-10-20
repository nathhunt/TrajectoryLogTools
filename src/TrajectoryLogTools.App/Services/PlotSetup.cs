// PlotSetup.cs - mouse behaviour shared by every chart: drag to pan (kept inside the data by
// ChartStyle.Finish), double-click to go back to the full view, and the scroll wheel:
//   * on a page of the main window, the wheel scrolls the page and Ctrl + wheel zooms the chart
//     (the page doesn't move while zooming);
//   * in a window of its own (a details window, or ScottPlot's "Open in new window"), the wheel
//     zooms the chart.

using System.Windows.Input;
using ScottPlot;
using ScottPlot.Interactivity.UserActionResponses;
using ScottPlot.WPF;
using TrajectoryLogTools.Charts;

namespace TrajectoryLogTools.App.Services
{
    public static class PlotSetup
    {
        /// <summary>Zoom per wheel notch: the axis span changes by 15 %, as ScottPlot's own wheel zoom.</summary>
        const double ZoomStep = 1.15;

        /// <summary>Charts on a page: wheel scrolls the page, Ctrl + wheel zooms.</summary>
        public static void Configure(params WpfPlot[] plots) { Configure(false, plots); }

        /// <summary>Charts in a window of their own: the wheel zooms.</summary>
        public static void ConfigureInWindow(params WpfPlot[] plots) { Configure(true, plots); }

        static void Configure(bool ownWindow, WpfPlot[] plots)
        {
            foreach (WpfPlot p in plots)
            {
                WpfPlot plot = p;
                // ScottPlot's default double-click toggles a benchmark overlay; use it to reset instead.
                plot.UserInputProcessor.DoubleLeftClickBenchmark(false);
                plot.MouseDoubleClick += (s, e) =>
                {
                    ChartStyle.Home(plot.Plot);
                    plot.Refresh();
                    e.Handled = true;
                };
                if (ownWindow) continue;

                // The wheel is ours: zoom only with Ctrl, otherwise leave the event to the page's scroll viewer.
                plot.UserInputProcessor.RemoveAll<MouseWheelZoom>();
                plot.PreviewMouseWheel += (s, e) =>
                {
                    if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                    System.Windows.Point at = e.GetPosition(plot);
                    float scale = plot.DisplayScale;
                    var pixel = new Pixel((float)(at.X * scale), (float)(at.Y * scale));
                    double f = e.Delta > 0 ? ZoomStep : 1 / ZoomStep;
                    plot.Plot.Axes.Zoom(pixel, f, f);
                    plot.Refresh();
                    e.Handled = true;   // handled here, so the page doesn't scroll
                };
            }
        }
    }
}
