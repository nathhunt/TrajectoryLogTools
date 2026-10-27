using System;
using System.Globalization;
using System.Linq;
using FellowOakDicom;

namespace TrajectoryLogTools.Tests
{
    /// <summary>
    /// The DICOM RT Plan of a simulated plan, laid out as Eclipse exports a Halcyon plan: a kV
    /// set-up beam, then one beam per field with devices X, Y, MLCX1 (28 distal pairs) and MLCX2
    /// (29 proximal pairs), leaf positions X1 side then X2 side, IEC 61217 mm and degrees. Later
    /// control points leave out what doesn't change (collimator, jaws, energy), as Eclipse does.
    /// </summary>
    public static class SimRtPlan
    {
        public const string Uid = "1.2.826.0.1.3680043.10.999.1";

        public static DicomDataset Build(SimPlan plan, string patientId = "SIM001", string uid = Uid, Action<int, int, double[]> editLeaves = null)
        {
            var ds = new DicomDataset().NotValidated();
            ds.Add(DicomTag.SOPClassUID, "1.2.840.10008.5.1.4.1.1.481.5");
            ds.Add(DicomTag.SOPInstanceUID, uid);
            ds.Add(DicomTag.Modality, "RTPLAN");
            ds.Add(DicomTag.PatientID, patientId);
            ds.Add(DicomTag.RTPlanLabel, "Simulated");
            ds.Add(DicomTag.RTPlanName, "Simulated");
            ds.Add(DicomTag.ApprovalStatus, "APPROVED");

            var refs = new DicomSequence(DicomTag.ReferencedBeamSequence);
            refs.Items.Add(Item(DicomTag.ReferencedBeamNumber, "1"));
            var beams = new DicomSequence(DicomTag.BeamSequence);
            beams.Items.Add(SetupBeam());
            for (int f = 0; f < plan.Fields.Count; f++)
            {
                SimPlan.Field fl = plan.Fields[f];
                refs.Items.Add(Item(DicomTag.ReferencedBeamNumber, (f + 2).ToString(CultureInfo.InvariantCulture), DicomTag.BeamMeterset, D(fl.Mu)));
                beams.Items.Add(Beam(fl, f, editLeaves));
            }
            var fg = new DicomDataset().NotValidated();
            fg.Add(DicomTag.FractionGroupNumber, "1");
            fg.Add(refs);
            ds.Add(new DicomSequence(DicomTag.FractionGroupSequence, fg));
            ds.Add(beams);
            return ds;
        }

        public static string Save(DicomDataset ds, string path)
        {
            new DicomFile(ds).Save(path);
            return path;
        }

        static DicomDataset SetupBeam()
        {
            var b = new DicomDataset().NotValidated();
            b.Add(DicomTag.BeamNumber, "1");
            b.Add(DicomTag.BeamName, "kVCBCT");
            b.Add(DicomTag.BeamType, "STATIC");
            b.Add(DicomTag.TreatmentDeliveryType, "SETUP");
            b.Add(DicomTag.NumberOfControlPoints, "2");
            b.Add(new DicomSequence(DicomTag.BeamLimitingDeviceSequence, Device("X", 1), Device("Y", 1)));
            var c0 = Item(DicomTag.ControlPointIndex, "0", DicomTag.CumulativeMetersetWeight, "0", DicomTag.GantryAngle, "0");
            var c1 = Item(DicomTag.ControlPointIndex, "1", DicomTag.CumulativeMetersetWeight, "1");
            b.Add(new DicomSequence(DicomTag.ControlPointSequence, c0, c1));
            return b;
        }

        static DicomDataset Beam(SimPlan.Field fl, int f, Action<int, int, double[]> editLeaves)
        {
            const int distal = 28, proximal = SimPlan.Pairs - distal;
            var b = new DicomDataset().NotValidated();
            b.Add(DicomTag.BeamNumber, (f + 2).ToString(CultureInfo.InvariantCulture));
            b.Add(DicomTag.BeamName, fl.Name);
            b.Add(DicomTag.BeamType, fl.Gantry0 == fl.Gantry1 ? "STATIC" : "DYNAMIC");
            b.Add(DicomTag.TreatmentDeliveryType, "TREATMENT");
            b.Add(DicomTag.NumberOfControlPoints, fl.Cps.ToString(CultureInfo.InvariantCulture));
            b.Add(DicomTag.FinalCumulativeMetersetWeight, "1");
            b.Add(new DicomSequence(DicomTag.BeamLimitingDeviceSequence, Device("X", 1), Device("Y", 1), Device("MLCX1", distal), Device("MLCX2", proximal)));
            var cps = new DicomSequence(DicomTag.ControlPointSequence);
            for (int k = 0; k < fl.Cps; k++)
            {
                var c = new DicomDataset().NotValidated();
                c.Add(DicomTag.ControlPointIndex, k.ToString(CultureInfo.InvariantCulture));
                c.Add(DicomTag.CumulativeMetersetWeight, D(fl.Mw[k]));
                double g = fl.Gantry0 + (fl.Gantry1 - fl.Gantry0) * k / (fl.Cps - 1);
                c.Add(DicomTag.GantryAngle, D(Iec(180 - g)));
                if (k == 0)
                {
                    c.Add(DicomTag.GantryRotationDirection, fl.Gantry0 == fl.Gantry1 ? "NONE" : fl.Gantry1 < fl.Gantry0 ? "CW" : "CC");
                    c.Add(DicomTag.BeamLimitingDeviceAngle, D(Iec(180 - fl.Coll)));
                    c.Add(DicomTag.PatientSupportAngle, "0");
                }
                var pos = new DicomSequence(DicomTag.BeamLimitingDevicePositionSequence);
                if (k == 0)
                {
                    pos.Items.Add(Positions("X", new[] { -140.0, 140.0 }));
                    pos.Items.Add(Positions("Y", new[] { -140.0, 140.0 }));
                }
                foreach (var layer in new[] { Tuple.Create("MLCX1", 0, distal), Tuple.Create("MLCX2", distal, proximal) })
                {
                    var v = new double[2 * layer.Item3];
                    for (int i = 0; i < layer.Item3; i++)
                    {
                        v[i] = fl.X1[k, layer.Item2 + i];
                        v[layer.Item3 + i] = fl.X2[k, layer.Item2 + i];
                    }
                    if (editLeaves != null) editLeaves(f, k, v);
                    pos.Items.Add(Positions(layer.Item1, v));
                }
                c.Add(pos);
                cps.Items.Add(c);
            }
            b.Add(cps);
            return b;
        }

        /// <summary>A dataset of (tag, string value) pairs.</summary>
        static DicomDataset Item(params object[] tagsAndValues)
        {
            var d = new DicomDataset().NotValidated();
            for (int i = 0; i < tagsAndValues.Length; i += 2) d.Add((DicomTag)tagsAndValues[i], (string)tagsAndValues[i + 1]);
            return d;
        }

        static DicomDataset Device(string type, int pairs)
        {
            return Item(DicomTag.RTBeamLimitingDeviceType, type, DicomTag.NumberOfLeafJawPairs, pairs.ToString(CultureInfo.InvariantCulture));
        }

        static DicomDataset Positions(string type, double[] v)
        {
            var p = new DicomDataset().NotValidated();
            p.Add(DicomTag.RTBeamLimitingDeviceType, type);
            p.Add(DicomTag.LeafJawPositions, v.Select(D).ToArray());
            return p;
        }

        static string D(double v) { return Math.Round(v, 6).ToString("0.######", CultureInfo.InvariantCulture); }
        static double Iec(double a) { a %= 360; if (a < 0) a += 360; return a; }
    }
}
