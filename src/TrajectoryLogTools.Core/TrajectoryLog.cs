// TrajectoryLog.cs - parsed trajectory log (Halcyon / Ethos v4.0 and v5.1).

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
    // Parsed trajectory log (v4.0 and v5.1)
    // =============================================================================================
    public sealed class Subbeam
    {
        public int ControlPoint;
        public float MU;
        public float RadTime;
        public int Seq;
        public string Name;
    }

    public sealed class TrajectoryLog
    {
        public const int HeaderSize = 1024;
        public const int SignatureBytes = 16;
        public const int VersionBytes = 16;
        public const int MetaDataBytes = 745;
        public const int SubbeamBytes = 560;
        public const int CrcBytes = 2;
        public const int TimeAxisId = 43;
        public const int BeamHoldAxisId = 41;
        public const int ControlPointAxisId = 42;
        public const int MlcAxisId = 50;
        /// <summary>Dose servo state "Hold": the only state in which no MV beam is delivered.</summary>
        public const int DoseServoHold = 2;

        /// <summary>
        /// Versions Parse accepts. Both are Halcyon / Ethos layouts (HAL 2.0 - 4.0 MR1 and HAL 5.0).
        /// TrueBeam 2.7 MR2+ also writes "4.0" (and earlier TrueBeam releases "3.0"), with the same
        /// header, subbeam and snapshot layout; see MlcGeometry for what TrueBeam support would add.
        /// </summary>
        public static readonly string[] SupportedVersions = { "4.0", "5.1" };
        public static readonly int[] CouchRotationAxisIds = { 9, 10, 11 }; // CouchRtn, CouchPit, CouchRol

        public byte[] Raw;
        public string Version;
        public bool VersionWide;
        public int SamplingIntervalMs;
        public int NumAxes;
        public int[] AxisIds;
        public int[] SamplesPerAxis;
        public int AxisScale;
        public int NumSubbeamsField;
        public int Truncated;
        public int NumSnapshots;
        public int MlcModel;
        public int MetaOffset;
        public string MetaDataText;
        public bool HasMachineInfo;          // v5.1 only
        public int MachineSpecifier;
        public string MachineSerial;
        public List<Subbeam> Subbeams = new List<Subbeam>();
        public int DataOffset;               // start of snapshot data
        public int FloatsPerSnapshot;        // (sum of samples) * 2
        public int[] AxisFloatOffset;        // float offset of each axis within a snapshot
        public int TimeAxisIndex = -1;
        public Crc16.Variant CrcVariant;
        public string CrcWarning;

        public static TrajectoryLog Parse(byte[] src, bool ignoreCrc)
        {
            var log = new TrajectoryLog { Raw = src };
            if (src.Length < HeaderSize + CrcBytes)
                throw new InvalidDataException("File is too short to be a trajectory log (" + src.Length + " bytes).");

            bool wide;
            string signature = BinUtil.ReadString(src, 0, SignatureBytes, out wide);
            if (signature != "VOSTL")
                throw new InvalidDataException("Signature is '" + signature + "', expected 'VOSTL'.");
            log.Version = BinUtil.ReadString(src, SignatureBytes, VersionBytes, out log.VersionWide);
            if (Array.IndexOf(SupportedVersions, log.Version) < 0)
                throw new InvalidDataException("Unsupported version '" + log.Version + "' (4.0 and 5.1 are supported).");

            int p = SignatureBytes + VersionBytes;
            int headerSize = BinUtil.ReadInt(src, ref p);
            log.SamplingIntervalMs = BinUtil.ReadInt(src, ref p);
            log.NumAxes = BinUtil.ReadInt(src, ref p);
            if (headerSize != HeaderSize)
                throw new InvalidDataException("Header size is " + headerSize + ", expected 1024.");
            if (log.NumAxes <= 0 || log.NumAxes > 64)
                throw new InvalidDataException("Implausible number of axes: " + log.NumAxes);

            log.AxisIds = new int[log.NumAxes];
            for (int i = 0; i < log.NumAxes; i++) log.AxisIds[i] = BinUtil.ReadInt(src, ref p);
            log.SamplesPerAxis = new int[log.NumAxes];
            for (int i = 0; i < log.NumAxes; i++)
            {
                log.SamplesPerAxis[i] = BinUtil.ReadInt(src, ref p);
                if (log.SamplesPerAxis[i] <= 0 || log.SamplesPerAxis[i] > 1000)
                    throw new InvalidDataException("Implausible samples-per-axis value " + log.SamplesPerAxis[i] +
                                                   " for axis " + log.AxisIds[i] + ".");
            }

            log.AxisScale = BinUtil.ReadInt(src, ref p);
            log.NumSubbeamsField = BinUtil.ReadInt(src, ref p);
            log.Truncated = BinUtil.ReadInt(src, ref p);
            log.NumSnapshots = BinUtil.ReadInt(src, ref p);
            log.MlcModel = BinUtil.ReadInt(src, ref p);
            log.MetaOffset = p;
            if (log.MetaOffset + MetaDataBytes + 7 > HeaderSize)
                throw new InvalidDataException("Header fields overflow 1024 bytes; too many axes?");
            if (log.NumSubbeamsField < 0 || log.NumSnapshots < 0)
                throw new InvalidDataException("Negative subbeam or snapshot count.");
            log.MetaDataText = BinUtil.UpToNull(Encoding.UTF8.GetString(src, log.MetaOffset, MetaDataBytes));

            int mi = log.MetaOffset + MetaDataBytes;
            log.MachineSpecifier = src[mi];
            log.MachineSerial = BinUtil.UpToNull(Encoding.ASCII.GetString(src, mi + 1, 6));
            log.HasMachineInfo = log.Version == "5.1";

            log.FloatsPerSnapshot = log.SamplesPerAxis.Sum() * 2;
            log.AxisFloatOffset = new int[log.NumAxes];
            for (int i = 1; i < log.NumAxes; i++)
                log.AxisFloatOffset[i] = log.AxisFloatOffset[i - 1] + log.SamplesPerAxis[i - 1] * 2;
            log.TimeAxisIndex = Array.IndexOf(log.AxisIds, TimeAxisId);

            long expectedLength = (long)HeaderSize + (long)log.NumSubbeamsField * SubbeamBytes
                                  + (long)log.NumSnapshots * log.FloatsPerSnapshot * 4 + CrcBytes;
            if (src.Length != expectedLength)
                throw new InvalidDataException("File length " + src.Length + " does not match header (expected " +
                                               expectedLength + ").");

            for (int k = 0; k < log.NumSubbeamsField; k++)
            {
                int o = HeaderSize + k * SubbeamBytes;
                int q = o;
                var sb = new Subbeam();
                sb.ControlPoint = BinUtil.ReadInt(src, ref q);
                sb.MU = BinUtil.ReadFloat(src, q); q += 4;
                sb.RadTime = BinUtil.ReadFloat(src, q); q += 4;
                sb.Seq = BinUtil.ReadInt(src, ref q);
                bool w;
                sb.Name = BinUtil.ReadString(src, q, 512, out w);
                log.Subbeams.Add(sb);
            }
            log.DataOffset = HeaderSize + log.NumSubbeamsField * SubbeamBytes;

            ushort storedCrc = (ushort)(src[src.Length - 2] | (src[src.Length - 1] << 8));
            log.CrcVariant = Crc16.Detect(src, src.Length - CrcBytes, storedCrc);
            if (log.CrcVariant == null)
            {
                string msg = string.Format("Input CRC 0x{0:X4} does not verify - file may be corrupt.", storedCrc);
                if (!ignoreCrc) throw new InvalidDataException(msg + " (use --ignore-crc to force)");
                log.CrcWarning = msg;
                log.CrcVariant = Crc16.Variants[0];
            }
            return log;
        }

        /// <summary>
        /// Quick version test: reads only the signature and version string (first 32 bytes), so it
        /// does not check the rest of the file. Returns e.g. "4.0" or "5.1".
        /// </summary>
        public static string ReadVersion(string path)
        {
            var head = new byte[SignatureBytes + VersionBytes];
            int read = 0;
            using (var fs = File.OpenRead(path))
                while (read < head.Length)
                {
                    int n = fs.Read(head, read, head.Length - read);
                    if (n == 0) throw new InvalidDataException("File is too short to be a trajectory log (" + read + " bytes).");
                    read += n;
                }
            bool wide;
            string signature = BinUtil.ReadString(head, 0, SignatureBytes, out wide);
            if (signature != "VOSTL")
                throw new InvalidDataException("Signature is '" + signature + "', expected 'VOSTL'.");
            return BinUtil.ReadString(head, SignatureBytes, VersionBytes, out wide);
        }

        public static bool IsVersion4(string path) { return ReadVersion(path) == "4.0"; }
        public static bool IsVersion51(string path) { return ReadVersion(path) == "5.1"; }

        int ValueOffset(int snapshot, int axisIndex, int sample)
        {
            return DataOffset + (snapshot * FloatsPerSnapshot + AxisFloatOffset[axisIndex] + sample * 2) * 4;
        }

        /// <summary>Expected value; values are stored as (expected, actual) pairs per sample.</summary>
        public float Expected(int snapshot, int axisIndex, int sample)
        {
            return BinUtil.ReadFloat(Raw, ValueOffset(snapshot, axisIndex, sample));
        }

        public float Actual(int snapshot, int axisIndex, int sample)
        {
            return BinUtil.ReadFloat(Raw, ValueOffset(snapshot, axisIndex, sample) + 4);
        }

        public static string AxisName(int id)
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
                default: return "Axis" + id;
            }
        }

        public static string AxisUnit(int id)
        {
            if (id == 0 || id == 1 || id == 9 || id == 10 || id == 11) return "deg";
            if ((id >= 2 && id <= 8) || id == 50) return "cm";
            if (id == 40) return "MU";
            if (id == 43) return "s";
            return "";
        }

        /// <summary>
        /// Reading of couch rotation / pitch / roll with the couch at its zero position. In the Varian
        /// machine scales (1 and 3) couch rotation reads 180 deg at the IEC 0 position (machine logs
        /// with axis scale 3 show 180.04); in Modified IEC 61217 (2) it reads 0. Pitch and roll read 0.
        /// </summary>
        public static float CouchZeroPosition(int axisId, int axisScale)
        {
            return axisId == 9 && axisScale != 2 ? 180f : 0f;
        }

        public static string AxisScaleName(int s)
        {
            switch (s)
            {
                case 1: return "Machine scale (couch in machine representation)";
                case 2: return "Modified IEC 61217";
                case 3: return "Machine scale (couch in isocentric representation)";
                default: return "Unknown";
            }
        }

        /// <summary>
        /// True when the MV beam is on: dose servo Normal, Freeze or Disabled. Only Hold (2) stops the
        /// beam (Freeze and Disabled occur in Service mode only).
        /// </summary>
        public static bool IsBeamOn(double doseServoState)
        {
            return !double.IsNaN(doseServoState) && (int)Math.Round(doseServoState) != DoseServoHold;
        }

        public static string DoseServoName(float v)
        {
            switch ((int)Math.Round(v))
            {
                case 0: return "Normal";
                case 1: return "Freeze";
                case 2: return "Hold";
                case 3: return "Disabled";
                default: return "";
            }
        }
    }
}
