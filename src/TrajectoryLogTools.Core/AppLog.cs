// AppLog.cs - the apps' log: what each user did (logs opened, conversions, exports, rebuilds,
// comparisons, reports saved, settings changed) and every error with its details.
//
// Where: a Logs folder beside the app (or the folder set in Settings), so all logs of a deployed
// copy are in one place. Each user on each computer writes a file of their own per day,
//     <app>_<yyyy-MM-dd>_<user>_<computer>.log
// so users never write to the same file. Within one file (the same user with the app open twice)
// a write that finds the file busy is retried for about a second.
// If the folder can't be written to (e.g. read-only for users), the log goes to the user's own
// %LocalAppData%\TrajectoryLogTools\Logs and Folder says where it went.
//
// Old logs: when the app starts, logs of this app last written more than the retention period
// ago (Settings; 30 days by default) are deleted, in the background. Only files named as above
// are touched. Two users cleaning at once, a file still open, or one this user may not delete:
// skipped quietly (another start will get it). Logging never throws.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TrajectoryLogTools.Core
{
    public static class AppLog
    {
        public const int DefaultRetentionDays = 30;

        /// <summary>How long logs can be kept, for Settings: days and label.</summary>
        public static readonly IReadOnlyList<KeyValuePair<int, string>> RetentionChoices = new[]
        {
            new KeyValuePair<int, string>(7, "1 week"),
            new KeyValuePair<int, string>(14, "2 weeks"),
            new KeyValuePair<int, string>(30, "1 month"),
            new KeyValuePair<int, string>(91, "3 months"),
            new KeyValuePair<int, string>(182, "6 months"),
            new KeyValuePair<int, string>(365, "1 year"),
        };

        /// <summary>Where the log is now, for Settings: the folder, and why it's the user's own folder if it is.</summary>
        public static string WhereText { get { return UsingFallback ? FallbackReason : "This user's log is in " + Folder + "."; } }
        static readonly object Gate = new object();
        static string _app = "TrajectoryLogTools";

        /// <summary>The folder the log is written to (after Start).</summary>
        public static string Folder { get; private set; }
        /// <summary>True when the chosen folder couldn't be written to and the user's own folder is used.</summary>
        public static bool UsingFallback { get; private set; }
        /// <summary>Why the chosen folder couldn't be used ("" when it is).</summary>
        public static string FallbackReason { get; private set; } = "";

        /// <summary>A Logs folder beside the app.</summary>
        public static string DefaultFolder { get { return Path.Combine(AppContext.BaseDirectory, "Logs"); } }

        /// <summary>The user's own folder, used when the chosen one can't be written to.</summary>
        public static string FallbackFolder
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrajectoryLogTools", "Logs"); }
        }

        /// <summary>The folder a Settings value means: empty = beside the app; a relative path is relative to the app.</summary>
        public static string Resolve(string setting)
        {
            string s = (setting ?? "").Trim().Trim('"');
            if (s.Length == 0) return DefaultFolder;
            return Path.IsPathRooted(s) ? s : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, s));
        }

        /// <summary>This user's file for today in <paramref name="folder"/>.</summary>
        public static string FileFor(string folder, string app, DateTime day)
        {
            return Path.Combine(folder, app + "_" + day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) + "_" +
                                        Safe(Environment.UserName) + "_" + Safe(Environment.MachineName) + ".log");
        }

        /// <summary>Today's file of this user (after Start).</summary>
        public static string FilePath { get { return FileFor(Folder ?? FallbackFolder, _app, DateTime.Now); } }

        static string Safe(string s)
        {
            s = string.IsNullOrWhiteSpace(s) ? "unknown" : s.Trim();
            var bad = Path.GetInvalidFileNameChars();
            return new string(s.Select(c => bad.Contains(c) || c == '_' || char.IsWhiteSpace(c) ? '-' : c).ToArray());
        }

        /// <summary>
        /// Starts logging for <paramref name="app"/> (e.g. "TrajectoryLogConverter"): picks the folder,
        /// writes the start line (version, user, computer, Windows, .NET) and, in the background,
        /// deletes this app's logs older than <paramref name="retentionDays"/>.
        /// </summary>
        public static void Start(string app, string version, string folderSetting, int retentionDays)
        {
            _app = string.IsNullOrWhiteSpace(app) ? "TrajectoryLogTools" : app;
            Open(folderSetting, retentionDays, "Started " + _app + " " + version + " · user " + Environment.UserName + " on " + Environment.MachineName +
                 " · " + System.Runtime.InteropServices.RuntimeInformation.OSDescription + " · " +
                 System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + " · " + AppContext.BaseDirectory);
        }

        /// <summary>The log settings were changed (Settings > Save): from now on, log there and keep logs this long.</summary>
        public static void Reconfigure(string folderSetting, int retentionDays)
        {
            string old = Folder;
            Open(folderSetting, retentionDays, "Log settings: folder " + Resolve(folderSetting) + ", logs kept " + Math.Max(1, retentionDays) + " days");
            if (old != null && !string.Equals(old, Folder, StringComparison.OrdinalIgnoreCase)) Info("(the log was in " + old + " until now)");
        }

        static void Open(string folderSetting, int retentionDays, string start)
        {
            string chosen = Resolve(folderSetting);
            string error;
            Folder = chosen;
            UsingFallback = false;
            FallbackReason = "";
            if (!TryAppend(FileFor(chosen, _app, DateTime.Now), Entry(start), out error))
            {
                Folder = FallbackFolder;
                UsingFallback = true;
                FallbackReason = "The log folder " + chosen + " can't be written to (" + error + "), so this user's log is in " + Folder + ".";
                TryAppend(FilePath, Entry(start + Environment.NewLine + FallbackReason), out error);
            }
            int days = Math.Max(1, retentionDays);
            string folder = Folder, fallback = FallbackFolder, name = _app;
            Task.Run(() =>
            {
                Cleanup(folder, name, days, DateTime.Now);
                if (!string.Equals(folder, fallback, StringComparison.OrdinalIgnoreCase)) Cleanup(fallback, name, days, DateTime.Now);
            });
        }

        /// <summary>Something done, e.g. "Converted 12 logs to X"; continuation lines are indented.</summary>
        public static void Info(string text)
        {
            string error;
            TryAppend(FilePath, Entry(text), out error);
        }

        /// <summary>A heading line, then the lines under it (e.g. what an export wrote, log by log).</summary>
        public static void Info(string heading, IEnumerable<string> lines)
        {
            Info(heading + string.Concat((lines ?? Enumerable.Empty<string>()).Select(l => Environment.NewLine + l)));
        }

        /// <summary>An error with its full details (message, type, stack trace).</summary>
        public static void Error(string where, Exception ex)
        {
            Info("ERROR " + where + Environment.NewLine + (ex == null ? "(no details)" : ex.ToString()));
        }

        static string Entry(string text)
        {
            string[] lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            var sb = new StringBuilder();
            sb.Append(Fmt.DateTimeSeconds(DateTime.Now)).Append("  ").Append(lines[0]).Append(Environment.NewLine);
            for (int i = 1; i < lines.Length; i++) sb.Append("    ").Append(lines[i]).Append(Environment.NewLine);
            return sb.ToString();
        }

        /// <summary>Appends; retries for about a second while the file is busy. Never throws.</summary>
        internal static bool TryAppend(string file, string text, out string error)
        {
            error = null;
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            lock (Gate)
            {
                for (int attempt = 0; ; attempt++)
                {
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(file));
                        using (var f = new FileStream(file, FileMode.Append, FileAccess.Write, FileShare.Read))
                            f.Write(bytes, 0, bytes.Length);
                        return true;
                    }
                    catch (IOException ex) when (attempt < 5 && !(ex is DirectoryNotFoundException) && !(ex is PathTooLongException))
                    {
                        Thread.Sleep(200);   // busy (the same user's other copy of the app): try again
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                        return false;
                    }
                }
            }
        }

        /// <summary>
        /// Deletes <paramref name="app"/>'s logs in <paramref name="folder"/> last written more than
        /// <paramref name="days"/> days before <paramref name="now"/>; returns how many. Files that
        /// can't be deleted (open, not allowed, already gone) are skipped.
        /// </summary>
        public static int Cleanup(string folder, string app, int days, DateTime now)
        {
            int deleted = 0;
            try
            {
                if (!Directory.Exists(folder)) return 0;
                DateTime cutoff = now.AddDays(-Math.Max(1, days));
                foreach (string f in Directory.EnumerateFiles(folder, app + "_*.log"))
                {
                    try
                    {
                        // only this app's own files: <app>_<yyyy-MM-dd>_...
                        string rest = Path.GetFileName(f).Substring(app.Length + 1);
                        DateTime day;
                        if (rest.Length < 11 || rest[10] != '_' ||
                            !DateTime.TryParseExact(rest.Substring(0, 10), "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture,
                                                    System.Globalization.DateTimeStyles.None, out day)) continue;
                        if (File.GetLastWriteTime(f) >= cutoff) continue;
                        File.Delete(f);
                        deleted++;
                    }
                    catch (Exception) { }   // open, not ours to delete, or deleted by someone else just now
                }
            }
            catch (Exception) { }
            return deleted;
        }
    }
}
