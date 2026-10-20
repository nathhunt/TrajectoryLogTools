using System;
using System.IO;
using System.Linq;
using ScottPlot;
using TrajectoryLogTools.Analysis;
using TrajectoryLogTools.Charts;
using Xunit;

namespace TrajectoryLogTools.Tests
{
    public class LeafErrorMapTests
    {
        static DeliveryRecord Vmat() { return DeliveryRecord.FromLog(LogBuilder.Parse(GoldenCases.Bin("vmat_A"))); }

        [Fact]
        public void ByTime_OneColumnPerSamplingInterval_HoldsBlank()
        {
            // v4.0: no clock, so exactly one snapshot per 20 ms
            var d = DeliveryRecord.FromLog(LogBuilder.Parse(GoldenCases.Bin("imrt_A")));
            var map = LeafErrorMap.ByTime(d);
            Assert.Equal(114, map.Leaves);
            double[] t = d.Column("ElapsedTime_s");
            Assert.Equal((int)Math.Round((t[t.Length - 1] - t[0]) / 0.02) + 1, map.Columns);
            Assert.Equal(t[0] - 0.01, map.X0, 9);
            int holds = Enumerable.Range(0, d.RowCount).Count(i => !d.IsBeamOn(i));
            int blank = Enumerable.Range(0, map.Columns).Count(g => double.IsNaN(map.Values[0, g]));
            Assert.Equal(holds, blank);
            Assert.True(map.MaxAbs > 0 && map.Rms > 0 && map.Rms <= map.MaxAbs);
        }

        [Fact]
        public void ByTime_APauseWithoutRecordsIsBlank()
        {
            // vmat_A's first snapshot is 2.48 s before the rest
            var map = LeafErrorMap.ByTime(Vmat());
            int blank = Enumerable.Range(0, (int)Math.Round(2.48 / 0.02)).Count(g => double.IsNaN(map.Values[0, g]));
            Assert.True(blank >= 120, blank.ToString());
        }

        [Fact]
        public void ByControlPoint_CoversThePlanAndLeavesTheArcGapBlank()
        {
            var map = LeafErrorMap.ByControlPoint(Vmat());
            Assert.Equal(-0.025, map.X0, 9);           // control point 0, half a 0.05 step to the left
            Assert.Equal(81.025, map.X1, 9);           // 2 arcs x 41 control points
            int mid = (int)Math.Round((40.5 - 0) / 0.05);   // between the arcs: collimator turns in a hold
            Assert.True(double.IsNaN(map.Values[0, mid]));
            Assert.False(double.IsNaN(map.Values[0, (int)Math.Round(20 / 0.05)]));
        }
    }

    public class ChartTests
    {
        static int ColorBars(Plot plt) { return plt.Axes.GetPanels().Count(p => p is ScottPlot.Panels.ColorBar); }

        [Fact]
        public void Redrawing_KeepsOneColourBar()
        {
            var d = DeliveryRecord.FromLog(LogBuilder.Parse(GoldenCases.Bin("vmat_A")));
            var plt = new Plot();
            for (int i = 0; i < 3; i++)
            {
                LogCharts.MlcError(plt, d, i % 2 == 0);
                plt.GetImage(800, 600);
            }
            Assert.Equal(1, ColorBars(plt));
            LogCharts.AxisTrace(plt, d, "GantryRtn_deg", false);
            Assert.Equal(0, ColorBars(plt));
        }

        [Fact]
        public void PanAndZoom_StayInsideTheData_AndHomeResets()
        {
            var d = DeliveryRecord.FromLog(LogBuilder.Parse(GoldenCases.Bin("vmat_A")));
            var map = LeafErrorMap.ByTime(d);
            var plt = new Plot();
            LogCharts.MlcError(plt, d, false);
            plt.GetImage(800, 600);
            AxisLimits home = plt.Axes.GetLimits();
            Assert.Equal(map.X0, home.Left, 6);
            Assert.Equal(map.X1, home.Right, 6);
            Assert.Equal(114.5, home.Bottom, 6);   // leaf 1 at the top
            Assert.Equal(0.5, home.Top, 6);

            // Zoom out far beyond the data: clamped to it.
            plt.Axes.SetLimits(-100, 1000, 500, -500);
            plt.GetImage(800, 600);
            AxisLimits l = plt.Axes.GetLimits();
            Assert.Equal(home.Left, l.Left, 6); Assert.Equal(home.Right, l.Right, 6);
            Assert.Equal(home.Bottom, l.Bottom, 6); Assert.Equal(home.Top, l.Top, 6);

            // Zoomed in and panned off the left edge: slides back, keeps its size.
            plt.Axes.SetLimits(-50, -40, 60, 50);
            plt.GetImage(800, 600);
            l = plt.Axes.GetLimits();
            Assert.Equal(home.Left, l.Left, 6);
            Assert.Equal(home.Left + 10, l.Right, 6);
            Assert.Equal(60, l.Bottom, 6); Assert.Equal(50, l.Top, 6);

            ChartStyle.Home(plt);
            l = plt.Axes.GetLimits();
            Assert.Equal(home.Left, l.Left, 6); Assert.Equal(home.Bottom, l.Bottom, 6);
        }

        [Fact]
        public void LineCharts_AreBoundedToo()
        {
            var d = DeliveryRecord.FromLog(LogBuilder.Parse(GoldenCases.Bin("imrt_A")));
            var plt = new Plot();
            LogCharts.AxisError(plt, d, "GantryRtn_deg", true);
            plt.GetImage(800, 600);
            AxisLimits home = plt.Axes.GetLimits();
            plt.Axes.SetLimits(home.Left - 100, home.Right + 100, home.Bottom - 100, home.Top + 100);
            plt.GetImage(800, 600);
            AxisLimits l = plt.Axes.GetLimits();
            Assert.Equal(home.Left, l.Left, 6); Assert.Equal(home.Top, l.Top, 6);
        }

        [Fact]
        public void ComparisonFigure_Saves()
        {
            using (var dir = new TempDir())
            {
                GoldenCases.WriteInputs(dir.Path);
                var r = DeliveryComparison.Compare(DeliveryRecord.Load(dir.File("imrt_A.bin")), DeliveryRecord.Load(dir.File("imrt_B.bin")));
                string png = dir.File("fig.png");
                DeliveryCharts.SaveFigure(r, png);
                Assert.True(new FileInfo(png).Length > 10000);
            }
        }
    }
}
