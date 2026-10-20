// StartupArgs.cs - command-line arguments, all optional:
//   <file.bin | file.csv> ...      logs to open (e.g. passed by the Eclipse launcher, or dragged onto the .exe)
//   <folder>                       opens Add from folder for it
//   --input <file | folder>        a log to open, or the folder the open dialogs start in
//   --start-folder <folder>        the folder the open dialogs start in
// Arguments of earlier versions (--output, --mode, --tab) are accepted and ignored.

using System.Collections.Generic;
using System.IO;

namespace TrajectoryLogTools.App
{
    public sealed class StartupArgs
    {
        public List<string> Files = new List<string>();
        public string Folder;
        public string StartFolder;

        public static StartupArgs Parse(string[] args)
        {
            var s = new StartupArgs();
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i].ToLowerInvariant();
                string v = i + 1 < args.Length ? args[i + 1] : null;
                if ((a == "--input" || a == "-i") && v != null)
                {
                    if (File.Exists(v)) s.Files.Add(v);
                    else if (Directory.Exists(v)) s.StartFolder = v;
                    i++;
                }
                else if (a == "--start-folder" && v != null) { if (Directory.Exists(v)) s.StartFolder = v; i++; }
                else if ((a == "--output" || a == "-o" || a == "--mode" || a == "--tab") && v != null) i++;
                else if (!a.StartsWith("-"))
                {
                    if (File.Exists(args[i])) s.Files.Add(args[i]);
                    else if (Directory.Exists(args[i]) && s.Folder == null) s.Folder = args[i];
                }
            }
            return s;
        }
    }
}
