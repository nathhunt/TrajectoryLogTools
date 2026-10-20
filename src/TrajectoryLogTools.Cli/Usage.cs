// Usage.cs - the command-line help text.

using System;

namespace TrajectoryLogTools.Cli
{
    public static class Usage
    {
        public const string Title = "Trajectory Log Tools - command line";

        public static void Print()
        {
            Console.WriteLine(Title);
            Console.WriteLine();
            Console.WriteLine("Conversion and export");
            Console.WriteLine();
            Console.WriteLine("  TrajectoryLogCli to-v4  <input.bin | folder> [-o <file|folder>] [options]");
            Console.WriteLine("      Convert v5.1 logs to v4.0. Default output: 'v4.0' subfolder, same file name.");
            Console.WriteLine("      --keep-machine-info   keep machine specifier + serial number");
            Console.WriteLine("      --axis-scale <1|2|3>  header axis-scale value (axis data NOT converted)");
            Console.WriteLine("      --time-csv            save the removed time axis to <output>_time.csv");
            Console.WriteLine("      --drop-couch-rotations  leave out couch rotation, pitch and roll");
            Console.WriteLine();
            Console.WriteLine("  TrajectoryLogCli to-csv <input.bin | folder> [-o <folder>] [options]");
            Console.WriteLine("      Export v4.0 / v5.1 logs to <name>.csv.");
            Console.WriteLine("      --no-mlc              leave out MLC columns");
            Console.WriteLine("      --actual-only         write actual values only");
            Console.WriteLine("      --full                write <name>_full.csv: whole file exactly as stored, every axis");
            Console.WriteLine();
            Console.WriteLine("  TrajectoryLogCli dump   <input.bin | folder> [-o <folder>]");
            Console.WriteLine("      Write <name>.txt: the whole file as readable text, exactly as stored, with byte");
            Console.WriteLine("      offsets. Works on any version and on damaged files.");
            Console.WriteLine();
            Console.WriteLine("Analysis");
            Console.WriteLine();
            Console.WriteLine("  TrajectoryLogCli compare-delivery <logA> <logB> [-o <folder>] [--no-plot] [--keep-first-snapshot]");
            Console.WriteLine("      Compare two deliveries of the same field (.bin logs or CSV exports), matched by");
            Console.WriteLine("      control point using beam-on samples only. Writes axis_summary.csv, per_leaf_A_minus_B.csv,");
            Console.WriteLine("      per_beam_A/B.csv, beam_holds_A/B.csv, report.txt and expected_vs_actual_comparison.png.");
            Console.WriteLine("      Default output: 'delivery_comparison' folder next to log A.");
            Console.WriteLine("      --keep-first-snapshot keep a first snapshot recorded > 0.5 s before the rest");
            Console.WriteLine();
            Console.WriteLine("  TrajectoryLogCli reconstruct-mlc <log.bin | log.csv> [options]");
            Console.WriteLine("      Rebuild the Eclipse MLC file (MLCPlan serialization CSV) of each field from the log.");
            Console.WriteLine("      -o, --out <file>        output (default <log>[_subN]_reconstructed_MLC.csv)");
            Console.WriteLine("      -t, --template <file>   Eclipse MLC export of the same beam (recommended); one per subbeam");
            Console.WriteLine("      --positions actual|expected   leaf positions to use (default actual)");
            Console.WriteLine("      --axis mu|cp            sample coordinate: delivered MU (default) or the log's control point");
            Console.WriteLine("      --gantry plan|actual    without template: gantry from plan or actual values (default plan)");
            Console.WriteLine("      --include-hold          also use beam-hold snapshots (not recommended)");
            Console.WriteLine("      --subbeam all|n|n,m     subbeam(s) to process (default all, one file each)");
            Console.WriteLine("      --mlc-model <name>      MLC model written without template (default: from the log's MLC)");
            Console.WriteLine("      --scale auto|varian|iec angle scale of the log (default auto: from the .bin header)");
            Console.WriteLine("      --min-gap <mm>          minimum dynamic leaf gap (default: from the MLC, 0.5 for SX2)");
            Console.WriteLine("      --report <file>         QA report (default <out>_report.txt)");
            Console.WriteLine();
            Console.WriteLine("Validation and testing");
            Console.WriteLine();
            Console.WriteLine("  TrajectoryLogCli to-v5  <input.bin | folder> [-o <file|folder>] [options]");
            Console.WriteLine("      Convert v4.0 logs to v5.1. Default output: 'v5.1' subfolder, same file name.");
            Console.WriteLine("      --time-from <csv>     restore the time axis from a to-v4 --time-csv file");
            Console.WriteLine("      --start-time <t>      first snapshot clock time, hh:mm:ss[.fff] (default: from file name)");
            Console.WriteLine("      --serial <text>       machine serial number (last 6 characters)");
            Console.WriteLine("      --add-couch-rotations add couch rotation, pitch and roll (if missing) at the zero position");
            Console.WriteLine("      --axis-scale <1|2|3>  header axis-scale value (axis data NOT converted)");
            Console.WriteLine();
            Console.WriteLine("  TrajectoryLogCli version <input.bin | folder> [--expect <4.0|5.1>]");
            Console.WriteLine("      Print the version of each log; --expect fails any log of another version.");
            Console.WriteLine();
            Console.WriteLine("  TrajectoryLogCli compare <a.bin | folderA> <b.bin | folderB> [options]");
            Console.WriteLine("      Compare header, subbeams and snapshots. Folders are matched by file name.");
            Console.WriteLine("      --ignore-version      ignore version string, time axis, axis order and machine info");
            Console.WriteLine("      --tolerance <x>       values within x count as equal (default 0 = exact)");
            Console.WriteLine("      --max-diffs <n>       individual value differences to list (default 10)");
            Console.WriteLine();
            Console.WriteLine("  Common: --overwrite, --ignore-crc");
            Console.WriteLine();
            Console.WriteLine("Exit code: 0 = OK (compare: same), 1 = a failure or difference, 2 = bad arguments.");
        }
    }
}
