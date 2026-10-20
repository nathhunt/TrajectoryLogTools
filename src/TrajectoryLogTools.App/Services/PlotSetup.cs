// PlotSetup.cs - mouse behaviour shared by every chart: scroll to zoom and drag to pan (ScottPlot
// defaults, kept inside the data by ChartStyle.Finish), double-click to go back to the full view.

using ScottPlot.WPF;
using TrajectoryLogTools.Charts;

namespace TrajectoryLogTools.App.Services
{
    public static class PlotSetup
    {
        public static void Configure(params WpfPlot[] plots)
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
            }
        }
    }
}
