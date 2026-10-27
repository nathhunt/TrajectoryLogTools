// RtPlan.cs - what the log-vs-plan comparison needs from a DICOM RT Plan: the plan's identity,
// its beams and, per control point, the cumulative meterset weight, gantry, collimator and MLC
// leaf positions (read with fo-dicom).
//
//   * Beams come from the RT Beams module (Beam Sequence), each beam's MU from the first fraction
//     group (Referenced Beam Sequence > Beam Meterset). DICOM PS3.3 C.8.8.13 and C.8.8.14.
//   * A control point only holds what changed since the one before, so every value carries
//     forward until it is given again.
//   * Angles and positions are IEC 61217 (degrees, mm). Leaf / Jaw Positions hold the N leaves of
//     the X1 (negative) side, then the N leaves of the X2 side.
//   * A dual-layer MLC is two devices, e.g. Halcyon / Ethos: MLCX1 (28 pairs, distal) and MLCX2
//     (29 pairs, proximal). They are put together in the pair order MlcGeometry uses (distal then
//     proximal, as Eclipse lists them), checked by the number of pairs in each layer. Verified
//     against an Eclipse plan and the Halcyon logs of its delivery.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FellowOakDicom;
using TrajectoryLogTools.Core;

namespace TrajectoryLogTools.Analysis
{
    public sealed class RtBeamDevice
    {
        /// <summary>RT Beam Limiting Device Type, e.g. "MLCX1", "X", "ASYMY".</summary>
        public string Type;
        public int Pairs;
        public bool IsMlc { get { return Type.StartsWith("MLC", StringComparison.OrdinalIgnoreCase); } }
    }

