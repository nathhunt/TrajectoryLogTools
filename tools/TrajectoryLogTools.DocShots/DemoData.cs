// DemoData.cs - the synthetic logs the screenshots show. Never patient data: every log is built by
// the test suite's DeliverySimulator / LogBuilder (the layout of the Varian specification), with
// obviously fake patients (PHANTOM-01 ...) and plans named "(demo)". The same files every run.
//
//   <root>\Halcyon logs\                 v5.1 logs, as copied from the machine
//       Prostate_VMAT_*.bin              PHANTOM-01, 2 arcs, fractions 1-4 (fraction 4 interrupted)
//       HN_IMRT_*.bin                    PHANTOM-02, 3 fields in one log
//       Pelvis_IMRT_G0/G120/G240_*.bin   PHANTOM-03, one log per field (for Rebuild)
//       Brain_VMAT_*.bin                 PHANTOM-04, CRC damaged
//       Breast_IMRT_*.bin                PHANTOM-05, a v4.0 log (the converter skips it)
//       Copied from machine\HN_IMRT_*.bin  a second copy of the H&N log (duplicate file name)
//   <root>\Halcyon logs v4\              v4.0 output: Prostate fraction 4 already converted
//   <root>\Eclipse exports\              Eclipse MLC exports of the pelvis fields (G240's doesn't fit)
//   <root>\Exports\                      where the Export screenshots write

using System;
using System.Collections.Generic;
using System.IO;
using TrajectoryLogTools.Core;
using TrajectoryLogTools.Tests;

namespace TrajectoryLogTools.DocShots
{
    public sealed class DemoData
    {
        /// <summary>The "today" of the screenshots: the day the converter and filters look at.</summary>
        public static readonly DateTime Today = new DateTime(2026, 10, 20);

        public string Root { get; }
        public string Logs { get { return Path.Combine(Root, "Halcyon logs"); } }
        public string LogsV4 { get { return Path.Combine(Root, "Halcyon logs v4"); } }
        public string EclipseExports { get { return Path.Combine(Root, "Eclipse exports"); } }
        public string Exports { get { return Path.Combine(Root, "Exports"); } }
        public string Copied { get { return Path.Combine(Logs, "Copied from machine"); } }

        // The logs, by role
        public string ProstateFx1, ProstateFx2, ProstateFx3, ProstateFx4, HeadNeck, Brain, Breast, HeadNeckCopy, ProstateFx4V4;
        public List<string> Pelvis = new List<string>();
        public List<string> PelvisExports = new List<string>();

        const string Marker = "DocShots demo data - safe to delete.txt";

        public DemoData(string root) { Root = Path.GetFullPath(root); }

