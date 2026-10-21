// Theme.cs - light / dark / follow Windows, for the WPF Fluent theme and the charts.
//
// Switching the Fluent theme replaces the theme resources of every open window. The charts are
// redrawn afterwards (Changed is raised once that has finished, not in the middle of it), each
// view guards its redraw, and a failure of the switch itself is logged instead of closing the app.

using System;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using TrajectoryLogTools.Charts;

namespace TrajectoryLogTools.App.Services
{
    public static class Theme
    {
        /// <summary>Raised after the theme changes (deferred until the switch has finished), so views can redraw their charts.</summary>
        public static event EventHandler Changed;

        public static bool IsDark { get; private set; }

        public static void Apply(string theme)
        {
            bool dark = theme == "Dark" || (theme != "Light" && WindowsUsesDarkApps());
            ThemeMode mode = theme == "Dark" ? ThemeMode.Dark : theme == "Light" ? ThemeMode.Light : ThemeMode.System;
            try
            {
                if (Application.Current.ThemeMode != mode) Application.Current.ThemeMode = mode;
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Theme switch to " + theme, ex);
                return;   // keep the theme that is showing; the charts keep matching it
            }
            IsDark = dark;
            ChartStyle.Dark = dark;
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                var h = Changed;
                if (h != null) h(null, EventArgs.Empty);
            }));
        }

        static bool WindowsUsesDarkApps()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = key == null ? null : key.GetValue("AppsUseLightTheme");
                    return v is int i && i == 0;
                }
            }
            catch (Exception) { return false; }
        }
    }
}
