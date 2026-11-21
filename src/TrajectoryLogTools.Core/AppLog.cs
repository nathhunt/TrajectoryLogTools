// AppLog.cs - the apps' log: what each user did (logs opened, conversions, exports, rebuilds,
// comparisons, reports saved, settings changed) and every error with its details.
//
// Entries: "dd/MM/yyyy HH:mm:ss.fff  LEVEL  message" (INFO, WARN, ERROR), then indented lines.
// A WARN or ERROR says what was being done (the Scope(s) it happened in), the context (source,
// output folder, ...), the exception in full (type, message, stack trace, every inner exception),
// the Windows error code in words and, for a file or folder problem, a check of each path
// involved: does it exist, is it read-only, can this user create files in its folder.
// Entries written before Start (e.g. while reading the settings) are kept and written once the
// log has started; without Start (the command-line tool, tests) nothing is written.
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
            Open(folderSetting, retentionDays, "Started " + _app + " " + version + " · user " + Environment.UserDomainName + "\\" + Environment.UserName + " on " + Environment.MachineName +
                 " · process " + System.Diagnostics.Process.GetCurrentProcess().Id + " · " + System.Runtime.InteropServices.RuntimeInformation.OSDescription + " · " +
                 System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + " · " + AppContext.BaseDirectory);
        }

        /// <summary>Stops writing (entries are dropped until the next Start). For tests.</summary>
        public static void Stop()
        {
            lock (Pending) { _started = false; Pending.Clear(); }
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
                TryAppend(FilePath, Entry(Level.Warn, start + Environment.NewLine + FallbackReason), out error);
            }
            List<string> pending;
            lock (Pending)
            {
                _started = true;
                pending = Pending.ToList();
                Pending.Clear();
            }
            if (pending.Count > 0) TryAppend(FilePath, string.Concat(pending), out error);
            int days = Math.Max(1, retentionDays);
            string folder = Folder, fallback = FallbackFolder, name = _app;
            Task.Run(() =>
            {
                Cleanup(folder, name, days, DateTime.Now);
                if (!string.Equals(folder, fallback, StringComparison.OrdinalIgnoreCase)) Cleanup(fallback, name, days, DateTime.Now);
            });
        }

        // =========================================================================================
        // Writing entries
        // =========================================================================================

        public enum Level { Info, Warn, Error }

        static bool _started;
        static readonly List<string> Pending = new List<string>();   // entries before Start (e.g. reading settings)
        const int MaxPending = 200;

        /// <summary>Something done, e.g. "Converted 12 logs to X"; continuation lines are indented.</summary>
        public static void Info(string text) { Write(Level.Info, text, null, null); }

        /// <summary>A heading line, then the lines under it (e.g. what an export wrote, log by log).</summary>
        public static void Info(string heading, IEnumerable<string> lines)
        {
            Write(Level.Info, heading + string.Concat((lines ?? Enumerable.Empty<string>()).Select(l => Environment.NewLine + l)), null, null);
        }

        /// <summary>Something done, with context as name / value pairs ("Source", path, "Output folder", folder, ...).</summary>
        public static void Activity(string message, params object[] context) { Write(Level.Info, message, null, context); }

        /// <summary>Something that went wrong but was dealt with (a log left out, a file skipped), with the exception if there is one.</summary>
        public static void Warn(string message, Exception ex = null, params object[] context) { Write(Level.Warn, message, ex, context); }

        /// <summary>
        /// An error: what failed, the operation it happened in and its context (see Scope), the
        /// exception in full (every inner exception, with stack traces), the Windows error code, and
        /// for a file or folder problem, checks of the paths involved (do they exist, read-only, can
        /// this user create files in the folder).
        /// </summary>
        public static void Error(string message, Exception ex, params object[] context) { Write(Level.Error, message, ex, context); }

        // ---- scopes: the operation an entry belongs to, and its context ----

        sealed class ScopeFrame : IDisposable
        {
            public readonly ScopeFrame Parent;
            public readonly string Operation;
            public readonly object[] Context;
            public ScopeFrame(ScopeFrame parent, string operation, object[] context) { Parent = parent; Operation = operation; Context = context ?? new object[0]; }
            public void Dispose() { if (Current.Value == this) Current.Value = Parent; }
        }

        static readonly AsyncLocal<ScopeFrame> Current = new AsyncLocal<ScopeFrame>();

        /// <summary>
        /// Marks an operation and its context ("Convert to v4.0", "Source", path, ...) until disposed:
        /// every entry written inside it (also from a Task.Run started inside it) says what was being
        /// done and lists the context. Scopes nest: "Convert to v4.0 › Write the v4.0 file".
        /// </summary>
        public static IDisposable Scope(string operation, params object[] context)
        {
            var f = new ScopeFrame(Current.Value, operation, context);
            Current.Value = f;
            return f;
        }

        static void Write(Level level, string message, Exception ex, object[] context)
        {
            string text;
            try { text = Compose(level, message, ex, context); }
            catch (Exception composeError) { text = Entry(level, (message ?? "") + Environment.NewLine + (ex == null ? "" : ex.ToString()) + Environment.NewLine + "(log detail failed: " + composeError.Message + ")"); }
            lock (Pending)
            {
                if (!_started)
                {
                    if (Pending.Count < MaxPending) Pending.Add(text);
                    return;
                }
            }
            string error;
            TryAppend(FilePath, text, out error);
        }

        static string Compose(Level level, string message, Exception ex, object[] context)
        {
            var lines = new List<string>();
            string[] msg = (message ?? "").Replace("\r\n", "\n").Split('\n');
            lines.AddRange(msg.Skip(1));

            // the operation and its context, outermost first; a name given again inside replaces the outer value
            var frames = new List<ScopeFrame>();
            for (ScopeFrame f = Current.Value; f != null; f = f.Parent) frames.Insert(0, f);
            var pairs = new List<KeyValuePair<string, object>>();
            Action<object[]> add = c =>
            {
                for (int i = 0; i + 1 < c.Length; i += 2)
                {
                    string key = Convert.ToString(c[i], System.Globalization.CultureInfo.InvariantCulture);
                    pairs.RemoveAll(p => p.Key == key);
                    pairs.Add(new KeyValuePair<string, object>(key, c[i + 1]));
                }
            };
            foreach (ScopeFrame f in frames) add(f.Context);
            if (context != null) add(context);
            bool detailed = level != Level.Info || ex != null;
            if (frames.Count > 0 && detailed) lines.Add("While: " + string.Join(" › ", frames.Select(f => f.Operation)));
            if (detailed || (context != null && context.Length > 0))
                foreach (var kv in pairs) lines.Add(kv.Key + ": " + Show(kv.Value));

            if (ex != null)
            {
                lines.Add("Exception:");
                lines.AddRange(ex.ToString().Replace("\r\n", "\n").Split('\n').Select(l => "  " + l));
                Exception root = ex;
                while (root.InnerException != null) root = root.InnerException;
                string win = WindowsError(ex);
                if (win != null) lines.Add("Windows error: " + win);
                if (IsFileProblem(ex))
                {
                    lines.Add("Path checks, as " + Environment.UserDomainName + "\\" + Environment.UserName + " on " + Environment.MachineName + ":");
                    foreach (string path in PathsFrom(pairs.Select(kv => kv.Value), ex)) lines.Add("  " + DescribePath(path));
                }
            }
            return Entry(level, msg[0] + (lines.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, lines) : ""));
        }

        static string Show(object v)
        {
            if (v == null) return "(none)";
            if (v is DateTime) return Fmt.DateTimeSeconds((DateTime)v);
            var e = v as System.Collections.IEnumerable;
            if (e != null && !(v is string)) return string.Join(", ", e.Cast<object>().Select(Show));
            return Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture);
        }

        static string Entry(Level level, string text)
        {
            string[] lines = (text ?? "").Replace("\r\n", "\n").Split('\n');
            var sb = new StringBuilder();
            DateTime now = DateTime.Now;   // read once: seconds and milliseconds must come from the same instant
            sb.Append(Fmt.DateTimeSeconds(now)).Append('.').Append(now.ToString("fff", System.Globalization.CultureInfo.InvariantCulture))
              .Append("  ").Append(level == Level.Info ? "INFO " : level == Level.Warn ? "WARN " : "ERROR").Append("  ").Append(lines[0]).Append(Environment.NewLine);
            for (int i = 1; i < lines.Length; i++) sb.Append("        ").Append(lines[i]).Append(Environment.NewLine);
            return sb.ToString();
        }

        static string Entry(string text) { return Entry(Level.Info, text); }

        // ---- error details ----

        static bool IsFileProblem(Exception ex)
        {
            for (Exception e = ex; e != null; e = e.InnerException)
                if (e is IOException || e is UnauthorizedAccessException || e is System.Security.SecurityException) return true;
            return false;
        }

        /// <summary>"5 (0x80070005): access denied" from the innermost exception that carries a Windows error, or null.</summary>
        public static string WindowsError(Exception ex)
        {
            Exception found = null;
            for (Exception e = ex; e != null; e = e.InnerException)
                if ((unchecked((uint)e.HResult) & 0xFFFF0000u) == 0x80070000u) found = e;
            if (found == null) return null;
            uint hr = unchecked((uint)found.HResult);
            int code = (int)(hr & 0xFFFF);
            string name;
            switch (code)
            {
                case 2: name = "file not found"; break;
                case 3: name = "folder (path) not found"; break;
                case 5: name = "access denied: this user isn't allowed to do this to the file or folder (permissions), or the file is read-only"; break;
                case 19: name = "the media is write-protected"; break;
                case 32: name = "sharing violation: the file is open in another program (or by another user)"; break;
                case 33: name = "lock violation: part of the file is locked by another program"; break;
                case 53: name = "the network path was not found"; break;
                case 59: name = "an unexpected network error occurred"; break;
                case 64: name = "the network name is no longer available"; break;
                case 67: name = "the network name cannot be found"; break;
                case 80: case 183: name = "the file already exists"; break;
                case 112: name = "the disk is full"; break;
                case 206: name = "the file name or path is too long"; break;
                case 1326: name = "user name or password is incorrect (network logon)"; break;
                case 1920: name = "the file cannot be accessed by the system"; break;
                default: name = found.GetType().Name; break;
            }
            return code + " (0x" + hr.ToString("X8") + "): " + name;
        }

        /// <summary>The paths to check: context values that are full paths, and paths quoted in the exception messages.</summary>
        static IEnumerable<string> PathsFrom(IEnumerable<object> values, Exception ex)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var found = new List<string>();
            Action<string> add = s =>
            {
                s = (s ?? "").Trim().Trim('"');
                bool looksLikePath = s.Length > 2 && (s.IndexOf('\\') >= 0 || s.IndexOf('/') >= 0) && s.IndexOfAny(Path.GetInvalidPathChars()) < 0;
                if (!looksLikePath) return;
                try { if (!Path.IsPathRooted(s)) return; s = Path.GetFullPath(s); }
                catch (Exception) { return; }
                if (seen.Add(s)) found.Add(s);
            };
            foreach (object v in values) add(v as string);
            for (Exception e = ex; e != null; e = e.InnerException)
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(e.Message ?? "", "'([^']+)'")) add(m.Groups[1].Value);
            return found;
        }

        /// <summary>One line on a path: what it is and, for a folder, whether this user can create a file in it. Never throws.</summary>
        public static string DescribePath(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    var fi = new FileInfo(path);
                    return path + ": file, " + fi.Length.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " bytes, last written " +
                           Fmt.DateTimeSeconds(fi.LastWriteTime) + ((fi.Attributes & FileAttributes.ReadOnly) != 0 ? ", READ-ONLY" : ", not read-only") +
                           "; folder: " + FolderCheck(fi.DirectoryName);
                }
                if (Directory.Exists(path)) return path + ": " + FolderCheck(path);
                string parent = Path.GetDirectoryName(path);
                if (parent != null && Directory.Exists(parent)) return path + ": does not exist; its folder " + parent + ": " + FolderCheck(parent);
                return path + ": does not exist, nor does its folder" + (parent == null ? "" : " (" + parent + ")");
            }
            catch (Exception e) { return path + ": could not be checked (" + e.GetType().Name + ": " + e.Message + ")"; }
        }

        static string FolderCheck(string folder)
        {
            string test = Path.Combine(folder, ".tlt-write-check-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
            try
            {
                using (new FileStream(test, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
                return "folder exists, this user CAN create files in it";
            }
            catch (Exception e) { return "folder exists, this user can NOT create files in it (" + e.GetType().Name + ": " + e.Message + ")"; }
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
