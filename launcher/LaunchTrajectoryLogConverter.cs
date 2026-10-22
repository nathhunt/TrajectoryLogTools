// LaunchTrajectoryLogConverter.cs
//
// Single-file ESAPI plug-in script that starts Trajectory Log Converter (TrajectoryLogConverter.exe)
// from Eclipse: Tools > Scripts > LaunchTrajectoryLogConverter.cs. The converter turns v5.1 logs
// into v4.0 logs, from the default input folder to the default output folder set in its Settings
// (saved in TrajectoryLogConverter.settings.json beside the exe), or one file at a time.
//
// Eclipse runs scripts on .NET Framework 4.8; the converter is a .NET 10 application. They do not
// need to share a runtime, because this script only starts the application as a separate process.
// The application is published self-contained (it carries its own .NET runtime), so the
// workstation needs no .NET 10 install. Eclipse stays usable while the converter is open, and
// closing Eclipse does not stop it.
//
// Setup: set ExePath below to TrajectoryLogConverter.exe in the deployed folder (a local path or
// a UNC share; keep the whole folder together). It is published into the same folder as Trajectory
// Log Tools.
//
// The script reads no patient data, so it runs with or without a patient open and does not need
// to be approved as a writeable script.

using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using VMS.TPS.Common.Model.API;

namespace VMS.TPS
{
    public class Script
    {
        // ---- Edit this for your clinic -------------------------------------------------------
        // Full path of TrajectoryLogConverter.exe (in the deployed folder, next to its DLLs).
        const string ExePath = @"TrajectoryLogConverter.exe";
        // ---------------------------------------------------------------------------------------

        public Script() { }

        public void Execute(ScriptContext context)
        {
            if (!File.Exists(ExePath))
            {
                MessageBox.Show("Trajectory Log Converter was not found at:\n\n" + ExePath +
                                "\n\nEdit ExePath at the top of LaunchTrajectoryLogConverter.cs to point to " +
                                "TrajectoryLogConverter.exe in the deployed folder.",
                                "Trajectory Log Converter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                var psi = new ProcessStartInfo(ExePath)
                {
                    UseShellExecute = false,
                    WorkingDirectory = Path.GetDirectoryName(ExePath)
                };
                Process.Start(psi);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not start Trajectory Log Converter:\n\n" + ex.Message +
                                "\n\nIf the message mentions a policy or access, the application folder may need to be " +
                                "allowed by your IT department (application whitelisting).",
                                "Trajectory Log Converter", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
