// V4Batch.cs - what the v4.0 converter app does: take the logs found in a folder (or one file),
// work out which of them to convert to v4.0 and where each goes, then convert them one by one.
//
// Every log goes to the same output folder, under its own file name. A log is not converted when:
//   * it is not a v5.1 log (already v4.0, or not a trajectory log);
//   * its v4.0 file is already in the output folder (unless overwrite);
//   * another log in the list has the same file name (from a subfolder): only the first is converted;
//   * the output folder is the log's own folder (the v4.0 file would replace the original).
// Only the v4.0 log is written (no CSV, time-axis file or run log), always the same way:
//   machine specifier TrueBeam (0), serial number removed, couch axes kept, and nothing else in the
//   header changed (patient, plan, field names, metadata are copied as they are).

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace TrajectoryLogTools.Core
{
    public enum V4BatchState
    {
        /// <summary>Will be (or was) converted.</summary>
        Convert,
        /// <summary>The v4.0 file is already in the output folder.</summary>
        AlreadyConverted,
        /// <summary>Not a v5.1 log (e.g. already v4.0).</summary>
        NotV51,
        /// <summary>The header can't be read.</summary>
        Unreadable,
        /// <summary>Another log in the list has the same file name.</summary>
        DuplicateName,
        /// <summary>The output folder is the log's own folder.</summary>
        SameFolder,
    }

    public sealed class V4BatchItem
    {
        public string Path { get; set; }
        public string FileName { get { return System.IO.Path.GetFileName(Path); } }
        /// <summary>Folder relative to the folder searched ("" = top level, or a single file).</summary>
        public string RelativeDir { get; set; } = "";
        public DateTime Date { get; set; }
        public string Version { get; set; } = "";
        public string PlanName { get; set; }
        public string PatientId { get; set; }
        public string FieldsText { get; set; } = "";
        public string OutputPath { get; set; }
        public V4BatchState State { get; set; }
        /// <summary>Why it is not converted, in a few words ("" when it is).</summary>
        public string Reason { get; set; } = "";
        public bool WillConvert { get { return State == V4BatchState.Convert; } }
    }

    public static class V4Batch
    {
        /// <summary>The plan for the logs found (in their order), all going to <paramref name="outputFolder"/>.</summary>
        public static List<V4BatchItem> Plan(IEnumerable<LogFile> logs, string outputFolder, bool overwrite)
        {
            var result = new List<V4BatchItem>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (LogFile f in logs)
            {
                V4BatchItem item = Describe(f.FullPath, outputFolder, overwrite);
                item.RelativeDir = f.RelativeDir ?? "";
                item.Date = f.Date;
                if (item.State == V4BatchState.Convert || item.State == V4BatchState.AlreadyConverted)
                {
                    if (!names.Add(item.FileName))
                    {
                        item.State = V4BatchState.DuplicateName;
                        item.Reason = "same file name as another log in the list";
                    }
                }
                result.Add(item);
            }
            return result;
        }

        /// <summary>The plan for one file.</summary>
        public static V4BatchItem PlanOne(string path, string outputFolder, bool overwrite)
        {
            V4BatchItem item = Describe(path, outputFolder, overwrite);
            LogFile f = LogFinder.Describe(path, null);
            item.Date = f.Date;
            return item;
        }

        static V4BatchItem Describe(string path, string outputFolder, bool overwrite)
        {
            var item = new V4BatchItem { Path = System.IO.Path.GetFullPath(path) };
            item.OutputPath = System.IO.Path.Combine(outputFolder ?? "", item.FileName);
            LogHeader h;
            try { h = LogHeader.Read(item.Path); }
            catch (Exception ex)
            {
                item.State = V4BatchState.Unreadable;
                item.Reason = "can't be read: " + ex.Message;
                return item;
            }
            item.Version = h.Version;
            item.PlanName = h.PlanName;
            item.PatientId = h.PatientId;
            item.FieldsText = h.FieldsText;
            if (h.Version != "5.1")
            {
                item.State = V4BatchState.NotV51;
                item.Reason = "already v" + h.Version;
            }
            else if (LogFinder.PathEquals(System.IO.Path.GetDirectoryName(item.Path), System.IO.Path.GetFullPath(outputFolder ?? ".")))
            {
                item.State = V4BatchState.SameFolder;
                item.Reason = "the output folder is this log's own folder";
            }
            else if (!overwrite && File.Exists(item.OutputPath))
            {
                item.State = V4BatchState.AlreadyConverted;
                item.Reason = "already in the output folder";
            }
            else item.State = V4BatchState.Convert;
            return item;
        }

        /// <summary>The converter's fixed v4.0 options: TrueBeam, serial number removed, couch axes kept, nothing else.</summary>
        public static ExportOptions Options(bool overwrite)
        {
            return new ExportOptions
            {
                ToV4 = true, ToCsv = false, FullCsv = false, Dump = false, TimeCsv = false,
                Machine = 0, KeepSerial = false, NewSerial = null, KeepCouch = true,
                Overwrite = overwrite,
            };
        }

        /// <summary>Converts one log of the plan to v4.0 in its output folder (see <see cref="Options"/>); nothing else is written.</summary>
        public static ExportOutcome Convert(V4BatchItem item, bool overwrite, List<string> lines)
        {
            return Exporter.Run(item.Path, System.IO.Path.GetDirectoryName(item.OutputPath), Options(overwrite), lines);
        }
    }
}
