// App.xaml.cs - start-up: reads the command-line arguments and opens the main window.

using System.Windows;
using System.Windows.Threading;
using TrajectoryLogTools.App.ViewModels;

namespace TrajectoryLogTools.App
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnUnhandledException;
            var vm = new MainViewModel(StartupArgs.Parse(e.Args));
            var window = new MainWindow { DataContext = vm };
            MainWindow = window;
            window.Show();
        }

        static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(e.Exception.Message, "Trajectory Log Tools", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
