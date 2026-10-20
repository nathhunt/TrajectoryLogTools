// RawDump.cs - a trajectory log (.bin) as human-readable text, exactly as stored
// (ported from TrajectoryLogDump.cs).
//
// Nothing is converted, reordered, scaled, rounded or left out:
//   * header fields are listed in file order with their byte offsets;
//   * axes are listed in the order of the file's axis enumeration;
//   * every snapshot value (expected and actual, every sample of every axis, including the
//     "no value" marker 3.40282347E+38) is written with 9 significant digits ("G9"), which
//     round-trips a 32-bit float exactly;
//   * reserved / padding bytes and the stored CRC are shown as hex.
// Names next to the numbers (axis names, axis-scale meaning) are annotations only.
//
// Unlike TrajectoryLog.Parse, this does not validate the file: any version with the same layout,
// a wrong length or a bad CRC is dumped as far as the data goes, with warnings.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace TrajectoryLogTools.Core
{
    public static class RawDump
    {
        const int HeaderSize = 1024;
        const int MetaDataBytes = 745;
        const int SubbeamBytes = 560;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>The dump as text (CRLF line ends). Problems found are added to <paramref name="warnings"/>.</summary>
        public static string ToText(byte[] b, string path, List<string> warnings)
        {
            var w = new StringWriter(Inv) { NewLine = "\r\n" };
            Write(b, path, w, warnings);
            return w.ToString();
        }

        static void Write(byte[] b, string path, TextWriter w, List<string> warnings)
        {
            if (b.Length < HeaderSize + 2)
                throw new InvalidDataException("File is " + b.Length + " bytes, shorter than the 1024-byte header + CRC.");

            w.WriteLine("TRAJECTORY LOG DUMP");
            w.WriteLine("Source file : " + (path == null ? "(not from a file)" : Path.GetFullPath(path)));
            w.WriteLine("File size   : " + b.Length + " bytes");
            w.WriteLine("All values are shown exactly as stored in the file (little-endian int32 / float32),");
            w.WriteLine("in file order. Floats use 9 significant digits, which reproduces the stored value exactly.");
            w.WriteLine("Text in (brackets) is an annotation, not file content.");
            w.WriteLine();

            // ---------------- header ----------------
            w.WriteLine("==================================== HEADER ====================================");
            w.WriteLine(Row("Offset", "Field", "Value"));
            int p = 0;
            w.WriteLine(Row(p, "Signature", Str(b, p, 16))); p += 16;
            string version = Str(b, p, 16);
            w.WriteLine(Row(p, "Version", version)); p += 16;
            int headerSize = Int(b, p); w.WriteLine(Row(p, "Header size", headerSize)); p += 4;
            int interval = Int(b, p); w.WriteLine(Row(p, "Sampling interval (ms)", interval)); p += 4;
            int numAxes = Int(b, p); w.WriteLine(Row(p, "Number of axes", numAxes)); p += 4;
            if (numAxes <= 0 || p + numAxes * 8 + 20 + MetaDataBytes + 7 > HeaderSize)
                throw new InvalidDataException("Number of axes (" + numAxes + ") does not fit in the header.");

            var axisIds = new int[numAxes];
            var samples = new int[numAxes];
            int axisEnumOffset = p;
            for (int i = 0; i < numAxes; i++) axisIds[i] = Int(b, p + i * 4);
            p += numAxes * 4;
            int samplesOffset = p;
            for (int i = 0; i < numAxes; i++) samples[i] = Int(b, p + i * 4);
            p += numAxes * 4;

            w.WriteLine(Row(axisEnumOffset, "Axis enumeration", "(" + numAxes + " values, below)"));
            w.WriteLine(Row(samplesOffset, "Samples per axis", "(" + numAxes + " values, below)"));
            w.WriteLine("        #   Axis id   Samples   (Axis name)");
            for (int i = 0; i < numAxes; i++)
                w.WriteLine(string.Format(Inv, "        {0,-3} {1,-9} {2,-9} ({3})", i + 1, axisIds[i], samples[i], AxisName(axisIds[i])));

            int axisScale = Int(b, p); w.WriteLine(Row(p, "Axis scale", axisScale + "   (" + AxisScaleName(axisScale) + ")")); p += 4;
            int numSubbeams = Int(b, p); w.WriteLine(Row(p, "Number of subbeams", numSubbeams)); p += 4;
            int truncated = Int(b, p); w.WriteLine(Row(p, "Is truncated", truncated)); p += 4;
            int numSnapshots = Int(b, p); w.WriteLine(Row(p, "Number of snapshots", numSnapshots)); p += 4;
            int mlcModel = Int(b, p); w.WriteLine(Row(p, "MLC model", mlcModel)); p += 4;

            w.WriteLine(Row(p, "Metadata", "(" + MetaDataBytes + " bytes, text up to the first zero byte below)"));
            int metaLen = TextLength(b, p, MetaDataBytes);
            string meta = Encoding.UTF8.GetString(b, p, metaLen);
            foreach (string line in meta.Replace("\r\n", "\n").Split('\n'))
                w.WriteLine("        | " + line);
            WriteTrailingBytes(w, b, p + metaLen, MetaDataBytes - metaLen, "        Metadata after text");
            p += MetaDataBytes;

            w.WriteLine(Row(p, "Machine specifier", b[p].ToString(Inv) + "   (byte value)")); p += 1;
            w.WriteLine(Row(p, "Machine serial number", "\"" + Encoding.ASCII.GetString(b, p, TextLength(b, p, 6)) + "\"   (hex: " + Hex(b, p, 6) + ")")); p += 6;
            WriteTrailingBytes(w, b, p, HeaderSize - p, Row(p, "Reserved", "").TrimEnd());
            w.WriteLine();

            if (headerSize != HeaderSize) warnings.Add("Header size field is " + headerSize + ", expected 1024.");

            // ---------------- subbeams ----------------
            int dataOffset = HeaderSize + Math.Max(0, numSubbeams) * SubbeamBytes;
            int snapFloats = 0;
            foreach (int n in samples) snapFloats += n * 2;
            long expected = (long)dataOffset + (long)Math.Max(0, numSnapshots) * snapFloats * 4 + 2;
            if (b.Length != expected)
                warnings.Add("File length " + b.Length + " does not match the header (expected " + expected + "); dumping what is present.");

            w.WriteLine("=================================== SUBBEAMS ===================================");
            w.WriteLine("(" + numSubbeams + " subbeam(s), " + SubbeamBytes + " bytes each, starting at offset " + HeaderSize + ")");
            for (int k = 0; k < numSubbeams; k++)
            {
                int o = HeaderSize + k * SubbeamBytes;
                if (o + SubbeamBytes > b.Length - 2) { w.WriteLine("Subbeam " + (k + 1) + ": missing (file too short)"); break; }
                w.WriteLine("Subbeam " + (k + 1) + "  (offset " + o + ")");
                w.WriteLine(Row(o, "  Control point", Int(b, o)));
                w.WriteLine(Row(o + 4, "  MU", F(Float(b, o + 4))));
                w.WriteLine(Row(o + 8, "  Radiation time", F(Float(b, o + 8))));
                w.WriteLine(Row(o + 12, "  Sequence", Int(b, o + 12)));
                w.WriteLine(Row(o + 16, "  Name", "\"" + Str(b, o + 16, 512) + "\""));
                WriteTrailingBytes(w, b, o + 528, SubbeamBytes - 528, Row(o + 528, "  Reserved", "").TrimEnd());
            }
            w.WriteLine();

            // ---------------- snapshots ----------------
            long available = Math.Max(0, (b.Length - 2L - dataOffset) / Math.Max(1, snapFloats * 4));
            long snaps = Math.Min(Math.Max(0, numSnapshots), available);
            w.WriteLine("================================== SNAPSHOTS ===================================");
            w.WriteLine("(" + snaps + " snapshot(s) starting at offset " + dataOffset + ", " + snapFloats * 4 +
                        " bytes each; tab-separated; columns in file order: for each axis, for each sample,");
            w.WriteLine(" the expected value then the actual value. Column names: AxisName(axis id)[sample]_Exp/_Act.");
            w.WriteLine(" The first column is the snapshot number, counted from 1; it is not stored in the file.)");
            w.WriteLine();

            var cols = new List<string> { "Snapshot" };
            for (int a = 0; a < numAxes; a++)
            {
                string name = AxisName(axisIds[a]) + "(" + axisIds[a] + ")";
                for (int k = 0; k < samples[a]; k++)
                {
                    string s = samples[a] == 1 ? name : name + "[" + (k + 1) + "]";
                    cols.Add(s + "_Exp");
                    cols.Add(s + "_Act");
                }
            }
            w.WriteLine(string.Join("\t", cols));

            var sb = new StringBuilder(snapFloats * 12);
            for (long s = 0; s < snaps; s++)
            {
                sb.Clear();
                sb.Append(s + 1);
                long o = dataOffset + s * snapFloats * 4;
                for (int f = 0; f < snapFloats; f++)
                    sb.Append('\t').Append(F(Float(b, (int)(o + f * 4))));
                w.WriteLine(sb.ToString());
            }
            w.WriteLine();

            // ---------------- CRC / trailing bytes ----------------
            w.WriteLine("===================================== CRC ======================================");
            long end = dataOffset + snaps * snapFloats * 4;
            if (end < b.Length - 2)
                WriteTrailingBytes(w, b, (int)end, (int)(b.Length - 2 - end), Row((int)end, "Unparsed bytes", "").TrimEnd());
            int crcPos = b.Length - 2;
            w.WriteLine(Row(crcPos, "CRC (stored)", "0x" + (b[crcPos] | (b[crcPos + 1] << 8)).ToString("X4", Inv) +
                                                    "   (bytes: " + Hex(b, crcPos, 2) + ")"));
        }

        // ---------------- helpers ----------------

        static int Int(byte[] b, int p) { return b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24); }

        static float Float(byte[] b, int p)
        {
            if (BitConverter.IsLittleEndian) return BitConverter.ToSingle(b, p);
            return BitConverter.ToSingle(new[] { b[p + 3], b[p + 2], b[p + 1], b[p] }, 0);
        }

        // "R" is not exact for float on .NET Framework; G9 always round-trips a float32.
        static string F(float v) { return v.ToString("G9", Inv); }

        static int TextLength(byte[] b, int p, int max)
        {
            int n = 0;
            while (n < max && b[p + n] != 0) n++;
            return n;
        }

        // Text field: 8-bit text, or UTF-16LE if it looks like it (the spec says "Unicode").
        static string Str(byte[] b, int p, int len)
        {
            bool wide = len >= 4 && b[p] != 0 && b[p + 1] == 0 && b[p + 2] != 0 && b[p + 3] == 0;
            if (wide)
            {
                int n = 0;
                while (n + 1 < len && (b[p + n] != 0 || b[p + n + 1] != 0)) n += 2;
                return Encoding.Unicode.GetString(b, p, n) + "   (UTF-16)";
            }
            return Encoding.UTF8.GetString(b, p, TextLength(b, p, len));
        }

        static void WriteTrailingBytes(TextWriter w, byte[] b, int p, int len, string label)
        {
            if (len <= 0) return;
            bool allZero = true;
            for (int i = 0; i < len; i++) if (b[p + i] != 0) { allZero = false; break; }
            if (allZero) { w.WriteLine(label + "   (" + len + " bytes, all 0x00)"); return; }
            w.WriteLine(label + "   (" + len + " bytes, hex)");
            for (int i = 0; i < len; i += 16)
                w.WriteLine("        " + (p + i).ToString(Inv).PadRight(8) + Hex(b, p + i, Math.Min(16, len - i)));
        }

        static string Hex(byte[] b, int p, int len)
        {
            var sb = new StringBuilder(len * 3);
            for (int i = 0; i < len; i++) { if (i > 0) sb.Append(' '); sb.Append(b[p + i].ToString("X2", Inv)); }
            return sb.ToString();
        }

        static string Row(object offset, string field, object value)
        {
            return string.Format(Inv, "{0,-8}{1,-26}{2}", offset, field, value);
        }

        static string AxisName(int id)
        {
            switch (id)
            {
                case 0: return "CollRtn";
                case 1: return "GantryRtn";
                case 2: return "Y1";
                case 3: return "Y2";
                case 4: return "X1";
                case 5: return "X2";
                case 6: return "CouchVrt";
                case 7: return "CouchLng";
                case 8: return "CouchLat";
                case 9: return "CouchRtn";
                case 10: return "CouchPit";
                case 11: return "CouchRol";
                case 40: return "MU";
                case 41: return "BeamHold";
                case 42: return "ControlPoint";
                case 43: return "Time";
                case 50: return "MLC";
                default: return "Axis";
            }
        }

        static string AxisScaleName(int s)
        {
            switch (s)
            {
                case 1: return "Machine scale, couch in machine representation";
                case 2: return "Modified IEC 61217";
                case 3: return "Machine scale, couch in isocentric representation";
                default: return "unknown";
            }
        }
    }
}
