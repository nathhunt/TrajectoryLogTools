// CsvExporter.cs - data CSV and full-dump CSV export.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TrajectoryLogTools.Core
{
    // =============================================================================================
    // CSV export
    // =============================================================================================
    public static class CsvExporter
    {
        public const float NoValue = 3.0e38f; // log uses float.MaxValue for axes that don't exist (jaws)

        public static string DataCsv(TrajectoryLog log, Options opt)
        {
            bool both = !opt.ActualOnly;
            int cpIdx = Array.IndexOf(log.AxisIds, 42);
            int timeIdx = log.TimeAxisIndex;
            var sb = new StringBuilder(log.NumSnapshots * 64);
            bool[] hasData = AxesWithData(log);

            // ---- column header ----
            var cols = new List<string> { "Snapshot", "ElapsedTime_s" };
            if (timeIdx >= 0) cols.Add("ClockTime");
            if (log.Subbeams.Count > 0 && cpIdx >= 0) cols.Add("Subbeam");
            for (int a = 0; a < log.NumAxes; a++)
            {
                int id = log.AxisIds[a];
                if (id == TrajectoryLog.TimeAxisId) continue;
                if (id == 42) { cols.Add("ControlPoint"); continue; }
                if (id == 50)
                {
                    if (opt.NoMlc) continue;
                    for (int k = 0; k < log.SamplesPerAxis[a]; k++)
                        AddPair(cols, MlcColumn(k, log.SamplesPerAxis[a]) + "_cm", both);
                    continue;
                }
                if (!hasData[a]) continue;
                string unit = TrajectoryLog.AxisUnit(id);
                string name = TrajectoryLog.AxisName(id) + (unit.Length > 0 && unit != "MU" ? "_" + unit : "");
                if (log.SamplesPerAxis[a] == 1) AddPair(cols, name, both);
                else for (int k = 0; k < log.SamplesPerAxis[a]; k++) AddPair(cols, name + "_" + (k + 1), both);
                if (id == 41) cols.Add("DoseServoState");
            }
            sb.Append(string.Join(",", cols)).Append("\r\n");

            // ---- rows ----
            double t0 = timeIdx >= 0 && log.NumSnapshots > 0 ? log.Expected(0, timeIdx, 0) : 0;
            for (int s = 0; s < log.NumSnapshots; s++)
            {
                sb.Append(s + 1);
                if (timeIdx >= 0)
                {
                    double t = log.Expected(s, timeIdx, 0);
                    double el = t - t0;
                    if (el < -43200) el += 86400; // treatment crossed midnight
                    sb.Append(',').Append(Fmt.Fixed(el, 3)).Append(',').Append(Fmt.Clock(t));
                }
                else
                {
                    sb.Append(',').Append(Fmt.Fixed(s * log.SamplingIntervalMs / 1000.0, 3));
                }
                if (log.Subbeams.Count > 0 && cpIdx >= 0)
                    sb.Append(',').Append(SubbeamFor(log, log.Expected(s, cpIdx, 0)));

                for (int a = 0; a < log.NumAxes; a++)
                {
                    int id = log.AxisIds[a];
                    if (id == TrajectoryLog.TimeAxisId) continue;
                    if (id == 42) { sb.Append(',').Append(Fmt.Num(log.Expected(s, a, 0))); continue; }
                    if ((id == 50 && opt.NoMlc) || !hasData[a]) continue;
                    for (int k = 0; k < log.SamplesPerAxis[a]; k++)
                    {
                        if (both) sb.Append(',').Append(Val(log.Expected(s, a, k)));
                        sb.Append(',').Append(Val(log.Actual(s, a, k)));
                    }
                    if (id == 41) sb.Append(',').Append(TrajectoryLog.DoseServoName(log.Actual(s, a, 0)));
                }
                sb.Append("\r\n");
            }
            return sb.ToString();
        }

        /// <summary>
        /// False for axes that hold the "no value" marker in every snapshot (e.g. Halcyon jaws);
        /// those are left out of the data CSV.
        /// </summary>
        public static bool[] AxesWithData(TrajectoryLog log)
        {
            var has = new bool[log.NumAxes];
            for (int a = 0; a < log.NumAxes; a++)
            {
                for (int s = 0; s < log.NumSnapshots && !has[a]; s++)
                    for (int k = 0; k < log.SamplesPerAxis[a] && !has[a]; k++)
                        if (Math.Abs(log.Expected(s, a, k)) < NoValue || Math.Abs(log.Actual(s, a, k)) < NoValue)
                            has[a] = true;
                if (log.NumSnapshots == 0) has[a] = true;
            }
            return has;
        }

        /// <summary>
        /// The whole log as CSV, exactly as stored and in file order (to-csv --full): header fields
        /// with byte offsets, subbeams, every value of every axis (G9, "no value" markers included),
        /// reserved bytes as hex and the stored CRC.
        /// </summary>
        public static string FullCsv(TrajectoryLog log, string sourcePath)
        {
            byte[] b = log.Raw;
            var sb = new StringBuilder(log.NumSnapshots * log.FloatsPerSnapshot * 12 + 8192);
            Action<string[]> row = cells => sb.Append(string.Join(",", cells.Select(CsvCell))).Append("\r\n");
            Action<int, string, string> field = (offset, name, value) => row(new[] { Inv(offset), name, value });

            row(new[] { "TRAJECTORY LOG FULL DUMP" });
            row(new[] { "Source file", sourcePath == null ? "" : Path.GetFullPath(sourcePath) });
            row(new[] { "File size (bytes)", Inv(b.Length) });
            row(new[] { "Values are exactly as stored in the file (little-endian int32 / float32), in file order. " +
                        "Floats use 9 significant digits, which reproduces the stored value exactly. " +
                        "Text in (brackets) is an annotation, not file content." });
            sb.Append("\r\n");

            // ---- header ----
            row(new[] { "HEADER" });
            row(new[] { "Offset", "Field", "Value" });
            int p = 0;
            field(p, "Signature", HeaderText(b, p, TrajectoryLog.SignatureBytes)); p += TrajectoryLog.SignatureBytes;
            field(p, "Version", HeaderText(b, p, TrajectoryLog.VersionBytes)); p += TrajectoryLog.VersionBytes;
            field(p, "Header size", Inv(BinUtil.ReadInt(b, ref p)));
            field(p, "Sampling interval (ms)", Inv(BinUtil.ReadInt(b, ref p)));
            field(p, "Number of axes", Inv(BinUtil.ReadInt(b, ref p)));
            field(p, "Axis enumeration", "(" + log.NumAxes + " values, below)");
            field(p + log.NumAxes * 4, "Samples per axis", "(" + log.NumAxes + " values, below)");
            p += log.NumAxes * 8;
            row(new[] { "#", "Axis id", "Samples", "(Axis name)" });
            for (int a = 0; a < log.NumAxes; a++)
                row(new[] { Inv(a + 1), Inv(log.AxisIds[a]), Inv(log.SamplesPerAxis[a]), "(" + TrajectoryLog.AxisName(log.AxisIds[a]) + ")" });
            field(p, "Axis scale", Inv(log.AxisScale) + "   (" + TrajectoryLog.AxisScaleName(log.AxisScale) + ")"); p += 4;
            field(p, "Number of subbeams", Inv(log.NumSubbeamsField)); p += 4;
            field(p, "Is truncated", Inv(log.Truncated)); p += 4;
            field(p, "Number of snapshots", Inv(log.NumSnapshots)); p += 4;
            field(p, "MLC model", Inv(log.MlcModel)); p += 4;

            int metaLen = 0;
            while (metaLen < TrajectoryLog.MetaDataBytes && b[p + metaLen] != 0) metaLen++;
            field(p, "Metadata", "(" + TrajectoryLog.MetaDataBytes + " bytes, text up to the first zero byte: one line per row below)");
            foreach (string line in Encoding.UTF8.GetString(b, p, metaLen).Replace("\r\n", "\n").Split('\n'))
                row(new[] { "", "", line });
            Bytes(row, b, p + metaLen, TrajectoryLog.MetaDataBytes - metaLen, "Metadata after text");
            p += TrajectoryLog.MetaDataBytes;

            field(p, "Machine specifier", Inv(b[p]) + "   (byte value)"); p += 1;
            int serialLen = 0;
            while (serialLen < 6 && b[p + serialLen] != 0) serialLen++;
            field(p, "Machine serial number", Encoding.ASCII.GetString(b, p, serialLen) + "   (hex: " + Hex(b, p, 6) + ")"); p += 6;
            Bytes(row, b, p, TrajectoryLog.HeaderSize - p, "Reserved");
            sb.Append("\r\n");

            // ---- subbeams ----
            row(new[] { "SUBBEAMS", "(" + log.NumSubbeamsField + " subbeam(s), " + TrajectoryLog.SubbeamBytes +
                                    " bytes each, starting at offset " + TrajectoryLog.HeaderSize + ")" });
            row(new[] { "Subbeam", "Offset", "Control point", "MU", "Radiation time", "Sequence", "Name", "Reserved (hex)" });
            for (int k = 0; k < log.NumSubbeamsField; k++)
            {
                int o = TrajectoryLog.HeaderSize + k * TrajectoryLog.SubbeamBytes;
                int cp = o, seq = o + 12;
                row(new[] { Inv(k + 1), Inv(o), Inv(BinUtil.ReadInt(b, ref cp)), Fmt.Exact(BinUtil.ReadFloat(b, o + 4)),
                            Fmt.Exact(BinUtil.ReadFloat(b, o + 8)), Inv(BinUtil.ReadInt(b, ref seq)),
                            HeaderText(b, o + 16, 512), HexOrZeros(b, o + 528, TrajectoryLog.SubbeamBytes - 528) });
            }
            sb.Append("\r\n");

            // ---- snapshots ----
            row(new[] { "SNAPSHOTS", "(" + log.NumSnapshots + " snapshot(s) starting at offset " + log.DataOffset + ", " +
                                     log.FloatsPerSnapshot * 4 + " bytes each. Columns in file order: for each axis, for each sample, " +
                                     "the expected value then the actual value. Snapshot is counted from 1 and is not stored in the file.)" });
            var cols = new List<string> { "Snapshot" };
            for (int a = 0; a < log.NumAxes; a++)
            {
                string name = TrajectoryLog.AxisName(log.AxisIds[a]) + "(" + log.AxisIds[a] + ")";
                for (int k = 0; k < log.SamplesPerAxis[a]; k++)
                {
                    string s = log.SamplesPerAxis[a] == 1 ? name : name + "[" + (k + 1) + "]";
                    cols.Add(s + "_Exp");
                    cols.Add(s + "_Act");
                }
            }
            row(cols.ToArray());
            for (int s = 0; s < log.NumSnapshots; s++)
            {
                sb.Append(s + 1);
                int o = log.DataOffset + s * log.FloatsPerSnapshot * 4;
                for (int f = 0; f < log.FloatsPerSnapshot; f++)
                    sb.Append(',').Append(Fmt.Exact(BinUtil.ReadFloat(b, o + f * 4)));
                sb.Append("\r\n");
            }
            sb.Append("\r\n");

            // ---- CRC ----
            row(new[] { "CRC" });
            row(new[] { "Offset", "Field", "Value" });
            int crcPos = b.Length - TrajectoryLog.CrcBytes;
            field(crcPos, "CRC (stored)", "0x" + (b[crcPos] | (b[crcPos + 1] << 8)).ToString("X4", CultureInfo.InvariantCulture) +
                                          "   (bytes: " + Hex(b, crcPos, 2) + ")");
            field(crcPos, "CRC check", log.CrcWarning != null ? "(does not verify)" : "(verifies, " + log.CrcVariant.Name + ")");
            return sb.ToString();
        }

        static string Inv(int v) { return v.ToString(CultureInfo.InvariantCulture); }

        // Signature / version / subbeam name: 8-bit text, or UTF-16LE if it looks like it.
        static string HeaderText(byte[] b, int p, int len)
        {
            bool wide;
            string s = BinUtil.ReadString(b, p, len, out wide);
            return wide ? s + "   (UTF-16)" : s;
        }

        static void Bytes(Action<string[]> row, byte[] b, int p, int len, string label)
        {
            if (len > 0) row(new[] { Inv(p), label, HexOrZeros(b, p, len) });
        }

        static string HexOrZeros(byte[] b, int p, int len)
        {
            for (int i = 0; i < len; i++)
                if (b[p + i] != 0) return Hex(b, p, len) + "   (" + len + " bytes, hex)";
            return "(" + len + " bytes, all 0x00)";
        }

        static string Hex(byte[] b, int p, int len)
        {
            var sb = new StringBuilder(len * 3);
            for (int i = 0; i < len; i++) { if (i > 0) sb.Append(' '); sb.Append(b[p + i].ToString("X2", CultureInfo.InvariantCulture)); }
            return sb.ToString();
        }

        static string CsvCell(string s)
        {
            if (s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }

        static void AddPair(List<string> cols, string name, bool both)
        {
            if (both) cols.Add(name + "_Expected");
            cols.Add(name + "_Actual");
        }

        // First two MLC samples are the banks, the rest are leaves (spec: "all leaves plus the 2 banks").
        public static string MlcColumn(int k, int total)
        {
            int leaves = total - 2;
            if (k < 2) return "MLC_Bank" + (k + 1);
            return "MLC_Leaf" + (k - 1).ToString(leaves >= 100 ? "000" : "00", CultureInfo.InvariantCulture);
        }

        // Subbeam = last subbeam whose starting control point is <= the current control point.
        public static int SubbeamFor(TrajectoryLog log, float cp)
        {
            int result = 1;
            for (int k = 0; k < log.Subbeams.Count; k++)
                if (log.Subbeams[k].ControlPoint <= cp + 1e-4) result = k + 1;
            return result;
        }

        static string Val(float v)
        {
            if (float.IsNaN(v) || Math.Abs(v) >= NoValue) return "";
            return Fmt.Num(v);
        }
    }
}
