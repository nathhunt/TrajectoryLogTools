using System.IO;
using System.Linq;
using TrajectoryLogTools.Analysis;
using Xunit;

namespace TrajectoryLogTools.Tests
{
    public class TemplateMatcherTests
    {
        [Theory]
        [InlineData("vmat_A")]
        [InlineData("vmat_B")]
        [InlineData("imrt_A")]
        [InlineData("imrt_B")]
        public void Describe_CountsControlPointsAsTheReconstructionDoes(string name)
        {
            using (var dir = new TempDir())
            {
                GoldenCases.WriteInputs(dir.Path);
                string log = dir.File(name + ".bin");
                var record = DeliveryRecord.Load(log);
                var infos = TemplateMatcher.Describe(record);
                var result = MlcReconstructor.Run(record, log, new MlcReconstructionOptions { OutputFolder = dir.Sub("out") });
                Assert.Equal(result.Subbeams.Select(s => s.ControlPoints), infos.Select(i => i.ControlPoints));
                Assert.Equal(result.Subbeams.Select(s => s.FirstLogControlPoint), infos.Select(i => i.FirstLogControlPoint));
                Assert.True(File.Exists(Path.Combine(dir.Path, "out", name + "_sub1_reconstructed_MLC.csv")));
            }
        }

        [Fact]
        public void Match_ByControlPointsAndGantry()
        {
            using (var dir = new TempDir())
            {
                GoldenCases.WriteInputs(dir.Path);
                var infos = TemplateMatcher.Describe(DeliveryRecord.Load(dir.File("imrt_A.bin")));
                Assert.Equal(new[] { 0.0, 120.0, 240.0 }, infos.Select(i => System.Math.Round(i.GantryIec, 1)));
                // Given in the wrong order, plus a VMAT template that fits no field.
                var templates = new[] { 3, 1, 2 }.Select(f => TemplateMatcher.Read(GoldenCases.TemplatePath(dir.Path, "imrt", f)))
                    .Concat(new[] { TemplateMatcher.Read(GoldenCases.TemplatePath(dir.Path, "vmat", 1)) }).ToList();
                var m = TemplateMatcher.Match(infos, templates);
                Assert.Equal(3, m.Count);
                for (int f = 1; f <= 3; f++) Assert.Equal(GoldenCases.TemplatePath(dir.Path, "imrt", f), m[f].Path);
            }
        }

        [Fact]
        public void Read_ReportsABadFile()
        {
            using (var dir = new TempDir())
            {
                File.WriteAllText(dir.File("x.csv"), "# nothing\r\n");
                Assert.NotNull(TemplateMatcher.Read(dir.File("x.csv")).Error);
            }
        }
    }
}
