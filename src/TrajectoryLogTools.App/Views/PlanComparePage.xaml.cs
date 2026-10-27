using System;
using System.Windows;
using System.Windows.Controls;
using TrajectoryLogTools.App.Services;
using TrajectoryLogTools.App.ViewModels;
using TrajectoryLogTools.Charts;

namespace TrajectoryLogTools.App.Views
{
    public partial class PlanComparePage : UserControl
    {
        PlanComparePageViewModel _vm;

        public PlanComparePage()
        {
            InitializeComponent();
            PlotSetup.Configure(LeafPlot, GantryPlot, MuPlot, TransferPlot);
            DataContextChanged += OnDataContextChanged;
            Loaded += (s, e) => { Theme.Changed += OnThemeChanged; Display.AnglesChanged += OnAnglesChanged; };
            Unloaded += (s, e) => { Theme.Changed -= OnThemeChanged; Display.AnglesChanged -= OnAnglesChanged; };
        }

        void OnThemeChanged(object sender, EventArgs e) { Redraw(null, EventArgs.Empty); }
        void OnAnglesChanged(object sender, EventArgs e) { _vm?.RefreshAngles(); }

        void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null) _vm.ChartsChanged -= Redraw;
            _vm = DataContext as PlanComparePageViewModel;
            if (_vm != null) _vm.ChartsChanged += Redraw;
            Redraw(null, EventArgs.Empty);
        }

        void Redraw(object sender, EventArgs e)
        {
            var f = _vm?.SelectedField?.Field;
            try
            {
                if (f == null)
                    foreach (var p in new[] { LeafPlot, GantryPlot, MuPlot, TransferPlot }) ChartStyle.Empty(p.Plot, "");
                else
                {
                    PlanCharts.LeafDifference(LeafPlot.Plot, f, true);
                    PlanCharts.LeafDifference(TransferPlot.Plot, f, false);
                    PlanCharts.Gantry(GantryPlot.Plot, f, legend: false, scale: Display.Angles);   // one legend for both, above them
                    PlanCharts.Mu(MuPlot.Plot, f, legend: false);
                }
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Plan comparison charts", ex);
                ChartStyle.Empty(LeafPlot.Plot, "Chart error: " + ex.Message);
            }
            foreach (var p in new[] { LeafPlot, GantryPlot, MuPlot, TransferPlot }) p.Refresh();
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
