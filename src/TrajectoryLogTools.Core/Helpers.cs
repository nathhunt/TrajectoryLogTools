// Helpers.cs - formatting, file and binary helpers.

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
    // Helpers
    // =============================================================================================
    public static class Csv
    {
        public static string Field(string s)
        {
            if (s == null) return "";
            if (s.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0) return s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
    }

    public static class Fmt
    {
        // 7 significant digits = full single-precision resolution, without float noise
        // (e.g. 101.9 rather than 101.899994).
        public static string Num(float v) { return v.ToString("G7", CultureInfo.InvariantCulture); }

        // 9 significant digits always round-trip a float exactly.
        public static string Exact(float v) { return v.ToString("G9", CultureInfo.InvariantCulture); }

        public static string Fixed(double v, int decimals)
        {
            return v.ToString("F" + decimals, CultureInfo.InvariantCulture);
        }

        public static string Clock(double secondsSinceMidnight)
        {
            if (double.IsNaN(secondsSinceMidnight) || secondsSinceMidnight < 0 || secondsSinceMidnight >= 172800) return "";
            return TimeSpan.FromSeconds(secondsSinceMidnight).ToString(@"hh\:mm\:ss\.fff", CultureInfo.InvariantCulture);
        }
    }

    public static class FileUtil
    {
        static readonly Regex StampRx = new Regex(@"(?<!\d)\d{14}(?!\d)");

        /// <summary>Time of day from a "yyyyMMddHHmmss" stamp in the file name (last one wins).</summary>
        public static bool TryTimeFromName(string path, out TimeSpan time)
        {
            MatchCollection ms = StampRx.Matches(Path.GetFileNameWithoutExtension(path) ?? "");
            for (int i = ms.Count - 1; i >= 0; i--)
            {
                DateTime d;
                if (DateTime.TryParseExact(ms[i].Value, "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out d))
                {
                    time = d.TimeOfDay;
                    return true;
                }
            }
            time = TimeSpan.Zero;
            return false;
        }

        /// <summary>Write via a temp file so a failure never leaves a half-written output behind.</summary>
        public static void WriteAtomic(string path, byte[] data)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, data);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
    }

    public static class BinUtil
    {
        public static string UpToNull(string s)
        {
            int z = s.IndexOf('\0');
            return z >= 0 ? s.Substring(0, z) : s;
        }

        public static int ReadInt(byte[] b, ref int p)
        {
            int v = b[p] | (b[p + 1] << 8) | (b[p + 2] << 16) | (b[p + 3] << 24);
            p += 4;
            return v;
        }

        public static void WriteInt(byte[] b, ref int p, int v)
        {
            b[p] = (byte)v; b[p + 1] = (byte)(v >> 8); b[p + 2] = (byte)(v >> 16); b[p + 3] = (byte)(v >> 24);
            p += 4;
        }

        public static void WriteFloat(byte[] b, int p, float v)
        {
            byte[] f = BitConverter.GetBytes(v);
            if (!BitConverter.IsLittleEndian) Array.Reverse(f);
            Buffer.BlockCopy(f, 0, b, p, 4);
        }

        public static float ReadFloat(byte[] b, int p)
        {
            if (BitConverter.IsLittleEndian) return BitConverter.ToSingle(b, p);
            var r = new[] { b[p + 3], b[p + 2], b[p + 1], b[p] };
            return BitConverter.ToSingle(r, 0);
        }

        // The spec says "zero terminated Unicode string"; logs in the field use 8-bit text.
        // UTF-16LE is detected (and preserved on write) in case a writer uses it.
        public static string ReadString(byte[] b, int offset, int length, out bool wide)
        {
            wide = length >= 4 && b[offset] != 0 && b[offset + 1] == 0 && b[offset + 2] != 0 && b[offset + 3] == 0;
            string s = wide ? Encoding.Unicode.GetString(b, offset, length)
                            : Encoding.UTF8.GetString(b, offset, length);
            int z = s.IndexOf('\0');
            return (z >= 0 ? s.Substring(0, z) : s).Trim();
        }

        public static void WriteString(byte[] b, int offset, int length, string value, bool wide)
        {
            Array.Clear(b, offset, length);
            byte[] bytes = wide ? Encoding.Unicode.GetBytes(value) : Encoding.ASCII.GetBytes(value);
            Buffer.BlockCopy(bytes, 0, b, offset, Math.Min(bytes.Length, length - (wide ? 2 : 1)));
        }
    }

    /// <summary>
    /// 16-bit CCITT CRC (poly 0x1021, seed 0xFFFF) as stated in the spec. The spec does not say
    /// whether the bit order is reflected or whether a final XOR is applied, so the variant that
    /// reproduces the input file's CRC is detected and reused for the output.
    /// </summary>
}
