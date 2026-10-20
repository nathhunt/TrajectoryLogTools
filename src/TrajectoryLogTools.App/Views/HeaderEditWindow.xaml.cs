using System.Windows;
using TrajectoryLogTools.App.Services;
using TrajectoryLogTools.App.ViewModels;

namespace TrajectoryLogTools.App.Views
{
    public partial class HeaderEditWindow : Window
    {
        public HeaderEditWindow() { InitializeComponent(); }

        void Ok_Click(object sender, RoutedEventArgs e)
        {
            string error = (DataContext as HeaderEditViewModel)?.Validate();
            if (error != null) { Dialogs.Warn(error); return; }
            DialogResult = true;
        }

        void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; }
    }
}
