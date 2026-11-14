// ErrorLog.cs - errors, with their full details, in the app's log (Core.AppLog: the Logs folder
// beside the app, one file per user, computer and day), so a problem can be traced after the fact.

using System;
using TrajectoryLogTools.Core;

namespace TrajectoryLogTools.Wpf
{
    public static class ErrorLog
    {
        /// <summary>Today's log file of this user, where the error was written.</summary>
        public static string FilePath { get { return AppLog.FilePath; } }

        /// <summary>Appends the error; never throws (a log that can't be written is let go).</summary>
        public static void Write(string where, Exception ex) { AppLog.Error(where, ex); }
    }
}
