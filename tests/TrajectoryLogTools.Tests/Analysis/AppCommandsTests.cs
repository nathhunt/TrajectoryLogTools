using System.IO;
using System.Linq;
using TrajectoryLogTools.Cli;
using TrajectoryLogTools.Core;
using Xunit;

namespace TrajectoryLogTools.Tests
{
    /// <summary>The CLI commands that do what the app does (export, rebuild-mlc, compare-*).</summary>
    public class AppCommandsTests
    {
        static int RunMain(out ConsoleCapture output, params string[] args)
        {
            output = new ConsoleCapture();
            try { return Program.Main(args); }
            finally { output.Dispose(); }
        }

        [Fact]
        public void Export_DefaultsAsTheApp()
        {
            using (var dir = new TempDir())
            {
                string input = dir.Write("a.bin", LogBuilder.V51().Build());
                ConsoleCapture c;
                Assert.Equal(0, RunMain(out c, "export", input));
                var v4 = LogBuilder.Parse(File.ReadAllBytes(Path.Combine(dir.Path, "converted", "a.bin")));
                Assert.Equal("4.0", v4.Version);
                Assert.Equal(0, v4.MachineSpecifier);                                  // TrueBeam
                Assert.Equal("", v4.MachineSerial);                                    // removed
                Assert.DoesNotContain(9, v4.AxisIds);                                  // couch rotation removed
                Assert.False(File.Exists(Path.Combine(dir.Path, "converted", "a.csv")));   // v4.0 only
                Assert.Contains("1 done, 0 skipped, 0 failed.", c.Out);
            }
        }

        [Fact]
        public void Export_OptionsAndOneFolder()
        {
            using (var dir = new TempDir())
            {
                string input = dir.Write("a.bin", LogBuilder.V51().Build());
                string outDir = dir.File("out");
                ConsoleCapture c;
                Assert.Equal(0, RunMain(out c, "export", input, "-o", outDir, "--v4", "--csv", "--machine", "halcyon", "--serial", "keep", "--couch", "keep", "--run-log"));
                var v4 = LogBuilder.Parse(File.ReadAllBytes(Path.Combine(outDir, "a.bin")));
                Assert.Equal(1, v4.MachineSpecifier);
                Assert.Equal("H12345", v4.MachineSerial);
                Assert.Contains(9, v4.AxisIds);
                Assert.True(File.Exists(Path.Combine(outDir, "a.csv")));
                Assert.Single(Directory.GetFiles(outDir, "TrajectoryLogConversion_*.log"));
                // no run log unless asked
                string other = dir.File("out2");
                Assert.Equal(0, RunMain(out c, "export", input, "-o", other));
                Assert.Empty(Directory.GetFiles(other, "*.log"));
                // the log's own folder is refused for v4.0
                Assert.Equal(2, RunMain(out c, "export", input, "-o", dir.Path));
                Assert.Equal(2, RunMain(out c, "export", input, "--serial", "maybe"));
            }
        }

        [Fact]
        public void Export_HeaderDetails_OneLog_BeamNameFollowsField1()
        {
            using (var dir = new TempDir())
            {
                var b = LogBuilder.V51();
                b.Metadata = "Patient ID:\tPAT001\r\nPlan Name:\tPlan1\r\nBeamName:\tArc 1\r\n";
                string input = dir.Write("a.bin", b.Build());
                ConsoleCapture c;
                Assert.Equal(0, RunMain(out c, "export", input, "--patient-id", "QA001", "--plan-uid", "1.2.3", "--field-name", "1=Arc A", "--new-serial", "QA1"));
                var v4 = LogBuilder.Parse(File.ReadAllBytes(Path.Combine(dir.Path, "converted", "a.bin")));
                Assert.Equal("Patient ID:\tQA001\r\nPlan Name:\tPlan1\r\nBeamName:\tArc A\r\nPlan UID:\t1.2.3\r\n", v4.MetaDataText);
                Assert.Equal("Arc A", v4.Subbeams[0].Name);
                Assert.Equal("QA1", v4.MachineSerial);
                string other = dir.Write("b.bin", LogBuilder.V51().Build());
                Assert.Equal(2, RunMain(out c, "export", input, other, "--patient-id", "X"));
                Assert.Contains("one log at a time", c.Err);
            }
        }

        [Fact]
        public void BeamName_NotChangedWhenSetExplicitly_OrWithoutField1()
        {
            var b = LogBuilder.V51();
            b.Metadata = "BeamName:\tArc 1\r\n";
            var log = LogBuilder.Parse(b.Build());
            var opt = new Options();
            opt.SubbeamNames[2] = "Arc B";
            Assert.Equal("BeamName:\tArc 1\r\n", LogBuilder.Parse(V4Converter.Convert(log, opt, new System.Collections.Generic.List<string>())).MetaDataText);
            opt.SubbeamNames[1] = "Arc A";
            opt.MetaEdits.Add(new System.Collections.Generic.KeyValuePair<string, string>("BeamName", "Own"));
            Assert.Equal("BeamName:\tOwn\r\n", LogBuilder.Parse(V4Converter.Convert(log, opt, new System.Collections.Generic.List<string>())).MetaDataText);
        }

