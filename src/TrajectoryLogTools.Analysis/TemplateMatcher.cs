// TemplateMatcher.cs - pairs Eclipse MLC exports with the subbeams (fields) of a log, so the user
// does not have to give them in subbeam order.
//
// A template fits a subbeam when it has the same number of control points; among those, the one
// whose first gantry angle is closest to the subbeam's first beam-on gantry angle is chosen.

using System;
using System.Collections.Generic;
using System.Linq;
using TrajectoryLogTools.Core;

namespace TrajectoryLogTools.Analysis
{
    /// <summary>What the reconstruction will see of one subbeam.</summary>
    public sealed class SubbeamInfo
    {
        public int Subbeam;
        /// <summary>Control points the reconstruction will fit (as LogToEclipseMlc counts them).</summary>
        public int ControlPoints;
        /// <summary>Log control point of the field's control point 0.</summary>
        public int FirstLogControlPoint;
        /// <summary>Planned gantry at the first beam-on snapshot, IEC 61217 degrees (NaN if unknown).</summary>
        public double GantryIec = double.NaN;
        public double Mu;
    }

    public sealed class TemplateInfo
    {
        public string Path;
        public int ControlPoints;
        public double GantryIec;
        public string MlcModel;
        public string Error;
    }

    public static class TemplateMatcher
    {
        /// <summary>The subbeams of a log as the reconstruction will see them (beam-on snapshots only).</summary>
        public static List<SubbeamInfo> Describe(DeliveryRecord d, string scale = "auto")
        {
            var result = new List<SubbeamInfo>();
            if (!d.Has("ControlPoint") || !d.Has("MU_Actual")) return result;
            string sc = scale == "auto" ? (d.AxisScale == 2 ? "iec" : "varian") : scale;
            double[] sub = d.TryColumn("Subbeam"), cp = d.Column("ControlPoint"), muA = d.Column("MU_Actual");
            double[] muE = d.TryColumn("MU_Expected"), g = d.TryColumn("GantryRtn_deg_Expected");
            foreach (int sb in MlcReconstructor.SubbeamsIn(d))
            {
                var rows = Enumerable.Range(0, d.RowCount)
                    .Where(i => (sub == null || (!double.IsNaN(sub[i]) && (int)Math.Round(sub[i]) == sb)) && !double.IsNaN(cp[i]) && !double.IsNaN(muA[i]))
                    .ToList();
                if (rows.Count == 0) continue;
                double mu0 = muA[rows[0]];
                int nSetup = 0;
                while (nSetup < rows.Count && muA[rows[nSetup]] - mu0 < MlcReconstructor.SetupMuTol) nSetup++;
                var delivery = rows.Skip(nSetup).ToList();
                if (delivery.Count == 0) { result.Add(new SubbeamInfo { Subbeam = sb }); continue; }
                int cp0 = (int)Math.Floor(delivery.Min(i => cp[i]) + 1e-6);
                var on = delivery.Where(d.IsBeamOn).ToList();
                var info = new SubbeamInfo { Subbeam = sb, FirstLogControlPoint = cp0 };
                if (on.Count > 0)
                {
                    info.ControlPoints = (int)Math.Round(on.Max(i => cp[i]) - cp0) + 1;
                    if (g != null) info.GantryIec = sc == "varian" ? Wrap360(180.0 - g[on[0]]) : Wrap360(g[on[0]]);
                }
                if (muE != null) info.Mu = rows.Max(i => muE[i]) - muE[rows[0]];
                result.Add(info);
            }
            return result;
        }

        public static TemplateInfo Read(string path)
        {
            var t = new TemplateInfo { Path = path };
            try
            {
                var f = EclipseMlcFile.Load(path);
                t.ControlPoints = f.Cps.Count;
                t.GantryIec = f.Cps[0][5];
                t.MlcModel = f.MlcModel;
            }
            catch (Exception ex) { t.Error = ex.Message; }
            return t;
        }

        /// <summary>
        /// Assigns templates to subbeams: subbeam number -> template. Templates that fit no
        /// remaining subbeam are left out of the result.
        /// </summary>
        public static Dictionary<int, TemplateInfo> Match(IList<SubbeamInfo> subbeams, IEnumerable<TemplateInfo> templates)
        {
            var result = new Dictionary<int, TemplateInfo>();
            foreach (TemplateInfo t in templates.Where(t => t.Error == null))
            {
                SubbeamInfo best = subbeams
                    .Where(s => !result.ContainsKey(s.Subbeam) && s.ControlPoints == t.ControlPoints)
                    .OrderBy(s => double.IsNaN(s.GantryIec) ? 0 : Math.Abs(AngleDiff(s.GantryIec, t.GantryIec)))
                    .ThenBy(s => s.Subbeam)
                    .FirstOrDefault();
                if (best != null) result[best.Subbeam] = t;
            }
            return result;
        }

        static double Wrap360(double a) { a %= 360.0; if (a < 0) a += 360.0; return a; }

        static double AngleDiff(double a, double b)
        {
            double d = (a - b) % 360.0;
            if (d > 180) d -= 360; if (d < -180) d += 360;
            return d;
        }
    }
}
