// StartupArgs.cs - command-line arguments, all optional. The Eclipse launcher uses them to
// pre-fill the form:
//   --input <file | folder>    log(s) for Convert & export (a single .bin also opens in the viewer)
//   --output <folder>          export folder
//   --mode <v4 | csv | both | dump>   initial Convert & export options
//   --tab <convert | viewer | compare | mlc>   tab to open on
//   <file.bin>                 a log to open in the viewer (e.g. dragged onto the .exe)

using System;
using System.IO;

namespace TrajectoryLogTools.App
{
    public sealed class StartupArgs
    {
        public string Input;
        public string Output;
        public string Mode;
        public string Tab;
        public string OpenLog;

        public static StartupArgs Parse(string[] args)
        {
            var s = new StartupArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                string v = i + 1 < args.Length ? args[i + 1] : null;
                if ((a == "--input" || a == "-i") && v != null) { s.Input = v; i++; }
                else if ((a == "--output" || a == "-o") && v != null) { s.Output = v; i++; }
                else if (a == "--mode" && v != null) { s.Mode = v.ToLowerInvariant(); i++; }
                else if (a == "--tab" && v != null) { s.Tab = v.ToLowerInvariant(); i++; }
                else if (!a.StartsWith("-") && s.OpenLog == null && File.Exists(args[i])) s.OpenLog = args[i];
            }
            if (s.OpenLog == null && s.Input != null && File.Exists(s.Input)) s.OpenLog = s.Input;
            return s;
        }
    }
}
