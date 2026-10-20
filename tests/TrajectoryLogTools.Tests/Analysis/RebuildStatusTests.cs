using System.IO;
using System.Linq;
using TrajectoryLogTools.Analysis;
using TrajectoryLogTools.Core;
using Xunit;

namespace TrajectoryLogTools.Tests
{
    public class RebuildStatusTests
    {
        static MlcReconstructionResult Run(DeliveryRecord d, TempDir dir, MlcReconstructionOptions o = null)
        {
            o = o ?? new MlcReconstructionOptions();
            o.OutputFolder = dir.Sub("out");
            return MlcReconstructor.Run(d, dir.File("log.bin"), o);
        }

        [Theory]
        [InlineData("vmat_A")]
        [InlineData("imrt_B")]
        public void CleanDelivery_IsReady(string name)
        {
            using (var dir = new TempDir())
            {
                var r = Run(DeliveryRecord.FromLog(LogBuilder.Parse(GoldenCases.Bin(name))), dir);
                Assert.All(r.Subbeams, s =>
                {
                    Assert.Equal(RebuildStatus.Ready, s.Status);
                    Assert.Empty(s.Problems);
                    Assert.Equal(0, s.RemainingGapProblems);
                    Assert.True(s.DeliveredMaxMm > 0 && s.DeliveredRmsMm > 0 && s.DeliveredRmsMm < s.DeliveredMaxMm);
                    Assert.StartsWith("CP ", s.DeliveredMaxAt);
                    Assert.True(double.IsNaN(s.MethodCheckMaxMm));
                    // the report's numbers, structured
                    Assert.Equal("actual", s.Positions);
                    Assert.Equal("mu", s.Axis);
                    Assert.Equal("plan", s.GantrySource);
                    Assert.Equal(114, s.Leaves);
                    Assert.True(s.SnapshotsUsed > s.ControlPoints);
                    Assert.True(s.DeliveredMu > 0 && System.Math.Abs(s.DeliveredMu - s.Mu) < 0.05 * s.Mu);
                    Assert.Equal(0.5, s.MinGapMm, 6);
                    Assert.True(double.IsNaN(s.TemplateGantryMaxDeg));
                });
                Assert.StartsWith("evenly spaced arc", r.Subbeams[0].GantryNote.Replace("static gantry", "evenly spaced arc"));
            }
        }

        [Fact]
        public void MissingSamples_AreReported()
        {
            using (var dir = new TempDir())
            {
                var d = DeliveryRecord.FromLog(LogBuilder.Parse(GoldenCases.Bin("vmat_A")));
                double[] cp = d.Column("ControlPoint");
                // take out every snapshot of arc 1 between control points 10 and 12
                var keep = Enumerable.Range(0, d.RowCount).Where(i => !(cp[i] > 9.9 && cp[i] < 12.1)).ToList();
                var r = Run(d.Subset(keep), dir, new MlcReconstructionOptions { Axis = "cp", Subbeams = "1" });
                var s = r.Subbeams.Single();
                Assert.Equal(RebuildStatus.CheckFirst, s.Status);
                Assert.Equal(new[] { 10, 11 }, s.EmptyIntervals);
                Assert.Contains(s.Problems, p => p.Contains("between control points 10-12"));
            }
        }

        [Fact]
        public void WithTemplate_MethodCheckIsAboutZero_AndAWrongTemplateIsFlagged()
        {
            using (var dir = new TempDir())
            {
                GoldenCases.WriteInputs(dir.Path);
                var d = DeliveryRecord.FromLog(LogBuilder.Parse(GoldenCases.Bin("imrt_A")));
                var o = new MlcReconstructionOptions { Subbeams = "1" };
                o.Templates.Add(GoldenCases.TemplatePath(dir.Path, "imrt", 1));
                var good = Run(d, dir, o).Subbeams.Single();
                Assert.Equal(RebuildStatus.Ready, good.Status);
                Assert.True(good.MethodCheckMaxMm < MlcReconstructor.MethodCheckLimitMm);

                // field 2's export has the same number of control points but is another beam
                o = new MlcReconstructionOptions { Subbeams = "1" };
                o.Templates.Add(GoldenCases.TemplatePath(dir.Path, "imrt", 2));
                var wrong = Run(d, dir, o).Subbeams.Single();
                Assert.Equal(RebuildStatus.CheckFirst, wrong.Status);
                Assert.Contains(wrong.Problems, p => p.Contains("does not look like this beam"));
                Assert.Contains(wrong.Problems, p => p.Contains("Method check"));
            }
        }

        [Fact]
        public void Failure_IsFailed()
        {
            using (var dir = new TempDir())
            {
                GoldenCases.WriteInputs(dir.Path);
                var o = new MlcReconstructionOptions { Subbeams = "1" };
                o.Templates.Add(GoldenCases.TemplatePath(dir.Path, "vmat", 1));   // 41 CPs for a 15-CP field
                var s = Run(DeliveryRecord.FromLog(LogBuilder.Parse(GoldenCases.Bin("imrt_A"))), dir, o).Subbeams.Single();
                Assert.Equal(RebuildStatus.Failed, s.Status);
                Assert.False(s.Ok);
            }
        }
    }

    public class LogHeaderTests
    {
        [Fact]
        public void ReadsHeaderAndSubbeamsOnly()
        {
            using (var dir = new TempDir())
            {
                var b = LogBuilder.V51(20);
                b.Metadata = "Patient ID:\tX\r\nPlan Name:\tVMAT PlanQA\r\n";
                byte[] bytes = b.Build();
                System.Array.Resize(ref bytes, 1024 + 2 * 560 + 50);   // snapshots cut off: still readable
                string p = dir.Write("Field_20261016142318.bin", bytes);
                var h = LogHeader.Read(p);
                Assert.Equal("5.1", h.Version);
                Assert.Equal(20, h.NumSnapshots);
                Assert.Equal("VMAT PlanQA", h.PlanName);
                Assert.Equal("X", h.PatientId);
                Assert.Null(h.PlanUid);
                Assert.Equal("Arc 1 + Arc 2", h.FieldsText);
                Assert.True(h.DateFromName);
                Assert.Equal(new System.DateTime(2026, 10, 16, 14, 23, 18), h.Date);
                Assert.Equal("H12345", h.MachineSerial);
            }
        }

        [Fact]
        public void RejectsANonLog()
        {
            using (var dir = new TempDir())
            {
                string p = dir.Write("x.bin", new byte[2000]);
                Assert.Throws<InvalidDataException>(() => LogHeader.Read(p));
            }
        }
    }
}
