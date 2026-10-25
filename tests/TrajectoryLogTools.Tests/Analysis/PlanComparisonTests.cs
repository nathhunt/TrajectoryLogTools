using System;
using System.IO;
using System.Linq;
using FellowOakDicom;
using TrajectoryLogTools.Analysis;
using TrajectoryLogTools.Cli;
using TrajectoryLogTools.Core;
using Xunit;

namespace TrajectoryLogTools.Tests
{
    public class PlanComparisonTests
    {
        static DeliveryRecord Log(SimPlan plan, SimDelivery d) { return DeliveryRecord.FromLog(LogBuilder.Parse(DeliverySimulator.Build(plan, d))); }

        static SimDelivery Delivery(string golden, string planUid = SimRtPlan.Uid)
        {
            SimDelivery d = GoldenCases.Delivery(golden);
            d.PlanUid = planUid;
            return d;
        }

        static RtPlan Plan(SimPlan plan, TempDir dir, string patientId = "SIM001", string uid = SimRtPlan.Uid, Action<int, int, double[]> edit = null)
        {
            return RtPlan.Load(SimRtPlan.Save(SimRtPlan.Build(plan, patientId, uid, edit), dir.File("RP.test.dcm")));
        }

        [Fact]
        public void RtPlan_ReadsBeamsControlPointsAndLeaves()
        {
            using (var dir = new TempDir())
            {
                SimPlan sim = SimPlan.Vmat();
                string path = SimRtPlan.Save(SimRtPlan.Build(sim), dir.File("plan_without_extension"));
                Assert.True(RtPlan.IsDicom(path));   // by content, not name
                RtPlan p = RtPlan.Load(path);
                Assert.Equal("SIM001", p.PatientId);
                Assert.Equal(SimRtPlan.Uid, p.SopInstanceUid);
                Assert.Equal(3, p.Beams.Count);
                Assert.Equal(new[] { "Arc 1", "Arc 2" }, p.TreatmentBeams.Select(b => b.Name).ToArray());   // the kV set-up beam is left out
                RtBeam b1 = p.Beams[1];
                Assert.Equal(sim.Fields[0].Mu, b1.Mu, 6);
                Assert.Equal(sim.Fields[0].Cps, b1.ControlPoints.Count);
                // collimator only on control point 0: carried forward
                Assert.All(b1.ControlPoints, c => Assert.Equal(15.0, c.CollimatorIec, 6));   // 180 - 165
                Assert.Equal(sim.Fields[0].Mu, b1.ControlPoints.Last().Mu, 6);
                double[,] leaves = b1.Leaves(MlcGeometry.Sx2);
                for (int k = 0; k < sim.Fields[0].Cps; k += 7)
                    for (int q = 0; q < SimPlan.Pairs; q++)
                    {
                        Assert.Equal(sim.Fields[0].X1[k, q], leaves[k, 2 * q], 6);
                        Assert.Equal(sim.Fields[0].X2[k, q], leaves[k, 2 * q + 1], 6);
                    }
            }
        }

        [Theory]
        [InlineData("vmat_A")]
        [InlineData("imrt_B")]
        public void SamePlan_MatchesAndMeasuresTheDelivery(string golden)
        {
            using (var dir = new TempDir())
            {
                SimPlan sim = GoldenCases.Plan(golden);
                PlanComparisonResult r = PlanComparison.Compare(Log(sim, Delivery(golden)), Plan(sim, dir), "log");
                Assert.True(r.SamePatient);
                Assert.True(r.SamePlanUid);
                Assert.Equal(sim.Fields.Count, r.Fields.Count);
                Assert.Empty(r.BeamsNotInLog);
                for (int i = 0; i < sim.Fields.Count; i++)
                {
                    PlanFieldComparison f = r.Fields[i];
                    Assert.True(f.Compared, f.Error);
                    Assert.Equal(sim.Fields[i].Name, f.Beam.Name);
                    Assert.Equal("name", f.MatchedBy);
                    Assert.False(f.Partial);
                    Assert.Equal(0, f.FirstPlanCp);
                    Assert.Equal(sim.Fields[i].Cps, f.ControlPoints);
                    Assert.True(f.TransferOk, f.Label + ": transfer " + f.TransferLeafMaxMm + " mm, " + f.TransferGantryMaxDeg + " deg, " + f.TransferMuMax + " MU");
                    Assert.True(f.TransferLeafMaxMm < 0.01);
                    // the simulated leaves lag and are noisy: the delivery differs from the plan, a little
                    Assert.True(f.Leaves.Max > 0.01 && f.Leaves.Max < 5, "leaves max " + f.Leaves.Max);
                    Assert.True(f.Leaves.Rms < f.Leaves.Max && f.Leaves.P95 <= f.Leaves.Max);
                    Assert.True(f.CpLeaves.Max > 0 && f.CpLeaves.Max < 5);
                    Assert.Equal(sim.Fields[i].Mu, f.MuPlan, 6);
                    Assert.InRange(f.MuDelivered, 0.99 * f.MuPlan, 1.01 * f.MuPlan);
                    Assert.True(f.Snapshots > 2 * f.ControlPoints);
                    Assert.Contains("pair ", f.Leaves.MaxAt);
                }
                Assert.True(r.TransferOk);
                string report = PlanComparison.Report(r);
                Assert.Contains("-> same plan", report);
                Assert.Contains("MATCHES", report);
                Assert.DoesNotContain("DIFFERS", report);
            }
        }

