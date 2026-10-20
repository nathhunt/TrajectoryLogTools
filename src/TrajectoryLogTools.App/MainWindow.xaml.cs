using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TrajectoryLogTools.App.ViewModels;

namespace TrajectoryLogTools.App
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }

        MainViewModel Vm { get { return DataContext as MainViewModel; } }

        void Window_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        void Window_Drop(object sender, DragEventArgs e)
        {
            if (Vm == null || !e.Data.GetDataPresent(DataFormats.FileDrop)) return;
            var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
            Vm.OpenPaths(paths);
        }

        void MoreButton_Click(object sender, RoutedEventArgs e)
        {
            var b = (Button)sender;
            b.ContextMenu.PlacementTarget = b;
            b.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            b.ContextMenu.IsOpen = true;
        }

        void Recent_Click(object sender, RoutedEventArgs e)
        {
            var mi = e.OriginalSource as MenuItem;
            if (mi != null && mi.DataContext is string path && Vm != null) Vm.OpenPaths(new[] { path });
        }

        // Clicking the log that is already selected brings its page back (e.g. from Compare).
        void LogList_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var dep = e.OriginalSource as DependencyObject;
            while (dep != null && !(dep is ListBoxItem))
            {
                if (dep is CheckBox) return;
                dep = dep is Visual || dep is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(dep) : LogicalTreeHelper.GetParent(dep);
            }
            var item = (dep as ListBoxItem)?.DataContext as LogItem;
            if (item != null && Vm != null && item == Vm.CurrentLog) Vm.ShowLog(item);
        }
    }
}
