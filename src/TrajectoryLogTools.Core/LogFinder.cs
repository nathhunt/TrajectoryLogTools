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
        /// <summary>Export folder; logs inside it are skipped so earlier output is not picked up.</summary>
        public string Output;
    }

    public static class LogFinder
    {
        static readonly Regex StampRx = new Regex(@"(?<!\d)\d{14}(?!\d)");

        /// <summary>Treatment date/time from a "yyyyMMddHHmmss" stamp in the file name (last one wins).</summary>
        public static bool TryDateFromName(string path, out DateTime date)
        {
            MatchCollection ms = StampRx.Matches(Path.GetFileNameWithoutExtension(path));
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

        public static List<LogFile> Find(FindSettings s)
        {
            var result = new List<LogFile>();
            if (!s.FolderMode)
            {
                result.Add(Describe(s.Input, null));
                return result;
            }

            string root = Path.GetFullPath(s.Input);
            string outRoot = string.IsNullOrEmpty(s.Output) ? null : Path.GetFullPath(s.Output);
            var option = s.IncludeSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            foreach (string path in Directory.GetFiles(root, "*.bin", option))
            {
                // Don't pick up our own earlier output when it sits inside the input folder.
                if (outRoot != null && !PathEquals(outRoot, root) && IsUnder(path, outRoot)) continue;
                LogFile f = Describe(path, root);
                if (s.UseDateRange && (f.Date < s.From || f.Date > s.To)) continue;
                result.Add(f);
            }
            return result.OrderBy(f => f.RelativeDir, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(f => f.Date)
                         .ThenBy(f => f.FullPath, StringComparer.OrdinalIgnoreCase)
                         .ToList();
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
