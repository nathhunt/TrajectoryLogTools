using System.Linq;
using ScottPlot;
using TrajectoryLogTools.Analysis;
using TrajectoryLogTools.Charts;
using Xunit;

namespace TrajectoryLogTools.Tests
{
    public class AnglesTests
    {
        [Theory]
        [InlineData(180.0, 0.0)]
        [InlineData(90.0, 90.0)]
        [InlineData(0.0, 180.0)]
        [InlineData(270.0, 270.0)]
        [InlineData(181.0, 359.0)]
        [InlineData(179.0, 1.0)]
        public void VarianToIec(double varian, double iec)
        {
            Assert.Equal(iec, Angles.Convert(varian, AngleScale.Varian, AngleScale.Iec), 9);
            Assert.Equal(varian, Angles.Convert(iec, AngleScale.Iec, AngleScale.Varian), 9);   // its own inverse
            Assert.Equal(varian, Angles.Convert(varian, AngleScale.Varian, AngleScale.Varian), 9);
        }

        [Fact]
        public void ScaleOfHeader()
        {
            Assert.Equal(AngleScale.Iec, Angles.ScaleOf(2));
            Assert.Equal(AngleScale.Varian, Angles.ScaleOf(1));
            Assert.Equal(AngleScale.Varian, Angles.ScaleOf(3));   // Halcyon: machine scale, isocentric couch
            Assert.Equal(AngleScale.Varian, Angles.ScaleOf(0));   // CSV export
        }

        [Fact]
        public void Differences()
        {
            Assert.Equal(-1.0, Angles.DifferenceSign(AngleScale.Varian, AngleScale.Iec));
            Assert.Equal(1.0, Angles.DifferenceSign(AngleScale.Iec, AngleScale.Iec));
            Assert.Equal(0.2, Angles.WrapDifference(0.1 - 359.9), 9);
            Assert.Equal(-0.2, Angles.WrapDifference(359.9 - 0.1), 9);
            Assert.Equal(180.0, Angles.WrapDifference(-180.0), 9);
        }

        [Fact]
        public void ConvertText_KeepsTheFormat()
        {
            Assert.Equal("359.0->1.0", Angles.ConvertText("181.0->179.0", AngleScale.Varian, AngleScale.Iec));
            Assert.Equal("90", Angles.ConvertText("90", AngleScale.Varian, AngleScale.Iec));
            Assert.Equal("0.00", Angles.ConvertText("180.00", AngleScale.Varian, AngleScale.Iec));
            Assert.Equal("", Angles.ConvertText("", AngleScale.Varian, AngleScale.Iec));
            Assert.Equal("181.0->179.0", Angles.ConvertText("181.0->179.0", AngleScale.Varian, AngleScale.Varian));
        }

        /// <summary>The log's gantry, converted, is the gantry of the Eclipse exports of the same fields.</summary>
        [Fact]
        public void LogGantryInIec_MatchesTheEclipseExports()
        {
            using (var dir = new TempDir())
            {
                GoldenCases.WriteInputs(dir.Path);
                DeliveryRecord d = DeliveryRecord.Load(dir.File("imrt_A.bin"));
                Assert.Equal(AngleScale.Varian, Angles.ScaleOf(d.AxisScale));
                double[] g = d.Column("GantryRtn_deg_Expected"), sub = d.Column("Subbeam");
                for (int f = 1; f <= 3; f++)
                {
                    int first = Enumerable.Range(0, d.RowCount).First(i => (int)sub[i] == f && d.IsBeamOn(i));
                    double eclipse = TemplateMatcher.Read(GoldenCases.TemplatePath(dir.Path, "imrt", f)).GantryIec;
                    Assert.Equal(eclipse, Angles.Convert(g[first], AngleScale.Varian, AngleScale.Iec), 1);
                }
            }
        }

        [Fact]
        public void Charts_TitleTheScale()
        {
            var d = DeliveryRecord.FromLog(LogBuilder.Parse(GoldenCases.Bin("vmat_A")));
            var plt = new Plot();
            LogCharts.AxisTrace(plt, d, "GantryRtn_deg", false, AngleScale.Iec);
            Assert.Contains("IEC 61217", plt.Axes.Title.Label.Text);
            LogCharts.AxisError(plt, d, "GantryRtn_deg", true, AngleScale.Varian);
            Assert.Contains("Varian Standard", plt.Axes.Title.Label.Text);
            Assert.Equal("GantryRtn deg", LogCharts.AxisTitle("GantryRtn_deg", d, null));
            Assert.Equal("CouchRtn deg (as recorded)", LogCharts.AxisTitle("CouchRtn_deg", d, AngleScale.Iec));
            plt.GetImage(600, 400);
        }
    }
}
