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
                try
                {
                    Assert.Equal(folder, AppLog.Folder);
                    Assert.False(AppLog.UsingFallback);
                    AppLog.Info("Converted 2 logs", new[] { "OK a.bin", "FAIL b.bin: broken" });
                    AppLog.Error("Somewhere", new InvalidOperationException("boom"));
                    string text = File.ReadAllText(AppLog.FilePath);
                    Assert.Contains("INFO   Started TestApp 1.2.3", text);
                    Assert.Contains("INFO   Converted 2 logs" + Environment.NewLine + "        OK a.bin" + Environment.NewLine + "        FAIL b.bin: broken", text);
                    Assert.Contains("ERROR  Somewhere", text);
                    Assert.Contains("System.InvalidOperationException: boom", text);
                    Assert.Matches(@"\d\d/\d\d/\d{4} \d\d:\d\d:\d\d\.\d{3}  ERROR", text);   // NZ date, milliseconds
                    Assert.Equal(folder, Path.GetDirectoryName(AppLog.FilePath));
                }
                finally { AppLog.Stop(); }
            }
        }

        [Fact]
        public void Error_SaysWhatWasBeingDone_TheContext_TheWholeException_AndChecksThePaths()
        {
            using (var dir = new TempDir())
            {
                AppLog.Start("TestApp", "1", dir.Sub("Logs"), 30);
                try
                {
                    string source = dir.Write(Path.Combine("in", "A.bin"), new byte[] { 1, 2, 3 });
                    string outDir = dir.Sub("out");
                    string blocker = dir.Write("blocker", new byte[] { 1 });
                    using (AppLog.Scope("Convert to v4.0", "Source", source))
                    using (AppLog.Scope("Write the v4.0 file", "Output folder", outDir))
                    {
                        // a write that fails the way a real one does: the folder can't be created
                        var ex = Assert.Throws<IOException>(() => FileUtil.WriteVerified(Path.Combine(blocker, "A.bin"), new byte[] { 1 }, false));
                        Assert.Contains("Could not write " + Path.Combine(blocker, "A.bin"), ex.Message);
                        Assert.Contains("while trying to create the folder " + blocker, ex.Message);
                        AppLog.Error("Conversion of A.bin failed", ex, "Target", Path.Combine(blocker, "A.bin"));
                    }
                    string text = File.ReadAllText(AppLog.FilePath);
                    Assert.Contains("ERROR  Conversion of A.bin failed", text);
                    Assert.Contains("While: Convert to v4.0 › Write the v4.0 file", text);
                    Assert.Contains("Source: " + source, text);
                    Assert.Contains("Output folder: " + outDir, text);
                    Assert.Contains("Exception:", text);
                    Assert.Contains("System.IO.IOException: Could not write", text);
                    Assert.Contains("---> ", text);                                  // the inner exception, in full
                    Assert.Contains("at TrajectoryLogTools.Core.FileUtil.WriteVerified", text);   // stack trace
                    Assert.Contains("Path checks, as ", text);
                    // the path checks show full, normalised paths
                    Assert.Contains(Path.GetFullPath(source) + ": file, 3 bytes", text);
                    Assert.Contains(Path.GetFullPath(outDir) + ": folder exists, this user CAN create files in it", text);
                    // no test files left behind by the checks
                    Assert.Empty(Directory.GetFiles(outDir));
                }
                finally { AppLog.Stop(); }
            }
        }

        [Fact]
        public void EntriesBeforeStart_AreWrittenWhenItStarts_NothingWhenStopped()
        {
            using (var dir = new TempDir())
            {
                AppLog.Stop();
                AppLog.Warn("Settings file unreadable", new FormatException("bad json"), "Settings file", "C:\\x\\s.json");
                AppLog.Start("TestApp", "1", dir.Sub("Logs"), 30);
                string file = AppLog.FilePath;
                AppLog.Stop();
                AppLog.Info("dropped");
                string text = File.ReadAllText(file);
                Assert.Contains("WARN   Settings file unreadable", text);
                Assert.Contains("System.FormatException: bad json", text);
                Assert.DoesNotContain("dropped", text);
            }
        }

        [Fact]
        public void WindowsError_InWords()
        {
            Assert.Equal("5 (0x80070005): access denied: this user isn't allowed to do this to the file or folder (permissions), or the file is read-only",
                         AppLog.WindowsError(new IOException("outer", new UnauthorizedAccessException("Access to the path is denied."))));
            Assert.StartsWith("32 (0x80070020): sharing violation", AppLog.WindowsError(new IOException("busy", unchecked((int)0x80070020))));
            Assert.Null(AppLog.WindowsError(new InvalidOperationException("x")));
        }

        [Fact]
        public void Start_FolderThatCantBeWritten_UsesTheUsersOwnFolder()
        {
            using (var dir = new TempDir())
            {
                string blocker = dir.Write("a-file", new byte[] { 1 });   // a "folder" inside a file can't be created
                AppLog.Start("TestAppFallback", "1.0", Path.Combine(blocker, "Logs"), 30);
                AppLog.Stop();
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

        [Fact]
        public void TryAppend_WaitingForABusyFile_DoesNotHoldUpOtherEntries()
        {
            using (var dir = new TempDir())
            {
                string busy = dir.File("busy.log"), other = dir.File("other.log");
                string error;
                using (new FileStream(busy, FileMode.Create, FileAccess.Write, FileShare.None))   // another program has it
                {
                    var waiting = System.Threading.Tasks.Task.Run(() => AppLog.TryAppend(busy, "a", out error));   // retries for about a second
                    System.Threading.Thread.Sleep(100);
                    Assert.True(AppLog.TryAppend(other, "b", out error));
                    // Written while the first is still retrying: its wait doesn't block everyone else.
                    Assert.False(waiting.IsCompleted, "the other entry waited for the busy file's retries");
                    Assert.False(waiting.Result);   // the busy file never became free
                }
                Assert.Equal("b", File.ReadAllText(other));
            }
        }
    }
}
