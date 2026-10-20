using System;
using System.Windows;
using System.Windows.Controls;
using TrajectoryLogTools.App.Services;
using TrajectoryLogTools.App.ViewModels;
using TrajectoryLogTools.Charts;

namespace TrajectoryLogTools.App.Views
{
    public partial class ViewerView : UserControl
    {
        ViewerViewModel _vm;

        public ViewerView()
        {
            InitializeComponent();
            PlotSetup.Configure(AxisPlot, ErrorPlot, MlcPlot);
            DataContextChanged += OnDataContextChanged;
        }

        void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null) _vm.ChartsChanged -= Redraw;
            _vm = DataContext as ViewerViewModel;
            if (_vm != null) { _vm.ChartsChanged += Redraw; Redraw(null, EventArgs.Empty); }
        }

        void Redraw(object sender, EventArgs e)
        {
            var d = _vm == null ? null : _vm.Record;
            string axis = _vm == null ? null : _vm.SelectedAxis.Key;
            try
            {
                if (d == null || axis == null)
                {
                    ChartStyle.Empty(AxisPlot.Plot, "Open a log");
                    ErrorPlot.Plot.Clear();
                }
                else
                {
                    LogCharts.AxisTrace(AxisPlot.Plot, d, axis, _vm.VsControlPoint);
                    LogCharts.AxisError(ErrorPlot.Plot, d, axis, _vm.VsControlPoint);
                }
                if (d == null) ChartStyle.Empty(MlcPlot.Plot, "Open a log");
                else LogCharts.MlcError(MlcPlot.Plot, d, _vm.VsControlPoint);
            }
            catch (Exception ex) { ChartStyle.Empty(AxisPlot.Plot, "Chart error: " + ex.Message); }
            AxisPlot.Refresh();
            ErrorPlot.Refresh();
            MlcPlot.Refresh();
        }
    }
}
