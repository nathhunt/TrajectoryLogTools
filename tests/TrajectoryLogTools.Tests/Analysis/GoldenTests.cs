using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TrajectoryLogTools.Analysis;
using TrajectoryLogTools.Core;
using Xunit;

namespace TrajectoryLogTools.Tests
{
    /// <summary>
    /// The ports give the same results as the original tools (reference/) on the simulated logs.
    /// Golden files: tests/TrajectoryLogTools.Tests/Golden, made by reference/golden/regenerate.sh.
    /// </summary>
    public class MlcReconstructionGoldenTests
    {
        public static IEnumerable<object[]> Cases()
        {
            yield return new object[] { "vmat_A_default" };
            yield return new object[] { "vmat_A_template" };
            yield return new object[] { "vmat_B_cp_expected" };
            yield return new object[] { "imrt_A_sub2_gantry_actual" };
            yield return new object[] { "imrt_B_template_include_hold" };
        }

        static string LogOf(string name) { return name.Substring(0, 6); }

        static MlcReconstructionOptions OptionsFor(string name, string inputs, string outDir)
        {
            var o = new MlcReconstructionOptions { OutPath = Path.Combine(outDir, "out.csv") };
            switch (name)
            {
                case "vmat_A_template":
                    o.Templates.Add(GoldenCases.TemplatePath(inputs, "vmat", 1));
                    o.Templates.Add(GoldenCases.TemplatePath(inputs, "vmat", 2));
                    break;
                case "vmat_B_cp_expected": o.Axis = "cp"; o.Positions = "expected"; break;
                case "imrt_A_sub2_gantry_actual": o.Subbeams = "2"; o.GantryMode = "actual"; break;
                case "imrt_B_template_include_hold":
                    o.IncludeHold = true;
                    for (int f = 1; f <= 3; f++) o.Templates.Add(GoldenCases.TemplatePath(inputs, "imrt", f));
                    break;
            }
            return o;
        }

        // Lines that hold run-specific text (time stamps, paths).
        static bool Volatile(string line)
        {
            return line.StartsWith("# Exported") || line.StartsWith("Generated") || line.StartsWith("Log file") ||
                   line.StartsWith("Output file") || line.StartsWith("Template");
        }

