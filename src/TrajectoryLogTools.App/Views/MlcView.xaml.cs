using System;
using System.Windows;
using System.Windows.Controls;
using TrajectoryLogTools.App.Services;
using TrajectoryLogTools.App.ViewModels;
using TrajectoryLogTools.Charts;

namespace TrajectoryLogTools.App.Views
{
    public partial class MlcView : UserControl
    {
        MlcViewModel _vm;

        public MlcView()
        {
            InitializeComponent();
            PlotSetup.Configure(DiffPlot, LeafPlot);
            DataContextChanged += OnDataContextChanged;
        }

        void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null) _vm.ChartsChanged -= Redraw;
            _vm = DataContext as MlcViewModel;
            if (_vm != null) { _vm.ChartsChanged += Redraw; Redraw(null, EventArgs.Empty); }
        }

        void Redraw(object sender, EventArgs e)
        {
            var r = _vm == null ? null : _vm.SelectedResult;
            MlcCharts.Difference(DiffPlot.Plot, r);
            if (r != null && r.Reconstructed != null)
            {
                int k = Math.Max(0, Math.Min(_vm.SelectedValue, r.Reconstructed.GetLength(1) - 1));
                MlcCharts.Leaf(LeafPlot.Plot, r, k);
            }
            else ChartStyle.Empty(LeafPlot.Plot, "No reconstruction");
            DiffPlot.Refresh();
            LeafPlot.Refresh();
        }
    }
}
