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
