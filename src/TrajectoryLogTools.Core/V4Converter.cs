// V4Converter.cs - v5.1 -> v4.0.

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
    // v5.1 -> v4.0
    // =============================================================================================
    public static class V4Converter
    {
        /// <summary>Axis order of v4.0 logs, as written by HAL 2.0 - 4.0 MR1 machines; the spec does not state it.</summary>
        public static readonly int[] V4AxisOrder = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 40, 41, 42, 50 };

        public static byte[] Convert(TrajectoryLog log, Options opt, List<string> notes)
        {
            byte[] src = log.Raw;
            int timeIdx = log.TimeAxisIndex;
            if (timeIdx < 0) notes.Add("No time axis (43) found; axis data copied unchanged.");
            if (log.CrcWarning != null) notes.Add("WARNING: " + log.CrcWarning + " Output CRC written as " + log.CrcVariant.Name + ".");
            else if (log.CrcVariant != Crc16.Variants[0])
                notes.Add("Input CRC matched " + log.CrcVariant.Name + "; the same variant is used for the output.");

            // Axes in the v4.0 order (time axis left out); any other axes follow in their input order.
            var keep = new List<int>();
            foreach (int id in V4AxisOrder)
            {
                int i = Array.IndexOf(log.AxisIds, id);
                if (i >= 0) keep.Add(i);
            }
            for (int i = 0; i < log.NumAxes; i++)
                if (i != timeIdx && !keep.Contains(i)) keep.Add(i);
            bool reordered = !keep.SequenceEqual(Enumerable.Range(0, log.NumAxes).Where(i => i != timeIdx));
            var dropped = new List<int>();
            if (opt.DropCouchRotations)
            {
                dropped = keep.Where(i => Array.IndexOf(TrajectoryLog.CouchRotationAxisIds, log.AxisIds[i]) >= 0).ToList();
                keep.RemoveAll(dropped.Contains);
            }
            int[] keepIdx = keep.ToArray();
            int newAxisScale = opt.AxisScale ?? log.AxisScale;

            // Header
            var header = new byte[TrajectoryLog.HeaderSize]; // zero-filled => reserved bytes are 0
            Buffer.BlockCopy(src, 0, header, 0, TrajectoryLog.SignatureBytes);
            BinUtil.WriteString(header, TrajectoryLog.SignatureBytes, TrajectoryLog.VersionBytes, "4.0", log.VersionWide);
            int q = TrajectoryLog.SignatureBytes + TrajectoryLog.VersionBytes;
            BinUtil.WriteInt(header, ref q, TrajectoryLog.HeaderSize);
            BinUtil.WriteInt(header, ref q, log.SamplingIntervalMs);
            BinUtil.WriteInt(header, ref q, keepIdx.Length);
            foreach (int i in keepIdx) BinUtil.WriteInt(header, ref q, log.AxisIds[i]);
            foreach (int i in keepIdx) BinUtil.WriteInt(header, ref q, log.SamplesPerAxis[i]);
            BinUtil.WriteInt(header, ref q, newAxisScale);
            BinUtil.WriteInt(header, ref q, log.NumSubbeamsField);
            BinUtil.WriteInt(header, ref q, log.Truncated);
            BinUtil.WriteInt(header, ref q, log.NumSnapshots);
            BinUtil.WriteInt(header, ref q, log.MlcModel);
            Buffer.BlockCopy(src, log.MetaOffset, header, q, TrajectoryLog.MetaDataBytes);
            q += TrajectoryLog.MetaDataBytes;
            if (opt.KeepMachineInfo)
                Buffer.BlockCopy(src, log.MetaOffset + TrajectoryLog.MetaDataBytes, header, q, 7);

            // Body
            int snapBytes = log.FloatsPerSnapshot * 4;
            int newSnapBytes = keepIdx.Sum(i => log.SamplesPerAxis[i]) * 2 * 4;
            long outLength = (long)TrajectoryLog.HeaderSize + (long)log.NumSubbeamsField * TrajectoryLog.SubbeamBytes
                             + (long)log.NumSnapshots * newSnapBytes + TrajectoryLog.CrcBytes;
            if (outLength > int.MaxValue) throw new InvalidDataException("Output would exceed 2 GB.");
            var dst = new byte[outLength];

            Buffer.BlockCopy(header, 0, dst, 0, TrajectoryLog.HeaderSize);
            Buffer.BlockCopy(src, TrajectoryLog.HeaderSize, dst, TrajectoryLog.HeaderSize,
                             log.NumSubbeamsField * TrajectoryLog.SubbeamBytes);

            int dstPos = log.DataOffset;
            for (int s = 0; s < log.NumSnapshots; s++)
            {
                int sp = log.DataOffset + s * snapBytes;
                foreach (int i in keepIdx)
                {
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
                log.NumAxes, keepIdx.Length, log.NumSubbeamsField, log.NumSnapshots, src.Length, dst.Length));
            if (reordered)
                notes.Add("Axes reordered to the v4.0 order: " + string.Join(", ", keepIdx.Select(i => TrajectoryLog.AxisName(log.AxisIds[i]))) + ".");
            if (opt.DropCouchRotations)
                notes.Add(dropped.Count > 0
                    ? "Couch axes removed: " + string.Join(", ", dropped.Select(i => TrajectoryLog.AxisName(log.AxisIds[i]))) + "."
                    : "No couch rotation / pitch / roll axes to remove.");
            if (log.AxisScale == 3 && newAxisScale == 3)
                notes.Add("Axis scale kept at 3 (machine scale, isocentric couch) - valid in v4.0.");
            if (opt.AxisScale.HasValue && opt.AxisScale.Value != log.AxisScale)
                notes.Add(string.Format("WARNING: axis-scale flag changed {0} -> {1}; axis values were NOT converted.",
                                        log.AxisScale, newAxisScale));
            if (!opt.KeepMachineInfo)
                notes.Add(string.Format("Machine specifier {0} / serial '{1}' removed from header.",
                                        log.MachineSpecifier, log.MachineSerial));
            return dst;
        }
    }
}
