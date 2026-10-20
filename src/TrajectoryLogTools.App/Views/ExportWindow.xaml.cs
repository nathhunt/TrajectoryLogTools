using System.ComponentModel;
using System.Windows;
using TrajectoryLogTools.App.Services;
using TrajectoryLogTools.App.ViewModels;

namespace TrajectoryLogTools.App.Views
{
    public partial class ExportWindow : Window
    {
        public ExportWindow() { InitializeComponent(); }

        void Close_Click(object sender, RoutedEventArgs e) { Close(); }

        protected override void OnClosing(CancelEventArgs e)
        {
            var vm = DataContext as ExportViewModel;
            if (vm != null && vm.IsBusy)
            {
                if (!Dialogs.Confirm("The export is still running. Stop it and close?")) { e.Cancel = true; return; }
                vm.CancelCommand.Execute(null);
            }
            base.OnClosing(e);
        }
    }
}
