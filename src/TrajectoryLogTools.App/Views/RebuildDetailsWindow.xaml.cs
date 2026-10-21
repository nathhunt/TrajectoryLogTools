using System;
using System.Windows;
using TrajectoryLogTools.App.Services;
using TrajectoryLogTools.App.ViewModels;
using TrajectoryLogTools.Charts;

namespace TrajectoryLogTools.App.Views
{
    public partial class RebuildDetailsWindow : Window
    {
        RebuildDetailsViewModel _vm;

        public RebuildDetailsWindow()
        {
            InitializeComponent();
            PlotSetup.ConfigureInWindow(DiffPlot, LeafPlot);   // a window of its own: the wheel zooms
            DataContextChanged += (s, e) =>
            {
                if (_vm != null) _vm.ChartsChanged -= Redraw;
                _vm = DataContext as RebuildDetailsViewModel;
                if (_vm != null) _vm.ChartsChanged += Redraw;
                Redraw(null, EventArgs.Empty);
            };
            Theme.Changed += OnThemeChanged;
            Display.AnglesChanged += OnAnglesChanged;
            Closed += (s, e) => { Theme.Changed -= OnThemeChanged; Display.AnglesChanged -= OnAnglesChanged; };
        }

        void OnAnglesChanged(object sender, EventArgs e) { _vm?.RefreshAngles(); }

        void OnThemeChanged(object sender, EventArgs e) { Redraw(null, EventArgs.Empty); }

        void Redraw(object sender, EventArgs e)
        {
            var r = _vm?.Result;
            try
            {
                MlcCharts.Difference(DiffPlot.Plot, r);
                if (r != null && r.Reconstructed != null)
                    MlcCharts.Leaf(LeafPlot.Plot, r, Math.Max(0, Math.Min(_vm.SelectedValue, r.Reconstructed.GetLength(1) - 1)));
                else ChartStyle.Empty(LeafPlot.Plot, "");
            }
            catch (Exception ex)
            {
                ErrorLog.Write("Rebuild details charts", ex);
                ChartStyle.Empty(DiffPlot.Plot, "Chart error: " + ex.Message);
            }
            DiffPlot.Refresh();
            LeafPlot.Refresh();
        }

        void Close_Click(object sender, RoutedEventArgs e) { Close(); }
    }
}
