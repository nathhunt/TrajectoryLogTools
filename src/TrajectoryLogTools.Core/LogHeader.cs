// LogHeader.cs - a quick look at a log: reads only the 1024-byte header and the subbeam records
// (not the snapshots, and without checking the length or CRC), so a long list of logs can be
// shown at once. TrajectoryLog.Parse reads and checks the whole file.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace TrajectoryLogTools.Core
{
    public sealed class LogHeader
    {
        public string Path;
        public string Version;
        public int SamplingIntervalMs;
        public int NumSnapshots;
        public int AxisScale;
        public int MlcModel;
        public string MetaDataText = "";
        public string MachineSerial = "";
        public List<Subbeam> Subbeams = new List<Subbeam>();
        /// <summary>Treatment date and time: from the file name's time stamp, else the file's modified time.</summary>
        public DateTime Date;
        public bool DateFromName;

        /// <summary>Value of a metadata line "Key: value" / "Key:&lt;tab&gt;value" / "Key=value" (case-insensitive key), or null.</summary>
        public string Meta(string key)
        {
            foreach (string raw in MetaDataText.Replace("\r\n", "\n").Split('\n'))
            {
                int i = raw.IndexOfAny(new[] { ':', '=' });
                if (i <= 0) continue;
                if (!string.Equals(raw.Substring(0, i).Trim(), key, StringComparison.OrdinalIgnoreCase)) continue;
                string v = raw.Substring(i + 1).Trim();
                return v.Length > 0 ? v : null;
            }
            return null;
        }

        /// <summary>Plan name from the metadata, else null.</summary>
        public string PlanName
        {
            get { return Meta("Plan Name") ?? Meta("PlanName") ?? Meta("Plan Label") ?? Meta("Plan"); }
        }

        /// <summary>The subbeam (field) names, e.g. "Arc 1 + Arc 2", or "3 fields" when there are many.</summary>
        public string FieldsText
        {
            get
            {
                var names = Subbeams.Select(s => s.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
                if (Subbeams.Count == 0) return "";
                if (names.Count == Subbeams.Count && names.Count <= 3) return string.Join(" + ", names);
                return Subbeams.Count + (Subbeams.Count == 1 ? " field" : " fields");
            }
        }

        public static LogHeader Read(string path)
        {
            const int H = TrajectoryLog.HeaderSize;
            byte[] head = ReadBytes(path, H);
            if (head.Length < H) throw new InvalidDataException("File is too short to be a trajectory log.");
            bool wide;
            if (BinUtil.ReadString(head, 0, TrajectoryLog.SignatureBytes, out wide) != "VOSTL")
                throw new InvalidDataException("Not a trajectory log (no VOSTL signature).");

            var h = new LogHeader { Path = path };
            h.Version = BinUtil.ReadString(head, TrajectoryLog.SignatureBytes, TrajectoryLog.VersionBytes, out wide);
            int p = TrajectoryLog.SignatureBytes + TrajectoryLog.VersionBytes + 4;   // after the header size
            h.SamplingIntervalMs = BinUtil.ReadInt(head, ref p);
            int numAxes = BinUtil.ReadInt(head, ref p);
            if (numAxes <= 0 || numAxes > 64) throw new InvalidDataException("Implausible number of axes: " + numAxes);
            p += numAxes * 8;
            h.AxisScale = BinUtil.ReadInt(head, ref p);
            int numSubbeams = BinUtil.ReadInt(head, ref p);
            p += 4; // truncated
            h.NumSnapshots = BinUtil.ReadInt(head, ref p);
            h.MlcModel = BinUtil.ReadInt(head, ref p);
            if (p + TrajectoryLog.MetaDataBytes + 7 <= H)
            {
                h.MetaDataText = BinUtil.UpToNull(Encoding.UTF8.GetString(head, p, TrajectoryLog.MetaDataBytes));
                int mi = p + TrajectoryLog.MetaDataBytes;
                h.MachineSerial = BinUtil.UpToNull(Encoding.ASCII.GetString(head, mi + 1, 6));
            }

            if (numSubbeams > 0 && numSubbeams < 1000)
            {
                byte[] all = ReadBytes(path, H + numSubbeams * TrajectoryLog.SubbeamBytes);
                for (int k = 0; k < numSubbeams && H + (k + 1) * TrajectoryLog.SubbeamBytes <= all.Length; k++)
                {
                    int q = H + k * TrajectoryLog.SubbeamBytes;
                    var sb = new Subbeam();
                    sb.ControlPoint = BinUtil.ReadInt(all, ref q);
                    sb.MU = BinUtil.ReadFloat(all, q); q += 4;
                    sb.RadTime = BinUtil.ReadFloat(all, q); q += 4;
                    sb.Seq = BinUtil.ReadInt(all, ref q);
                    sb.Name = BinUtil.ReadString(all, q, 512, out wide);
                    h.Subbeams.Add(sb);
                }
            }

            LogFile f = LogFinder.Describe(path, null);
            h.Date = f.Date;
            h.DateFromName = f.DateFromName;
            return h;
        }

        static byte[] ReadBytes(string path, int count)
        {
            using (var fs = File.OpenRead(path))
            {
                var b = new byte[Math.Min(count, fs.Length)];
                int read = 0;
                while (read < b.Length)
                {
                    int n = fs.Read(b, read, b.Length - read);
                    if (n == 0) break;
                    read += n;
                }
                return b;
            }
        }
    }
}
