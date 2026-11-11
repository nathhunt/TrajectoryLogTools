using System.Collections.Generic;
using System.IO;
using System.Linq;
using TrajectoryLogTools.Core;
using Xunit;

namespace TrajectoryLogTools.Tests
{
    /// <summary>The v4.0 converter app's plan and conversion.</summary>
    public class V4BatchTests
    {
        [Fact]
        public void Plan_ConvertsV51_SkipsTheRestWithAReason()
        {
            using (var dir = new TempDir())
            {
                string inDir = dir.Sub("in"), outDir = dir.Sub("out");
                dir.Write("in/A_20261020081500.bin", LogBuilder.V51().Build());
                dir.Write("in/B_20261020091500.bin", LogBuilder.V40().Build());                      // already v4.0
                dir.Write("in/C_20261020101500.bin", LogBuilder.V51().Build());
                dir.Write("out/C_20261020101500.bin", LogBuilder.V40().Build());                     // already converted
                dir.Write("in/sub/A_20261020081500.bin", LogBuilder.V51().Build());                  // same name in a subfolder
                File.WriteAllBytes(Path.Combine(inDir, "D_20261020111500.bin"), new byte[10]);       // not a log
                var found = LogFinder.Find(new FindSettings { FolderMode = true, Input = inDir, IncludeSubfolders = true });
                var plan = V4Batch.Plan(found, outDir, overwrite: false).ToDictionary(i => i.RelativeDir + "/" + i.FileName);

                Assert.Equal(V4BatchState.Convert, plan["/A_20261020081500.bin"].State);
                Assert.Equal(Path.Combine(outDir, "A_20261020081500.bin"), plan["/A_20261020081500.bin"].OutputPath);
                Assert.Equal("PAT001", plan["/A_20261020081500.bin"].PatientId);
                Assert.Equal(V4BatchState.NotV51, plan["/B_20261020091500.bin"].State);
                Assert.Equal(V4BatchState.AlreadyConverted, plan["/C_20261020101500.bin"].State);
                Assert.Equal(V4BatchState.DuplicateName, plan["sub/A_20261020081500.bin"].State);
                Assert.Equal(V4BatchState.Unreadable, plan["/D_20261020111500.bin"].State);
                Assert.All(plan.Values.Where(i => !i.WillConvert), i => Assert.NotEqual("", i.Reason));

                // with overwrite, the converted one is converted again
                var again = V4Batch.Plan(found, outDir, overwrite: true);
                Assert.Equal(V4BatchState.Convert, again.Single(i => i.RelativeDir == "" && i.FileName.StartsWith("C_")).State);
            }
        }

        /// <summary>A kV CBCT log as Halcyon writes it: no MU, "Energy: 0k", beam KVCBCT_Setup.</summary>
        static LogBuilder Cbct(string energy = "0k", string beam = "KVCBCT_Setup", float mu = 0f, string originalMu = "0", string plan = "RA_kV_CBCT")
        {
            var b = LogBuilder.V51();
            b.Subbeams.Clear();
            b.Subbeams.Add(new Subbeam { ControlPoint = 0, MU = mu, RadTime = 0f, Seq = 0, Name = beam });
            b.Metadata = "Patient ID:\tPAT001\r\nPlan Name:\t" + plan + "\r\nOriginal MU:\t" + originalMu + "\r\nRemaining MU:\t0\r\nEnergy:\t" + energy + "\r\nBeamName:\t" + beam + "\r\n";
            return b;
        }

        static int _n;
        static bool IsCbct(TempDir dir, LogBuilder b) { return LogHeader.Read(dir.Write("log" + (++_n) + ".bin", b.Build())).IsKvImaging; }

