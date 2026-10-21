using System.Linq;
using TrajectoryLogTools.Analysis;
using Xunit;

namespace TrajectoryLogTools.Tests
{
    /// <summary>
    /// Fields after the first, as Halcyon logs them: the header and the counter give the previous
    /// field's last control point, then a one-step transition with no planned MU (seen in a clinical
    /// 3-arc log: subbeams at 0, 179, 359; fields 0-179, 180-359, 360-539).
    /// </summary>
    public class FieldSpanTests
    {
        static DeliveryRecord Halcyon()
        {
            var d = new SimDelivery { HalcyonTransition = true };
            return DeliveryRecord.FromLog(LogBuilder.Parse(DeliverySimulator.Build(SimPlan.Vmat(), d)));
        }

        [Fact]
        public void Header_GivesThePreviousFieldsLastControlPoint()
        {
            var d = Halcyon();
            Assert.Equal(new[] { 0, 40 }, d.Log.Subbeams.Select(s => s.ControlPoint));
        }

        [Fact]
        public void Span_StartsAfterTheTransition()
        {
            var d = Halcyon();
            int first, last;
            Assert.True(FieldSpan.Of(d, 1, out first, out last));
            Assert.Equal(0, first); Assert.Equal(40, last);
            Assert.True(FieldSpan.Of(d, 2, out first, out last));
            Assert.Equal(41, first); Assert.Equal(81, last);
            // the field's beam-on snapshots all have its own collimator (195, not arc 1's 165 of the transition)
            double[] coll = d.Column("CollRtn_deg_Expected");
            Assert.All(FieldSpan.BeamOnRows(d, 2), i => Assert.Equal(195.0, coll[i], 3));
        }

        [Fact]
        public void Rebuild_HasTheFieldsOwnControlPoints()
        {
            using (var dir = new TempDir())
            {
                var r = MlcReconstructor.Run(Halcyon(), dir.File("log.bin"), new MlcReconstructionOptions { OutputFolder = dir.Path });
                Assert.Equal(new[] { 41, 41 }, r.Subbeams.Select(s => s.ControlPoints));
                Assert.Equal(new[] { 0, 41 }, r.Subbeams.Select(s => s.FirstLogControlPoint));
                Assert.All(r.Subbeams, s =>
                {
                    Assert.Equal(RebuildStatus.Ready, s.Status);
                    Assert.Equal(0, s.RemainingGapProblems);
                    Assert.True(s.DeliveredRmsMm < 0.1, "RMS " + s.DeliveredRmsMm);
                });
                // gantry: arc 2 runs 310 -> 50 (log, Varian) = IEC 230 -> 130, not from the transition
                Assert.Equal(230.0, r.Subbeams[1].GantryStartIec, 1);
                Assert.Equal(345.0, r.Subbeams[1].CollimatorDeg, 1);   // IEC of 195 (180 - 195)
            }
        }

        [Fact]
        public void Matcher_CountsTheSame()
        {
            var infos = TemplateMatcher.Describe(Halcyon());
            Assert.Equal(new[] { 41, 41 }, infos.Select(i => i.ControlPoints));
            Assert.Equal(new[] { 0, 41 }, infos.Select(i => i.FirstLogControlPoint));
        }

        [Fact]
        public void FirstControlPoint_UsesThePlannedMu()
        {
            // counter holds at 40, steps to 41 with planned MU flat, then the field delivers
            var cp = new[] { 40.0, 40.0, 40.5, 41.0, 41.2, 41.4 };
            var muE = new[] { 10.0, 10.0, 10.0, 10.0, 10.3, 10.6 };
            Assert.Equal(41.0, FieldSpan.FirstControlPoint(cp, muE));
            Assert.True(double.IsNaN(FieldSpan.FirstControlPoint(cp, new[] { 1.0, 1, 1, 1, 1, 1 })));
        }
    }
}