    public sealed class RtControlPoint
    {
        public int Index;
        /// <summary>Cumulative meterset weight, as stored (0 .. the beam's final weight).</summary>
        public double Weight;
        /// <summary>Cumulative MU at this control point: the beam MU times weight / final weight (NaN if the beam has no MU).</summary>
        public double Mu;
        public double GantryIec, CollimatorIec, CouchIec;
        /// <summary>CW, CC or NONE.</summary>
        public string GantryDirection = "";
        /// <summary>Positions per device type (IEC 61217 mm): X1 side then X2 side.</summary>
        public Dictionary<string, double[]> Positions = new Dictionary<string, double[]>(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class RtBeam
    {
        public int Number;
        public string Name = "", Description = "";
        /// <summary>STATIC or DYNAMIC.</summary>
        public string Type = "";
        /// <summary>TREATMENT, SETUP, ...</summary>
        public string DeliveryType = "";
        public string MachineName = "";
        /// <summary>Beam MU from the fraction group; NaN when the plan gives none.</summary>
        public double Mu = double.NaN;
        public double FinalWeight = 1.0;
        public List<RtBeamDevice> Devices = new List<RtBeamDevice>();
        public List<RtControlPoint> ControlPoints = new List<RtControlPoint>();

        public bool IsTreatment
        {
            get
            {
                return string.Equals(DeliveryType, "TREATMENT", StringComparison.OrdinalIgnoreCase) && Devices.Any(d => d.IsMlc)
                       && !double.IsNaN(Mu) && Mu > 0;
            }
        }

        public string Label { get { return string.IsNullOrWhiteSpace(Name) ? "Beam " + Number : Name; } }

        /// <summary>The MLC devices in the pair order of <paramref name="geom"/> (by the number of pairs per layer), or an error.</summary>
        public List<RtBeamDevice> MlcLayers(MlcGeometry geom, out string error)
        {
            error = null;
            List<RtBeamDevice> mlc = Devices.Where(d => d.IsMlc).OrderBy(d => d.Type, StringComparer.OrdinalIgnoreCase).ToList();
            if (mlc.Count == 0) { error = "The beam has no MLC."; return null; }
            // pairs per layer of the geometry, in its pair order: e.g. SX2 28 distal + 29 proximal
            var layers = new List<int>();
            string last = null;
            for (int p = 0; p < geom.PairCount; p++)
            {
                string l = geom.LayerOfPair(p);
                if (l != last) { layers.Add(0); last = l; }
                layers[layers.Count - 1]++;
            }
            Func<List<RtBeamDevice>, bool> fits = list => list.Select(d => d.Pairs).SequenceEqual(layers);
            if (fits(mlc)) return mlc;
            var reversed = Enumerable.Reverse(mlc).ToList();
            if (fits(reversed)) return reversed;
            error = "The plan's MLC (" + string.Join(" + ", mlc.Select(d => d.Type + " " + d.Pairs + " pairs")) +
                    ") is not the log's " + geom.Description + ".";
            return null;
        }

        /// <summary>
        /// Leaf positions per control point, IEC 61217 mm, in Eclipse value order (X1, X2 of pair 1,
        /// then pair 2, ...), pairs in the order of <paramref name="geom"/>: [control point, value].
        /// </summary>
        public double[,] Leaves(MlcGeometry geom)
        {
            string error;
            List<RtBeamDevice> layers = MlcLayers(geom, out error);
            if (layers == null) throw new InvalidDataException(error);
            var v = new double[ControlPoints.Count, geom.LeafCount];
            for (int c = 0; c < ControlPoints.Count; c++)
            {
                int pair = 0;
                foreach (RtBeamDevice d in layers)
                {
                    double[] pos;
                    if (!ControlPoints[c].Positions.TryGetValue(d.Type, out pos) || pos.Length != 2 * d.Pairs)
                        throw new InvalidDataException("Control point " + c + " of " + Label + " has no " + d.Type + " leaf positions.");
                    for (int i = 0; i < d.Pairs; i++)
                    {
                        v[c, 2 * (pair + i)] = pos[i];               // X1 side
                        v[c, 2 * (pair + i) + 1] = pos[d.Pairs + i];  // X2 side
                    }
                    pair += d.Pairs;
                }
            }
            return v;
        }
    }

    public sealed class RtPlan
    {
        public const string RtPlanStorage = "1.2.840.10008.5.1.4.1.1.481.5";
        public const string RtIonPlanStorage = "1.2.840.10008.5.1.4.1.1.481.8";

        public string Path;
        public string Label = "", Name = "", PatientId = "", SopInstanceUid = "", ApprovalStatus = "";
        public DateTime? Date;
        public List<RtBeam> Beams = new List<RtBeam>();

        public string DisplayName { get { return !string.IsNullOrWhiteSpace(Label) ? Label : !string.IsNullOrWhiteSpace(Name) ? Name : System.IO.Path.GetFileName(Path); } }
        public IEnumerable<RtBeam> TreatmentBeams { get { return Beams.Where(b => b.IsTreatment); } }

        /// <summary>True when the file looks like DICOM: "DICM" after the 128-byte preamble, or a .dcm name.</summary>
        public static bool IsDicom(string path)
        {
            try
            {
                if (string.Equals(System.IO.Path.GetExtension(path), ".dcm", StringComparison.OrdinalIgnoreCase)) return true;
                using (var f = File.OpenRead(path))
                {
                    if (f.Length < 132) return false;
                    var b = new byte[132];
                    int n = 0;
                    while (n < 132) { int r = f.Read(b, n, 132 - n); if (r <= 0) return false; n += r; }
                    return b[128] == 'D' && b[129] == 'I' && b[130] == 'C' && b[131] == 'M';
                }
            }
            catch (Exception) { return false; }
        }

        public static RtPlan Load(string path)
        {
            DicomFile file;
            try { file = DicomFile.Open(path, FileReadOption.ReadAll); }
            catch (Exception ex) { throw new InvalidDataException("Not a readable DICOM file: " + ex.Message, ex); }
            return From(file.Dataset, path);
        }

        public static RtPlan From(DicomDataset ds, string path)
        {
            // The standard RT Plan class, or (as ARIA exports Halcyon plans) a Varian private class
            // with modality RTPLAN and the same modules.
            string sopClass = Str(ds, DicomTag.SOPClassUID), modality = Str(ds, DicomTag.Modality);
            if (sopClass == RtIonPlanStorage || ds.Contains(DicomTag.IonBeamSequence))
                throw new InvalidDataException("This is an RT Ion Plan (protons or ions); only photon RT Plans are supported.");
            if (sopClass != RtPlanStorage && !string.Equals(modality, "RTPLAN", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("This DICOM file is not an RT Plan" + (modality.Length > 0 ? " (it is " + modality + ")" : "") + ".");
            var plan = new RtPlan
            {
                Path = path,
                Label = Str(ds, DicomTag.RTPlanLabel),
                Name = Str(ds, DicomTag.RTPlanName),
                PatientId = Str(ds, DicomTag.PatientID),
                SopInstanceUid = Str(ds, DicomTag.SOPInstanceUID),
                ApprovalStatus = Str(ds, DicomTag.ApprovalStatus),
            };
            DateTime d;
            if (DateTime.TryParseExact(Str(ds, DicomTag.RTPlanDate), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out d)) plan.Date = d;

            // beam MU from the first fraction group
            var mu = new Dictionary<int, double>();
            DicomSequence fgs;
            if (ds.TryGetSequence(DicomTag.FractionGroupSequence, out fgs) && fgs.Items.Count > 0)
            {
                DicomSequence refs;
                if (fgs.Items[0].TryGetSequence(DicomTag.ReferencedBeamSequence, out refs))
                    foreach (DicomDataset r in refs.Items)
                    {
                        int n = Int(r, DicomTag.ReferencedBeamNumber, -1);
                        double m = Num(r, DicomTag.BeamMeterset, double.NaN);
                        if (n >= 0) mu[n] = m;
                    }
            }

            DicomSequence beams;
            if (!ds.TryGetSequence(DicomTag.BeamSequence, out beams) || beams.Items.Count == 0)
                throw new InvalidDataException("The RT Plan has no beams.");
            foreach (DicomDataset b in beams.Items) plan.Beams.Add(ReadBeam(b, mu));
            return plan;
        }

        static RtBeam ReadBeam(DicomDataset b, Dictionary<int, double> mu)
        {
            var beam = new RtBeam
            {
                Number = Int(b, DicomTag.BeamNumber, 0),
                Name = Str(b, DicomTag.BeamName),
                Description = Str(b, DicomTag.BeamDescription),
                Type = Str(b, DicomTag.BeamType),
                DeliveryType = Str(b, DicomTag.TreatmentDeliveryType),
                MachineName = Str(b, DicomTag.TreatmentMachineName),
                FinalWeight = Num(b, DicomTag.FinalCumulativeMetersetWeight, 1.0),
            };
            if (beam.DeliveryType.Length == 0) beam.DeliveryType = "TREATMENT";   // the attribute's defined default
            double m;
            if (mu.TryGetValue(beam.Number, out m)) beam.Mu = m;
            if (!(beam.FinalWeight > 0)) beam.FinalWeight = 1.0;

            DicomSequence devs;
            if (b.TryGetSequence(DicomTag.BeamLimitingDeviceSequence, out devs))
                foreach (DicomDataset dv in devs.Items)
                    beam.Devices.Add(new RtBeamDevice { Type = Str(dv, DicomTag.RTBeamLimitingDeviceType), Pairs = Int(dv, DicomTag.NumberOfLeafJawPairs, 1) });

            DicomSequence cps;
            if (!b.TryGetSequence(DicomTag.ControlPointSequence, out cps)) return beam;
            RtControlPoint prev = null;
            foreach (DicomDataset c in cps.Items)
            {
                var cp = new RtControlPoint
                {
                    Index = Int(c, DicomTag.ControlPointIndex, beam.ControlPoints.Count),
                    Weight = Num(c, DicomTag.CumulativeMetersetWeight, prev == null ? 0 : prev.Weight),
                    GantryIec = Num(c, DicomTag.GantryAngle, prev == null ? double.NaN : prev.GantryIec),
                    CollimatorIec = Num(c, DicomTag.BeamLimitingDeviceAngle, prev == null ? double.NaN : prev.CollimatorIec),
                    CouchIec = Num(c, DicomTag.PatientSupportAngle, prev == null ? double.NaN : prev.CouchIec),
                    GantryDirection = c.Contains(DicomTag.GantryRotationDirection) ? Str(c, DicomTag.GantryRotationDirection) : prev == null ? "" : prev.GantryDirection,
                };
                if (prev != null) foreach (var kv in prev.Positions) cp.Positions[kv.Key] = kv.Value;
                DicomSequence pos;
                if (c.TryGetSequence(DicomTag.BeamLimitingDevicePositionSequence, out pos))
                    foreach (DicomDataset p in pos.Items)
                    {
                        string type = Str(p, DicomTag.RTBeamLimitingDeviceType);
                        if (type.Length > 0 && p.Contains(DicomTag.LeafJawPositions)) cp.Positions[type] = p.GetValues<double>(DicomTag.LeafJawPositions);
                    }
                cp.Mu = double.IsNaN(beam.Mu) ? double.NaN : beam.Mu * cp.Weight / beam.FinalWeight;
                beam.ControlPoints.Add(cp);
                prev = cp;
            }
            return beam;
        }

        static string Str(DicomDataset ds, DicomTag tag)
        {
            try { return (ds.GetSingleValueOrDefault<string>(tag, "") ?? "").Trim(); }
            catch (Exception) { return ""; }
        }

        static int Int(DicomDataset ds, DicomTag tag, int fallback)
        {
            string s = Str(ds, tag);
            int v;
            return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }

        static double Num(DicomDataset ds, DicomTag tag, double fallback)
        {
            string s = Str(ds, tag);
            double v;
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v) ? v : fallback;
        }
    }
}
