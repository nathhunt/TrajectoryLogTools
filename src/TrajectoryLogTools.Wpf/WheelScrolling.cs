// WheelScrolling.cs - the mouse wheel, the same everywhere in the app.
//
// The rule: wherever the mouse is, exactly one thing scrolls vertically.
//   * A page (log, compare, rebuild, the dialogs) shows its tables and texts at full length,
//     with no scroll bar of their own, so the wheel over them scrolls the page.
//   * Where a table has to scroll (the File comparison and details windows), it fills its part of
//     the window and nothing around it scrolls, so the wheel over it scrolls the table.
//   * Shift + wheel scrolls the table or text under the mouse sideways, when it is wider than
//     its space. Ctrl + wheel zooms a chart (PlotSetup).
//
// WPF gives the wheel to the innermost scroll viewer under the mouse, and a scroll viewer keeps
// the wheel even when it has nothing to scroll (a table sized to its rows), which would stop the
// page while the mouse is over it. Here, a scroll viewer that can't scroll further that way passes
// the wheel on to the next one out.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace TrajectoryLogTools.Wpf
{
    public static class WheelScrolling
    {
        public static void Register()
        {
            EventManager.RegisterClassHandler(typeof(ScrollViewer), UIElement.PreviewMouseWheelEvent, new MouseWheelEventHandler(OnPreviewMouseWheel));
        }

        static void OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            var sv = (ScrollViewer)sender;
            if (e.Handled || (Keyboard.Modifiers & ModifierKeys.Control) != 0) return;
            // Only the scroll viewer nearest the mouse decides (the event tunnels through the outer ones first).
            if (Nearest<ScrollViewer>(e.OriginalSource as DependencyObject) != sv) return;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
            {
                // Sideways: the nearest scroll viewer that is wider than its space.
                DependencyObject d = sv;
                while (d != null && !(d is ScrollViewer w && w.ScrollableWidth > 0)) d = Parent(d);
                if (d is ScrollViewer wide)
                {
                    wide.ScrollToHorizontalOffset(wide.HorizontalOffset - e.Delta * 0.5);
                    e.Handled = true;
                }
                return;
            }
            bool up = e.Delta > 0;
            bool canScroll = sv.ScrollableHeight > 0 && (up ? sv.VerticalOffset > 0 : sv.VerticalOffset < sv.ScrollableHeight);
            if (canScroll) return;
            DependencyObject parent = Parent(sv);
            if (Nearest<ScrollViewer>(parent) == null) return;
            e.Handled = true;
            var pass = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta) { RoutedEvent = UIElement.MouseWheelEvent, Source = sv };
            (parent as UIElement ?? (parent as FrameworkContentElement)?.Parent as UIElement)?.RaiseEvent(pass);
        }

        static T Nearest<T>(DependencyObject d) where T : DependencyObject
        {
            while (d != null && !(d is T)) d = Parent(d);
            return d as T;
        }

        static DependencyObject Parent(DependencyObject d)
        {
            if (d is Visual || d is System.Windows.Media.Media3D.Visual3D) return VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
            if (d is TextElement t) return t.Parent;
            return LogicalTreeHelper.GetParent(d);
        }
    }
}
