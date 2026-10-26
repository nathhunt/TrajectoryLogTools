// DataCursor.cs - the data cursor: with it on (right-click a chart > Show data cursor, one setting
// for every chart, remembered), the value under the mouse is shown beside it, as ChartProbe finds
// it: the nearest point of a line (marked with a ring), the cell of a difference map, or a bar.

using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using ScottPlot;
using ScottPlot.Plottables;
using ScottPlot.WPF;
using TrajectoryLogTools.Charts;

namespace TrajectoryLogTools.App.Services
{
    public static class DataCursor
    {
        public static event EventHandler EnabledChanged;

        public static bool Enabled
        {
            get { return AppSettings.Current.DataCursor; }
            set
            {
                if (AppSettings.Current.DataCursor == value) return;
                AppSettings.Current.DataCursor = value;
                AppSettings.Current.Save();
                var h = EnabledChanged;
                if (h != null) h(null, EventArgs.Empty);
            }
        }

        public static string MenuLabel { get { return Enabled ? "Hide data cursor" : "Show data cursor"; } }

        /// <summary>Adds the cursor to a chart (PlotSetup does this for every chart).</summary>
        public static void Attach(WpfPlot plot)
        {
            var c = new Cursor(plot);
            plot.MouseMove += (s, e) => c.Move(e);
            plot.MouseLeave += (s, e) => c.Hide();
            plot.PreviewMouseLeftButtonDown += (s, e) => c.Hide();   // panning: out of the way
            EventHandler off = (s, e) => { if (!Enabled) c.Hide(); };
            plot.Loaded += (s, e) => EnabledChanged += off;
            plot.Unloaded += (s, e) => { EnabledChanged -= off; c.Hide(); };
        }

        sealed class Cursor
        {
            readonly WpfPlot _plot;
            readonly Popup _popup;
            readonly TextBlock _text;
            Marker _marker;

            public Cursor(WpfPlot plot)
            {
                _plot = plot;
                _text = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.NoWrap };
                _text.SetResourceReference(TextBlock.ForegroundProperty, "TextFillColorPrimaryBrush");
                var border = new Border { Child = _text, Padding = new Thickness(8, 5, 8, 5), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1) };
                border.SetResourceReference(Border.BackgroundProperty, "ApplicationBackgroundBrush");
                border.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
                _popup = new Popup
                {
                    Child = border, PlacementTarget = plot, Placement = PlacementMode.Relative,
                    AllowsTransparency = true, IsHitTestVisible = false, StaysOpen = true, Focusable = false,
                };
            }

            public void Hide()
            {
                _popup.IsOpen = false;
                RemoveMarker(true);
            }

            void RemoveMarker(bool refresh)
            {
                if (_marker == null) return;
                _plot.Plot.Remove(_marker);
                _marker = null;
                if (refresh) _plot.Refresh();
            }

            public void Move(MouseEventArgs e)
            {
                if (!Enabled || e.LeftButton == MouseButtonState.Pressed || e.RightButton == MouseButtonState.Pressed) { Hide(); return; }
                Plot plt = _plot.Plot;
                Point at = e.GetPosition(_plot);
                var px = new Pixel((float)(at.X * _plot.DisplayScale), (float)(at.Y * _plot.DisplayScale));
                RenderDetails rd = plt.LastRender;
                if (!rd.DataRect.Contains(px)) { Hide(); return; }
                ChartProbe.Hit hit;
                try { hit = ChartProbe.At(plt, px); }
                catch (Exception) { hit = null; }   // a chart being redrawn: try again on the next move
                if (hit == null) { Hide(); return; }
                string text = hit.Text;
                Coordinates? point = hit.Point;
                Color color = hit.Color;

                // ring on the point of a line
                if (point.HasValue)
                {
                    if (_marker == null || !plt.GetPlottables().Contains(_marker))
                    {
                        _marker = plt.Add.Marker(point.Value, MarkerShape.OpenCircle, 11, color);
                        _marker.MarkerStyle.LineWidth = 2;
                        _plot.Refresh();
                    }
                    else if (!_marker.Location.Equals(point.Value) || !_marker.Color.Equals(color))
                    {
                        _marker.Location = point.Value;
                        _marker.Color = color;
                        _plot.Refresh();   // only when the ring moves: a chart redraw isn't free
                    }
                }
                else RemoveMarker(true);

                _text.Text = text;
                double x = at.X + 16, y = at.Y + 16;
                _popup.Child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Size size = _popup.Child.DesiredSize;
                if (x + size.Width > _plot.ActualWidth) x = Math.Max(0, at.X - size.Width - 12);
                if (y + size.Height > _plot.ActualHeight) y = Math.Max(0, at.Y - size.Height - 12);
                _popup.HorizontalOffset = x;
                _popup.VerticalOffset = y;
                if (!_popup.IsOpen) _popup.IsOpen = true;
            }
        }

    }
}
