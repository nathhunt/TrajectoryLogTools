using System.Windows;

namespace TrajectoryLogTools.App.Views
{
    public partial class AddFromFolderWindow : Window
    {
        public AddFromFolderWindow() { InitializeComponent(); }

        void Add_Click(object sender, RoutedEventArgs e) { DialogResult = true; }
    }
}
