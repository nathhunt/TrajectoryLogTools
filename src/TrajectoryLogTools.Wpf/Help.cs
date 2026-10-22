// Help.cs - the user guide, opened in the default browser from the Help button or F1.
//
// The guide is an offline HTML site (built from docs/user with MkDocs) that publishing copies into
// a Help folder beside each app's exe: Help\index.html and one page per topic. Each app says which
// page fits each window (Register); F1 in any window, and the Help buttons, open that page.

using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;

namespace TrajectoryLogTools.Wpf
{
    public static class Help
    {
        /// <summary>The Help folder beside the app.</summary>
        public static string Folder { get { return Path.Combine(AppContext.BaseDirectory, "Help"); } }

        static Func<Window, string> _pageFor = w => null;

        /// <summary>
        /// F1 (the Help command) in every window opens the page <paramref name="pageFor"/> gives for
        /// that window (a path in the site, e.g. "how-to/compare-deliveries.html"; null = the start page).
        /// Call once at start-up.
        /// </summary>
        public static void Register(Func<Window, string> pageFor)
        {
            _pageFor = pageFor ?? (w => null);
            CommandManager.RegisterClassCommandBinding(typeof(Window), new CommandBinding(ApplicationCommands.Help, (s, e) =>
            {
                string page = null;
                try { page = _pageFor((Window)s); }
                catch (Exception ex) { ErrorLog.Write("Help page", ex); }
                Open(page);
                e.Handled = true;
            }));
        }

        /// <summary>Opens a page of the guide (null = the start page), or says where the guide should be.</summary>
        public static void Open(string page)
        {
            string index = Path.Combine(Folder, "index.html");
            string file = string.IsNullOrEmpty(page) ? index : Path.Combine(Folder, page.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(file)) file = index;
            if (!File.Exists(file))
            {
                Dialogs.Warn("The user guide isn't installed with this copy of the app.\n\nIt should be in:\n" + Folder +
                             "\n\nAsk whoever looks after the deployment to publish the app with its Help folder.");
                return;
            }
            try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); }
            catch (Exception ex)
            {
                ErrorLog.Write("Open help", ex);
                Dialogs.Warn("The user guide could not be opened: " + ex.Message + "\n\nYou can open it yourself: " + file);
            }
        }
    }
}
