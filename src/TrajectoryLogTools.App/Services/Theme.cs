// Theme.cs - light / dark / follow Windows, for the WPF Fluent theme and the charts.

using System;
using System.Windows;
using Microsoft.Win32;
using TrajectoryLogTools.Charts;

namespace TrajectoryLogTools.App.Services
{
    public static class Theme
    {
        /// <summary>Raised after the theme changes, so views can redraw their charts.</summary>
        public static event EventHandler Changed;

        public static bool IsDark { get; private set; }

        public static void Apply(string theme)
        {
            bool dark = theme == "Dark" || (theme != "Light" && WindowsUsesDarkApps());
            Application.Current.ThemeMode = theme == "Dark" ? ThemeMode.Dark : theme == "Light" ? ThemeMode.Light : ThemeMode.System;
            IsDark = dark;
            ChartStyle.Dark = dark;
            var h = Changed;
            if (h != null) h(null, EventArgs.Empty);
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
