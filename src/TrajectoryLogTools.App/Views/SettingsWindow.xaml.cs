using System.Windows;
using TrajectoryLogTools.App.Services;
using TrajectoryLogTools.App.ViewModels;

namespace TrajectoryLogTools.App.Views
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow() { InitializeComponent(); }

        void Save_Click(object sender, RoutedEventArgs e)
        {
            if ((DataContext as SettingsViewModel)?.Save() == true) DialogResult = true;
        }

        void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; }

        protected override void OnClosed(System.EventArgs e)
        {
            if (DialogResult != true) Theme.Apply(AppSettings.Current.Theme);   // undo a theme previewed but not saved
            base.OnClosed(e);
        }
    }
}
