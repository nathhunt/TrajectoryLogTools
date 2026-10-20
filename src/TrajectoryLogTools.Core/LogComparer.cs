// LogComparer.cs - compares two logs value by value (validation / testing).

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
    // Compare two logs (validation / testing)
    // =============================================================================================
    public sealed class CompareResult
    {
        public bool ByteIdentical;
        public long DifferenceCount;                          // header/subbeam fields + individual values
        public List<string> Differences = new List<string>();
        public List<string> Info = new List<string>();        // differences that were ignored, etc.
        public bool Same { get { return DifferenceCount == 0; } }
    }

    public static class LogComparer
    {
        /// <summary>
        /// Compares header fields, subbeams and every expected / actual snapshot value. Axes are matched
        /// by axis id, so a v4.0 and a v5.1 log can be compared; with <paramref name="ignoreVersion"/>
        /// the version string, the time axis and the machine specifier / serial number are not compared.
        /// Values count as equal when within <paramref name="tolerance"/> (0 = exactly equal).
        /// </summary>
        public static CompareResult Compare(TrajectoryLog a, TrajectoryLog b, double tolerance = 0,
                                            bool ignoreVersion = false, int maxValueDiffs = 10)
        {
            var r = new CompareResult();
            if (a.Raw.Length == b.Raw.Length && a.Raw.SequenceEqual(b.Raw)) { r.ByteIdentical = true; return r; }

            Action<string, object, object> field = (name, x, y) =>
            {
                if (Equals(x, y)) return;
                r.DifferenceCount++;
                r.Differences.Add(name + ": " + x + " vs " + y);
            };

            // ---- header ----
            if (!ignoreVersion) field("Version", a.Version, b.Version);
            else if (a.Version != b.Version) r.Info.Add("version " + a.Version + " vs " + b.Version + " ignored");
            field("Sampling interval (ms)", a.SamplingIntervalMs, b.SamplingIntervalMs);
            field("Axis scale", a.AxisScale, b.AxisScale);
            field("Number of subbeams", a.NumSubbeamsField, b.NumSubbeamsField);
            field("Truncated", a.Truncated, b.Truncated);
            field("Number of snapshots", a.NumSnapshots, b.NumSnapshots);
            field("MLC model", a.MlcModel, b.MlcModel);
            field("Metadata", Quote(a.MetaDataText), Quote(b.MetaDataText));
            if (!ignoreVersion)
            {
                field("Machine specifier", a.MachineSpecifier, b.MachineSpecifier);
                field("Machine serial", Quote(a.MachineSerial), Quote(b.MachineSerial));
            }

            Func<TrajectoryLog, int[]> axes = l => l.AxisIds.Where(id => !(ignoreVersion && id == TrajectoryLog.TimeAxisId)).ToArray();
            int[] axesA = axes(a), axesB = axes(b);
            // v4.0 and v5.1 logs store the axes in a different order, so with ignoreVersion only the
            // set of axes has to match.
            if (ignoreVersion && axesA.Length == axesB.Length && !axesA.Except(axesB).Any() && !axesB.Except(axesA).Any())
            {
                if (!axesA.SequenceEqual(axesB)) r.Info.Add("axis order differs, ignored");
            }
            else if (!axesA.SequenceEqual(axesB))
                field("Axes", AxisList(axesA), AxisList(axesB));
            if (ignoreVersion && (a.TimeAxisIndex >= 0) != (b.TimeAxisIndex >= 0))
                r.Info.Add("time axis only in " + (a.TimeAxisIndex >= 0 ? "A" : "B") + ", ignored");

            // ---- subbeams ----
            for (int k = 0; k < Math.Min(a.Subbeams.Count, b.Subbeams.Count); k++)
            {
                Subbeam x = a.Subbeams[k], y = b.Subbeams[k];
                string p = "Subbeam " + (k + 1) + " ";
                field(p + "control point", x.ControlPoint, y.ControlPoint);
                if (!Same(x.MU, y.MU, tolerance)) field(p + "MU", Fmt.Exact(x.MU), Fmt.Exact(y.MU));
                if (!Same(x.RadTime, y.RadTime, tolerance)) field(p + "radiation time", Fmt.Exact(x.RadTime), Fmt.Exact(y.RadTime));
                field(p + "sequence", x.Seq, y.Seq);
                field(p + "name", Quote(x.Name), Quote(y.Name));
            }

            // ---- snapshots (axes matched by id) ----
            int snaps = Math.Min(a.NumSnapshots, b.NumSnapshots);
            int listed = 0;
            foreach (int id in axesA.Distinct())
            {
                int ia = Array.IndexOf(a.AxisIds, id), ib = Array.IndexOf(b.AxisIds, id);
                if (ib < 0) continue; // already reported in the axis list
                string axis = TrajectoryLog.AxisName(id);
                int n = a.SamplesPerAxis[ia];
                if (n != b.SamplesPerAxis[ib])
                {
                    field(axis + " samples per snapshot", n, b.SamplesPerAxis[ib]);
                    continue;
                }

                long count = 0;
                var details = new List<string>();
                var diffSnapshots = new HashSet<int>();
                double maxDiff = 0;
                for (int s = 0; s < snaps; s++)
                    for (int k = 0; k < n; k++)
                        for (int act = 0; act < 2; act++)
                        {
                            float x = act == 0 ? a.Expected(s, ia, k) : a.Actual(s, ia, k);
                            float y = act == 0 ? b.Expected(s, ib, k) : b.Actual(s, ib, k);
                            if (Same(x, y, tolerance)) continue;
                            count++;
                            diffSnapshots.Add(s);
                            double d = Math.Abs((double)x - y);
                            if (!double.IsNaN(d) && d > maxDiff) maxDiff = d;
                            if (listed < maxValueDiffs)
                            {
                                listed++;
                                details.Add(string.Format(CultureInfo.InvariantCulture, "  snapshot {0}, {1} {2}: {3} vs {4}",
                                    s + 1, SampleName(id, k, n), act == 0 ? "expected" : "actual", Fmt.Exact(x), Fmt.Exact(y)));
                            }
                        }
                if (count == 0) continue;
                r.DifferenceCount += count;
                r.Differences.Add(string.Format(CultureInfo.InvariantCulture,
                    "{0}: {1:N0} value(s) differ in {2:N0} snapshot(s), max |difference| {3}",
                    axis, count, diffSnapshots.Count, maxDiff.ToString("G6", CultureInfo.InvariantCulture)));
                r.Differences.AddRange(details);
            }
            if (r.Differences.Count > 0 && listed == maxValueDiffs && maxValueDiffs > 0)
                r.Info.Add("value differences listed up to --max-diffs " + maxValueDiffs);
            if (r.Same && a.CrcVariant != b.CrcVariant)
                r.Info.Add("CRC variant " + a.CrcVariant.Name + " vs " + b.CrcVariant.Name);
            return r;
        }

        /// <summary>Equal bit pattern, both NaN, or within the tolerance.</summary>
        static bool Same(float x, float y, double tolerance)
        {
            if (x == y || (float.IsNaN(x) && float.IsNaN(y))) return true;
            return tolerance > 0 && Math.Abs((double)x - y) <= tolerance;
        }

        static string SampleName(int axisId, int k, int total)
        {
            if (axisId == 50) return CsvExporter.MlcColumn(k, total);
            return TrajectoryLog.AxisName(axisId) + (total > 1 ? "_" + (k + 1) : "");
        }

        static string AxisList(int[] ids) { return "[" + string.Join(", ", ids.Select(TrajectoryLog.AxisName)) + "]"; }

        static string Quote(string s) { return "'" + s + "'"; }
    }
}