        /// <summary>Writes the demo set from scratch. Refuses a folder it didn't make, so nothing else is deleted.</summary>
        public void Create()
        {
            if (Directory.Exists(Root))
            {
                if (!File.Exists(Path.Combine(Root, Marker)))
                    throw new InvalidOperationException("The folder " + Root + " exists and wasn't made by DocShots, so it is left alone. " +
                                                        "Remove it, or choose another with --root <folder>.");
                Directory.Delete(Root, true);
            }
            Directory.CreateDirectory(Root);
            File.WriteAllText(Path.Combine(Root, Marker), "Synthetic trajectory logs for the user guide's screenshots (tools/TrajectoryLogTools.DocShots).\r\n");
            foreach (string d in new[] { Logs, LogsV4, EclipseExports, Copied }) Directory.CreateDirectory(d);

            SimPlan vmat = SimPlan.Vmat();
            ProstateFx1 = Write(Logs, "Prostate_VMAT", new DateTime(2026, 10, 15, 8, 40, 12), vmat, Delivery(31, "PHANTOM-01", "Prostate VMAT (demo)"));
            ProstateFx2 = Write(Logs, "Prostate_VMAT", new DateTime(2026, 10, 16, 8, 41, 5), vmat, Delivery(32, "PHANTOM-01", "Prostate VMAT (demo)"));
            ProstateFx3 = Write(Logs, "Prostate_VMAT", new DateTime(2026, 10, 19, 8, 42, 10), vmat, Delivery(33, "PHANTOM-01", "Prostate VMAT (demo)"));
            SimDelivery fx4 = Delivery(34, "PHANTOM-01", "Prostate VMAT (demo)");
            fx4.LeafLagCp = 0.07;
            fx4.Interruptions.Add(Tuple.Create(0, 17.3, 60));   // 1.2 s interruption in Arc 1
            ProstateFx4 = Write(Logs, "Prostate_VMAT", Today.Add(new TimeSpan(8, 45, 30)), vmat, fx4);

            SimPlan imrt = SimPlan.Imrt();
            RenameFields(imrt, "G0", "G120", "G240");
            HeadNeck = Write(Logs, "HN_IMRT", Today.Add(new TimeSpan(9, 30, 5)), imrt, Delivery(41, "PHANTOM-02", "H&N IMRT (demo)"));

            // One log per field: the machine wrote a log for each.
            SimPlan pelvisAll = SimPlan.Imrt();
            RenameFields(pelvisAll, "G0", "G120", "G240");
            pelvisAll.Fields[0].Gantry0 = pelvisAll.Fields[0].Gantry1 = 180;   // Varian 180 = IEC 0
            for (int f = 0; f < 3; f++)
            {
                var one = new SimPlan();
                one.Fields.Add(pelvisAll.Fields[f]);
                string name = "Pelvis_IMRT_" + pelvisAll.Fields[f].Name;
                Pelvis.Add(Write(Logs, name, Today.Add(new TimeSpan(10, 15 + 2 * f, 20 * f)), one, Delivery(51 + f, "PHANTOM-03", "Pelvis IMRT (demo)")));
                // The Eclipse export of the field; G240's comes from a plan with the gantry 2 degrees off (Check first).
                SimPlan exportOf = one;
                if (f == 2)
                {
                    SimPlan other = SimPlan.Imrt();
                    other.Fields[2].Gantry0 = other.Fields[2].Gantry1 = 298;
                    exportOf = new SimPlan();
                    exportOf.Fields.Add(other.Fields[2]);
                }
                string export = Path.Combine(EclipseExports, "Pelvis IMRT (demo) - " + pelvisAll.Fields[f].Name + ".csv");
                File.WriteAllText(export, exportOf.EclipseExport(0), new System.Text.UTF8Encoding(true));
                PelvisExports.Add(export);
            }

            SimDelivery brain = Delivery(61, "PHANTOM-04", "Brain VMAT (demo)");
            Brain = Write(Logs, "Brain_VMAT", Today.Add(new TimeSpan(11, 5, 0)), vmat, brain);
            byte[] bytes = File.ReadAllBytes(Brain);
            bytes[bytes.Length - 1] ^= 0x5A;   // damaged in copying: the CRC no longer verifies
            File.WriteAllBytes(Brain, bytes);

            SimDelivery breast = Delivery(71, "PHANTOM-05", "Breast IMRT (demo)");
            breast.Version = "4.0";
            SimPlan tangents = SimPlan.Imrt();
            tangents.Fields.RemoveAt(2);
            RenameFields(tangents, "Med tangent", "Lat tangent");
            Breast = Write(Logs, "Breast_IMRT", Today.Add(new TimeSpan(13, 20, 0)), tangents, breast);

            HeadNeckCopy = Path.Combine(Copied, Path.GetFileName(HeadNeck));
            File.Copy(HeadNeck, HeadNeckCopy);

            // Fraction 4, converted earlier exactly as Trajectory Log Converter does it.
            var lines = new List<string>();
            V4BatchItem item = V4Batch.PlanOne(ProstateFx4, LogsV4, false);
            if (V4Batch.Convert(item, false, lines) != ExportOutcome.Done) throw new InvalidOperationException(string.Join("; ", lines));
            ProstateFx4V4 = item.OutputPath;
        }

        static SimDelivery Delivery(int seed, string patient, string plan)
        {
            return new SimDelivery
            {
                Seed = seed, SnapshotsPerCp = 5, Version = "5.1", HalcyonTransition = true, LeafLagCp = 0.05,
                Metadata = "Patient ID:\t" + patient + "\r\nPlan Name:\t" + plan + "\r\nPlan UID:\t1.2.3.999." + seed + "\r\n",
            };
        }

        static void RenameFields(SimPlan p, params string[] names)
        {
            for (int i = 0; i < names.Length; i++) p.Fields[i].Name = names[i];
        }

        /// <summary>Halcyon-style name: &lt;name&gt;_yyyyMMddHHmmss.bin; the time stamp is the treatment time the apps show.</summary>
        static string Write(string folder, string name, DateTime treated, SimPlan plan, SimDelivery d)
        {
            string path = Path.Combine(folder, name + "_" + treated.ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".bin");
            File.WriteAllBytes(path, DeliverySimulator.Build(plan, d));
            File.SetLastWriteTime(path, treated);
            return path;
        }
    }
}
