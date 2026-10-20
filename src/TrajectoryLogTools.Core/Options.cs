// Options.cs - command-line options of the conversion commands.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TrajectoryLogTools.Core
{
    public enum Command { ToV4, ToCsv, ToV5, Version, Compare, Dump }

    public sealed class SkipException : Exception
    {
        public SkipException(string message) : base(message) { }
    }

    public sealed class Options
    {
        public Command Command;
        public string Input;
        public string Input2;                // compare: second log / folder
        public string Output;
        public bool Overwrite;
        public bool IgnoreCrc;
        // to-v4
        public bool KeepMachineInfo;
        public int? AxisScale;               // also to-v5
        public bool TimeCsv;
        public bool DropCouchRotations;
        /// <summary>to-v4: machine specifier written (0 TrueBeam, 1 Halcyon / Ethos); null = 0, or kept with KeepMachineInfo.</summary>
        public int? MachineSpecifier;
        /// <summary>to-v4: keep the serial number (otherwise removed, unless KeepMachineInfo or Serial).</summary>
        public bool KeepSerial;
        /// <summary>to-v4: metadata lines to change, in order: key -> new value (null removes the line).</summary>
        public List<KeyValuePair<string, string>> MetaEdits = new List<KeyValuePair<string, string>>();
        /// <summary>to-v4: new subbeam (field) names, by 1-based subbeam number.</summary>
        public Dictionary<int, string> SubbeamNames = new Dictionary<int, string>();
        // to-v5
        public string TimeFrom;
        public double? StartTime;            // seconds since midnight
        public string Serial;                // also to-v4
        public bool AddCouchRotations;
        // to-csv
        public bool NoMlc;
        public bool ActualOnly;
        public bool Full;
        // version
        public string ExpectVersion;
        // compare
        public bool IgnoreVersion;
        public double Tolerance;
        public int MaxDiffs = 10;

        public static Options Parse(string[] args)
        {
            if (args.Length == 0) return null;
            var o = new Options();
            string cmd = args[0].ToLowerInvariant();
            if (cmd == "-h" || cmd == "--help" || cmd == "/?") return null;
            if (cmd == "to-v4") o.Command = Command.ToV4;
            else if (cmd == "to-csv") o.Command = Command.ToCsv;
            else if (cmd == "to-v5") o.Command = Command.ToV5;
            else if (cmd == "version") o.Command = Command.Version;
            else if (cmd == "compare") o.Command = Command.Compare;
            else if (cmd == "dump") o.Command = Command.Dump;
            else throw new ArgumentException("First argument must be a command: " + string.Join(", ", CommandNames));

            var inputs = new List<string>();
            for (int i = 1; i < args.Length; i++)
            {
                string a = args[i];
                if (a.Length == 0) continue; // e.g. an empty optional argument from a VS Code launch input
                switch (a)
                {
                    case "-h": case "--help": case "/?": return null;
                    case "-o": case "--output": RequireCmd(o, a, Command.ToV4, Command.ToV5, Command.ToCsv, Command.Dump); o.Output = Next(args, ref i, a); break;
                    case "--overwrite": o.Overwrite = true; break;
                    case "--ignore-crc": o.IgnoreCrc = true; break;
                    case "--keep-machine-info": RequireCmd(o, a, Command.ToV4); o.KeepMachineInfo = true; break;
                    case "--time-csv": RequireCmd(o, a, Command.ToV4); o.TimeCsv = true; break;
                    case "--drop-couch-rotations": RequireCmd(o, a, Command.ToV4); o.DropCouchRotations = true; break;
                    case "--machine":
                        RequireCmd(o, a, Command.ToV4);
                        o.MachineSpecifier = ParseMachine(Next(args, ref i, a));
                        break;
                    case "--keep-serial": RequireCmd(o, a, Command.ToV4); o.KeepSerial = true; break;
                    case "--set-meta":
                    {
                        RequireCmd(o, a, Command.ToV4);
                        string kv = Next(args, ref i, a);
                        int eq = kv.IndexOf('=');
                        if (eq <= 0) throw new ArgumentException("--set-meta needs KEY=VALUE, e.g. \"Patient ID=QA001\" (an empty VALUE removes the line)");
                        string v = kv.Substring(eq + 1);
                        o.MetaEdits.Add(new KeyValuePair<string, string>(kv.Substring(0, eq).Trim(), v.Length == 0 ? null : v));
                        break;
                    }
                    case "--subbeam-name":
                    {
                        RequireCmd(o, a, Command.ToV4);
                        string kv = Next(args, ref i, a);
                        int eq = kv.IndexOf('=');
                        int n;
                        if (eq <= 0 || !int.TryParse(kv.Substring(0, eq), NumberStyles.Integer, CultureInfo.InvariantCulture, out n) || n < 1)
                            throw new ArgumentException("--subbeam-name needs N=NAME, with N the subbeam number from 1");
                        o.SubbeamNames[n] = kv.Substring(eq + 1);
                        break;
                    }
                    case "--add-couch-rotations": RequireCmd(o, a, Command.ToV5); o.AddCouchRotations = true; break;
                    case "--axis-scale":
                        RequireCmd(o, a, Command.ToV4, Command.ToV5);
                        int s;
                        if (!int.TryParse(Next(args, ref i, a), out s) || s < 1 || s > 3)
                            throw new ArgumentException("--axis-scale must be 1, 2 or 3");
                        o.AxisScale = s; break;
                    case "--time-from": RequireCmd(o, a, Command.ToV5); o.TimeFrom = Next(args, ref i, a); break;
                    case "--start-time":
                        RequireCmd(o, a, Command.ToV5);
                        o.StartTime = ParseClock(Next(args, ref i, a));
                        break;
                    case "--serial":
                        RequireCmd(o, a, Command.ToV4, Command.ToV5);
                        o.Serial = Next(args, ref i, a);
                        if (o.Serial.Length > 6 || o.Serial.Any(c => c < 32 || c > 126))
                            throw new ArgumentException("--serial must be up to 6 ASCII characters");
                        break;
                    case "--no-mlc": RequireCmd(o, a, Command.ToCsv); o.NoMlc = true; break;
                    case "--actual-only": RequireCmd(o, a, Command.ToCsv); o.ActualOnly = true; break;
                    case "--full": RequireCmd(o, a, Command.ToCsv); o.Full = true; break;
                    case "--expect":
                        RequireCmd(o, a, Command.Version);
                        o.ExpectVersion = NormaliseVersion(Next(args, ref i, a));
                        break;
                    case "--ignore-version": RequireCmd(o, a, Command.Compare); o.IgnoreVersion = true; break;
                    case "--tolerance":
                        RequireCmd(o, a, Command.Compare);
                        if (!double.TryParse(Next(args, ref i, a), NumberStyles.Float, CultureInfo.InvariantCulture, out o.Tolerance)
                            || o.Tolerance < 0 || double.IsNaN(o.Tolerance))
                            throw new ArgumentException("--tolerance must be a number >= 0");
                        break;
                    case "--max-diffs":
                        RequireCmd(o, a, Command.Compare);
                        if (!int.TryParse(Next(args, ref i, a), out o.MaxDiffs) || o.MaxDiffs < 0)
                            throw new ArgumentException("--max-diffs must be a whole number >= 0");
                        break;
                    default:
                        if (a.StartsWith("-")) throw new ArgumentException("Unknown option " + a);
                        inputs.Add(a); break;
                }
            }
            if (o.Full && (o.NoMlc || o.ActualOnly))
                throw new ArgumentException("--full writes every value; it cannot be combined with --no-mlc or --actual-only");
            int needed = o.Command == Command.Compare ? 2 : 1;
            if (inputs.Count < needed) throw new ArgumentException(needed == 2 ? "compare needs two inputs" : "No input given");
            if (inputs.Count > needed) throw new ArgumentException(needed == 2 ? "compare takes exactly two inputs" : "Only one input path is allowed");
            o.Input = inputs[0];
            if (needed == 2) o.Input2 = inputs[1];
            return o;
        }

        static void RequireCmd(Options o, string name, params Command[] allowed)
        {
            if (Array.IndexOf(allowed, o.Command) < 0)
                throw new ArgumentException(name + " only applies to " + string.Join(" / ", allowed.Select(CommandName)));
        }

        /// <summary>Every command <see cref="Parse"/> accepts, as typed on the command line.</summary>
        public static readonly string[] CommandNames = { "to-v4", "to-csv", "to-v5", "dump", "version", "compare" };

        public static string CommandName(Command c)
        {
            switch (c)
            {
                case Command.ToV4: return "to-v4";
                case Command.ToCsv: return "to-csv";
                case Command.ToV5: return "to-v5";
                case Command.Version: return "version";
                case Command.Dump: return "dump";
                default: return "compare";
            }
        }

        /// <summary>"truebeam" / "0" -> 0, "halcyon" / "ethos" / "1" -> 1.</summary>
        public static int ParseMachine(string v)
        {
            switch (v.Trim().ToLowerInvariant())
            {
                case "truebeam": case "0": return 0;
                case "halcyon": case "ethos": case "1": return 1;
                default: throw new ArgumentException("--machine must be truebeam (0) or halcyon (1)");
            }
        }

        static string NormaliseVersion(string v)
        {
            string s = v.Trim().TrimStart('v', 'V');
            if (s == "4" || s == "4.0") return "4.0";
            if (s == "5" || s == "5.1") return "5.1";
            throw new ArgumentException("--expect must be 4.0 or 5.1");
        }

        /// <summary>"hh:mm:ss[.fff]" or a number of seconds since midnight.</summary>
        static double ParseClock(string v)
        {
            double sec;
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out sec) && sec >= 0 && sec < 86400)
                return sec;
            TimeSpan ts;
            if (v.Contains(":") && TimeSpan.TryParse(v, CultureInfo.InvariantCulture, out ts) && ts >= TimeSpan.Zero && ts.TotalDays < 1)
                return ts.TotalSeconds;
            throw new ArgumentException("--start-time must be hh:mm:ss[.fff] or seconds since midnight (0 - 86399)");
        }

        static string Next(string[] args, ref int i, string name)
        {
            if (i + 1 >= args.Length) throw new ArgumentException(name + " needs a value");
            return args[++i];
        }
    }
}
