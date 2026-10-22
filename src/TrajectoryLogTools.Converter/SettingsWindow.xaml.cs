using System.Windows;

namespace TrajectoryLogTools.Converter
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow() { InitializeComponent(); }

        void Save_Click(object sender, RoutedEventArgs e)
        {
            if ((DataContext as SettingsViewModel)?.Save() == true) DialogResult = true;
        }
    }
}
