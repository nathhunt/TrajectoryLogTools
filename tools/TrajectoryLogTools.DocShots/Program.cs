// Program.cs - DocShots: the screenshots of the user guide (docs/user/images), made from the real
// windows and view models of Trajectory Log Tools and Trajectory Log Converter, showing synthetic
// demo logs only (DemoData). Windows only; not part of either deployed folder.
//
//   dotnet run --project tools/TrajectoryLogTools.DocShots -- [options]
//     --out <folder>     where the PNGs go (default: docs/user/images in the repository)
//     --root <folder>    where the demo logs are written (default: C:\Demo; must be a folder DocShots made, or new)
//     --only <a,b,...>   only these scenarios (see --list)
//     --list             list the scenarios
//
// Each scenario sets up a window, waits until it has settled (logs read, charts drawn, the
// dispatcher idle), then renders it at a fixed size and DPI. Light theme, New Zealand dates, a
// fixed "today" and the same demo files every time, so re-running gives the same images and a
// diff shows only real changes to the UI. A PNG whose pixels haven't changed is not rewritten.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TrajectoryLogTools.App.Services;
using TrajectoryLogTools.Wpf;

namespace TrajectoryLogTools.DocShots
{
    public static class Program
    {
        static readonly List<string> Failures = new List<string>();

        [STAThread]
        public static int Main(string[] args)
        {
            string output = null, root = @"C:\Demo";
            var only = new List<string>();
            bool list = false;
            for (int i = 0; i < args.Length; i++)
            {
                string a = args[i];
                string v = i + 1 < args.Length ? args[i + 1] : null;
                if (a == "--out" && v != null) { output = v; i++; }
                else if (a == "--root" && v != null) { root = v; i++; }
                else if (a == "--only" && v != null) { only.AddRange(v.Split(',').Select(s => s.Trim()).Where(s => s.Length > 0)); i++; }
                else if (a == "--list") list = true;
                else { Console.Error.WriteLine("Unknown argument: " + a); return 2; }
            }
            if (list)
            {
                foreach (Scenario s in Scenarios.All) Console.WriteLine(s.Name.PadRight(22) + s.Description);
                return 0;
            }
            output = Path.GetFullPath(output ?? Path.Combine(RepositoryRoot(), "docs", "user", "images"));

            // As the apps start: New Zealand dates, the light theme; settings in memory only.
            NzCulture.Apply();
            var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
#pragma warning disable WPF0001
            app.ThemeMode = ThemeMode.Light;
#pragma warning restore WPF0001
            app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/TrajectoryLogTools;component/Resources.xaml") });
            AppSettings.UseInMemory(new AppSettings());
            Theme.Apply("Light");
            app.DispatcherUnhandledException += (s, e) =>
            {
                e.Handled = true;
                Fail("Unhandled: " + e.Exception);
            };

            int code = 0;
            app.Startup += async (s, e) =>
            {
                try
                {
                    var demo = new DemoData(root);
                    Log("Demo logs: " + demo.Root);
                    demo.Create();
                    Log("Images:    " + output);
                    var shots = new Shots(demo, output);
                    foreach (Scenario sc in Scenarios.All.Where(x => only.Count == 0 || only.Contains(x.Name)))
                    {
                        Log(sc.Name);
                        try { await sc.Run(shots); }
                        catch (Exception ex) { Fail(sc.Name + ": " + ex); }
                        finally { shots.CloseAll(); }
                    }
                }
                catch (Exception ex) { Fail(ex.ToString()); }
                code = Failures.Count == 0 ? 0 : 1;
                if (code != 0) Log(Failures.Count + " problem(s):" + Environment.NewLine + string.Join(Environment.NewLine, Failures));
                app.Shutdown(code);
            };
            app.Run();
            return code;
        }

        public static void Log(string line) { Console.WriteLine(line); }

        public static void Fail(string message)
        {
            Failures.Add(message);
            Console.Error.WriteLine("FAILED " + message);
        }

        /// <summary>The folder with TrajectoryLogTools.sln, above the tool's output folder.</summary>
        static string RepositoryRoot()
        {
            for (string d = AppContext.BaseDirectory; d != null; d = Path.GetDirectoryName(d.TrimEnd(Path.DirectorySeparatorChar)))
                if (File.Exists(Path.Combine(d, "TrajectoryLogTools.sln"))) return d;
            throw new InvalidOperationException("Can't find the repository (TrajectoryLogTools.sln); give --out.");
        }

        static readonly Dictionary<string, ImageSource> Icons = new Dictionary<string, ImageSource>();

        /// <summary>The app's own icon for the window frame (the .ico files the exes carry).</summary>
        public static ImageSource IconFor(Window w)
        {
            string name = w.GetType().Namespace.StartsWith("TrajectoryLogTools.Converter", StringComparison.Ordinal) ? "converter.ico" : "tools.ico";
            ImageSource icon;
            if (Icons.TryGetValue(name, out icon)) return icon;
            string path = Path.Combine(AppContext.BaseDirectory, "Icons", name);
            if (File.Exists(path))
            {
                var dec = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                icon = dec.Frames.OrderBy(f => Math.Abs(f.PixelWidth - 32)).First();
                icon.Freeze();
            }
            Icons[name] = icon;
            return icon;
        }
    }
}
