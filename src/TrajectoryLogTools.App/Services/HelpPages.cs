// HelpPages.cs - which page of the user guide F1 and the Help button open, for each window and page
// (paths in the site built from docs/user; see TrajectoryLogTools.Wpf.Help).

using System.Windows;
using TrajectoryLogTools.App.ViewModels;
using TrajectoryLogTools.App.Views;

namespace TrajectoryLogTools.App.Services
{
    public static class HelpPages
    {
        public static string For(Window w)
        {
            switch (w)
            {
                case ExportWindow _: return "how-to/export.html";
                case HeaderEditWindow _: return "how-to/export-header-details.html";
                case FileCompareWindow _: return "how-to/validate-conversion.html";
                case RebuildDetailsWindow _: return "reference/rebuild-statuses.html";
                case SettingsWindow _: return "reference/settings.html";
                case AddFromFolderWindow _: return "how-to/open-logs.html";
            }
            return ForPage((w?.DataContext as MainViewModel)?.CurrentPage);
        }

        /// <summary>The page for what the main window shows.</summary>
        public static string ForPage(object page)
        {
            switch (page)
            {
                case LogPageViewModel _: return "reference/log-page.html";
                case ComparePageViewModel _: return "how-to/compare-deliveries.html";
                case RebuildPageViewModel _: return "how-to/rebuild-mlc.html";
            }
            return "index.html";
        }
    }
}
