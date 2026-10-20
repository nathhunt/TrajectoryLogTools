using System.IO;
using System.Linq;
using TrajectoryLogTools.Analysis;
using TrajectoryLogTools.Cli;
using TrajectoryLogTools.Core;
using Xunit;

namespace TrajectoryLogTools.Tests
{
    public class MlcGeometryTests
    {
        [Fact]
        public void Sx2_MapsLogLeavesToEclipseOrder()
        {
            var g = MlcGeometry.Sx2;
            Assert.Equal(57, g.PairCount);
            LeafMapping first = g.ToIec(0), x1 = g.ToIec(57), last = g.ToIec(113);
            Assert.Equal(1, first.EclipseIndex);   // log leaf 1 -> X2 of pair 1
            Assert.Equal(1.0, first.Sign);
            Assert.Equal(0, x1.EclipseIndex);      // log leaf 58 -> X1 of pair 1, sign flipped
            Assert.Equal(-1.0, x1.Sign);
            Assert.Equal(112, last.EclipseIndex);
            Assert.Equal("distal", g.LayerOfPair(27));
            Assert.Equal("proximal", g.LayerOfPair(28));
        }

        [Fact]
        public void Detect()
        {
            Assert.Same(MlcGeometry.Sx2, MlcGeometry.FromLeafCount(114));
            Assert.Null(MlcGeometry.FromLeafCount(120));   // e.g. TrueBeam Millennium: not defined yet
            Assert.Same(MlcGeometry.Sx2, MlcGeometry.FromLog(LogBuilder.Parse(LogBuilder.V51().Build())));
            Assert.Contains("not supported yet", MlcGeometry.UnsupportedMessage(120));
        }

        [Fact]
        public void Reconstruction_RefusesAnUnknownMlc()
        {
            var b = LogBuilder.V51(20).SetSamples(50, 122);
            var r = DeliveryRecord.FromLog(LogBuilder.Parse(b.Build()));
            var ex = Assert.Throws<InvalidDataException>(() => MlcReconstructor.Run(r, "x.bin", new MlcReconstructionOptions()));
            Assert.Contains("not supported yet", ex.Message);
        }
    }

    public class DeliveryRecordTests
    {
        [Fact]
        public void FromLog_HasTheDataCsvColumns()
        {
            var log = LogBuilder.Parse(LogBuilder.V51(4).Build());
            var fromLog = DeliveryRecord.FromLog(log);
            using (var dir = new TempDir())
            {
                string csv = dir.File("a.csv");
                File.WriteAllText(csv, CsvExporter.DataCsv(log, new Options { Command = Command.ToCsv }));
                var fromCsv = DeliveryRecord.FromCsv(csv);
                Assert.Equal(fromCsv.ColumnNames.Where(c => c != "ClockTime"), fromLog.ColumnNames);
                Assert.Equal(fromCsv.ServoState, fromLog.ServoState);
                Assert.Equal(114, fromLog.LeafCount);
                foreach (string c in fromLog.ColumnNames)
                    for (int s = 0; s < 4; s++)
                    {
                        double a = fromLog.Column(c)[s], b = fromCsv.Column(c)[s];
                        Assert.True(double.IsNaN(a) ? double.IsNaN(b) : System.Math.Abs(a - b) <= 1e-6 * System.Math.Max(1, System.Math.Abs(a)), c);
                    }
            }
        }

        [Fact]
        public void BeamOn_IsAnythingButHold()
        {
            Assert.True(TrajectoryLog.IsBeamOn(0));
            Assert.True(TrajectoryLog.IsBeamOn(1));   // Freeze: beam still on
            Assert.False(TrajectoryLog.IsBeamOn(2));  // Hold
            Assert.True(TrajectoryLog.IsBeamOn(3));   // Disabled: beam still on
        }

        [Fact]
        public void FromCsv_RejectsAnEclipseExportAndAFullDump()
        {
            using (var dir = new TempDir())
            {
                File.WriteAllText(dir.File("e.csv"), "# VARIAN ARIA Eclipse - MLCPlan serialization\r\n");
                Assert.Contains("Eclipse MLC export", Assert.Throws<InvalidDataException>(() => DeliveryRecord.FromCsv(dir.File("e.csv"))).Message);
                File.WriteAllText(dir.File("f.csv"), "TRAJECTORY LOG FULL DUMP\r\n");
                Assert.Contains("full dump", Assert.Throws<InvalidDataException>(() => DeliveryRecord.FromCsv(dir.File("f.csv"))).Message);
            }
        }
    }

