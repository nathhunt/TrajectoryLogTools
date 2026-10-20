// App.xaml.cs - start-up: theme, command-line arguments, main window.

using System.Windows;
using System.Windows.Threading;
using TrajectoryLogTools.App.Services;
using TrajectoryLogTools.App.ViewModels;

namespace TrajectoryLogTools.App
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += OnUnhandledException;
            Theme.Apply(AppSettings.Current.Theme);
            var window = new MainWindow();
            MainWindow = window;
            window.Show();   // before the view model: it may open a dialog owned by the window
            window.DataContext = new MainViewModel(StartupArgs.Parse(e.Args));
        }

        static void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(e.Exception.Message, "Trajectory Log Tools", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        }
    }
}
