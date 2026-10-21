// FieldSpan.cs - where a field (subbeam) starts and ends in the log's control-point counter.
//
// In a multi-field log the ControlPoint and MU counters run on across the fields, and each field
// after the first opens with a transition: the counter sits on the previous field's last control
// point (the subbeam header gives that value as the field's start) while the next field is set
// up - the collimator turns, the leaves move - with no planned MU. Halcyon logs show it as a step
// of one control point: for fields of 180 control points, subbeam 2's header says 179, the
// counter holds at 179 through the set-up and steps 179 -> 180 with the gantry still and no
// planned MU, and the field's own control point 0 is log control point 180.
//
// So a field starts at the control point where the PLANNED (expected) MU starts rising. The
// delivered (actual) MU is not used for this: it creeps up by ~0.001 MU during the set-up beam
// hold (dosimetry noise), which is enough to look like the start of delivery.

using System;
using System.Collections.Generic;
using System.Linq;

namespace TrajectoryLogTools.Analysis
{
    public static class FieldSpan
    {
        /// <summary>MU above the field's starting value that counts as delivery having started.</summary>
        public const double MuTolerance = 0.001;

        /// <summary>
        /// The field's first control point (an integer of the log's counter), from the snapshots of
        /// the field in order: the control point just below the first snapshot whose planned MU has
        /// risen. NaN when the planned MU never rises (or there is no planned MU).
        /// </summary>
        public static double FirstControlPoint(IReadOnlyList<double> cp, IReadOnlyList<double> muExpected)
        {
            if (cp.Count == 0 || muExpected == null || muExpected.Count != cp.Count) return double.NaN;
            double mu0 = muExpected.FirstOrDefault(v => !double.IsNaN(v));
            for (int i = 0; i < cp.Count; i++)
                if (!double.IsNaN(cp[i]) && !double.IsNaN(muExpected[i]) && muExpected[i] - mu0 > MuTolerance)
                    return Math.Floor(cp[i] + 1e-6);
            return double.NaN;
        }

        /// <summary>The field's control points in the log's counter: first, last (inclusive); found = false without data.</summary>
        public static bool Of(DeliveryRecord d, int subbeam, out int first, out int last)
        {
            first = last = 0;
            double[] sub = d.TryColumn("Subbeam"), cp = d.TryColumn("ControlPoint"), muE = d.TryColumn("MU_Expected");
            if (cp == null) return false;
            var rows = Enumerable.Range(0, d.RowCount)
                .Where(i => (sub == null || (!double.IsNaN(sub[i]) && (int)Math.Round(sub[i]) == subbeam)) && !double.IsNaN(cp[i])).ToList();
            var on = rows.Where(d.IsBeamOn).ToList();
            if (on.Count == 0) return false;
            double f = muE == null ? double.NaN : FirstControlPoint(rows.Select(i => cp[i]).ToList(), rows.Select(i => muE[i]).ToList());
            first = double.IsNaN(f) ? (int)Math.Floor(on.Min(i => cp[i]) + 1e-6) : (int)f;
            last = (int)Math.Round(on.Max(i => cp[i]));
            return last >= first;
        }

        /// <summary>The beam-on snapshots of the field from its first control point on (the transition left out).</summary>
        public static List<int> BeamOnRows(DeliveryRecord d, int subbeam)
        {
            int first, last;
            double[] sub = d.TryColumn("Subbeam"), cp = d.TryColumn("ControlPoint");
            bool found = Of(d, subbeam, out first, out last);
            return Enumerable.Range(0, d.RowCount)
                .Where(i => (sub == null || (!double.IsNaN(sub[i]) && (int)Math.Round(sub[i]) == subbeam)) && d.IsBeamOn(i)
                            && (!found || cp == null || cp[i] >= first - 1e-6)).ToList();
        }
    }
}
