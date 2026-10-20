using System;
using System.Windows;
using System.Windows.Controls;
using TrajectoryLogTools.App.Services;
using TrajectoryLogTools.App.ViewModels;
using TrajectoryLogTools.Charts;

namespace TrajectoryLogTools.App.Views
{
    public partial class ComparePage : UserControl
    {
        ComparePageViewModel _vm;

        public ComparePage()
        {
            InitializeComponent();
            PlotSetup.Configure(ActualPlot, GantryPlot, MuPlot, ExpectedPlot);
            DataContextChanged += OnDataContextChanged;
            Loaded += (s, e) => Theme.Changed += OnThemeChanged;
            Unloaded += (s, e) => Theme.Changed -= OnThemeChanged;
        }

        void OnThemeChanged(object sender, EventArgs e) { Redraw(null, EventArgs.Empty); }

        void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null) _vm.ChartsChanged -= Redraw;
            _vm = DataContext as ComparePageViewModel;
            if (_vm != null) _vm.ChartsChanged += Redraw;
            Redraw(null, EventArgs.Empty);
        }

        void Redraw(object sender, EventArgs e)
        {
            var r = _vm?.Result;
            if (r == null)
                foreach (var p in new[] { ActualPlot, GantryPlot, MuPlot, ExpectedPlot }) ChartStyle.Empty(p.Plot, "");
            else
            {
                DeliveryCharts.LeafDifference(ActualPlot.Plot, r, true);
                DeliveryCharts.LeafDifference(ExpectedPlot.Plot, r, false);
                DeliveryCharts.AxisDifference(GantryPlot.Plot, r, "Gantry", legend: false);   // one legend for both, above them
                DeliveryCharts.AxisDifference(MuPlot.Plot, r, "MU", legend: false);
            }
            foreach (var p in new[] { ActualPlot, GantryPlot, MuPlot, ExpectedPlot }) p.Refresh();
        }

        void Drop_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
            e.Handled = true;
        }

        void DropA_Drop(object sender, DragEventArgs e) { DropOn(true, e); }
        void DropB_Drop(object sender, DragEventArgs e) { DropOn(false, e); }

        void DropOn(bool isA, DragEventArgs e)
        {
            e.Handled = true;   // not also opened by the window
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (_vm != null && files != null && files.Length > 0) _vm.Replace(isA, files[0]);
        }
    }
}