        [Fact]
        public void ChangedControlPoint_FailsTheTransferCheck()
        {
            using (var dir = new TempDir())
            {
                SimPlan sim = SimPlan.Vmat();
                // move one distal X2 leaf of Arc 2, control point 20, by 0.3 mm
                RtPlan plan = Plan(sim, dir, edit: (f, k, v) => { if (f == 1 && k == 20 && v.Length == 56) v[28 + 5] += 0.3; });
                PlanComparisonResult r = PlanComparison.Compare(Log(sim, Delivery("vmat_B")), plan);
                Assert.True(r.Fields[0].TransferOk);
                PlanFieldComparison f = r.Fields[1];
                Assert.False(f.TransferOk);
                Assert.InRange(f.TransferLeafMaxMm, 0.1, 0.3);
                Assert.Contains("pair 6 X2 (distal)", f.TransferLeafMaxAt);
                Assert.False(r.TransferOk);
                Assert.Contains("DIFFERS", PlanComparison.Report(r));
            }
        }

        [Fact]
        public void OtherPatientAndPlanUid_AreFlagged()
        {
            using (var dir = new TempDir())
            {
                SimPlan sim = SimPlan.Imrt();
                PlanComparisonResult r = PlanComparison.Compare(Log(sim, Delivery("imrt_A", "1.2.3.4")), Plan(sim, dir, "OTHER01"));
                Assert.False(r.SamePatient);
                Assert.False(r.SamePlanUid);
                string report = PlanComparison.Report(r);
                Assert.Contains("DIFFERENT PATIENTS", report);
                Assert.Contains("DIFFERENT PLAN", report);
                // the golden logs record no plan UID: not checked
                Assert.Null(PlanComparison.Compare(Log(sim, GoldenCases.Delivery("imrt_A")), Plan(sim, dir)).SamePlanUid);
            }
        }

        [Fact]
        public void WithoutNames_FieldsArePairedByTheirPlanValues()
        {
            using (var dir = new TempDir())
            {
                SimPlan sim = SimPlan.Imrt();   // three static fields with the same number of control points
                RtPlan plan = Plan(sim, dir);
                foreach (RtBeam b in plan.Beams) b.Name = "Beam " + b.Number;
                PlanComparisonResult r = PlanComparison.Compare(Log(sim, Delivery("imrt_B")), plan);
                for (int i = 0; i < 3; i++)
                {
                    Assert.Equal(i + 2, r.Fields[i].Beam.Number);
                    Assert.Equal("control points and plan values", r.Fields[i].MatchedBy);
                    Assert.True(r.Fields[i].TransferOk);
                }
            }
        }

        [Fact]
        public void LogOfPartOfABeam_IsAlignedOnThePlansControlPoints()
        {
            using (var dir = new TempDir())
            {
                SimPlan full = SimPlan.Vmat();
                SimPlan part = Slice(full, 0, 12, 30);
                PlanComparisonResult r = PlanComparison.Compare(Log(part, new SimDelivery { Seed = 5, PlanUid = SimRtPlan.Uid }), Plan(full, dir));
                PlanFieldComparison f = r.Fields.Single();
                Assert.True(f.Compared, f.Error);
                Assert.Equal("Arc 1", f.Beam.Name);
                Assert.Equal(12, f.FirstPlanCp);
                Assert.Equal(19, f.ControlPoints);
                Assert.True(f.Partial);
                Assert.True(f.TransferOk, "transfer " + f.TransferLeafMaxMm);
                Assert.Equal(full.Fields[0].Mu * (full.Fields[0].Mw[30] - full.Fields[0].Mw[12]), f.MuPlanCovered, 3);
                Assert.Equal(new[] { "Arc 2" }, r.BeamsNotInLog.Select(b => b.Name).ToArray());
                Assert.Contains("PARTIAL", PlanComparison.Report(r));
            }
        }

