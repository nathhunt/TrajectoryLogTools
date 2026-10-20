// LaunchTrajectoryLogTools.cs
//
// Single-file ESAPI plug-in script that starts Trajectory Log Tools (TrajectoryLogTools.exe) from
// Eclipse: Tools > Scripts > LaunchTrajectoryLogTools.cs.
//
// Eclipse runs scripts on .NET Framework 4.8; Trajectory Log Tools is a .NET 10 application. They
// do not need to share a runtime, because this script only starts the application as a separate
// process. The application is published self-contained (it carries its own .NET runtime), so the
// workstation needs no .NET 10 install. Eclipse stays usable while the tool is open, and closing
// Eclipse does not stop it.
//
// Setup: set ExePath below to TrajectoryLogTools.exe in the deployed folder (a local path or a UNC
// share; keep the whole folder together), and optionally the defaults the tool opens with.
//
// The script reads no patient data, so it runs with or without a patient open and does not need
// to be approved as a writeable script.

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using VMS.TPS.Common.Model.API;

namespace VMS.TPS
{
    public class Script
    {
        // ---- Edit these for your clinic ------------------------------------------------------
        // Full path of TrajectoryLogTools.exe (in the deployed folder, next to its DLLs).
        const string ExePath = @"TrajectoryLogTools.exe";
        // Folder the Convert & export tab opens with as input (e.g. where logs are copied from the
        // machine). Leave empty to start with a blank input.
        const string DefaultInputFolder = @"";
        // Folder converted files are exported to by default. Leave empty to start blank.
        const string DefaultExportFolder = @"";
        // Initial Convert & export options: "v4" (v5.1 -> v4.0 .bin), "csv", "both" or "dump".
        const string DefaultMode = "v4";
        // Tab to open on: "convert", "viewer", "compare" or "mlc" (Eclipse MLC reconstruction).
        const string DefaultTab = "convert";
        // ---------------------------------------------------------------------------------------

        public Script() { }

        public void Execute(ScriptContext context)
        {
            if (!File.Exists(ExePath))
            {
                MessageBox.Show("Trajectory Log Tools was not found at:\n\n" + ExePath +
                                "\n\nEdit ExePath at the top of LaunchTrajectoryLogTools.cs to point to " +
                                "TrajectoryLogTools.exe in the deployed folder.",
                                "Trajectory Log Tools", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var args = new StringBuilder();
            if (DefaultInputFolder.Length > 0) args.Append("--input ").Append(Quote(DefaultInputFolder)).Append(' ');
            if (DefaultExportFolder.Length > 0) args.Append("--output ").Append(Quote(DefaultExportFolder)).Append(' ');
            if (DefaultMode.Length > 0) args.Append("--mode ").Append(DefaultMode).Append(' ');
            if (DefaultTab.Length > 0) args.Append("--tab ").Append(DefaultTab);

            try
            {
                var psi = new ProcessStartInfo(ExePath, args.ToString().Trim())
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(ExePath)
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not start Trajectory Log Tools:\n\n" + ex.Message +
                                "\n\nIf the message mentions a policy or access, the application folder may need to be " +
                                "allowed by your IT department (application whitelisting).",
                                "Trajectory Log Tools", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Quote a path for the command line. A trailing backslash would escape the closing quote
        // ("C:\Logs\" -> C:\Logs"), so it is dropped unless the path is a drive root.
        static string Quote(string path)
        {
            string p = path.Trim();
            if (p.EndsWith("\\") && !p.EndsWith(":\\")) p = p.TrimEnd('\\');
            else if (p.EndsWith(":\\")) p += "\\";   // "C:\" -> "C:\\" so the quote survives
            return "\"" + p + "\"";
        }
    }
}
