using System;
using System.IO;
using System.Linq;
using TrajectoryLogTools.Core;
using Xunit;

namespace TrajectoryLogTools.Tests
{
    public class AppLogTests
    {
        [Fact]
        public void FileFor_OneFilePerAppDayUserAndComputer()
        {
            string f = Path.GetFileName(AppLog.FileFor("x", "TrajectoryLogConverter", new DateTime(2026, 11, 14)));
            Assert.StartsWith("TrajectoryLogConverter_2026-11-14_", f);
            Assert.EndsWith(".log", f);
            string[] parts = Path.GetFileNameWithoutExtension(f).Split('_');
            Assert.Equal(4, parts.Length);   // app, day, user, computer: '_' in a name is replaced
        }

        [Fact]
        public void Cleanup_DeletesOnlyThisAppsLogsOlderThanTheRetention()
        {
            using (var dir = new TempDir())
            {
                DateTime now = new DateTime(2026, 11, 14, 9, 0, 0);
                Func<string, int, string> file = (name, daysOld) =>
                {
                    string p = dir.Write(name, new byte[] { 1 });
                    File.SetLastWriteTime(p, now.AddDays(-daysOld));
                    return p;
                };
                string old = file("TrajectoryLogConverter_2026-10-01_ann_WS1.log", 44);
                string recent = file("TrajectoryLogConverter_2026-11-01_bob_WS2.log", 13);
                string otherApp = file("TrajectoryLogTools_2026-10-01_ann_WS1.log", 44);
                string notOurs = file("TrajectoryLogConverter_notes.log", 44);   // not <app>_<date>_...
                string other = file("readme.txt", 400);

                Assert.Equal(1, AppLog.Cleanup(dir.Path, "TrajectoryLogConverter", 30, now));
                Assert.False(File.Exists(old));
                Assert.True(File.Exists(recent) && File.Exists(otherApp) && File.Exists(notOurs) && File.Exists(other));
                // a shorter retention reaches the recent one too; a missing folder is no error
                Assert.Equal(1, AppLog.Cleanup(dir.Path, "TrajectoryLogConverter", 7, now));
                Assert.Equal(0, AppLog.Cleanup(dir.File("missing"), "TrajectoryLogConverter", 7, now));
            }
        }

        [Fact]
        public void Start_WritesToTheChosenFolder_InfoAndErrorsAppend()
        {
            using (var dir = new TempDir())
            {
                string folder = dir.Sub("Logs");
                AppLog.Start("TestApp", "1.2.3", folder, 30);
                Assert.Equal(folder, AppLog.Folder);
                Assert.False(AppLog.UsingFallback);
                AppLog.Info("Converted 2 logs", new[] { "OK a.bin", "FAIL b.bin: broken" });
                AppLog.Error("Somewhere", new InvalidOperationException("boom"));
                string text = File.ReadAllText(AppLog.FilePath);
                Assert.Contains("Started TestApp 1.2.3", text);
                Assert.Contains("Converted 2 logs" + Environment.NewLine + "    OK a.bin" + Environment.NewLine + "    FAIL b.bin: broken", text);
                Assert.Contains("ERROR Somewhere", text);
                Assert.Contains("System.InvalidOperationException: boom", text);
                Assert.Equal(folder, Path.GetDirectoryName(AppLog.FilePath));
            }
        }

        [Fact]
        public void Start_FolderThatCantBeWritten_UsesTheUsersOwnFolder()
        {
            using (var dir = new TempDir())
            {
                string blocker = dir.Write("a-file", new byte[] { 1 });   // a "folder" inside a file can't be created
                AppLog.Start("TestAppFallback", "1.0", Path.Combine(blocker, "Logs"), 30);
                try
                {
                    Assert.True(AppLog.UsingFallback);
                    Assert.Equal(AppLog.FallbackFolder, AppLog.Folder);
                    Assert.Contains("can't be written to", AppLog.WhereText);
                }
                finally
                {
                    try { File.Delete(AppLog.FilePath); } catch (Exception) { }
                }
            }
        }

        [Fact]
        public void Resolve_EmptyIsBesideTheApp_RelativeIsUnderIt()
        {
            Assert.Equal(AppLog.DefaultFolder, AppLog.Resolve(""));
            Assert.Equal(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "MyLogs")), AppLog.Resolve("MyLogs"));
            string abs = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "x"));
            Assert.Equal(abs, AppLog.Resolve(abs));
        }
    }
}