        /// <summary>Control points a..b of one field, as a plan of its own (as a log of a resumed delivery holds it).</summary>
        static SimPlan Slice(SimPlan p, int field, int a, int b)
        {
            SimPlan.Field s = p.Fields[field];
            int n = b - a + 1;
            double w0 = s.Mw[a], w1 = s.Mw[b];
            var f = new SimPlan.Field
            {
                Name = s.Name, Cps = n, Mu = s.Mu * (w1 - w0), Coll = s.Coll,
                Gantry0 = s.Gantry0 + (s.Gantry1 - s.Gantry0) * a / (s.Cps - 1),
                Gantry1 = s.Gantry0 + (s.Gantry1 - s.Gantry0) * b / (s.Cps - 1),
                Mw = Enumerable.Range(a, n).Select(k => (s.Mw[k] - w0) / (w1 - w0)).ToArray(),
                X1 = new double[n, SimPlan.Pairs], X2 = new double[n, SimPlan.Pairs],
            };
            for (int k = 0; k < n; k++)
                for (int q = 0; q < SimPlan.Pairs; q++) { f.X1[k, q] = s.X1[a + k, q]; f.X2[k, q] = s.X2[a + k, q]; }
            var r = new SimPlan();
            r.Fields.Add(f);
            return r;
        }

        [Fact]
        public void CsvExport_ComparesWithoutIdentity()
        {
            using (var dir = new TempDir())
            {
                SimPlan sim = SimPlan.Imrt();
                string csv = dir.File("imrt_A.csv");
                File.WriteAllText(csv, GoldenCases.Csv("imrt_A"));
                PlanComparisonResult r = PlanComparison.Compare(DeliveryRecord.Load(csv), Plan(sim, dir));
                Assert.Null(r.SamePatient);
                Assert.Null(r.SamePlanUid);
                Assert.All(r.Fields, f => Assert.True(f.Compared && f.TransferOk, f.Error));
            }
        }

        [Fact]
        public void NotAnRtPlan_IsRefused()
        {
            using (var dir = new TempDir())
            {
                var ds = new DicomDataset().NotValidated();
                ds.Add(DicomTag.SOPClassUID, "1.2.840.10008.5.1.4.1.1.2");
                ds.Add(DicomTag.SOPInstanceUID, "1.2.3");
                ds.Add(DicomTag.Modality, "CT");
                string path = SimRtPlan.Save(ds, dir.File("ct.dcm"));
                var ex = Assert.Throws<InvalidDataException>(() => RtPlan.Load(path));
                Assert.Contains("not an RT Plan (it is CT)", ex.Message);
                Assert.Throws<InvalidDataException>(() => RtPlan.Load(dir.Write("junk.dcm", new byte[300])));
                Assert.False(RtPlan.IsDicom(dir.Write("log.bin", GoldenCases.Bin("imrt_A"))));
            }
        }

        [Fact]
        public void Cli_ComparePlan_PrintsAndWrites()
        {
            using (var dir = new TempDir())
            {
                SimPlan sim = SimPlan.Vmat();
                string log = dir.Write("vmat.bin", DeliverySimulator.Build(sim, Delivery("vmat_A")));
                string plan = SimRtPlan.Save(SimRtPlan.Build(sim), dir.File("RP.vmat.dcm"));
                string outDir = dir.File("out");
                int code;
                string printed;
                using (var c = new ConsoleCapture()) { code = Program.Main(new[] { "compare-plan", log, plan, "-o", outDir }); printed = c.Out; }
                Assert.Equal(0, code);
                Assert.Contains("LOG VS PLAN COMPARISON", printed);
                Assert.True(File.Exists(Path.Combine(outDir, "plan_comparison_report.txt")));
                Assert.True(File.Exists(Path.Combine(outDir, "plan_comparison_Arc_1.csv")));
                Assert.True(File.Exists(Path.Combine(outDir, "plan_comparison_Arc_2.png")));

                // another patient: exit code 1
                string other = SimRtPlan.Save(SimRtPlan.Build(sim, "OTHER01"), dir.File("RP.other.dcm"));
                using (new ConsoleCapture()) code = Program.Main(new[] { "compare-plan", log, other });
                Assert.Equal(1, code);
            }
        }
    }
}
