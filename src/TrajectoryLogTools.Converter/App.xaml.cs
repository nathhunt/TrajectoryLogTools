// App.xaml.cs - start-up, and what happens to errors nothing else caught (logged in full to
// errors.log, then a message; the app carries on).

using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TrajectoryLogTools.Wpf;

namespace TrajectoryLogTools.Converter
{
    public partial class App : Application
    {
        bool _showingError;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            NzCulture.Apply();
            Dialogs.Title = "Trajectory Log Converter";
            DispatcherUnhandledException += OnUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (s, a) => ErrorLog.Write("Unhandled (closing)", a.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, a) => { ErrorLog.Write("Background task", a.Exception); a.SetObserved(); };
            WheelScrolling.Register();
            // F1 in every window: the converter's pages of the user guide
            Help.Register(w => w is SettingsWindow ? "how-to/converter-folders.html" : "how-to/convert-days-logs.html");
            var window = new MainWindow();
            MainWindow = window;
            window.Show();   // before the view model: it may show a message owned by the window
            window.DataContext = new MainViewModel();
        }

        void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            ErrorLog.Write("Unhandled", e.Exception);
            if (_showingError) return;
            _showingError = true;
            string message = e.Exception.Message;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                try
                {
                    MessageBox.Show(message + Environment.NewLine + Environment.NewLine + "The details were saved to " + ErrorLog.FilePath + ".",
                                    Dialogs.Title, MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally { _showingError = false; }
            }));
        }
    }
}
