using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using TrajectoryLogTools.Core;

namespace TrajectoryLogTools.Tests
{
    /// <summary>
    /// The simulated deliveries the golden tests use. The golden files in Golden/ were made by
    /// running the original tools (reference/) on exactly these logs; see reference/README.md.
    /// </summary>
    public static class GoldenCases
    {
        public static readonly string[] Names = { "vmat_A", "vmat_B", "imrt_A", "imrt_B" };

        public static SimPlan Plan(string name) { return name.StartsWith("vmat") ? SimPlan.Vmat() : SimPlan.Imrt(); }

        public static SimDelivery Delivery(string name)
        {
            switch (name)
            {
                case "vmat_A":
                    return new SimDelivery { Seed = 1, SnapshotsPerCp = 5, Version = "5.1", StrayFirstSnapshot = true,
                                             Interruptions = { Tuple.Create(0, 17.3, 12) } };
                case "vmat_B":
                    return new SimDelivery { Seed = 2, SnapshotsPerCp = 6, Version = "5.1", LeafLagCp = 0.04,
                                             Interruptions = { Tuple.Create(1, 25.6, 20) } };
                case "imrt_A":
                    return new SimDelivery { Seed = 3, SnapshotsPerCp = 5, Version = "4.0" };
                case "imrt_B":
                    return new SimDelivery { Seed = 4, SnapshotsPerCp = 4, Version = "4.0", LeafLagCp = 0.07,
                                             Interruptions = { Tuple.Create(2, 7.5, 10) } };
            }
            throw new ArgumentException(name);
        }

        public static byte[] Bin(string name) { return DeliverySimulator.Build(Plan(name), Delivery(name)); }

        /// <summary>The data CSV (to-csv) of a case, as the original tools read it.</summary>
        public static string Csv(string name)
        {
            var log = TrajectoryLog.Parse(Bin(name), false);
            return CsvExporter.DataCsv(log, new Options { Command = Command.ToCsv });
        }

        /// <summary>Writes the .bin, .csv and Eclipse template files of every case to a folder.</summary>
        public static void WriteInputs(string folder)
        {
            Directory.CreateDirectory(folder);
            foreach (string n in Names)
            {
                File.WriteAllBytes(Path.Combine(folder, n + ".bin"), Bin(n));
                File.WriteAllText(Path.Combine(folder, n + ".csv"), Csv(n), new UTF8Encoding(false));
            }
            foreach (string p in new[] { "vmat", "imrt" })
            {
                SimPlan plan = Plan(p);
                for (int f = 0; f < plan.Fields.Count; f++)
                    File.WriteAllText(TemplatePath(folder, p, f + 1), plan.EclipseExport(f), new UTF8Encoding(true));
            }
        }

        public static string TemplatePath(string folder, string plan, int field)
        {
            return Path.Combine(folder, plan + "_field" + field + "_eclipse.csv");
        }

        public static string GoldenDir
        {
            get { return Path.Combine(AppContext.BaseDirectory, "Golden"); }
        }
    }
}
