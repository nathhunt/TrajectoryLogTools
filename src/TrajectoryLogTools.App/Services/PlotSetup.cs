// PlotSetup.cs - mouse behaviour shared by every chart: drag to pan (kept inside the data by
// ChartStyle.Finish), double-click to go back to the full view, and the scroll wheel:
//   * on a page of the main window, the wheel scrolls the page and Ctrl + wheel zooms the chart
//     (the page doesn't move while zooming);
//   * in a window of its own (a details window, or "Open in New Window"), the wheel zooms the chart.
// The right-click menu adds Show / Hide data cursor (DataCursor), and "Open in New Window" opens
// a window whose chart behaves the same way (data cursor included).

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ScottPlot;
using ScottPlot.Interactivity.UserActionResponses;
using ScottPlot.WPF;
using TrajectoryLogTools.Charts;

namespace TrajectoryLogTools.App.Services
{
    public static class PlotSetup
    {
        /// <summary>Zoom per wheel notch: the axis span changes by 15 %, as ScottPlot's own wheel zoom.</summary>
        const double ZoomStep = 1.15;

        /// <summary>Charts on a page: wheel scrolls the page, Ctrl + wheel zooms.</summary>
        public static void Configure(params WpfPlot[] plots) { Configure(false, plots); }

        /// <summary>Charts in a window of their own: the wheel zooms.</summary>
        public static void ConfigureInWindow(params WpfPlot[] plots) { Configure(true, plots); }

        static void Configure(bool ownWindow, WpfPlot[] plots)
        {
            foreach (WpfPlot p in plots)
            {
                WpfPlot plot = p;
                // ScottPlot's default double-click toggles a benchmark overlay; use it to reset instead.
                plot.UserInputProcessor.DoubleLeftClickBenchmark(false);
                plot.MouseDoubleClick += (s, e) =>
                {
                    ChartStyle.Home(plot.Plot);
                    plot.Refresh();
                    e.Handled = true;
                };
                DataCursor.Attach(plot);
                SetUpMenu(plot);
                if (ownWindow) continue;

                // The wheel is ours: zoom only with Ctrl, otherwise leave the event to the page's scroll viewer.
                plot.UserInputProcessor.RemoveAll<MouseWheelZoom>();
                plot.PreviewMouseWheel += (s, e) =>
                {
                    if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
                    System.Windows.Point at = e.GetPosition(plot);
                    float scale = plot.DisplayScale;
                    var pixel = new Pixel((float)(at.X * scale), (float)(at.Y * scale));
                    double f = e.Delta > 0 ? ZoomStep : 1 / ZoomStep;
                    plot.Plot.Axes.Zoom(pixel, f, f);
                    plot.Refresh();
                    e.Handled = true;   // handled here, so the page doesn't scroll
                };
            }
        }

        /// <summary>ScottPlot's menu, with Show / Hide data cursor first and our own Open in New Window.</summary>
        static void SetUpMenu(WpfPlot plot)
        {
            var menu = plot.Menu as WpfPlotMenu;
            if (menu == null) return;
            var items = menu.ContextMenuItems;
            int open = items.FindIndex(i => !i.IsSeparator && i.Label != null && i.Label.IndexOf("New Window", StringComparison.OrdinalIgnoreCase) >= 0);
            if (open >= 0) items[open] = new ContextMenuItem { Label = items[open].Label, OnInvoke = p => OpenInWindow(plot) };
            items.Insert(0, new ContextMenuItem { Label = DataCursor.MenuLabel, OnInvoke = p => DataCursor.Enabled = !DataCursor.Enabled });
            items.Insert(1, new ContextMenuItem { IsSeparator = true });
            // the label says what the item will do: refresh it before the menu opens
            plot.PreviewMouseRightButtonDown += (s, e) =>
            {
                if (items.Count > 0 && !items[0].IsSeparator)
                    items[0] = new ContextMenuItem { Label = DataCursor.MenuLabel, OnInvoke = items[0].OnInvoke };
            };
        }

        /// <summary>The chart in a window of its own, where the wheel zooms.</summary>
        static void OpenInWindow(WpfPlot source)
        {
            var copy = new WpfPlot();
            copy.Reset(source.Plot);
            ConfigureInWindow(copy);
            string title = source.Plot.Axes.Title.Label.Text;
            var w = new Window
            {
                Title = string.IsNullOrWhiteSpace(title) ? "Chart" : title.Split('\n')[0].Trim(),
                Content = copy, Width = 900, Height = 600,
                Owner = Window.GetWindow(source), WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            w.SetResourceReference(Control.BackgroundProperty, "ApplicationBackgroundBrush");
            // the same Plot draws in both: redraw the page's chart when the window closes
            w.Closed += (s, e) => source.Refresh();
            w.Show();
            copy.Refresh();
        }
    }
}
