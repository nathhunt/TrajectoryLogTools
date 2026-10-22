// ErrorLog.cs - errors, with their full details, appended to
// %LocalAppData%\TrajectoryLogTools\errors.log, so a problem can be traced after the fact.

using System;
using System.IO;
using System.Text;

namespace TrajectoryLogTools.Wpf
{
    public static class ErrorLog
    {
        public static string FilePath
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TrajectoryLogTools", "errors.log"); }
        }

        /// <summary>Appends the error; never throws (a log that can't be written is let go).</summary>
        public static void Write(string where, Exception ex)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var sb = new StringBuilder();
                var app = (System.Reflection.Assembly.GetEntryAssembly() ?? typeof(ErrorLog).Assembly).GetName();
                sb.AppendLine("==== " + TrajectoryLogTools.Core.Fmt.DateTimeSeconds(DateTime.Now) + "  " + where + "  (" +
                              app.Name + " " + app.Version + ", " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + ", " +
                              System.Runtime.InteropServices.RuntimeInformation.OSDescription + ")");
                sb.AppendLine(ex == null ? "(no details)" : ex.ToString());
                sb.AppendLine();
                File.AppendAllText(FilePath, sb.ToString(), Encoding.UTF8);
            }
            catch (Exception) { }
        }
    }
}
