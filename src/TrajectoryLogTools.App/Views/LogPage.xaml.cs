using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TrajectoryLogTools.App.Services;
using TrajectoryLogTools.App.ViewModels;
using TrajectoryLogTools.Charts;

namespace TrajectoryLogTools.App.Views
{
    public partial class LogPage : UserControl
    {
        LogPageViewModel _vm;

        public LogPage()
        {
            InitializeComponent();
            PlotSetup.Configure(AxisPlot, ErrorPlot, MlcPlot, RmsPlot);
            AxisPlot.Plot.Axes.Link(ErrorPlot, true, false);
            ErrorPlot.Plot.Axes.Link(AxisPlot, true, false);
            MlcPlot.Plot.Axes.Link(RmsPlot, false, true);
            RmsPlot.Plot.Axes.Link(MlcPlot, false, true);
            DataContextChanged += OnDataContextChanged;
            Loaded += (s, e) => { Theme.Changed += OnThemeChanged; Display.AnglesChanged += OnAnglesChanged; };
            Unloaded += (s, e) => { Theme.Changed -= OnThemeChanged; Display.AnglesChanged -= OnAnglesChanged; };
        }

        void OnThemeChanged(object sender, EventArgs e) { Redraw(null, EventArgs.Empty); }
        void OnAnglesChanged(object sender, EventArgs e) { _vm?.RefreshAngles(); }

        void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_vm != null) _vm.ChartsChanged -= Redraw;
            _vm = DataContext as LogPageViewModel;
            if (_vm != null) _vm.ChartsChanged += Redraw;
            Redraw(null, EventArgs.Empty);
        }

        // Only the visible tab's charts are drawn (an MLC map of a long log is millions of values);
        // switching tabs raises ChartsChanged again.
        void Redraw(object sender, EventArgs e)
        {
            var d = _vm?.Record;
            int tab = _vm?.SelectedTab ?? -1;
            try
            {
                if (tab == 1)
                {
                    string axis = _vm.SelectedAxis.Key;
                    if (d == null || axis == null) { ChartStyle.Empty(AxisPlot.Plot, ""); ChartStyle.Empty(ErrorPlot.Plot, ""); }
                    else
                    {
                        LogCharts.AxisTrace(AxisPlot.Plot, d, axis, _vm.VsControlPoint, Display.Angles);
                        LogCharts.AxisError(ErrorPlot.Plot, d, axis, _vm.VsControlPoint, Display.Angles);
                    }
                    AxisPlot.Refresh();
                    ErrorPlot.Refresh();
                }
                else if (tab == 2)
                {
                    if (d == null) { ChartStyle.Empty(MlcPlot.Plot, ""); ChartStyle.Empty(RmsPlot.Plot, ""); }
                    else
                    {
                        LogCharts.MlcError(MlcPlot.Plot, d, _vm.VsControlPoint);
                        LogCharts.LeafRms(RmsPlot.Plot, d);
                    }
                    MlcPlot.Refresh();
                    RmsPlot.Refresh();
                }
            }
            catch (Exception ex)
            {
                var target = tab == 2 ? MlcPlot : AxisPlot;
                ChartStyle.Empty(target.Plot, "Chart error: " + ex.Message);
                target.Refresh();
            }
        }

        void FindBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter) { FindNext(); e.Handled = true; }
        }

        void FindNext_Click(object sender, RoutedEventArgs e) { FindNext(); }

        void FindNext()
        {
            string what = FindBox.Text;
            string text = RawBox.Text ?? "";
            if (what.Length == 0 || text.Length == 0) return;
            int from = RawBox.SelectionStart + RawBox.SelectionLength;
            int i = text.IndexOf(what, from, StringComparison.OrdinalIgnoreCase);
            if (i < 0) i = text.IndexOf(what, 0, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return;
            RawBox.Focus();
            RawBox.Select(i, what.Length);
            RawBox.ScrollToLine(RawBox.GetLineIndexFromCharacterIndex(i));
        }
    }
}
