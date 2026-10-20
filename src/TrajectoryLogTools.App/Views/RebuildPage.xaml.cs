using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using TrajectoryLogTools.App.Services;
using TrajectoryLogTools.App.ViewModels;
using TrajectoryLogTools.Charts;

namespace TrajectoryLogTools.App.Views
{
    public partial class RebuildPage : UserControl
    {
        RebuildPageViewModel _vm;

        public RebuildPage()
        {
            InitializeComponent();
            PlotSetup.Configure(DiffPlot, LeafPlot);
            AllowDrop = true;
            Drop += OnDrop;
            DataContextChanged += OnDataContextChanged;
            Loaded += (s, e) => Theme.Changed += OnThemeChanged;
            Unloaded += (s, e) => Theme.Changed -= OnThemeChanged;
        }

        void OnThemeChanged(object sender, EventArgs e) { Redraw(null, EventArgs.Empty); }

        // Eclipse exports (.csv) dropped on this page go to the fields; anything else to the window.
        void OnDrop(object sender, DragEventArgs e)
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (_vm == null || files == null) return;
            var csv = files.Where(f => f.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)).ToList();
            if (csv.Count == 0) return;
            e.Handled = true;
            _vm.AddExportFiles(csv);
        }

        void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null) _vm.ChartsChanged -= Redraw;
            _vm = DataContext as RebuildPageViewModel;
            if (_vm != null) _vm.ChartsChanged += Redraw;
            Redraw(null, EventArgs.Empty);
        }

        void Redraw(object sender, EventArgs e)
        {
            var r = _vm?.Selected?.Result;
            MlcCharts.Difference(DiffPlot.Plot, r);
            if (r != null && r.Reconstructed != null)
                MlcCharts.Leaf(LeafPlot.Plot, r, Math.Max(0, Math.Min(_vm.SelectedValue, r.Reconstructed.GetLength(1) - 1)));
            else ChartStyle.Empty(LeafPlot.Plot, "");
            DiffPlot.Refresh();
            LeafPlot.Refresh();
        }
    }
}