        [Fact]
        public void KvImaging_NoMuAndKvEnergyOrCbctName()
        {
            using (var dir = new TempDir())
            {
                Assert.True(IsCbct(dir, Cbct()));
                Assert.True(IsCbct(dir, Cbct(energy: "6X")));                       // a CBCT name, no MU
                Assert.True(IsCbct(dir, Cbct(energy: "100 kV", beam: "Setup", plan: "Pelvis")));    // a kV energy, no MU
                // never a treatment log: it has planned MU
                Assert.False(IsCbct(dir, Cbct(mu: 150f, originalMu: "150")));
                Assert.False(IsCbct(dir, Cbct(originalMu: "150")));
                Assert.False(IsCbct(dir, LogBuilder.V51()));
                // no MU but nothing says kV imaging: not assumed
                Assert.False(IsCbct(dir, Cbct(energy: "6xFFF", beam: "Field 1", plan: "Pelvis")));
            }
        }

        [Fact]
        public void Plan_LeavesOutCbctLogs_UnlessAsked_OneFileIsConverted()
        {
            using (var dir = new TempDir())
            {
                string inDir = dir.Sub("in"), outDir = dir.Sub("out");
                dir.Write("in/A_20261020081500.bin", LogBuilder.V51().Build());
                string cbct = dir.Write("in/CBCT_20261020081000.bin", Cbct().Build());
                var found = LogFinder.Find(new FindSettings { FolderMode = true, Input = inDir });

                var plan = V4Batch.Plan(found, outDir, overwrite: false);
                V4BatchItem c = plan.Single(i => i.FileName.StartsWith("CBCT"));
                Assert.Equal(V4BatchState.Imaging, c.State);
                Assert.Equal("kV imaging (CBCT) log", c.Reason);
                Assert.True(c.IsKvImaging);
                Assert.Equal(V4BatchState.Convert, plan.Single(i => i.FileName.StartsWith("A_")).State);
                Assert.False(plan.Single(i => i.FileName.StartsWith("A_")).IsKvImaging);

                Assert.Equal(V4BatchState.Convert, V4Batch.Plan(found, outDir, false, includeImaging: true).Single(i => i.FileName.StartsWith("CBCT")).State);
                V4BatchItem one = V4Batch.PlanOne(cbct, outDir, false);
                Assert.True(one.WillConvert);
                Assert.True(one.IsKvImaging);
            }
        }

        [Fact]
        public void Plan_RefusesTheLogsOwnFolder()
        {
            using (var dir = new TempDir())
            {
                string log = dir.Write("A.bin", LogBuilder.V51().Build());
                Assert.Equal(V4BatchState.SameFolder, V4Batch.PlanOne(log, dir.Path, false).State);
            }
        }

        [Fact]
        public void Convert_WritesOnlyTheV4Log_TrueBeam_SerialRemoved_CouchKept_HeaderUnchanged()
        {
            using (var dir = new TempDir())
            {
                string log = dir.Write("in/A.bin", LogBuilder.V51().Build());
                string outDir = dir.Sub("out");
                V4BatchItem item = V4Batch.PlanOne(log, outDir, false);
                Assert.True(item.WillConvert);
                var lines = new List<string>();
                Assert.Equal(ExportOutcome.Done, V4Batch.Convert(item, false, lines));
                Assert.Equal(new[] { "A.bin" }, Directory.GetFiles(outDir).Select(Path.GetFileName));   // no time-axis file, no run log
                var src = LogBuilder.Parse(File.ReadAllBytes(log));
                var v4 = LogBuilder.Parse(File.ReadAllBytes(item.OutputPath));
                Assert.Equal("4.0", v4.Version);
                Assert.Equal(0, v4.MachineSpecifier);                       // TrueBeam
                Assert.Equal("", v4.MachineSerial);                         // removed
                Assert.Contains(9, v4.AxisIds);                             // couch rotation kept
                Assert.Contains(10, v4.AxisIds);
                Assert.Contains(11, v4.AxisIds);
                Assert.Equal(src.MetaDataText, v4.MetaDataText);            // header details unchanged
                Assert.Equal(src.Subbeams.Select(s => s.Name), v4.Subbeams.Select(s => s.Name));
                // already there: refused without overwrite, replaced with it
                Assert.Equal(V4BatchState.AlreadyConverted, V4Batch.PlanOne(log, outDir, false).State);
                Assert.Equal(ExportOutcome.Done, V4Batch.Convert(V4Batch.PlanOne(log, outDir, true), true, lines));
            }
        }
    }
}
