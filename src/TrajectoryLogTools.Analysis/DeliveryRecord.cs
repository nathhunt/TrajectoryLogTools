// DeliveryRecord.cs - a log as named columns of numbers, the form the analyses work on.
//
// The columns and their names are those of the data CSV that to-csv writes (Snapshot,
// ElapsedTime_s, [ClockTime,] Subbeam, ControlPoint, <axis>_Expected / _Actual, DoseServoState,
// MLC_Bank1/2_cm_*, MLC_LeafNNN_cm_*), so a record can come straight from a .bin log or from an
// earlier CSV export, and the analyses give the same results either way (up to the 7 significant
// digits the CSV keeps). Values that are absent ("no value" marker, empty cell) are NaN.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TrajectoryLogTools.Core;

namespace TrajectoryLogTools.Analysis
{
    public sealed class DeliveryRecord
    {
        public const string ServoColumn = "DoseServoState";

        readonly List<string> _names = new List<string>();
        readonly Dictionary<string, double[]> _cols = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);

        /// <summary>File the record was read from (a .bin log or a CSV export).</summary>
        public string SourcePath { get; private set; }
        public int RowCount { get; private set; }
        /// <summary>Dose servo state name per row ("Normal", "Hold", ...); "" if unknown.</summary>
        public string[] ServoState { get; private set; }
        /// <summary>Header axis scale of the .bin log (1 - 3), or 0 for a CSV export (not recorded).</summary>
        public int AxisScale { get; private set; }
        /// <summary>The parsed log when the record came from a .bin file, else null.</summary>
        public TrajectoryLog Log { get; private set; }

        /// <summary>Numeric columns in file order (DoseServoState excluded).</summary>
        public IReadOnlyList<string> ColumnNames { get { return _names; } }

        public bool Has(string name) { return _cols.ContainsKey(name); }

        public double[] Column(string name)
        {
            double[] c;
            if (!_cols.TryGetValue(name, out c)) throw new InvalidDataException("Log is missing column '" + name + "'.");
            return c;
        }

        public double[] TryColumn(string name)
        {
            double[] c;
            return _cols.TryGetValue(name, out c) ? c : null;
        }

