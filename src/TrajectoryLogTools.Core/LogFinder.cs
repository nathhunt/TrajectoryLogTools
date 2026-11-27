// LogFinder.cs - finds the logs to process: a single file, or a folder (optionally with
// subfolders), optionally limited to logs treated within a date range.
//
// Treatment date of a log: Halcyon / Ethos logs are named "..._yyyyMMddHHmmss.bin"; that time
// stamp is used. Files without one fall back to the file's last-modified date.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace TrajectoryLogTools.Core
{
    /// <summary>A log file found in the input, with the date used for the date-range filter.</summary>
    public sealed class LogFile
    {
        public string FullPath;
        public string RelativeDir;   // folder relative to the input folder ("" = top level)
        public DateTime Date;
        public bool DateFromName;    // true = from "_yyyyMMddHHmmss" in the name, false = file date
    }

    /// <summary>What to look for.</summary>
    public sealed class FindSettings
    {
        public bool FolderMode;
        public string Input;
        public bool IncludeSubfolders;
        public bool UseDateRange;
        public DateTime From;        // inclusive, start of day
        public DateTime To;          // inclusive, end of day
        /// <summary>Only logs treated between these times of day (inclusive), on any day; a window past midnight (22:00 - 02:00) works too.</summary>
        public bool UseTimeOfDay;
        public TimeSpan TimeFrom, TimeTo;
        /// <summary>Export folder; logs inside it are skipped so earlier output is not picked up.</summary>
        public string Output;
    }

    public static class LogFinder
    {
        /// <summary>True when <paramref name="t"/> is in [from, to]; when from is after to the window runs past midnight.</summary>
        public static bool InTimeWindow(TimeSpan t, TimeSpan from, TimeSpan to)
        {
            return from <= to ? t >= from && t <= to : t >= from || t <= to;
        }

        /// <summary>"8:30", "08:30", "0830" or "8" -> time of day; false otherwise.</summary>
        public static bool TryParseTimeOfDay(string text, out TimeSpan time)
        {
            time = TimeSpan.Zero;
            string s = (text ?? "").Trim().Replace('.', ':');
            if (s.Length == 0) return false;
            int h, m = 0;
            if (s.Contains(":"))
            {
                string[] p = s.Split(':');
                if (p.Length != 2 || !int.TryParse(p[0], NumberStyles.None, CultureInfo.InvariantCulture, out h) ||
                    !int.TryParse(p[1], NumberStyles.None, CultureInfo.InvariantCulture, out m) || p[1].Length != 2) return false;
            }
            else if (s.Length <= 2) { if (!int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out h)) return false; }
            else if (s.Length == 4 && int.TryParse(s.Substring(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out h) &&
                     int.TryParse(s.Substring(2), NumberStyles.None, CultureInfo.InvariantCulture, out m)) { }
            else return false;
            if (h > 23 || m > 59) { if (!(h == 24 && m == 0)) return false; }
            time = h == 24 ? new TimeSpan(23, 59, 59) : new TimeSpan(h, m, 0);
            return true;
        }

        static readonly Regex StampRx = new Regex(@"(?<!\d)\d{14}(?!\d)");

        /// <summary>Treatment date/time from a "yyyyMMddHHmmss" stamp in the file name (last one wins).</summary>
        public static bool TryDateFromName(string path, out DateTime date)
        {
            MatchCollection ms = StampRx.Matches(Path.GetFileNameWithoutExtension(path) ?? "");
            for (int i = ms.Count - 1; i >= 0; i--)
                if (DateTime.TryParseExact(ms[i].Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture,
                                           DateTimeStyles.None, out date))
                    return true;
            date = DateTime.MinValue;
            return false;
        }

        public static LogFile Describe(string path, string rootFolder)
        {
            var f = new LogFile { FullPath = Path.GetFullPath(path) };
            f.DateFromName = TryDateFromName(path, out f.Date);
            if (!f.DateFromName) f.Date = File.GetLastWriteTime(path);
            string dir = Path.GetDirectoryName(f.FullPath);
            f.RelativeDir = rootFolder == null ? "" : RelativePath(rootFolder, dir);
            return f;
        }

        public static List<LogFile> Find(FindSettings s) { return Find(s, null); }

        /// <summary>
        /// The logs found. A subfolder that can't be searched (no permission, removed meanwhile, ...)
        /// is left out rather than failing the whole search; each is added to <paramref name="skipped"/>
        /// ("folder: reason") when given, and logged. The folder itself must be readable.
        /// </summary>
        public static List<LogFile> Find(FindSettings s, List<string> skipped)
        {
            var result = new List<LogFile>();
            if (!s.FolderMode)
            {
                result.Add(Describe(s.Input, null));
                return result;
            }

            string root = Path.GetFullPath(s.Input);
            string outRoot = string.IsNullOrEmpty(s.Output) ? null : Path.GetFullPath(s.Output);
            foreach (string path in BinFiles(root, s.IncludeSubfolders, skipped, Directory.GetFiles, Directory.GetFiles, Directory.GetDirectories))
            {
                // Don't pick up our own earlier output when it sits inside the input folder.
                if (outRoot != null && !PathEquals(outRoot, root) && IsUnder(path, outRoot)) continue;
                LogFile f = Describe(path, root);
                if (s.UseDateRange && (f.Date < s.From || f.Date > s.To)) continue;
                if (s.UseTimeOfDay && !InTimeWindow(f.Date.TimeOfDay, s.TimeFrom, s.TimeTo)) continue;
                result.Add(f);
            }
            return result.OrderBy(f => f.RelativeDir, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(f => f.Date)
                         .ThenBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase)
                         .ToList();
        }

        /// <summary>Subfolder levels searched at most when walking folder by folder (stops a link loop).</summary>
        internal const int MaxDepth = 64;

        /// <summary>
        /// The .bin files of <paramref name="root"/> (and its subfolders). One call for the whole tree, as
        /// before; only if that fails is the tree walked folder by folder, so the folders that can't be read
        /// are left out (and listed in <paramref name="skipped"/>) instead of failing the search. The
        /// listing functions are parameters for the tests.
        /// </summary>
        internal static string[] BinFiles(string root, bool recurse, List<string> skipped,
                                          Func<string, string, SearchOption, string[]> tree,
                                          Func<string, string, string[]> files, Func<string, string[]> folders)
        {
            try { return tree(root, "*.bin", recurse ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly); }
            catch (Exception ex) when (recurse && FileUtil.IsFileError(ex)) { }   // walk it below, to find which folder

            string[] top = files(root, "*.bin");   // the folder chosen must be readable: throws as before
            var found = new List<string>(top);
            var pending = new Stack<KeyValuePair<string, int>>();
            foreach (string sub in Enumerable.Reverse(folders(root))) pending.Push(new KeyValuePair<string, int>(sub, 1));
            while (pending.Count > 0)
            {
                var next = pending.Pop();
                string dir = next.Key;
                string[] here, subs;
                try
                {
                    here = files(dir, "*.bin");
                    subs = next.Value < MaxDepth ? folders(dir) : new string[0];
                }
                catch (Exception ex) when (FileUtil.IsFileError(ex))
                {
                    if (skipped != null) skipped.Add(dir + ": " + ex.Message);
                    AppLog.Warn("A subfolder was left out of the search, it can't be read: " + dir, ex, "Folder searched", root, "Subfolder", dir);
                    continue;
                }
                found.AddRange(here);
                foreach (string sub in Enumerable.Reverse(subs)) pending.Push(new KeyValuePair<string, int>(sub, next.Value + 1));
            }
            return found.ToArray();
        }

        public static string Normalize(string path)
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public static bool PathEquals(string a, string b)
        {
            return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsUnder(string path, string folder)
        {
            return Path.GetFullPath(path).StartsWith(Normalize(folder) + Path.DirectorySeparatorChar,
                                                     StringComparison.OrdinalIgnoreCase);
        }

        static string RelativePath(string root, string dir)
        {
            string r = Normalize(root), d = Normalize(dir);
            if (string.Equals(r, d, StringComparison.OrdinalIgnoreCase)) return "";
            return d.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                ? d.Substring(r.Length + 1) : "";
        }
    }
}
