// App.xaml.cs - start-up: theme, command-line arguments, main window, and what happens to errors
// nothing else caught (logged in full to errors.log, then a message; the app carries on).

using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TrajectoryLogTools.App.Services;
using TrajectoryLogTools.App.ViewModels;

namespace TrajectoryLogTools.App
{
    public partial class App : Application
    {
        bool _showingError;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (s, a) => ErrorLog.Write("Unhandled (closing)", a.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, a) => { ErrorLog.Write("Background task", a.Exception); a.SetObserved(); };
            Theme.Apply(AppSettings.Current.Theme);
            WheelScrolling.Register();
            var window = new MainWindow();
            MainWindow = window;
            window.Show();   // before the view model: it may open a dialog owned by the window
            window.DataContext = new MainViewModel(StartupArgs.Parse(e.Args));
        }

        void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            ErrorLog.Write("Unhandled", e.Exception);
            // The message is shown after the handler returns: the error may come from inside layout or
            // rendering, where opening a message box would itself fail. One message at a time.
            if (_showingError) return;
            _showingError = true;
            string message = e.Exception.Message;
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                try
                {
                    MessageBox.Show(message + Environment.NewLine + Environment.NewLine + "The details were saved to " + ErrorLog.FilePath + ".",
                                    "Trajectory Log Tools", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                finally { _showingError = false; }
            }));
        }
    }
}