        /// <summary>True when the MV beam is on in this row (dose servo not in Hold).</summary>
        public bool IsBeamOn(int row)
        {
            double[] hold = TryColumn("BeamHold_Actual");
            if (hold != null && !double.IsNaN(hold[row])) return TrajectoryLog.IsBeamOn(hold[row]);
            return !string.Equals(ServoState[row], "Hold", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Column name of MLC leaf <paramref name="oneBased"/>, e.g. MLC_Leaf001_cm_Actual.</summary>
        public static string LeafColumn(int oneBased, string kind)
        {
            return "MLC_Leaf" + oneBased.ToString("000", CultureInfo.InvariantCulture) + "_cm_" + kind;
        }

        /// <summary>Number of MLC leaves with both Expected and Actual columns (0 if none).</summary>
        public int LeafCount
        {
            get
            {
                int n = 0;
                while (Has(LeafColumn(n + 1, "Actual")) && Has(LeafColumn(n + 1, "Expected"))) n++;
                return n;
            }
        }

        /// <summary>The known MLC with this many leaves, or null.</summary>
        public MlcGeometry Mlc { get { return MlcGeometry.FromLeafCount(LeafCount); } }

        // =========================================================================================
        // Loading
        // =========================================================================================

        /// <summary>A .bin trajectory log, or a data CSV exported from one.</summary>
        public static DeliveryRecord Load(string path, bool ignoreCrc = false)
        {
            if (string.Equals(Path.GetExtension(path), ".bin", StringComparison.OrdinalIgnoreCase))
            {
                var r = FromLog(TrajectoryLog.Parse(File.ReadAllBytes(path), ignoreCrc));
                r.SourcePath = path;
                return r;
            }
            return FromCsv(path);
        }

        /// <summary>The same columns and values as the data CSV, at full float precision.</summary>
        public static DeliveryRecord FromLog(TrajectoryLog log)
        {
            var r = new DeliveryRecord { RowCount = log.NumSnapshots, AxisScale = log.AxisScale, Log = log };
            int n = log.NumSnapshots;
            int cpIdx = Array.IndexOf(log.AxisIds, TrajectoryLog.ControlPointAxisId);
            int timeIdx = log.TimeAxisIndex;
            bool[] hasData = CsvExporter.AxesWithData(log);

            var snapshot = new double[n];
            var elapsed = new double[n];
            double t0 = timeIdx >= 0 && n > 0 ? log.Expected(0, timeIdx, 0) : 0;
            for (int s = 0; s < n; s++)
            {
                snapshot[s] = s + 1;
                if (timeIdx >= 0)
                {
                    double el = log.Expected(s, timeIdx, 0) - t0;
                    if (el < -43200) el += 86400; // treatment crossed midnight
                    elapsed[s] = Math.Round(el, 3);
                }
                else elapsed[s] = Math.Round(s * log.SamplingIntervalMs / 1000.0, 3);
            }
            r.Add("Snapshot", snapshot);
            r.Add("ElapsedTime_s", elapsed);
            if (log.Subbeams.Count > 0 && cpIdx >= 0)
            {
                var sub = new double[n];
                for (int s = 0; s < n; s++) sub[s] = CsvExporter.SubbeamFor(log, log.Expected(s, cpIdx, 0));
                r.Add("Subbeam", sub);
            }

            r.ServoState = Enumerable.Repeat("", n).ToArray();
            for (int a = 0; a < log.NumAxes; a++)
            {
                int id = log.AxisIds[a];
                if (id == TrajectoryLog.TimeAxisId) continue;
                if (id == TrajectoryLog.ControlPointAxisId)
                {
                    var cp = new double[n];
                    for (int s = 0; s < n; s++) cp[s] = log.Expected(s, a, 0);
                    r.Add("ControlPoint", cp);
                    continue;
                }
                if (id != TrajectoryLog.MlcAxisId && !hasData[a]) continue;
                int samples = log.SamplesPerAxis[a];
                for (int k = 0; k < samples; k++)
                {
                    string name;
                    if (id == TrajectoryLog.MlcAxisId) name = CsvExporter.MlcColumn(k, samples) + "_cm";
                    else
                    {
                        string unit = TrajectoryLog.AxisUnit(id);
                        name = TrajectoryLog.AxisName(id) + (unit.Length > 0 && unit != "MU" ? "_" + unit : "");
                        if (samples > 1) name += "_" + (k + 1);
                    }
                    var e = new double[n];
                    var act = new double[n];
                    for (int s = 0; s < n; s++)
                    {
                        e[s] = Value(log.Expected(s, a, k));
                        act[s] = Value(log.Actual(s, a, k));
                    }
                    r.Add(name + "_Expected", e);
                    r.Add(name + "_Actual", act);
                }
                if (id == TrajectoryLog.BeamHoldAxisId)
                    for (int s = 0; s < n; s++) r.ServoState[s] = TrajectoryLog.DoseServoName(log.Actual(s, a, 0));
            }
            return r;
        }

        static double Value(float v)
        {
            if (float.IsNaN(v) || Math.Abs(v) >= CsvExporter.NoValue) return double.NaN;
            return v;
        }

        /// <summary>A data CSV written by to-csv (or the old converter / GUI).</summary>
        public static DeliveryRecord FromCsv(string path)
        {
            var r = new DeliveryRecord { SourcePath = path };
            var servo = new List<string>();
            var rows = new List<double[]>();
            List<string> header;
            int servoCol = -1;
            using (var sr = new StreamReader(path, Encoding.UTF8, true))
            {
                string headerLine = sr.ReadLine();
                if (headerLine == null) throw new InvalidDataException("Log file is empty.");
                if (headerLine.TrimStart('﻿').Trim().StartsWith("#"))
                    throw new InvalidDataException("'" + Path.GetFileName(path) + "' looks like an Eclipse MLC export, not a trajectory log. " +
                        "Give the delivery log (.bin or its CSV export) and use the Eclipse export as the template.");
                if (headerLine.StartsWith("TRAJECTORY LOG FULL DUMP", StringComparison.Ordinal))
                    throw new InvalidDataException("'" + Path.GetFileName(path) + "' is a full dump (_full.csv); use the .bin log or its data CSV.");
                header = headerLine.Split(',').Select(s => s.Trim().Trim('"')).ToList();
                servoCol = header.FindIndex(h => string.Equals(h, ServoColumn, StringComparison.OrdinalIgnoreCase));

                string line;
                while ((line = sr.ReadLine()) != null)
                {
                    if (line.Trim().Length == 0) continue;
                    string[] f = line.Split(',');
                    var v = new double[header.Count];
                    for (int i = 0; i < v.Length; i++)
                    {
                        double d;
                        v[i] = i < f.Length && i != servoCol &&
                               double.TryParse(f[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out d) ? d : double.NaN;
                    }
                    rows.Add(v);
                    servo.Add(servoCol >= 0 && servoCol < f.Length ? f[servoCol].Trim() : "");
                }
            }
            r.RowCount = rows.Count;
            r.ServoState = servo.ToArray();
            for (int i = 0; i < header.Count; i++)
            {
                if (i == servoCol || r.Has(header[i])) continue;
                var c = new double[rows.Count];
                for (int s = 0; s < rows.Count; s++) c[s] = rows[s][i];
                r.Add(header[i], c);
            }
            return r;
        }

        void Add(string name, double[] values)
        {
            _names.Add(name);
            _cols[name] = values;
        }

        /// <summary>A record holding only the given rows (in the given order).</summary>
        public DeliveryRecord Subset(IList<int> rows)
        {
            var r = new DeliveryRecord { SourcePath = SourcePath, AxisScale = AxisScale, Log = Log, RowCount = rows.Count };
            r.ServoState = rows.Select(i => ServoState[i]).ToArray();
            foreach (string name in _names)
            {
                double[] src = _cols[name];
                r.Add(name, rows.Select(i => src[i]).ToArray());
            }
            return r;
        }

        /// <summary>A copy with one column replaced (same length).</summary>
        public DeliveryRecord WithColumn(string name, double[] values)
        {
            var r = Subset(Enumerable.Range(0, RowCount).ToList());
            if (!r._cols.ContainsKey(name)) r._names.Add(name);
            r._cols[name] = values;
            return r;
        }
    }
}