        static string[] Stable(string path)
        {
            return File.ReadAllLines(path).Where(l => !Volatile(l)).ToArray();
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void SameFilesAsLogToEclipseMlc(string name)
        {
            using (var dir = new TempDir())
            {
                GoldenCases.WriteInputs(dir.Path);
                string outDir = dir.Sub("out");
                string log = Path.Combine(dir.Path, LogOf(name) + ".csv");
                var result = MlcReconstructor.Run(DeliveryRecord.Load(log), log, OptionsFor(name, dir.Path, outDir));

                string golden = Path.Combine(GoldenCases.GoldenDir, "mlc", name);
                var expectedFiles = Directory.GetFiles(golden).Select(Path.GetFileName).Where(f => f != "console.txt").OrderBy(f => f).ToList();
                var actualFiles = Directory.GetFiles(outDir).Select(Path.GetFileName).OrderBy(f => f).ToList();
                Assert.Equal(expectedFiles, actualFiles);
                foreach (string f in expectedFiles)
                    Assert.Equal(Stable(Path.Combine(golden, f)), Stable(Path.Combine(outDir, f)));

                // Failed subbeams fail with the same message.
                string console = File.ReadAllText(Path.Combine(golden, "console.txt"));
                foreach (var sb in result.Subbeams.Where(s => !s.Ok))
                    Assert.Contains("ERROR (subbeam " + sb.Subbeam + "): " + sb.Error, console);
                Assert.Equal(console.Split('\n').Count(l => l.StartsWith("ERROR (subbeam")), result.Failed);
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void BinGivesTheSameLeafPositionsAsCsv(string name)
        {
            using (var dir = new TempDir())
            {
                GoldenCases.WriteInputs(dir.Path);
                string csv = Path.Combine(dir.Path, LogOf(name) + ".csv"), bin = Path.Combine(dir.Path, LogOf(name) + ".bin");
                var fromCsv = MlcReconstructor.Run(DeliveryRecord.Load(csv), csv, OptionsFor(name, dir.Path, dir.Sub("csv")));
                var fromBin = MlcReconstructor.Run(DeliveryRecord.Load(bin), bin, OptionsFor(name, dir.Path, dir.Sub("bin")));
                Assert.Equal(fromCsv.Subbeams.Select(s => s.Ok), fromBin.Subbeams.Select(s => s.Ok));
                foreach (var pair in fromCsv.Subbeams.Zip(fromBin.Subbeams, Tuple.Create).Where(p => p.Item1.Ok))
                {
                    double[,] a = pair.Item1.Reconstructed, b = pair.Item2.Reconstructed;
                    double max = 0;
                    for (int cp = 0; cp < a.GetLength(0); cp++)
                        for (int k = 0; k < a.GetLength(1); k++) max = Math.Max(max, Math.Abs(a[cp, k] - b[cp, k]));
                    // The CSV keeps 7 significant digits; the .bin the full float.
                    Assert.True(max < 0.01, name + ": max difference " + max + " mm");
                }
            }
        }
    }

    public class DeliveryComparisonGoldenTests
    {
        const double Tol = 1e-9;

        static void Near(double expected, double actual, string what)
        {
            if (double.IsNaN(expected)) { Assert.True(double.IsNaN(actual), what + ": expected NaN, got " + actual); return; }
            double tol = Tol * Math.Max(1, Math.Abs(expected));
            Assert.True(Math.Abs(expected - actual) <= tol, what + ": expected " + expected.ToString("R") + ", got " + actual.ToString("R"));
        }

        static double Num(JsonElement e)
        {
            return e.ValueKind == JsonValueKind.Null ? double.NaN : e.GetDouble();
        }

        static DeliveryComparisonResult Run(TempDir dir, string plan, string ext = ".csv")
        {
            GoldenCases.WriteInputs(dir.Path);
            var a = DeliveryRecord.Load(Path.Combine(dir.Path, plan + "_A" + ext));
            var b = DeliveryRecord.Load(Path.Combine(dir.Path, plan + "_B" + ext));
            return DeliveryComparison.Compare(a, b);
        }

        [Theory]
        [InlineData("vmat")]
        [InlineData("imrt")]
        public void SameNumbersAsComparePy(string plan)
        {
            using (var dir = new TempDir())
            {
                DeliveryComparisonResult r = Run(dir, plan);
                JsonElement g = JsonDocument.Parse(File.ReadAllText(Path.Combine(GoldenCases.GoldenDir, "delivery", plan + ".json"))).RootElement;

                var summary = g.GetProperty("summary").EnumerateArray().ToList();
                Assert.Equal(summary.Count, r.Summary.Count);
                for (int i = 0; i < summary.Count; i++)
                {
                    var e = summary[i]; var s = r.Summary[i];
                    string what = s.Axis + " " + s.Values + " " + s.Sampling;
                    Assert.Equal(e.GetProperty("Axis").GetString(), s.Axis);
                    Assert.Equal(e.GetProperty("Values").GetString(), s.Values);
                    Assert.Equal(e.GetProperty("Sampling").GetString(), s.Sampling);
                    Near(Num(e.GetProperty("Mean A-B")), s.Mean, what + " mean");
                    Near(Num(e.GetProperty("RMS A-B")), s.Rms, what + " rms");
                    Near(Num(e.GetProperty("Max |A-B|")), s.Max, what + " max");
                    Near(Num(e.GetProperty("P95 |A-B|")), s.P95, what + " p95");
                    Near(Num(e.GetProperty("P99 |A-B|")), s.P99, what + " p99");
                }

                var leaves = g.GetProperty("per_leaf").EnumerateArray().ToList();
                Assert.Equal(leaves.Count, r.PerLeaf.Count);
                for (int i = 0; i < leaves.Count; i++)
                {
                    var e = leaves[i]; var l = r.PerLeaf[i];
                    Assert.Equal(e.GetProperty("Leaf").GetInt32(), l.Leaf);
                    Near(Num(e.GetProperty("Mean_Actual_mm")), l.MeanActual, "leaf " + l.Leaf);
                    Near(Num(e.GetProperty("Mean_Expected_mm")), l.MeanExpected, "leaf " + l.Leaf);
                    Near(Num(e.GetProperty("RMS_Actual_mm")), l.RmsActual, "leaf " + l.Leaf);
                    Near(Num(e.GetProperty("RMS_Expected_mm")), l.RmsExpected, "leaf " + l.Leaf);
                    Near(Num(e.GetProperty("Max_Actual_mm")), l.MaxActual, "leaf " + l.Leaf);
                    Near(Num(e.GetProperty("Max_Expected_mm")), l.MaxExpected, "leaf " + l.Leaf);
                }

                var extra = g.GetProperty("extra");
                Near(Num(extra.GetProperty("corr(ExpectedDiff, ActualDiff)")), r.CorrExpectedActual, "corr");
                Near(Num(extra.GetProperty("RMS of (ActualDiff - ExpectedDiff) mm")), r.RmsActualMinusExpectedMm, "rms diff");
                Near(Num(extra.GetProperty("Max of |ActualDiff - ExpectedDiff| mm")), r.MaxActualMinusExpectedMm, "max diff");

                foreach (var x in new[] { Tuple.Create("A", r.A), Tuple.Create("B", r.B) })
                {
                    var e = g.GetProperty("logs").GetProperty(x.Item1);
                    LogSummary s = x.Item2;
                    Near(Num(e.GetProperty("mu_planned")), s.MuPlanned, x.Item1 + " MU planned");
                    Near(Num(e.GetProperty("mu_delivered")), s.MuDelivered, x.Item1 + " MU delivered");
                    Near(Num(e.GetProperty("total_time")), s.TotalTimeS, x.Item1 + " time");
                    var steps = e.GetProperty("steps");
                    Near(Num(steps.GetProperty("Expected")[0]), s.MaxLeafStepExpected, "step");
                    Near(Num(steps.GetProperty("Expected")[1]), s.P90LeafStepExpected, "step p90");
                    Near(Num(steps.GetProperty("Actual")[0]), s.MaxLeafStepActual, "step");
                    Near(Num(steps.GetProperty("Actual")[1]), s.P90LeafStepActual, "step p90");

                    var couch = e.GetProperty("couch").EnumerateObject().ToList();
                    Assert.Equal(couch.Select(c => c.Name), s.Couch.Select(c => c.Item1));
                    for (int i = 0; i < couch.Count; i++)
                    {
                        Near(Num(couch[i].Value[0]), s.Couch[i].Item2, "couch");
                        Near(Num(couch[i].Value[1]), s.Couch[i].Item3, "couch");
                        Near(Num(couch[i].Value[2]), s.Couch[i].Item4, "couch");
                    }

                    var beams = e.GetProperty("beams").EnumerateArray().ToList();
                    Assert.Equal(beams.Count, s.Beams.Count);
                    for (int i = 0; i < beams.Count; i++)
                    {
                        var eb = beams[i]; var b = s.Beams[i];
                        string what = x.Item1 + " beam " + b.Beam;
                        Assert.Equal((int)Num(eb.GetProperty("beam")), b.Beam);
                        var gantry = eb.GetProperty("gantry_deg");
                        if (gantry.ValueKind == JsonValueKind.String) Assert.Equal(gantry.GetString(), b.Gantry);
                        else Near(Num(gantry), b.GantryDeg, what + " gantry");
                        Near(Num(eb.GetProperty("coll_deg")), b.CollDeg, what + " coll");
                        Assert.Equal(eb.GetProperty("CP").GetString(), b.ControlPoints);
                        Near(Num(eb.GetProperty("MU_planned")), b.MuPlanned, what + " MU planned");
                        Near(Num(eb.GetProperty("MU_delivered")), b.MuDelivered, what + " MU delivered");
                        Near(Num(eb.GetProperty("beam_on_s")), b.BeamOnS, what + " beam on");
                        Near(Num(eb.GetProperty("hold_s")), b.HoldS, what + " hold");
                        Near(Num(eb.GetProperty("MLC_rms_err_mm")), b.MlcRmsErrMm, what + " rms");
                        Near(Num(eb.GetProperty("MLC_max_err_mm")), b.MlcMaxErrMm, what + " max");
                    }

                    var holds = e.GetProperty("holds").EnumerateArray().ToList();
                    Assert.Equal(holds.Count, s.Holds.Count);
                    for (int i = 0; i < holds.Count; i++)
                    {
                        var eh = holds[i]; var h = s.Holds[i];
                        string what = x.Item1 + " hold " + i;
                        Assert.Equal(eh.GetProperty("type").GetString(), h.Type);
                        Assert.Equal((int)Num(eh.GetProperty("beam")), h.Beam);
                        Near(Num(eh.GetProperty("t_start_s")), h.StartS, what + " start");
                        Near(Num(eh.GetProperty("duration_s")), h.DurationS, what + " duration");
                        Near(Num(eh.GetProperty("CP")), h.ControlPoint, what + " CP");
                        Near(Num(eh.GetProperty("MU")), h.Mu, what + " MU");
                        Near(Num(eh.GetProperty("MU_during")), h.MuDuring, what + " MU during");
                        Assert.Equal(eh.GetProperty("gantry").GetString(), h.Gantry);
                        Assert.Equal(eh.GetProperty("coll").GetString(), h.Coll);
                        Near(Num(eh.GetProperty("leaf_err_during_mm")), h.LeafErrDuringMm, what + " leaf err");
                        Near(Num(eh.GetProperty("leaf_err_1s_after_mm")), h.LeafErr1sAfterMm, what + " leaf err after");
                    }
                }
            }
        }

        [Theory]
        [InlineData("vmat")]
        [InlineData("imrt")]
        public void SameCsvFilesAsComparePy(string plan)
        {
            using (var dir = new TempDir())
            {
                DeliveryComparisonResult r = Run(dir, plan);
                string outDir = dir.Sub("results");
                DeliveryComparison.WriteResults(r, outDir);
                foreach (string f in new[] { "axis_summary.csv", "per_leaf_A_minus_B.csv", "per_beam_A.csv", "per_beam_B.csv",
                                             "beam_holds_A.csv", "beam_holds_B.csv" })
                    Assert.Equal(File.ReadAllLines(Path.Combine(GoldenCases.GoldenDir, "delivery", plan + "_" + f)),
                                 File.ReadAllLines(Path.Combine(outDir, f)));
                Assert.True(File.Exists(Path.Combine(outDir, "report.txt")));
            }
        }

        [Theory]
        [InlineData("vmat")]
        [InlineData("imrt")]
        public void BinGivesTheSameResultsAsCsv(string plan)
        {
            using (var dir = new TempDir())
            {
                DeliveryComparisonResult csv = Run(dir, plan), bin = Run(dir, plan, ".bin");
                Assert.Equal(csv.Summary.Count, bin.Summary.Count);
                for (int i = 0; i < csv.Summary.Count; i++)
                    Assert.True(Math.Abs(csv.Summary[i].Rms - bin.Summary[i].Rms) < 1e-4, csv.Summary[i].Axis);
                Assert.Equal(csv.A.Holds.Select(h => h.Type), bin.A.Holds.Select(h => h.Type));
                Assert.Equal(csv.B.Beams.Select(b => b.Gantry), bin.B.Beams.Select(b => b.Gantry));
            }
        }
    }

    public class RawDumpGoldenTests
    {
        [Fact]
        public void SameTextAsTrajectoryLogDump()
        {
            var golden = File.ReadAllLines(Path.Combine(GoldenCases.GoldenDir, "dump_sha256.txt"))
                             .Select(l => l.Split(' ')).ToDictionary(p => p[0], p => p[1]);
            foreach (string n in GoldenCases.Names)
            {
                var warnings = new List<string>();
                string text = RawDump.ToText(GoldenCases.Bin(n), "/x/" + n + ".bin", warnings);
                Assert.Empty(warnings);
                // sha256sum of the original's output with its "Source file" line removed (by grep).
                string body = System.Text.RegularExpressions.Regex.Replace(text, "^Source file : [^\n]*\n", "",
                                                                          System.Text.RegularExpressions.RegexOptions.Multiline);
                string hash;
                using (var sha = SHA256.Create())
                    hash = string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(body)).Select(b => b.ToString("x2")));
                Assert.Equal(golden[n], hash);
            }
        }
    }
}
