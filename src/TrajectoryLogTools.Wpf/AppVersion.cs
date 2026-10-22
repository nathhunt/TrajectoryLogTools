// AppVersion.cs - the running app's version as both apps show it. It comes from <Version> in
// Directory.Build.props (set it there, or with -p:Version=... when publishing); the build adds the
// git commit it was built from (e.g. 1.2.0-beta+aeaf93dafbd2...), shortened here to 7 characters.

using System;
using System.Reflection;

namespace TrajectoryLogTools.Wpf
{
    public static class AppVersion
    {
        /// <summary>The version as set, with any suffix (e.g. "1.2.0" or "1.2.0-beta"), without the commit.</summary>
        public static string Version { get; } = Read(false);

        /// <summary>The version and the commit it was built from (e.g. "1.2.0+aeaf93d"); just the version when built outside git.</summary>
        public static string Full { get; } = Read(true);

        /// <summary>The line at the bottom of each window: version, Windows, .NET runtime and the folder the app runs from.</summary>
        public static string EnvironmentText { get; } =
            "Version " + Full + "  ·  " +
            System.Runtime.InteropServices.RuntimeInformation.OSDescription + "  ·  " +
            System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription + "  ·  " + AppContext.BaseDirectory;

        static string Read(bool withCommit)
        {
            Assembly app = Assembly.GetEntryAssembly() ?? typeof(AppVersion).Assembly;
            string v = app.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                       ?? app.GetName().Version?.ToString(3) ?? "";
            int plus = v.IndexOf('+');
            if (plus < 0) return v;
            if (!withCommit) return v.Substring(0, plus);
            string commit = v.Substring(plus + 1);
            return v.Substring(0, plus) + "+" + (commit.Length > 7 ? commit.Substring(0, 7) : commit);
        }
    }
}