        [Fact]
        public void RebuildMlc_SeveralLogs_OneFolder()
        {
            using (var dir = new TempDir())
            {
                GoldenCases.WriteInputs(dir.Path);
                string outDir = dir.File("mlc");
                ConsoleCapture c;
                Assert.Equal(0, RunMain(out c, "rebuild-mlc", dir.File("vmat_A.bin"), dir.File("imrt_B.bin"), "-o", outDir));
                Assert.True(File.Exists(Path.Combine(outDir, "vmat_A_sub1_reconstructed_MLC.csv")));
                Assert.True(File.Exists(Path.Combine(outDir, "imrt_B_sub3_reconstructed_MLC.csv")));
                Assert.Contains("Ready to import", c.Out);
                Assert.Contains(" 0 failed.", c.Out);
            }
        }

        [Fact]
        public void RebuildMlc_ExportsMatchedAcrossLogs_SameFilesAsReconstruct()
        {
            using (var dir = new TempDir())
            {
                GoldenCases.WriteInputs(dir.Path);
                var exports = Enumerable.Range(1, 3).Select(f => GoldenCases.TemplatePath(dir.Path, "imrt", f)).Reverse().ToList();
                ConsoleCapture c;
                var args = new[] { "rebuild-mlc", dir.File("imrt_A.bin"), "-o", dir.File("new"), "--positions", "actual", "--axis", "mu" }.Concat(exports.SelectMany(e => new[] { "-e", e })).ToArray();
                Assert.Equal(0, RunMain(out c, args));
                // the same as reconstruct-mlc with the exports given in field order
                var old = new[] { "reconstruct-mlc", dir.File("imrt_A.bin") }.Concat(Enumerable.Range(1, 3).SelectMany(f => new[] { "-t", GoldenCases.TemplatePath(dir.Path, "imrt", f) })).ToArray();
                Assert.Equal(0, RunMain(out c, old));
                for (int f = 1; f <= 3; f++)
                    Assert.Equal(File.ReadAllText(dir.File("imrt_A_sub" + f + "_reconstructed_MLC.csv")),
                                 File.ReadAllText(Path.Combine(dir.File("new"), "imrt_A_sub" + f + "_reconstructed_MLC.csv")));
            }
        }

        [Fact]
        public void CompareAliases()
        {
            using (var dir = new TempDir())
            {
                GoldenCases.WriteInputs(dir.Path);
                ConsoleCapture c;
                Assert.Equal(0, RunMain(out c, "compare-files", dir.File("vmat_A.bin"), dir.File("vmat_A.bin")));
                Assert.StartsWith("SAME", c.Out);
                Assert.Equal(0, RunMain(out c, "compare-deliveries", dir.File("vmat_A.bin"), dir.File("vmat_B.bin"), "-o", dir.File("cmp"), "--no-plot"));
                Assert.True(File.Exists(Path.Combine(dir.File("cmp"), "report.txt")));
            }
        }

        [Fact]
        public void RebuildMlc_AppDefaults_PlannedPositionsByControlPoint_NoReports()
        {
            using (var dir = new TempDir())
            {
                GoldenCases.WriteInputs(dir.Path);
                ConsoleCapture c;
                Assert.Equal(0, RunMain(out c, "rebuild-mlc", dir.File("vmat_A.bin"), "-o", dir.File("a")));
                Assert.Empty(Directory.GetFiles(dir.File("a"), "*_report.txt"));
                // the same as reconstruct-mlc with expected positions and the control-point axis
                Assert.Equal(0, RunMain(out c, "reconstruct-mlc", dir.File("vmat_A.bin"), "--positions", "expected", "--axis", "cp"));
                foreach (int f in new[] { 1, 2 })
                    Assert.Equal(Body(dir.File("vmat_A_sub" + f + "_reconstructed_MLC.csv")), Body(Path.Combine(dir.File("a"), "vmat_A_sub" + f + "_reconstructed_MLC.csv")));
                Assert.Equal(0, RunMain(out c, "rebuild-mlc", dir.File("vmat_A.bin"), "-o", dir.File("b"), "--report"));
                Assert.Equal(2, Directory.GetFiles(dir.File("b"), "*_report.txt").Length);
            }
        }

        /// <summary>The MLC file without its "# Exported ..." time line.</summary>
        static string Body(string path) { return string.Join("\n", File.ReadAllLines(path).Where(l => !l.StartsWith("# Exported"))); }
    }
}