    public class AnalysisCliTests
    {
        static int RunMain(out ConsoleCapture output, params string[] args)
        {
            output = new ConsoleCapture();
            try { return Program.Main(args); }
            finally { output.Dispose(); }
        }

        [Fact]
        public void CompareDelivery_WritesResults()
        {
            using (var dir = new TempDir())
            {
                GoldenCases.WriteInputs(dir.Path);
                string outDir = dir.File("res");
                ConsoleCapture c;
                Assert.Equal(0, RunMain(out c, "compare-delivery", dir.File("imrt_A.bin"), dir.File("imrt_B.csv"), "-o", outDir, "--no-plot"));
                Assert.Contains("=== A - B by axis", c.Out);
                foreach (string f in new[] { "axis_summary.csv", "per_leaf_A_minus_B.csv", "per_beam_A.csv", "beam_holds_B.csv", "report.txt" })
                    Assert.True(File.Exists(Path.Combine(outDir, f)), f);
            }
        }

        [Fact]
        public void CompareDelivery_BadArguments()
        {
            ConsoleCapture c;
            Assert.Equal(2, RunMain(out c, "compare-delivery", "only-one.bin"));
            Assert.Contains("needs two logs", c.Err);
            Assert.Equal(2, RunMain(out c, "compare-delivery", "a.bin", "b.bin", "--bogus"));
            Assert.Equal(1, RunMain(out c, "compare-delivery", "missing-a.bin", "missing-b.bin"));
            Assert.Contains("Input not found", c.Err);
        }

        [Fact]
        public void ReconstructMlc_WritesOneFilePerSubbeam()
        {
            using (var dir = new TempDir())
            {
                GoldenCases.WriteInputs(dir.Path);
                ConsoleCapture c;
                Assert.Equal(0, RunMain(out c, "reconstruct-mlc", dir.File("vmat_A.bin")));
                Assert.True(File.Exists(dir.File("vmat_A_sub1_reconstructed_MLC.csv")));
                Assert.True(File.Exists(dir.File("vmat_A_sub2_reconstructed_MLC_report.txt")));
                Assert.Contains("angle scale: varian", c.Out);
            }
        }

        [Fact]
        public void ReconstructMlc_Errors()
        {
            using (var dir = new TempDir())
            {
                GoldenCases.WriteInputs(dir.Path);
                ConsoleCapture c;
                Assert.Equal(2, RunMain(out c, "reconstruct-mlc", dir.File("vmat_A.bin"), "--axis", "time"));
                Assert.Equal(2, RunMain(out c, "reconstruct-mlc", dir.File("vmat_A.bin"), "-t", GoldenCases.TemplatePath(dir.Path, "vmat", 1)));
                Assert.Contains("only one template", c.Err);
                Assert.Equal(1, RunMain(out c, "reconstruct-mlc", dir.File("vmat_A.bin"), "--subbeam", "1", "-t", dir.File("nope.csv")));
                Assert.Contains("Template not found", c.Err);
                // Wrong template for the field: the subbeam fails, exit 1.
                Assert.Equal(1, RunMain(out c, "reconstruct-mlc", dir.File("imrt_A.bin"), "--subbeam", "1",
                                        "-t", GoldenCases.TemplatePath(dir.Path, "vmat", 1)));
            }
        }

        [Fact]
        public void Dump_WritesText()
        {
            using (var dir = new TempDir())
            {
                string input = dir.Write("a.bin", LogBuilder.V51().Build());
                ConsoleCapture c;
                Assert.Equal(0, RunMain(out c, "dump", input));
                Assert.StartsWith("TRAJECTORY LOG DUMP", File.ReadAllText(dir.File("a.txt")));
                Assert.Equal(1, RunMain(out c, "dump", input));   // exists, no --overwrite
                Assert.Equal(0, RunMain(out c, "dump", input, "--overwrite"));
            }
        }

        [Fact]
        public void Dump_DamagedFileStillDumpsWithWarning()
        {
            using (var dir = new TempDir())
            {
                byte[] b = LogBuilder.V51().Build();
                System.Array.Resize(ref b, b.Length - 100);
                string input = dir.Write("cut.bin", b);
                ConsoleCapture c;
                Assert.Equal(0, RunMain(out c, "dump", input));
                Assert.Contains("WARNING: File length", c.Out);
            }
        }
    }
}
