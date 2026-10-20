using System.ComponentModel;
using System.Windows;
using TrajectoryLogTools.App.Services;
using TrajectoryLogTools.App.ViewModels;

namespace TrajectoryLogTools.App
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            var vm = DataContext as MainViewModel;
            if (vm != null && vm.Convert.IsBusy)
            {
                if (!Dialogs.Confirm("A conversion is running. Stop it and close?")) { e.Cancel = true; return; }
                vm.Convert.CancelCommand.Execute(null);
            }
            base.OnClosing(e);
        }
    }
}
