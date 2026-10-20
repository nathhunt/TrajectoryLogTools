using System;
using System.Windows;
using System.Windows.Controls;
using TrajectoryLogTools.App.Services;
using TrajectoryLogTools.App.ViewModels;
using TrajectoryLogTools.Charts;

namespace TrajectoryLogTools.App.Views
{
    public partial class CompareView : UserControl
    {
        CompareViewModel _vm;

        public CompareView()
        {
            InitializeComponent();
            PlotSetup.Configure(GantryPlot, MuPlot, ExpectedPlot, ActualPlot);
            DataContextChanged += OnDataContextChanged;
        }

        void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null) _vm.ChartsChanged -= Redraw;
            _vm = DataContext as CompareViewModel;
            if (_vm != null) { _vm.ChartsChanged += Redraw; Redraw(null, EventArgs.Empty); }
        }

        void Redraw(object sender, EventArgs e)
        {
            var r = _vm == null ? null : _vm.Result;
            if (r == null)
            {
                foreach (var p in new[] { GantryPlot, MuPlot, ExpectedPlot, ActualPlot }) ChartStyle.Empty(p.Plot, "");
                ChartStyle.Empty(GantryPlot.Plot, "Choose two logs and press Compare");
            }
            else
            {
                DeliveryCharts.AxisDifference(GantryPlot.Plot, r, "Gantry");
                DeliveryCharts.AxisDifference(MuPlot.Plot, r, "MU");
                DeliveryCharts.LeafDifference(ExpectedPlot.Plot, r, false);
                DeliveryCharts.LeafDifference(ActualPlot.Plot, r, true);
            }
            foreach (var p in new[] { GantryPlot, MuPlot, ExpectedPlot, ActualPlot }) p.Refresh();
        }
    }
}
