// App.xaml.cs - start-up (the log first), and what happens to errors nothing else caught (logged
// in full in the app's log, then a message; the app carries on).

using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using TrajectoryLogTools.Core;
using TrajectoryLogTools.Wpf;

namespace TrajectoryLogTools.Converter
{
    public partial class App : Application
    {
        bool _showingError;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            string settingsError;
            ConverterSettings settings = ConverterSettings.Load(out settingsError);
            AppLog.Start("TrajectoryLogConverter", AppVersion.Full, settings.LogFolder, settings.LogRetentionDays);
            NzCulture.Apply();
            Dialogs.Title = "Trajectory Log Converter";
            DispatcherUnhandledException += OnUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += (s, a) => ErrorLog.Write("Unhandled error: the app is closing", a.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (s, a) => { ErrorLog.Write("Unhandled error in a background task (the app carried on)", a.Exception); a.SetObserved(); };
            WheelScrolling.Register();
            var window = new MainWindow();
            MainWindow = window;
            window.Show();   // before the view model: it may show a message owned by the window
            window.DataContext = new MainViewModel();
        }

        void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            ErrorLog.Write("Unhandled error, shown to the user (the app carried on): " + e.Exception.Message, e.Exception);
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
