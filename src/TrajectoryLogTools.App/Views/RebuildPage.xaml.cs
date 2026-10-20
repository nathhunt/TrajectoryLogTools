using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TrajectoryLogTools.App.ViewModels;

namespace TrajectoryLogTools.App.Views
{
    public partial class RebuildPage : UserControl
    {
        public RebuildPage()
        {
            InitializeComponent();
            AllowDrop = true;
            Drop += OnDrop;
        }

        // Eclipse exports (.csv) dropped on this page go to the fields; anything else to the window.
        void OnDrop(object sender, DragEventArgs e)
        {
            var vm = DataContext as RebuildPageViewModel;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (vm == null || files == null) return;
            var csv = files.Where(f => f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)).ToList();
            if (csv.Count == 0) return;
            e.Handled = true;
            vm.AddExportFiles(csv);
        }
    }
}
