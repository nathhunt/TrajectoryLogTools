// V5Converter.cs - v4.0 -> v5.1 (validation / testing).

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
    // v4.0 -> v5.1 (validation / testing)
    // =============================================================================================
    public static class V5Converter
    {
        /// <summary>
        /// Axis order of HAL 5.0 (v5.1) logs, after the time axis, as found in machine logs; the spec
        /// does not state it. v4.0 logs use 0 - 11, 40, 41, 42, 50.
        /// </summary>
        public static readonly int[] V51AxisOrder = { 42, 40, 41, 1, 0, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 50 };

        /// <param name="sourcePath">Input file; its "_yyyyMMddHHmmss" stamp gives the default start time.</param>
        public static byte[] Convert(TrajectoryLog log, Options opt, string sourcePath, List<string> notes)
        {
            byte[] src = log.Raw;
            if (log.TimeAxisIndex >= 0) throw new InvalidDataException("Log already has a time axis (43).");
            if (log.CrcWarning != null) notes.Add("WARNING: " + log.CrcWarning + " Output CRC written as " + log.CrcVariant.Name + ".");
            else if (log.CrcVariant != Crc16.Variants[0])
                notes.Add("Input CRC matched " + log.CrcVariant.Name + "; the same variant is used for the output.");

            float[] expTime, actTime;
            TimeValues(log, opt, sourcePath, notes, out expTime, out actTime);

            // Axes in the order HAL 5.0 writes them; any other axes follow in their input order.
            // srcAxis[j] = input axis index of output axis j, NewTime for the time axis, or NewCouch
            // for a couch axis added by --add-couch-rotations (1 sample, couch at its zero position).
            const int NewTime = -1, NewCouch = -2;
            var srcAxis = new List<int> { NewTime };
            var axisIds = new List<int> { TrajectoryLog.TimeAxisId };
            var added = new List<int>();
            foreach (int id in V51AxisOrder)
            {
                int i = Array.IndexOf(log.AxisIds, id);
                if (i >= 0) { srcAxis.Add(i); axisIds.Add(id); }
                else if (opt.AddCouchRotations && Array.IndexOf(TrajectoryLog.CouchRotationAxisIds, id) >= 0)
                {
                    srcAxis.Add(NewCouch); axisIds.Add(id); added.Add(id);
                }
            }
            for (int i = 0; i < log.NumAxes; i++)
                if (!srcAxis.Contains(i)) { srcAxis.Add(i); axisIds.Add(log.AxisIds[i]); }
            var samples = srcAxis.Select(i => i < 0 ? 1 : log.SamplesPerAxis[i]).ToList();
            bool reordered = !srcAxis.Where(i => i >= 0).SequenceEqual(Enumerable.Range(0, log.NumAxes));
            int newAxisScale = opt.AxisScale ?? log.AxisScale;

            // Header
            var header = new byte[TrajectoryLog.HeaderSize]; // zero-filled => reserved bytes are 0
            Buffer.BlockCopy(src, 0, header, 0, TrajectoryLog.SignatureBytes);
            BinUtil.WriteString(header, TrajectoryLog.SignatureBytes, TrajectoryLog.VersionBytes, "5.1", log.VersionWide);
            int q = TrajectoryLog.SignatureBytes + TrajectoryLog.VersionBytes;
            BinUtil.WriteInt(header, ref q, TrajectoryLog.HeaderSize);
            BinUtil.WriteInt(header, ref q, log.SamplingIntervalMs);
            BinUtil.WriteInt(header, ref q, axisIds.Count);
            foreach (int id in axisIds) BinUtil.WriteInt(header, ref q, id);
            foreach (int n in samples) BinUtil.WriteInt(header, ref q, n);
            BinUtil.WriteInt(header, ref q, newAxisScale);
            BinUtil.WriteInt(header, ref q, log.NumSubbeamsField);
            BinUtil.WriteInt(header, ref q, log.Truncated);
            BinUtil.WriteInt(header, ref q, log.NumSnapshots);
            BinUtil.WriteInt(header, ref q, log.MlcModel);
            if (q + TrajectoryLog.MetaDataBytes + 7 > TrajectoryLog.HeaderSize)
                throw new InvalidDataException("No room in the 1024-byte header for another axis.");
            // Metadata + machine specifier / serial number.
            Buffer.BlockCopy(src, log.MetaOffset, header, q, TrajectoryLog.MetaDataBytes + 7);
            int mi = q + TrajectoryLog.MetaDataBytes;
            if (log.MachineSpecifier == 0 && log.MachineSerial.Length == 0)
            {
                header[mi] = 1; // 0 would mean TrueBeam
                notes.Add("Machine specifier set to 1 (Halcyon / Ethos); the input had none.");
            }
            if (opt.Serial != null)
            {
                Array.Clear(header, mi + 1, 6);
                byte[] serial = Encoding.ASCII.GetBytes(opt.Serial);
                Buffer.BlockCopy(serial, 0, header, mi + 1, serial.Length);
            }

            // Body
            int snapBytes = log.FloatsPerSnapshot * 4;
            const int oneSampleBytes = 2 * 4;
            long outLength = (long)TrajectoryLog.HeaderSize + (long)log.NumSubbeamsField * TrajectoryLog.SubbeamBytes
                             + (long)log.NumSnapshots * (snapBytes + oneSampleBytes * (1 + added.Count)) + TrajectoryLog.CrcBytes;
            if (outLength > int.MaxValue) throw new InvalidDataException("Output would exceed 2 GB.");
            var dst = new byte[outLength];

            Buffer.BlockCopy(header, 0, dst, 0, TrajectoryLog.HeaderSize);
            Buffer.BlockCopy(src, TrajectoryLog.HeaderSize, dst, TrajectoryLog.HeaderSize,
                             log.NumSubbeamsField * TrajectoryLog.SubbeamBytes);

            int dstPos = log.DataOffset;
            for (int s = 0; s < log.NumSnapshots; s++)
            {
                int sp = log.DataOffset + s * snapBytes;
                for (int j = 0; j < srcAxis.Count; j++)
                {
                    int i = srcAxis[j];
                    if (i == NewTime)
                    {
                        BinUtil.WriteFloat(dst, dstPos, expTime[s]);
                        BinUtil.WriteFloat(dst, dstPos + 4, actTime[s]);
                        dstPos += oneSampleBytes;
                        continue;
                    }
                    if (i == NewCouch)
                    {
                        float zero = TrajectoryLog.CouchZeroPosition(axisIds[j], newAxisScale);
                        BinUtil.WriteFloat(dst, dstPos, zero);
                        BinUtil.WriteFloat(dst, dstPos + 4, zero);
                        dstPos += oneSampleBytes;
                        continue;
                    }
                    int bytes = log.SamplesPerAxis[i] * 2 * 4;
                    Buffer.BlockCopy(src, sp + log.AxisFloatOffset[i] * 4, dst, dstPos, bytes);
                    dstPos += bytes;
                }
            }

            int crcPos = dst.Length - TrajectoryLog.CrcBytes;
            ushort crc = log.CrcVariant.Compute(dst, crcPos);
            dst[crcPos] = (byte)(crc & 0xFF);
            dst[crcPos + 1] = (byte)(crc >> 8);

            notes.Add(string.Format(CultureInfo.InvariantCulture,
                "{0} axes -> {1}, {2} subbeam(s), {3} snapshots, {4:N0} -> {5:N0} bytes",
                log.NumAxes, axisIds.Count, log.NumSubbeamsField, log.NumSnapshots, src.Length, dst.Length));
            if (reordered)
                notes.Add("Axes reordered to the v5.1 order: " + string.Join(", ", axisIds.Select(TrajectoryLog.AxisName)) + ".");
            if (opt.AddCouchRotations)
                notes.Add(added.Count > 0
                    ? "Couch axes added at the zero position for axis scale " + newAxisScale + ": " +
                      string.Join(", ", added.Select(id => TrajectoryLog.AxisName(id) + " = " +
                          Fmt.Num(TrajectoryLog.CouchZeroPosition(id, newAxisScale)))) + "."
                    : "Couch rotation / pitch / roll axes already present; left unchanged.");
            if (opt.AxisScale.HasValue && opt.AxisScale.Value != log.AxisScale)
                notes.Add(string.Format("WARNING: axis-scale flag changed {0} -> {1}; axis values were NOT converted.",
                                        log.AxisScale, newAxisScale));
            else if (newAxisScale != 3)
                notes.Add("WARNING: axis scale kept at " + newAxisScale + "; HAL 5.0 v5.1 logs use 3 (couch values are not converted).");
            return dst;
        }

        /// <summary>Time axis values per snapshot: from --time-from, else start time + index x interval.</summary>
        static void TimeValues(TrajectoryLog log, Options opt, string sourcePath, List<string> notes,
                               out float[] expected, out float[] actual)
        {
            int n = log.NumSnapshots;
            expected = new float[n];
            actual = new float[n];

            if (opt.TimeFrom != null)
            {
                string[] lines = File.ReadAllLines(opt.TimeFrom).Skip(1).Where(l => l.Trim().Length > 0).ToArray();
                if (lines.Length != n)
                    throw new InvalidDataException("Time CSV has " + lines.Length + " rows but the log has " + n + " snapshots: " + opt.TimeFrom);
                for (int s = 0; s < n; s++)
                {
                    string[] f = lines[s].Split(',');
                    float e, a;
                    double sec;
                    // Expected / Actual columns (exact values) if present, else SecondsSinceMidnight.
                    if (f.Length >= 5 && float.TryParse(f[3], NumberStyles.Float, CultureInfo.InvariantCulture, out e)
                                      && float.TryParse(f[4], NumberStyles.Float, CultureInfo.InvariantCulture, out a))
                    {
                        expected[s] = e; actual[s] = a;
                    }
                    else if (f.Length >= 2 && double.TryParse(f[1], NumberStyles.Float, CultureInfo.InvariantCulture, out sec))
                        expected[s] = (float)sec; // actual stays 0 ("empty" per the spec)
                    else
                        throw new InvalidDataException("Time CSV row " + (s + 2) + " is not valid: " + lines[s]);
                }
                notes.Add("Time axis restored from " + opt.TimeFrom);
                return;
            }

            double start;
            string from;
            TimeSpan stamp;
            if (opt.StartTime.HasValue) { start = opt.StartTime.Value; from = "--start-time"; }
            else if (FileUtil.TryTimeFromName(sourcePath, out stamp)) { start = stamp.TotalSeconds; from = "file name"; }
            else { start = 0; from = "no time stamp in file name"; }
            for (int s = 0; s < n; s++)
                expected[s] = (float)((start + s * log.SamplingIntervalMs / 1000.0) % 86400); // actual stays 0
            notes.Add("Time axis generated: " + Fmt.Clock(start) + " (" + from + ") + snapshot x " + log.SamplingIntervalMs +
                      " ms. v4.0 does not record beam pauses, so clock times are approximate.");
        }
    }
}
